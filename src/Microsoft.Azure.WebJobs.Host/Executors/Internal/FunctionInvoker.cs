﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Executors.Internal
{
    /// <summary>
    /// This is an internal API that supports the WebJobs infrastructure and not subject to the same compatibility
    /// standards as public APIs. It may be changed or removed without notice in any release. You should only use it
    /// directly in your code with extreme caution and knowing that doing so can result in application failures when
    /// updating to a new WebJobs release.
    /// </summary>
    public enum FunctionScope
    {
        /// <summary>
        /// In a system-owned scope.
        /// </summary>
        System = 0,

        /// <summary>
        /// In a user-owned scope.
        /// </summary>
        User = 1,
    }

    /// <summary>
    /// This is an internal API that supports the WebJobs infrastructure and not subject to the same compatibility
    /// standards as public APIs. It may be changed or removed without notice in any release. You should only use it
    /// directly in your code with extreme caution and knowing that doing so can result in application failures when
    /// updating to a new WebJobs release.
    /// </summary>
    [Obsolete("Not for public consumption.")]
    public static class FunctionInvoker
    {
        private static readonly AsyncLocal<FunctionScope> Local = new AsyncLocal<FunctionScope>()
        {
            Value = FunctionScope.System
        };

        /// <summary>
        /// Gets the function scope for the current execution context.
        /// </summary>
        public static FunctionScope CurrentScope => Local.Value;

        /// <summary>
        /// Enters a user scope in the current execution context.
        /// </summary>
        /// <returns>A disposable that will revert to previous section type on disposal.</returns>
        public static Scope BeginUserScope(
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Source.Log.BeginScope(FunctionScope.User, CurrentScope, memberName, filePath, lineNumber);
            return new Scope(FunctionScope.User);
        }

        /// <summary>
        /// Enters a system scope in the current execution context.
        /// </summary>
        /// <returns>A disposable that will revert to previous section type on disposal.</returns>
        public static Scope BeginSystemScope(
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            Source.Log.BeginScope(FunctionScope.System, CurrentScope, memberName, filePath, lineNumber);
            return new Scope(FunctionScope.System);
        }

        /// <summary>
        /// A scope that reverts to the previous scope value on disposal.
        /// </summary>
        public readonly struct Scope : IDisposable
        {
            private readonly FunctionScope _original;

            public Scope(FunctionScope target)
            {
                _original = Local.Value;
                Local.Value = target;
            }

            public void Dispose()
            {
                Source.Log.EndScope(_original, CurrentScope);
                Local.Value = _original;
            }
        }

        [EventSource(Name = "Microsoft-Azure-WebJobs-Executors-Internal-FunctionInvoker")]
        sealed class Source : EventSource
        {
            public static Source Log { get; } = new Source();

            [Event(1)]
            public void BeginScope(FunctionScope newScope, FunctionScope currentScope, string memberName, string filePath, int lineNumber)
            {
                WriteEvent(1, newScope, currentScope, memberName, filePath, lineNumber);
            }

            [Event(2)]
            public void EndScope(FunctionScope newScope, FunctionScope currentScope)
            {
                WriteEvent(1, newScope, currentScope);
            }
        }
    }
}
