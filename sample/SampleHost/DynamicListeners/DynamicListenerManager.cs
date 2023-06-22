// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Extensions.Logging;

namespace SampleHost
{
    internal class DynamicListenerManager : IDisposable
    {
        private readonly IDynamicListenerStatusProvider _statusProvider;
        private readonly List<DynamicListenerConfig> _dynamicListenerConfigs;
        private readonly ILogger _logger;
        private bool _disposed;

        public DynamicListenerManager(IDynamicListenerStatusProvider statusProvider, ILogger<DynamicListenerManager> logger)
        {
            _statusProvider = statusProvider;
            _dynamicListenerConfigs = new List<DynamicListenerConfig>();
            _logger = logger;
        }

        public bool RequiresDynamicListener(string functionId)
        {
            return _statusProvider.IsDynamic(functionId);
        }

        public bool TryCreate(IFunctionDefinition functionDefinition, IListener listener, out IListener dynamicListener)
        {
            dynamicListener = null;
            string functionId = functionDefinition.Descriptor.Id;

            if (RequiresDynamicListener(functionId))
            {
                _logger.LogInformation($"Creating dynamic listener for function {functionId}");

                dynamicListener = new DynamicListener(listener, functionDefinition, this);

                var config = new DynamicListenerConfig
                {
                    Listener = (DynamicListener)dynamicListener
                };
                _dynamicListenerConfigs.Add(config);

                return true;
            }

            return false;
        }

        private async Task<DynamicListenerStatus> OnListenerStarted(IListener listener)
        {
            // once the outer listener is started, we can start our dynamic monitoring
            DynamicListener dynamicListener = (DynamicListener)listener;
            string functionId = dynamicListener.FunctionId;

            var status = await _statusProvider.GetStatusAsync(functionId);
            if (!status.IsEnabled)
            {
                _logger.LogInformation($"Dynamic listener for function {functionId} will be initially disabled.");
            }

            // schedule the next check
            var config = _dynamicListenerConfigs.Single(c => c.Listener == dynamicListener);
            config.Timer = new Timer(OnTimer, config, (int)status.NextInterval.TotalMilliseconds, Timeout.Infinite);

            return status;
        }

        private void OnListenerStopped(IListener listener)
        {
            var config = _dynamicListenerConfigs.Single(c => c.Listener == listener);
            config.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void DisposeListener(string functionId, IListener listener)
        {
            _statusProvider.DisposeListener(functionId, listener);
        }

        private async void OnTimer(object state)
        {
            DynamicListenerConfig dynamicListenerConfig = (DynamicListenerConfig)state;
            var dynamicListener = dynamicListenerConfig.Listener;

            if (dynamicListener.IsStopped)
            {
                // once a listener is stopped (because the host has shut down has been put in drain mode)
                // we don't want to restart it, and we want to stop monitoring it
                dynamicListenerConfig.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            // get the current status
            string functionId = dynamicListener.FunctionId;
            var status = await _statusProvider.GetStatusAsync(dynamicListener.FunctionId);
            
            if (status.IsEnabled && dynamicListener.IsStarted && !dynamicListener.IsRunning)
            {
                _logger.LogInformation($"Restarting dynamic listener for function {functionId}");

                // listener isn't currently running because either it started in a disabled state
                // or ve've we've stopped it previously, so we need to restart it
                await dynamicListener.RestartAsync(CancellationToken.None);
            }
            else if (!status.IsEnabled && dynamicListener.IsRunning)
            {
                _logger.LogInformation($"Stopping dynamic listener for function {functionId}. Next status check in {status.NextInterval.TotalMilliseconds}ms.");

                // we need to pause the listener
                await dynamicListener.PauseAsync(CancellationToken.None);
            }

            // set the next interval
            SetTimerInterval(dynamicListenerConfig.Timer, (int)status.NextInterval.TotalMilliseconds);
        }

        private void SetTimerInterval(Timer timer, int dueTime)
        {
            if (!_disposed)
            {
                if (timer != null)
                {
                    try
                    {
                        timer.Change(dueTime, Timeout.Infinite);
                    }
                    catch (ObjectDisposedException)
                    {
                        // might race with dispose
                    }
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var config in _dynamicListenerConfigs)
                    {
                        config.Timer?.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private class DynamicListener : IListener
        {
            private readonly IFunctionDefinition _functionDefinition;
            private readonly IListener _initialListener;
            private readonly DynamicListenerManager _dynamicListenerManager;
            private IListener _activeListener;
            private bool _started;
            private bool _stopped;
            private bool _isRunning;

            public bool IsRunning => _isRunning;

            public bool IsStarted => _started;

            public bool IsStopped => _stopped && _started;

            public DynamicListener(IListener listener, IFunctionDefinition functionDefinition, DynamicListenerManager dynamicListenerManager)
            {
                _initialListener = _activeListener = listener;
                _functionDefinition = functionDefinition;
                _dynamicListenerManager = dynamicListenerManager;
            }

            public string FunctionId => _functionDefinition.Descriptor.Id;

            public IListenerFactory ListenerFactory => _functionDefinition.ListenerFactory;

            public void Cancel()
            {
                _activeListener.Cancel();
            }

            public void Dispose()
            {
                _activeListener.Dispose();
            }

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                // Once the host starts the listener, we need to notify the manager
                // and start tracking the listener's status. We don't want to start any
                // monitoring before this.
                var status = await _dynamicListenerManager.OnListenerStarted(this);

                if (status.IsEnabled)
                {
                    await _activeListener.StartAsync(cancellationToken);
                    _isRunning = true;
                }

                _started = true;
            }

            public async Task StopAsync(CancellationToken cancellationToken)
            {
                // when the host is stopping listeners (e.g. as part of host shutdown)
                // we want to stop monitoring.
                _dynamicListenerManager.OnListenerStopped(this);

                await _activeListener?.StopAsync(cancellationToken);

                _stopped = true;
                _isRunning = false;
            }

            public async Task RestartAsync(CancellationToken cancellationToken)
            {
                if (_activeListener == null)
                {
                    // If we're restarting, we've previously stopped the active listener.
                    // We need to create and start a new listener.
                    _activeListener = await ListenerFactory.CreateAsync(CancellationToken.None);
                }
                
                await _activeListener.StartAsync(CancellationToken.None);

                _isRunning = true;
            }

            public Task PauseAsync(CancellationToken cancellationToken)
            {
                // we need to stop the active listener and dispose it
                _ = Orphan(_activeListener);

                _activeListener = null;
                _isRunning = false;

                return Task.CompletedTask;
            }

            private async Task Orphan(IListener listener)
            {
                await listener.StopAsync(CancellationToken.None);

                if (!object.ReferenceEquals(listener, _initialListener))
                {
                    // For any listeners we've created, we need to dispose them
                    // ourselves.
                    // The initial listener is owned by the host and will be disposed
                    // externally.
                    _dynamicListenerManager.DisposeListener(FunctionId, listener);
                }
            }
        }

        private class DynamicListenerConfig
        {
            public DynamicListener Listener { get; set; }
            
            public Timer Timer { get; set; }
        }
    }
}
