//-----------------------------------------------------------------------------
// FILE:        AspNetFixture.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2024 by NEONFORGE LLC.  All rights reserved.
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Neon.Common;
using Neon.Data;
using Neon.Diagnostics;
using Neon.Net;

using Xunit.Abstractions;

namespace Neon.Xunit
{
    /// <summary>
    /// Fixture for testing ASP.NET Core based websites and services.
    /// </summary>
    public class AspNetFixture : TestFixture
    {
        private Action<IWebHostBuilder> hostConfigurator;

        /// <summary>
        /// Constructs the fixture.
        /// </summary>
        public AspNetFixture()
        {
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~AspNetFixture()
        {
            Dispose(false);
        }

        /// <summary>
        /// Returns a <see cref="JsonClient"/> suitable for querying the service.
        /// </summary>
        public JsonClient JsonClient { get; private set; }

        /// <summary>
        /// Returns an <see cref="HttpClient"/> suitable for querying the service.
        /// </summary>
        public HttpClient HttpClient => JsonClient?.HttpClient;

        /// <summary>
        /// Returns the base URI for the running service.
        /// </summary>
        public Uri BaseAddress => JsonClient?.BaseAddress;

        /// <summary>
        /// Returns the service's <see cref="IWebHost"/>.
        /// </summary>
        public IWebHost WebHost { get; private set; }

        /// <summary>
        /// <para>
        /// Starts the ASP.NET service using the default controller factory.
        /// </para>
        /// <note>
        /// You'll need to call <see cref="StartAsComposed{TStartup}(Action{IWebHostBuilder}, int)"/>
        /// instead when this fixture is being added to a <see cref="ComposedFixture"/>.
        /// </note>
        /// </summary>
        /// <typeparam name="TStartup">The startup class for the service.</typeparam>
        /// <param name="hostConfigurator">Optional action providing for customization of the hosting environment.</param>
        /// <param name="port">The port where the server will listen or zero to allow the operating system to select a free port.</param>
        public void Start<TStartup>(Action<IWebHostBuilder> hostConfigurator = null, int port = 0)
            where TStartup : class
        {
            base.CheckDisposed();

            base.Start(
                () =>
                {
                    StartAsComposed<TStartup>(hostConfigurator, port);
                });
        }

        /// <summary>
        /// Used to start the fixture within a <see cref="ComposedFixture"/>.
        /// </summary>
        /// <typeparam name="TStartup">The startup class for the service.</typeparam>
        /// <param name="hostConfigurator">Optional action providing for customization of the hosting environment.</param>
        /// <param name="port">The port where the server will listen or zero to allow the operating system to select a free port.</param>
        public void StartAsComposed<TStartup>(Action<IWebHostBuilder> hostConfigurator = null, int port = 0)
            where TStartup : class
        {
            this.hostConfigurator = hostConfigurator;

            base.CheckWithinAction();

            if (IsRunning)
            {
                return;
            }

            StartServer<TStartup>(port);

            // Get the address where the server is listening and create the client.

            JsonClient = new JsonClient()
            {
                BaseAddress = new Uri(WebHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.OfType<string>().FirstOrDefault())
            };

            IsRunning = true;
        }

        /// <summary>
        /// Starts the service using the default controller factory.
        /// </summary>
        /// <param name="port">The port where the server will listen.</param>
        private void StartServer<TStartup>(int port)
            where TStartup : class
        {
            Covenant.Requires<ArgumentException>(port == 0 || NetHelper.IsValidPort(port), nameof(port));

            var app = new WebHostBuilder()
                .UseStartup<TStartup>()
                .UseKestrel(
                    options =>
                    {
                        options.Listen(IPAddress.Loopback, port);
                    });

            hostConfigurator?.Invoke(app);
            WebHost = app.Build();
            WebHost.Start();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!base.IsDisposed)
                {
                    Reset();
                }

                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Restarts the web service.
        /// </summary>
        /// <typeparam name="TStartup">Specifies the web service startup class.</typeparam>
        public void Restart<TStartup>()
            where TStartup : class
        {
            Covenant.Requires<InvalidOperationException>(IsRunning);

            WebHost.StopAsync().WaitWithoutAggregate();
            StartServer<TStartup>(BaseAddress.Port);
        }

        /// <inheritdoc/>
        public override void Reset()
        {
            if (!IsDisposed)
            {
                JsonClient?.Dispose();
                WebHost?.StopAsync().WaitWithoutAggregate();

                JsonClient = null;
                WebHost    = null;
            }
        }
    }
}
