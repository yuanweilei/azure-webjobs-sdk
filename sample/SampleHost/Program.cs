﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SampleHost
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = new HostBuilder()
                .UseEnvironment("Development")
                .ConfigureWebJobs(b =>
                {
                    b.AddAzureStorageCoreServices()
                    .AddAzureStorageQueues();
                })
                .ConfigureAppConfiguration(b =>
                {
                    // Adding command line as a configuration source
                    b.AddCommandLine(args);
                })
                .ConfigureLogging((context, b) =>
                {
                    b.SetMinimumLevel(LogLevel.Information);
                    b.AddConsole();

                    b.AddFilter("Azure.Core", LogLevel.Error);

                    // If this key exists in any config, use it to enable App Insights
                    string appInsightsKey = context.Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"];
                    if (!string.IsNullOrEmpty(appInsightsKey))
                    {
                        b.AddApplicationInsightsWebJobs(o => o.InstrumentationKey = appInsightsKey);
                    }
                })
                .ConfigureServices(services =>
                {
                    // add some sample services to demonstrate job class DI
                    services.AddSingleton<ISampleServiceA, SampleServiceA>();
                    services.AddSingleton<ISampleServiceB, SampleServiceB>();

                    services.AddDynamicListeners();
                })
                .UseConsoleLifetime();

            var host = builder.Build();
            using (host)
            {
                // verify the customer status provider is working
                var statusProvider = host.Services.GetService<IFunctionActivityStatusProvider>();
                var status = statusProvider.GetStatus();

                await host.RunAsync();
            }
        }
    }
}
