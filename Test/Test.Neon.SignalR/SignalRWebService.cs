//-----------------------------------------------------------------------------
// FILE:        WebService.cs
// CONTRIBUTOR: Marcus Bowyer
// COPYRIGHT:   Copyright � 2005-2024 by NEONFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

using NATS.Client.Core;
using NATS.Client.Hosting;

using Neon.Diagnostics;
using Neon.Service;
using Neon.SignalR;

namespace Test.Neon.SignalR
{
    public class Startup
    {
        private WebService      service;
        private IConfiguration  configuration;
        public Startup(IConfiguration configuration, WebService service)
        {
            this.configuration = configuration;
            this.service       = service;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var natsServerUri     = service.GetEnvironmentVariable("NATS_URI", string.Empty);

            var logger     = TelemetryHub.CreateLogger("neon-signalr");

            services
                .AddSingleton<IUserIdProvider, UserNameIdProvider>()
                .AddSingleton<ILogger>(logger)
                .AddSignalR()
                .AddNats(configureOpts: opts => new NatsOpts()
                {
                    Url = natsServerUri
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<EchoHub>("/echo");
            });
        }

        private class UserNameIdProvider : IUserIdProvider
        {
            public string GetUserId(HubConnectionContext connection)
            {
                // This is an AWFUL way to authenticate users! We're just using it for test purposes.

                var userNameHeader = connection.GetHttpContext().Request.Headers["UserName"];

                if (!StringValues.IsNullOrEmpty(userNameHeader))
                {
                    return userNameHeader;
                }

                return null;
            }
        }
    }

    /// <summary>
    /// Implements the SignalR web service.
    /// </summary>
    public class WebService : NeonService
    {
        private IWebHost    webHost;
        public string       NatsServerUri;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The service name.</param>
        /// <param name="serviceMap">Optionally specifies the service map.</param>
        public WebService(string name, ServiceMap serviceMap = null)
            : base(name, options: new NeonServiceOptions() { ServiceMap = serviceMap })
        {
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Dispose web host if it's still running.

            if (webHost != null)
            {
                webHost.Dispose();
                webHost = null;
            }
        }

        /// <inheritdoc/>
        protected async override Task<int> OnRunAsync()
        {
            // Load the configuration environment variables, exiting with a
            // non-zero exit code if they don't exist.

            NatsServerUri = Environment.Get("NATS_URI", string.Empty);

            if (string.IsNullOrEmpty(NatsServerUri))
            {
                Logger.LogCriticalEx("Invalid configuration: [NATS_URI] environment variable is missing or invalid.");
                Exit(1);
            }

            // Start the HTTP service.

            var endpoint = Description.Endpoints.Default;

            webHost = new WebHostBuilder()
                .UseStartup<Startup>()
                .UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Any, endpoint.Port);
                    })
                .ConfigureServices(services => services.AddSingleton(typeof(WebService), this))
                .Build();

            webHost.Start();

            // Indicate that the service is running.

            await StartedAsync();

            // Handle termination gracefully.

            await Terminator.StopEvent.WaitAsync();
            Terminator.ReadyToExit();

            return 0;
        }
    }
}
