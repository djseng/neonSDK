//-----------------------------------------------------------------------------
// FILE:        Test_Metrics.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Service;
using Neon.Tasks;
using Neon.Xunit;

using Prometheus;
using Prometheus.DotNetRuntime;

using Xunit;

namespace TestNeonService
{
    [Trait(TestTrait.Category, TestArea.NeonService)]
    [Collection(TestCollection.NonParallel)]
    [CollectionDefinition(TestCollection.NonParallel, DisableParallelization = true)]
    public class Test_Metrics
    {
        //---------------------------------------------------------------------
        // Service implementation

        public class TestService : NeonService
        {
            //-----------------------------------------------------------------
            // Static members

            private static ServiceMap CreateServiceMap()
            {
                var serviceMap = new ServiceMap();

                var description = new ServiceDescription()
                {
                    Name = "test-service"
                };

                serviceMap.Add(description);

                return serviceMap;
            }

            //-----------------------------------------------------------------
            // Instance members

            private readonly Counter TestCounter = Metrics.CreateCounter("test_counter", "my test counter");

            public TestService()
                : base("test-service", version: "1.0", options: new NeonServiceOptions() { ServiceMap = CreateServiceMap() })
            {
            }

            /// <summary>
            /// Used to signal to the test case that the service has done its
            /// thing and is ready to exit.
            /// </summary>
            public AsyncAutoResetEvent ReadyToExitEvent { get; set; } = new AsyncAutoResetEvent();

            /// <summary>
            /// Used by the test case to signal that the service should exit.
            /// </summary>
            public AsyncAutoResetEvent CanExitEvent { get; set; } = new AsyncAutoResetEvent();

            /// <summary>
            /// Implements the test service.
            /// </summary>
            /// <returns>The service exit code.</returns>
            protected override async Task<int> OnRunAsync()
            {
                var logger = TelemetryHub.CreateLogger(this.Name);

                await StartedAsync();

                logger.LogDebugEx("debug event");
                logger.LogInformationEx("info event");
                logger.LogWarningEx("warn event");
                logger.LogErrorEx("error event");
                logger.LogCriticalEx("critical event");

                // Increment the test counter.

                TestCounter.Inc();

                // Signal that the the service is running.

                await StartedAsync();

                // Signal to the test case that the service has done its thing
                // and is ready to exit.

                ReadyToExitEvent.Set();

                // Wait for the test to signal that the service can exit.

                await CanExitEvent.WaitAsync();

                return await Task.FromResult(0);
            }
        }

        public class TestServiceWithRuntimeMetrics : NeonService
        {
            //-----------------------------------------------------------------
            // Static members

            private static ServiceMap CreateServiceMap()
            {
                var serviceMap = new ServiceMap();

                var description = new ServiceDescription()
                {
                    Name = "test-service"
                };

                serviceMap.Add(description);

                return serviceMap;
            }

            //-----------------------------------------------------------------
            // Instance members

            private readonly Counter TestCounter = Metrics.CreateCounter("test_counter", "my test counter");

            public TestServiceWithRuntimeMetrics()
                : base("test-service", options: new NeonServiceOptions() { ServiceMap = CreateServiceMap() })
            {
            }

            /// <summary>
            /// Used to signal to the test case that the service has done its
            /// thing and is ready to exit.
            /// </summary>
            public AsyncAutoResetEvent ReadyToExitEvent { get; set; } = new AsyncAutoResetEvent();

            /// <summary>
            /// Used by the test case to signal that the service should exit.
            /// </summary>
            public AsyncAutoResetEvent CanExitEvent { get; set; } = new AsyncAutoResetEvent();

            /// <summary>
            /// Implements the test service.
            /// </summary>
            /// <returns>The service exit code.</returns>
            protected override async Task<int> OnRunAsync()
            {
                // Increment the test counter.

                TestCounter.Inc();
                
                var logger = TelemetryHub.CreateLogger(this.Name);

                logger.LogDebugEx("debug event");
                logger.LogInformationEx("info event");
                logger.LogWarningEx("warn event");
                logger.LogErrorEx("error event");
                logger.LogCriticalEx("critical event");

                // Wait for the test to signal that the service can exit.

                await CanExitEvent.WaitAsync();

                // Signal to the test case that the service has done its thing
                // and is ready to exit.

                ReadyToExitEvent.Set();

                return await Task.FromResult(0);
            }
        }

        //---------------------------------------------------------------------
        // Tests

        /// <summary>
        /// Parses scraped metrics into a dictionary of metric values keyed by name.
        /// </summary>
        /// <param name="scrapedMetrics">The scraped Prometheus metrics.</param>
        /// <returns>The metrics dictionary.</returns>
        private Dictionary<string, double> ParseMetrics(string scrapedMetrics)
        {
            var metrics = new Dictionary<string, double>();

            using (var reader = new StringReader(scrapedMetrics))
            {
                foreach (var line in reader.Lines())
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        continue;   // Ignore comments or blank lines
                    }

                    try
                    {
                        var fields = line.Split(' ', 2);
                        var name   = fields[0];
                        var value  = double.Parse(fields[1]);

                        metrics[name] = value;
                    }
                    catch
                    {
                        // Ignore non-numeric metrics.
                    }
                }
            }

            return metrics;
        }

        [Fact]
        public async Task Disabled()
        {
            // Verify that a service with disabled metrics (the default) does not
            // expose a metrics endpoint.

            var service = new TestService();

            Assert.Equal(MetricsMode.Disabled, service.MetricsOptions.Mode);

            var runTask = service.RunAsync();

            // Wait for the test service to be ready.

            await service.WaitUntilStarted();

            // Perform the tests.

            using (var httpClient = new HttpClient())
            {
                // We're expecting the metrics scrape request to fail since metrics are disabled.

                await Assert.ThrowsAsync<HttpRequestException>(async () => await httpClient.GetAsync($"http://127.0.0.1:{NetworkPorts.PrometheusMetrics}"));
                await Assert.ThrowsAsync<HttpRequestException>(async () => await httpClient.GetAsync($"http://127.0.0.1:{NetworkPorts.PrometheusMetrics}/metrics/"));
            }

            // Tell the service it can exit.

            service.CanExitEvent.Set();
            await runTask;
        }

        [Fact]
        public async Task Scrape_Default()
        {
            // Verify that a service with default metric settings actually exposes metrics.

            var service = new TestService();

            service.MetricsOptions.Mode = MetricsMode.Scrape;

            var runTask = service.RunAsync();

            // Wait for the test service to be ready.

            await service.WaitUntilStarted();

            try
            {
                // Perform the tests.

                using (var httpClient = new HttpClient())
                {
                    var scrapedMetrics = await httpClient.GetStringAsync($"http://127.0.0.1:{NetworkPorts.PrometheusMetrics}/metrics/");
                    var metrics        = ParseMetrics(scrapedMetrics);

                    // Verify the test counter.

                    Assert.True(metrics["test_counter"] > 0);

                    // Verify Neon logging counters.

                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Fatal""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Debug""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Error""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Info""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""None""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Trace""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Warn""}"));
                }
            }
            finally
            {
                // Tell the service it can exit.

                service.CanExitEvent.Set();
                await runTask;
            }
        }

        [Fact]
        public async Task Scrape_WithPort()
        {
            // Verify that a service with a specified port and default path actually exposes metrics.

            var service     = new TestService();
            var metricsPort = NetHelper.GetUnusedTcpPort(IPAddress.Loopback);

            service.MetricsOptions.Mode = MetricsMode.Scrape;
            service.MetricsOptions.Port = metricsPort;

            var runTask = service.RunAsync();

            // Wait for the test service to be ready.

            await service.WaitUntilStarted();

            try
            {
                // Perform the tests.

                using (var httpClient = new HttpClient())
                {
                    var scrapedMetrics = await httpClient.GetStringAsync($"http://127.0.0.1:{metricsPort}/metrics/");
                    var metrics        = ParseMetrics(scrapedMetrics);

                    // Verify the test counter.

                    Assert.True(metrics["test_counter"] > 0);

                    // Verify Neon logging counters.

                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Fatal""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Debug""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Error""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Info""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""None""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Trace""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Warn""}"));
                }
            }
            finally
            {
                // Tell the service it can exit.

                service.CanExitEvent.Set();
                await runTask;
            }
        }

        [Fact]
        public async Task Scrape_WithPath()
        {
            // Verify that a service with the default port and a specific path actually exposes metrics.

            var service     = new TestService();
            var metricsPath = "foo/";

            service.MetricsOptions.Mode = MetricsMode.Scrape;
            service.MetricsOptions.Path = metricsPath;

            var runTask = service.RunAsync();

            // Wait for the test service to be ready.

            await service.WaitUntilStarted();

            try
            {
                // Perform the tests.

                using (var httpClient = new HttpClient())
                {
                    var scrapedMetrics = await httpClient.GetStringAsync($"http://127.0.0.1:{NetworkPorts.PrometheusMetrics}/{metricsPath}");
                    var metrics        = ParseMetrics(scrapedMetrics);

                    // Verify the test counter.

                    Assert.True(metrics["test_counter"] > 0);

                    // Verify Neon logging counters.

                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Fatal""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Debug""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Error""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Info""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""None""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Trace""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Warn""}"));
                }
            }
            finally
            {
                // Tell the service it can exit.

                service.CanExitEvent.Set();
                await runTask;
            }
        }

        [Fact]
        public async Task Scrape_WithPortAndPath()
        {
            // Verify that a service with the specific port and path actually exposes metrics.

            var service     = new TestService();
            var metricPort  = NetHelper.GetUnusedTcpPort(IPAddress.Loopback);
            var metricsPath = "foo/";

            service.MetricsOptions.Mode = MetricsMode.Scrape;
            service.MetricsOptions.Port = metricPort;
            service.MetricsOptions.Path = metricsPath;

            var runTask = service.RunAsync();

            // Wait for the test service to be ready.

            await service.WaitUntilStarted();

            try
            {
                // Perform the tests.

                using (var httpClient = new HttpClient())
                {
                    var scrapedMetrics = await httpClient.GetStringAsync($"http://127.0.0.1:{metricPort}/{metricsPath}");
                    var metrics        = ParseMetrics(scrapedMetrics);

                    // Verify the test counter.

                    Assert.True(metrics["test_counter"] > 0);

                    // Verify Neon logging counters.

                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Fatal""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Debug""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Error""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Info""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""None""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Trace""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Warn""}"));
                }
            }
            finally
            {
                // Tell the service it can exit.

                service.CanExitEvent.Set();
                await runTask;
            }
        }

        [Fact]
        public async Task Push()
        {
            // Verify that a service can be configured to push metrics to a
            // simulated Push Gateway.

            var service     = new TestService();
            var gatewayPort = NetHelper.GetUnusedTcpPort(IPAddress.Loopback);

            service.MetricsOptions.Mode    = MetricsMode.Push;
            service.MetricsOptions.PushUrl = $"http://127.0.0.1:{gatewayPort}/";

            // Start an emulated Pushgateway to receive the metrics.

            string                      rawMetrics;
            Dictionary<string, double>  metrics = null;

            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(service.MetricsOptions.PushUrl);
                listener.Start();

                var runTask = service.RunAsync();

                // Wait for the test service to be ready.

                await service.WaitUntilStarted();

                // Loop for up to 2 minutes until we receive metrics with a non-zero [test_counter]

                var stopwatch = new Stopwatch();

                stopwatch.Start();

                _ = Task.Run(
                    async () =>
                    {
                        while (true)
                        {
                            if (stopwatch.Elapsed >= TimeSpan.FromMinutes(2))
                            {
                                throw new TimeoutException("Timeout waiting for non-zero [test_counter].");
                            }

                            // $note(jefflill): This call will hang if the service doesn't push anything.

                            var context = await listener.GetContextAsync();
                            var request = context.Request;

                            Assert.Equal("POST", request.HttpMethod);

                            rawMetrics = Encoding.UTF8.GetString(await request.InputStream.ReadToEndAsync());

                            context.Response.StatusCode = 200;
                            context.Response.OutputStream.Close();

                            var receivedMetrics = ParseMetrics(rawMetrics);

                            if (receivedMetrics["test_counter"] > 0 && metrics == null)
                            {
                                metrics = receivedMetrics;
                            }
                        }
                    });

                // Tell the service it can exit.

                service.CanExitEvent.Set();
                await runTask;

                listener.Close();
            }

            // Verify the test counter.

            Assert.True(metrics["test_counter"] > 0);

            // Verify Neon logging counters.

            Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Fatal""}"));
            Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Debug""}"));
            Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Error""}"));
            Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Info""}"));
            Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""None""}"));
            Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Trace""}"));
            Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Warn""}"));
        }

        [Fact]
        public async Task RuntimeMetrics()
        {
            // Verify that we can also expose .NET Runtime exposes metrics via the [GetCollector()] callback.

            var service     = new TestService();
            var metricPort  = NetHelper.GetUnusedTcpPort(IPAddress.Loopback);
            var metricsPath = "foo/";

            service.MetricsOptions.Mode         = MetricsMode.Scrape;
            service.MetricsOptions.Port         = metricPort;
            service.MetricsOptions.Path         = metricsPath;
            service.MetricsOptions.GetCollector = () => DotNetRuntimeStatsBuilder.Default().StartCollecting();

            var runTask = service.RunAsync();

            // Wait for the test service to be ready.

            await service.WaitUntilStarted();

            try
            {
                // Perform the tests.

                using (var httpClient = new HttpClient())
                {
                    var scrapedMetrics = await httpClient.GetStringAsync($"http://127.0.0.1:{metricPort}/{metricsPath}");
                    var metrics        = ParseMetrics(scrapedMetrics);

                    // Verify the test counter.

                    Assert.True(metrics["test_counter"] > 0);

                    // Verify Neon logging counters.

                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Fatal""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Debug""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Error""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Info""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""None""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Trace""}"));
                    Assert.True(metrics.ContainsKey(@"neonsdk_log_events_total{level=""Warn""}"));

                    // Verify some .NET Runtime metrics

                    Assert.True(metrics.ContainsKey("process_private_memory_bytes"));
                }
            }
            finally
            {
                // Tell the service it can exit.

                service.CanExitEvent.Set();
                await runTask;
            }
        }
    }
}
