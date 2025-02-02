//-----------------------------------------------------------------------------
// FILE:        NeonService.cs
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
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DnsClient;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Neon.BuildInfo;
using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Tasks;
using Neon.Time;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Prometheus;

namespace Neon.Service
{
    /// <summary>
    /// Handy base class for application services.  This class handles process termination signals when
    /// running on Linux, OS/X, and similar environments and also provides some features to help you run
    /// unit tests on your service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Basing your service implementations on the <see cref="Service"/> class will
    /// make them easier to test via integration with the <b>ServiceFixture</b> from
    /// the <b>Neon.Xunit</b> library by providing some useful abstractions over 
    /// service configuration, startup and shutdown including a <see cref="ProcessTerminator"/>
    /// to handle termination signals from Linux or Kubernetes.
    /// </para>
    /// <para>
    /// This class is pretty easy to use.  Simply derive your service class from <see cref="NeonService"/>
    /// and implement the <see cref="OnRunAsync"/> method.  <see cref="OnRunAsync"/> will be called when 
    /// your service is started.  This is where you'll implement your service.  You should perform any
    /// initialization and then call <see cref="Started(NeonServiceStatus)"/> or <see cref="StartedAsync(NeonServiceStatus)"/>
    /// to indicate that the service is ready for business.
    /// </para>
    /// <note>
    /// We recommend that your service constructor be limited to configuring base service properties
    /// and that you perform the bulk of your service initialization in <see cref="OnRunAsync"/> before
    /// you call  <see cref="Started(NeonServiceStatus)"/> or <see cref="StartedAsync(NeonServiceStatus)"/>.
    /// Any logging performed in the constructor will be handled by a default console logger because the
    /// regular logger isn't initialized until <see cref="RunAsync(bool)"/> is called to start the service.
    /// We recommend that you avoid any logging from within your constructor.
    /// </note>
    /// <note>
    /// Note that calling <see cref="Started(NeonServiceStatus)"/> or <see cref="StartedAsync(NeonServiceStatus)"/>
    /// after your service has initialized is important because the <b>NeonServiceFixture</b> won't allow tests
    /// to proceed until the service indicates that it's ready.  This is necessary to avoid unit test race conditions.
    /// </note>
    /// <para>
    /// Note that your <see cref="OnRunAsync"/> method should generally not return until the 
    /// <see cref="Terminator"/> signals it to stop.  Alternatively, you can throw a <see cref="ProgramExitException"/>
    /// with an optional process exit code to proactively exit your service.
    /// </para>
    /// <note>
    /// All services should properly handle <see cref="Terminator"/> stop signals so services deployed as
    /// containers will stop promptly and cleanly (this also applies to services running in unit tests).  
    /// Your terminate handler method must return within a set period of time (30 seconds by default) 
    /// to avoid killed by by Docker or Kubernetes.  This is probably the trickiest thing you'll need to implement.
    /// For asynchronous service implementations, you consider passing the <see cref="ProcessTerminator.CancellationToken"/>
    /// to all async method calls.
    /// </note>
    /// <note>
    /// This class uses the <b>DEV_WORKSTATION</b> environment variable to determine whether
    /// the service is running in test mode or not.  This variable will typically be defined
    /// on developer workstations as well as CI/CD machines.  This variable must never be
    /// defined for production environments.  You can use the <see cref="InProduction"/>
    /// or <see cref="InDevelopment"/> properties to check this.
    /// </note>
    /// <para><b>CONFIGURATION</b></para>
    /// <para>
    /// Services are generally configured using environment variables and/or configuration
    /// files.  In production, environment variables will actually come from the environment
    /// after having been initialized by the container image or passed by Kubernetes when
    /// starting the service container.  Environment variables are retrieved by case sensitive
    /// name.
    /// </para>
    /// <para>
    /// Configuration files work the same way.  They are either present in the service 
    /// container image or mounted to the container as a secret or config file by Kubernetes. 
    /// Configuration files are specified by their path (case sensitive) within the
    /// running container.
    /// </para>
    /// <para>
    /// This class provides some abstractions for managing environment variables and 
    /// configuration files so that services running in production or as a unit test
    /// can configure themselves using the same code for both environments. 
    /// </para>
    /// <para>
    /// Services should use the <see cref="Environment"/> parser methods to retrieve important 
    /// variables rather obtaining these via <see cref="global::System.Environment.GetEnvironmentVariable(string)"/>.
    /// These methods handle type-safe parsing, validation and default values.
    /// </para>
    /// <para>
    /// In production, this simply returns values from the process environment variables.
    /// For tests, the environment variable can be returned from a local dictionary
    /// that was expicitly initialized by calls to <see cref="SetEnvironmentVariable(string, string)"/>.
    /// This local dictionary allows the testing of multiple services at the same
    /// time with each being presented their own environment variables.
    /// </para>
    /// <para>
    /// You may also use the <see cref="LoadEnvironmentVariableFile(string, Func{string, string})"/>
    /// methods to load environment variables from a text file (potentially encrypted via
    /// <see cref="NeonVault"/>).  This will typically be done only for unit tests.
    /// </para>
    /// <para>
    /// Configuration files work similarily.  You'll use <see cref="GetConfigFilePath(string)"/>
    /// to map a logical file path to a physical path.  The logical file path is typically
    /// specified as the path where the configuration file will be located in production.
    /// This can be any valid path with in a running production container and since we're
    /// currently Linux centric, will typically be a Linux file path like <c>/etc/MYSERVICE.yaml</c>
    /// or <c>/etc/MYSERVICE/config.yaml</c>.
    /// </para>
    /// <para>
    /// For production, <see cref="GetConfigFilePath(string)"/> will simply return the file
    /// path passed so that the configuration file located there will referenced.  For
    /// testing, <see cref="GetConfigFilePath(string)"/> will return the path specified by
    /// an earlier call to <see cref="SetConfigFilePath(string, string, Func{string, string})"/> or to a
    /// temporary file initialized by previous calls to <see cref="SetConfigFile(string, string, bool)"/>
    /// or <see cref="SetConfigFile(string, byte[])"/>.  This indirection provides a 
    /// consistent way to run services in production as well as in tests, including tests
    /// running multiple services simultaneously.
    /// </para>
    /// <para><b>DISPOSE IMPLEMENTATION</b></para>
    /// <para>
    /// All services, especially those that create unmanaged resources like ASP.NET services,
    /// sockets, NATS clients, HTTP clients, thread etc. should override and implement 
    /// <see cref="Dispose(bool)"/>  to ensure that any of these resources are proactively 
    /// disposed.  Your method should call the base class version of the method first before 
    /// disposing these resources.
    /// </para>
    /// <code language="C#">
    /// protected override Dispose(bool disposing)
    /// {
    ///     base.Dispose(disposing);
    ///     
    ///     if (appHost != null)
    ///     {
    ///         appHost.Dispose();
    ///         appHost = null;
    ///     }
    /// }
    /// </code>
    /// <para>
    /// The <b>disposing</b> parameter is passed as <c>true</c> when the base <see cref="NeonService.Dispose()"/>
    /// method was called or <c>false</c> if the garbage collector is finalizing the instance
    /// before discarding it.  The difference is subtle and most services can safely ignore
    /// this parameter (other than passing it through to the base <see cref="Dispose(bool)"/>
    /// method).
    /// </para>
    /// <para>
    /// In the example above, the service implements an ASP.NET web service where <c>appHost</c>
    /// was initialized as the <c>IWebHost</c> actually implementing the web service.  The code
    /// ensures that the <c>appHost</c> isn't already disposed before disposing it.  This will
    /// stop the web service and release the underlying listening socket.  You'll want to do
    /// something like this for any other unmanaged resources your service might hold.
    /// </para>
    /// <note>
    /// <para>
    /// It's very important that you take care to dispose things like running web services and
    /// listening sockets within your <see cref="Dispose(bool)"/> method.  You also need to
    /// ensure that any threads you've created are terminated.  This means that you'll need
    /// a way to signal threads to exit and then wait for them to actually exit.
    /// </para>
    /// <para>
    /// This is important when testing your services with a unit testing framework like
    /// Xunit because frameworks like this run all tests within the same Test Runner
    /// process and leaving something like a listening socket open on a port (say port 80)
    /// may prevent a subsequent test from running successfully due to it not being able 
    /// to open its listening socket on port 80. 
    /// </para>
    /// </note>
    /// <para><b>LOGGING</b></para>
    /// <para>
    /// The <see cref="NeonService"/> constructor initializes the OpenTelemetry logging pipeline
    /// by default, initializing the <see cref="Logger"/> property as well as initializing 
    /// <see cref="TelemetryHub.LoggerFactory"/> and adding the logger factor and logger to
    /// <see cref="NeonHelper.ServiceContainer"/>, making them available to NeonSDK libraries
    /// so they may emit logs.  The logger created by <see cref="NeonService"/> in this case
    /// will be configured to write JSON formatted logs to the process standard output stream.
    /// </para>
    /// <para>
    /// <see cref="NeonService"/> parses the current log level from the <b>LOG_LEVEL</b> 
    /// environment variable, defaulting to <see cref="LogLevel.Information"/> when this
    /// variable is present or cannot be parsed.  The possible log levels are: <b>CRITICAL</b>,
    /// <b>ERROR</b>, <b>WARNING</b>, <b>WARN</b>, <b>INFORMATION</b>, <b>INFO</b>, <b>DEBUG</b>
    /// or <b>TRACE</b> (case insensitive).
    /// </para>
    /// <para>
    /// If you need to customize the OpenTelemetry logging pipeline, you may accomplish this
    /// two ways:
    /// </para>
    /// <list type="bullet">
    /// <item>
    ///     <para>
    ///     The easist way to tweak the logging configuration is to implement the protected
    ///     <see cref="OnLoggerConfg(OpenTelemetryLoggerOptions)"/> method.  This is called 
    ///     by the <see cref="NeonService"/> constructor before any built-in logging configuration
    ///     has been done, giving your code the chance to modify the options.
    ///     </para>
    ///     <para>
    ///     Your <see cref="OnLoggerConfg(OpenTelemetryLoggerOptions)"/> method can then return
    ///     <c>false</c> when you want <see cref="NeonService"/> to continue with it's built-in
    ///     configuration or <c>true</c> if the service ill use your configuration unchanged.
    ///     </para>
    /// </item>
    /// <item>
    ///     <para>
    ///     You can configuring the pipeline before intantiating your service, by creating an
    ///     <see cref="ILoggerFactory"/>, doing any configuration as desired, setting the
    ///     <see cref="NeonServiceOptions.LoggerFactory"/> to this factory and then passing
    ///     the options to the <see cref="NeonService"/> constructor.
    ///     </para>
    ///     <para>
    ///     <see cref="OnLoggerConfg(OpenTelemetryLoggerOptions)"/> isn't called when a
    ///     <see cref="NeonServiceOptions.LoggerFactory"/> instance is passed and the service 
    ///     just use your configuration.
    ///     </para>
    /// </item>
    /// </list>
    /// <para>
    /// Your service can use the base <see cref="NeonService"/>;s <see cref="Logger"/> property or 
    /// call <see cref="TelemetryHub.CreateLogger{T}(LogAttributes, bool, bool)"/> or 
    /// <see cref="TelemetryHub.CreateLogger(string, LogAttributes, bool, bool)"/> to create loggers 
    /// you can use for logging events.  We then recommend that you add a <c>using Neon.Diagnostics;</c> 
    /// statement to your code and then use our <see cref="Neon.Diagnostics.LoggerExtensions"/> like:
    /// </para>
    /// <code language="C#">
    /// using System;
    /// using System.Diagnostics;
    /// 
    /// ...
    /// 
    /// var logger   = TelemetryHub.CreateLogger("my-logger");
    /// var username = "Sally";
    /// 
    /// logger.LogDebugEx("this is a test");
    /// logger.LogInformationEx(() => $"user: {username}");
    /// </code>
    /// <para><b>TRACING</b></para>
    /// <para>
    /// <see cref="NeonService"/> also has limited support for configuring OpenTelemetry tracing
    /// for targeting an OpenTelemetry Collector by URI.  This is controlled using two environment
    /// variables:
    /// </para>
    /// <note>
    /// <see cref="NeonService"/> only supports exporting traces to an OpenTelemetry Collector via
    /// and OTEL exporter, but you can always configure a custom OpenTelemetry pipeline before
    /// starting your <see cref="NeonService"/> when you need to send traces elsewhere.
    /// </note>
    /// <list type="table">
    /// <item>
    ///     <term><b>TRACE_COLLECTOR_URI</b></term>
    ///     <description>
    ///     <para>
    ///     When present and <see cref="NeonServiceOptions.TracerProvider"/> is <c>null</c>, then
    ///     this specifies the URI for the OpenTelemetry Collector where the traces will be sent.
    ///     If this environment variable is not present and the service is running in Kubernetes,
    ///     then we're going to assume that you're running in a NeonKUBE cluster and <see cref="NeonService"/>
    ///     will default to sending traces to the <see cref="NeonHelper.NeonKubeOtelCollectorUri"/>
    ///     (<b>http://grafana-agent-node.neon-monitor.svc.cluster.local</b>) service endpoint deployed to the same Kubernetes namespace.
    ///     </para>
    ///     <note>
    ///     This is ignored when <see cref="NeonServiceOptions.TracerProvider"/> is not <c>null</c>,
    ///     which indicates that a custom OpenTelemetry tracing pipeline has already been configured
    ///     or for services targeting the .NET Framework or .NET Core frameworks earlier than NET6.0.
    ///     </note>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>TRACE_LOG_LEVEL</b></term>
    ///     <description>
    ///     <para>
    ///     This optional environment variable configures the log level for events that will be
    ///     also recorded as trace events.  You can also specify <b>TRACE_LOG_LEVEL=None</b> to
    ///     disable automatic trace event submission completely.
    ///     </para>
    ///     <para>
    ///     This defaults to the <c>LOG_LEVEL</c> environment variable when present or <see cref="LogLevel.Information"/>.
    ///     </para>
    ///     <note>
    ///     This feature is disabled when <see cref="NeonServiceOptions.LoggerFactory"/> has been
    ///     set, indicating that the OpenTelemetry logging pipeline has already been customized.
    ///     </note>
    ///     <note>
    ///     <para>
    ///     Only events logged at the current <b>LOG_LEVEL</b> or greater will ever be candidates
    ///     for being recorded as trace events; <b>TRACE_LOG_LEVEL</b> is intended to be used for
    ///     adding an additional constrain on trace events over and above the <b>LOG_LEVEL</b>.
    ///     </para>
    ///     <para>
    ///     For example, by setting <c>LOG_LEVEL=Information</c> and <c>TRACE_LOG_LEVEL=Warning</c>,
    ///     you'd be logging all events for <b>Information></b> and above, but only submit log events with
    ///     for <b>Warning</b> or above as trace events.
    ///     </para>
    ///     <para>
    ///     Setting <c>TRACE_LOG_LEVEL=Debug</c> in this example won't actually record debug events
    ///     as trace events, because these events won't be logged due to <c>LOG_LEVEL=Information</c>.
    ///     </para>
    ///     </note>
    ///     </description>
    /// </item>
    /// <para>
    /// <see cref="ActivitySource"/> and <see cref="TelemetryHub.ActivitySource"/> will be set by
    /// <see cref="NeonService"/> to the <see cref="System.Diagnostics.ActivitySource"/> you can
    /// use to record traces like:
    /// </para>
    /// <code language="C#">
    /// using System;
    /// using System.Diagnostics;
    /// using Neon.Diagnostics;
    /// 
    /// ...
    /// 
    /// using (ActivitySource.CreateActivity("my-activity", ActivityKind.Internal))
    /// {
    ///     // Perform your operation.
    /// }
    /// </code>
    /// <para>
    /// By default, <see cref="NeonService"/> <b>DOES NOT</b> any and tracing instrumentation.  You
    /// can instrument by implementing the protected <see cref="OnTracerConfig(TracerProviderBuilder)"/>
    /// method.  Instrument <see cref="HttpClient"/> and <see cref="WebRequest"/> via the 
    /// <b>OpenTelemetry.Instrumentation.Http</b> package and ASPNETCORE via the <b>OpenTelemetry.Instrumentation.AspNetCore</b>
    /// package for websites and REST services.  Other trace instrumentation is available.
    /// </para>
    /// <para>
    /// You can also customize this by configuring a <see cref="TracerProvider"/> before instantiating 
    /// your service and passing your provider as <see cref="NeonServiceOptions.TracerProvider"/>.
    /// </para>
    /// <note>
    /// <b>IMPORTANT:</b> Trace instrumentation is only supported for projects targeting .NET6.0 or greater.
    /// </note>
    /// </list>
    /// <para><b>HEALTH PROBES</b></para>
    /// <note>
    /// Health probes are supported only on Linux running as AMD64.  This is not supported
    /// on Windows, OS/X, 32-bit or ARM platforms at this time.
    /// </note>
    /// <para>
    /// Hosting environments such as Kubernetes will often require service instances
    /// to be able to report their health via health probes.  These probes are typically
    /// implemented as a small executables that is called periodically by the hosting 
    /// environment with the return code indicating the service instance health.  Alternatively,
    /// services can expose one or more web endpoints that returns 200 status when the
    /// service is healthy.
    /// </para>
    /// <note>
    /// This class has built-in support for the the small executable health checker approach 
    /// to make it easier to implement health checks for workloads that don't expose a web
    /// interface.  Web workloads can continue use the built-in approach or just expose their
    /// own health endpoints.
    /// </note>
    /// <para>
    /// The <see cref="Neon.Service.NeonService"/> class supports this by optionally
    /// writing a text file with various strings indicating the health status.  The 
    /// status file will consist of a single line of text holding one of the serialized
    /// <see cref="NeonServiceStatus"/> values.  The status file path defaults to 
    /// <b>/health-status</b>.
    /// </para>
    /// <para>
    /// <see cref="Neon.Service.NeonService"/> also deploys two status checking tools
    /// called <b>health-check</b> and <b>ready-check</b> to the same directory where
    /// <b>health-status</b> is written.
    /// </para>
    /// <para>
    /// The health check tool will be created at <b>/health-check</b> by default and it
    /// returns a non-zero exit code when the service is not healthy.  A service is
    /// considered healthy only when the status is on of <see cref="NeonServiceStatus.Running"/>
    /// or <see cref="NeonServiceStatus.NotReady"/>.
    /// </para>
    /// <para>
    /// The ready check tool file will be created at <b>/ready-check</b> by default and it
    /// returns a non-zero exit code when the service is not ready.  A service is considered 
    /// ready only when the status is <see cref="NeonServiceStatus.Running"/>.
    /// </para>
    /// <para>
    /// You may pass a custom health folder path to the constructor so that the <b>status</b> 
    /// and <b>check</b> files so these can be located elsewhere to avoid conflicts such as 
    /// when multiple services will be running on a machine or container or when the root
    /// file system is read-only.  You can also disable this feature entirely by passing
    /// <b>"DISABLED"</b> as the health folder path.
    /// </para>
    /// <note>
    /// <para>
    /// For Kubernetes deployments, we recommend that you configure your pod specifications
    /// with startup and liveliness probes along with an optional readiness probe when appropriate.
    /// This will look something like:
    /// </para>
    /// <code language="yaml">
    /// apiVersion: apps/v1
    /// kind: Deployment
    /// metadata:
    ///   name: my-app
    /// spec:
    ///   replicas: 1
    ///   selector:
    ///     matchLabels:
    ///       operator: my-app
    ///   template:
    ///     metadata:
    ///       labels:
    ///         operator: my-app
    ///     spec:
    ///       enableServiceLinks: false
    ///       containers:
    ///       - name: my-app
    ///         image: docker.io/my-app:latest
    ///         startupProbe:
    ///           exec:
    ///             command:
    ///             - /health-check         # $lt;--- this script works for both startup and liveliness probes
    ///           initialDelaySeconds: 1
    ///           periodSeconds: 5
    ///           timeoutSeconds: 1
    ///         livenessProbe:
    ///           exec:
    ///             command:
    ///             - /health-check
    ///           initialDelaySeconds: 1    # $lt;--- we don't need a long (fixed) delay here with a startup probe
    ///           periodSeconds: 5
    ///           timeoutSeconds: 1
    ///         readinessProbe:
    ///           exec:
    ///             command:
    ///             - /ready-check          # $lt;--- separate script for readiness probes
    ///           initialDelaySeconds: 1
    ///           periodSeconds: 5
    ///           timeoutSeconds: 1
    ///         ports:
    ///         - containerPort: 5000
    ///           name: http
    ///       terminationGracePeriodSeconds: 10
    /// </code>
    /// </note>
    /// <para><b>SERVICE DEPENDENCIES</b></para>
    /// <para>
    /// Services often depend on other services to function, such as databases, REST APIs, etc.
    /// <see cref="NeonService"/> provides an easy to use integrated way to wait for other
    /// services to initialize themselves and become ready before your service will be allowed
    /// to start.  This is a great way to avoid a blizzard of service failures and restarts
    /// when starting a collection of related services on a platform like Kubernetes.
    /// </para>
    /// <para>
    /// You can use the <see cref="Dependencies"/> property to control this in code via the
    /// <see cref="ServiceDependencies"/> class or configure this via environment variables: 
    /// </para>
    /// <code>
    /// NEON_SERVICE_DEPENDENCIES_URIS=http://foo.com;tcp://10.0.0.55:1234
    /// NEON_SERVICE_DEPENDENCIES_TIMEOUT_SECONDS=30
    /// NEON_SERVICE_DEPENDENCIES_WAIT_SECONDS=5
    /// </code>
    /// <para>
    /// The basic idea is that the <see cref="RunAsync"/> call to start your service will
    /// need to successfully to establish socket connections to any service dependecy URIs 
    /// before your <see cref="OnRunAsync"/> method will be called.  Your service will be
    /// terminated if any of the services cannot be reached after the specified timeout.
    /// </para>
    /// <para>
    /// You can also specify additional time to wait after all services are available
    /// to give them a chance to perform additional internal initialization.
    /// </para>
    /// <code source="..\..\Snippets\Snippets.NeonService\Program-Dependencies.cs" language="c#" title="Waiting for service dependencies:"/>
    /// <note>
    /// Service dependencies are currently waited for when the service status is <see cref="NeonServiceStatus.Starting"/>,
    /// which means that they will need to complete before the startup or libeliness probes time out
    /// resulting in service termination.  This behavior may change in the future.
    /// </note>
    /// <para><b>PROMETHEUS METRICS</b></para>
    /// <para>
    /// <see cref="NeonService"/> can enable services to publish Prometheus metrics with a
    /// single line of code; simply set <see cref="NeonService.MetricsOptions"/>.<see cref="MetricsOptions.Mode"/> to
    /// <see cref="MetricsMode.Scrape"/> before calling <see cref="RunAsync(bool)"/>.  This configures
    /// your service to publish metrics via HTTP via <b>http://0.0.0.0:</b><see cref="NetworkPorts.PrometheusMetrics"/><b>/metrics/</b>.
    /// We've resistered port <see cref="NetworkPorts.PrometheusMetrics"/> with Prometheus as a standard port
    /// to be used for microservices running in Kubernetes or on other container platforms to make it 
    /// easy configure scraping for a cluster.
    /// </para>
    /// <para>
    /// You can also configure a custom port and path or configure metrics push to a Prometheus
    /// Pushgateway using other <see cref="MetricsOptions"/> properties.  You can also fully customize
    /// your Prometheus configuration by leaving this disabled in <see cref="NeonService.MetricsOptions"/>
    /// and setting things up using the standard <b>prometheus-net</b> mechanisms before calling
    /// <see cref="RunAsync(bool)"/>.
    /// </para>
    /// <para><b>.NET CORE RUNTIME METRICS</b></para>
    /// <para>
    /// For .NET Core apps, you'll also need to add a reference to the <b>prometheus-net.DotNetRuntime</b>
    /// nuget package and set <see cref="MetricsOptions.GetCollector"/> to a function that returns the  the
    /// collector to be used.  This will look like:
    /// </para>
    /// <code language="C#">
    /// Service.MetricsOptions.GetCollector =
    ///     () =>
    ///     {
    ///         return DotNetRuntimeStatsBuilder
    ///             .Default()
    ///             .StartCollecting();
    ///     };
    /// </code>
    /// <para><b>ASPNETCORE METRICS:</b></para>
    /// <para>
    /// For ASPNETCORE metrics, you'll need to add the <b>prometheus-net.AspNetCore</b> nuget package and
    /// then add the collector when configuring your application in the <b>Startup</b> class, like:
    /// </para>
    /// <code language="C#">
    /// public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    /// {
    ///     app.UseRouting();
    ///     app.UseHttpMetrics();           // &lt;--- add this
    /// }    
    /// </code>
    /// <para><b>CRON JOBS</b></para>
    /// <para>
    /// <see cref="NeonService"/>s that implement Kubernetes CRON jobs should consider setting 
    /// <see cref="AutoTerminateIstioSidecar"/><c>=true</c>.  This ensures that the pod scheduled
    /// for the job is terminated cleanly when it has Istio injected sidecars.  This is generally
    /// safe to set when running in a Kubernetes cluster.
    /// </para>
    /// </remarks>
    public abstract class NeonService : IDisposable
    {
        //---------------------------------------------------------------------
        // Private types

        /// <summary>
        /// Holds information about configuration files.
        /// </summary>
        private sealed class FileInfo : IDisposable
        {
            /// <summary>
            /// The physical path to the configuration file.
            /// </summary>
            public string PhysicalPath { get; set; }

            /// <summary>
            /// The file data as bytes or as a string encoded as UTF-8 encode bytes.
            /// </summary>
            public byte[] Data { get; set; }

            /// <summary>
            /// Set if the physical file is temporary.
            /// </summary>
            public TempFile TempFile { get; set; }

            /// <summary>
            /// Dispose the file.
            /// </summary>
            public void Dispose()
            {
                if (TempFile != null)
                {
                    TempFile.Dispose();
                    TempFile = null;
                }
            }
        }

        //---------------------------------------------------------------------
        // Static members

        private const string disableHealthChecks  = "DISABLED";

        private static readonly char[] equalArray  = new char[] { '=' };
        private static readonly Gauge infoGauge = Metrics.CreateGauge($"{NeonHelper.NeonMetricsPrefix}_service_info", "Describes your service version.", "version");

        // WARNING:
        //
        // The code below should be manually synchronized with similar code in [KubeHelper]
        // if NeonKUBE related folder names ever change in the future.

        private static string   testFolder;
        private static string   cachedNeonKubeUserFolder;
        private static string   cachedPasswordsFolder;

        /// <summary>
        /// Returns <c>true</c> if the service is running in test mode.
        /// </summary>
        private static bool IsTestMode
        {
            get
            {
                if (testFolder != null)
                {
                    return true;
                }

                testFolder = global::System.Environment.GetEnvironmentVariable(NeonHelper.TestModeFolderVar);

                return testFolder != null;
            }
        }

        /// <summary>
        /// Returns the path the folder holding user-specific Kubernetes files.
        /// </summary>
        /// <returns>The folder path.</returns>
        private static string GetNeonKubeUserFolder()
        {
            if (cachedNeonKubeUserFolder != null)
            {
                return cachedNeonKubeUserFolder;
            }

            if (IsTestMode)
            {
                cachedNeonKubeUserFolder = Path.Combine(testFolder, ".neonkube");

                Directory.CreateDirectory(cachedNeonKubeUserFolder);

                return cachedNeonKubeUserFolder;
            }

            var path = Path.Combine(NeonHelper.UserHomeFolder, ".neonkube");

            Directory.CreateDirectory(path);

            return cachedNeonKubeUserFolder = path;
        }

        /// <summary>
        /// Returns path to the folder holding the encryption passwords.
        /// </summary>
        /// <returns>The folder path.</returns>
        private static string PasswordsFolder
        {
            get
            {
                if (cachedPasswordsFolder != null)
                {
                    return cachedPasswordsFolder;
                }

                var path = Path.Combine(GetNeonKubeUserFolder(), "passwords");

                Directory.CreateDirectory(path);

                return cachedPasswordsFolder = path;
            }
        }

        /// <summary>
        /// Looks up a password given its name.
        /// </summary>
        /// <param name="passwordName">The password name.</param>
        /// <returns>The password value.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the password doesn't exist.</exception>
        private static string LookupPassword(string passwordName)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(passwordName), nameof(passwordName));

            var path = Path.Combine(PasswordsFolder, passwordName);

            if (!File.Exists(path))
            {
                throw new KeyNotFoundException(passwordName);
            }

            return File.ReadAllText(path).Trim();
        }

        //---------------------------------------------------------------------
        // Instance members

        private readonly object                 syncLock     = new object();
        private readonly AsyncMutex             asyncMutex   = new AsyncMutex();
        private readonly AsyncManualResetEvent  startedEvent = new AsyncManualResetEvent(false);
        private readonly NeonServiceOptions     options;
        private readonly Counter                runtimeCount;
        private readonly Counter                unhealthyCount;
        private bool                            isRunning;
        private bool                            isDisposed;
        private bool                            stopPending;
        private Dictionary<string, string>      environmentVariables;
        private Dictionary<string, FileInfo>    configFiles;
        private string                          healthFolder;
        private string                          healthStatusPath;
        private string                          healthCheckPath;
        private string                          readyCheckPath;
        private IRetryPolicy                    healthRetryPolicy = new LinearRetryPolicy(e => e is IOException, maxAttempts: 10, retryInterval: TimeSpan.FromMilliseconds(100));
        private Uri                             traceCollectorUri;
#if NET6_0_OR_GREATER
        private KestrelMetricServer             metricServer;
#else
        private MetricServer                    metricServer;
#endif
        private MetricPusher                    metricPusher;
        private IDisposable                     metricCollector;
        private string                          terminationMessagePath;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The name of this service.</param>
        /// <param name="version">
        /// Optionally specifies the version of your service formatted as a valid <see cref="SemanticVersion"/>.
        /// This will default to <b>"unknown"</b> when not set or when the value passed is invalid.
        /// </param>
        /// <param name="options">Optionally specifies <see cref="NeonService"/> options.</param>
        /// <exception cref="KeyNotFoundException">
        /// Thrown if there is no service description for <paramref name="name"/>
        /// within the <see cref="ServiceMap"/> when a service map is specified.
        /// </exception>
        public NeonService(
            string              name,
            string              version = null,
            NeonServiceOptions  options = null)
        {
            try
            {
                Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(name), nameof(name));

                this.Name    = name;
                this.options = options ??= new NeonServiceOptions();

                // Configure the service version.

                version ??= "0.0.0";

                try
                {
                    if (options.AppendGitInfo && !string.IsNullOrEmpty(ThisAssembly.Git.Branch))
                    {
                        version += $"+{ThisAssembly.Git.Branch}.{ThisAssembly.Git.Commit}";
                    }
                }
                catch
                {
                    // $note(jefflill):
                    //
                    // I'm not sure what GitInfo does when the project isn't in a git repo, so
                    // we'll catch and ignore any exceptions just to be safe.
                }

                this.Version = version;

                // Check the service map, if one was passed.

                if (options.ServiceMap != null)
                {
                    if (!options.ServiceMap.TryGetValue(name, out var description))
                    {
                        throw new KeyNotFoundException($"The service map does not include a service definition for [{name}].");
                    }
                    else
                    {
                        if (name != description.Name)
                        {
                            throw new ArgumentException($"Service [name={name}] does not match [description.Name={description.Name}.");
                        }

                        this.Description = description;
                    }
                }

                // Initialize service members.

                this.Name                   = name;
                this.ServiceMap             = options.ServiceMap;
                this.InProduction           = !NeonHelper.IsDevWorkstation;
                this.Terminator             = new ProcessTerminator(gracefulShutdownTimeout: options.GracefulShutdownTimeout, minShutdownTime: options.MinShutdownTime);
                this.Environment            = new EnvironmentParser(Logger, VariableSource);
                this.configFiles            = new Dictionary<string, FileInfo>();
                this.healthFolder           = options.HealthFolder ?? "/";
                this.terminationMessagePath = options.TerminationMessagePath ?? "/dev/termination-log";

                // Initialize the service environment.

                this.environmentVariables = new Dictionary<string, string>();

                LoadEnvironmentVariables();

                // Determine whether we're running in DEBUG mode.

                if (NeonHelper.IsDevWorkstation || !string.IsNullOrEmpty(Environment.Get("DEBUG", string.Empty)))
                {
                    DebugMode = true;
                }
                else
                {
                    DebugMode = false;
                }

                // This environment variable disables the ASPNETCORE startup prompt
                // so that won't pollute console logs.

                System.Environment.SetEnvironmentVariable("ASPNETCORE_SUPPRESSSTATUSMESSAGES", "true");

                // Parse the TRACE_COLLECTOR_URI environment variable when present otherwise
                // this will default to [NeonHelper.NeonKubeOtelCollectorUri] when we're running
                // in Kubernetes.

                var traceCollectorUriString = System.Environment.GetEnvironmentVariable("TRACE_COLLECTOR_URI");

                if (!string.IsNullOrEmpty(traceCollectorUriString))
                {
                    Uri.TryCreate(traceCollectorUriString, UriKind.Absolute, out traceCollectorUri);
                }

                if (NeonHelper.IsKubernetes && traceCollectorUri == null)
                {
                    traceCollectorUri = new Uri(NeonHelper.NeonKubeOtelCollectorUri);
                }

                //-------------------------------------------------------------
                // Configure the logging pipeline.

                if (options.LoggerFactory != null)
                {
                    // The logging pipeline has already been configured by the user.

                    NeonHelper.ServiceContainer.Add(new ServiceDescriptor(typeof(ILoggerFactory), options.LoggerFactory));

                    TelemetryHub.LoggerFactory = options.LoggerFactory;
                    this.Logger                = options.LoggerFactory.CreateLogger<NeonService>();
                }
                else
                {
                    // Built-in logger configuration.

                    var logLevel = TelemetryHub.ParseLogLevel(System.Environment.GetEnvironmentVariable("LOG_LEVEL") ?? LogLevel.Information.ToMemberString());

                    System.Environment.SetEnvironmentVariable("Logging__LogLevel__Default", logLevel.ToString());

                    var loggingConfig = new ConfigurationBuilder()
                        .AddCustomEnvironmentVariables(
                            prefix:         options.EnvironmentVariablePrefix,
                            dotReplacement: options.EnvironmentVariableDotReplacement)
                        .Build();
                   
                    var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(
                        builder =>
                        {
                            builder
                                .AddConfiguration(loggingConfig)
                                .SetMinimumLevel(logLevel)
                                .EnableEnrichment()
                                .AddOpenTelemetry(
                                    options =>
                                    {
                                        options.ParseStateValues        = true;
                                        options.IncludeFormattedMessage = true;

                                        // Give the derived service a chance to customize the logging configuration.
                                        // This method returns FALSE to continue with the built-in configuration or
                                        // TRUE when the derived class has full configured things.

                                        if (!OnLoggerConfg(options))
                                        {
                                            options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName: Name, serviceVersion: Version));

                                            // Configure the trace pipeline when the [TRACE_COLLECTOR_URI] environment
                                            // variable is present and valid.

                                            if (traceCollectorUri != null)
                                            {
                                                options.AddLogAsTraceProcessor(
                                                    options =>
                                                    {
                                                        var traceLogLevelString = System.Environment.GetEnvironmentVariable("TRACE_LOG_LEVEL") ?? LogLevel.Information.ToMemberString();

                                                        if (!NeonHelper.TryParseEnum<LogLevel>(traceLogLevelString, out var traceLogLevel))
                                                        {
                                                            traceLogLevel = LogLevel.Information;
                                                        }

                                                        options.LogLevel = traceLogLevel;
                                                    });
                                            }

                                            options.AddLogMetricsProcessor();
                                            options.AddConsoleJsonExporter();
                                        }
                                    });

                            var levels = new Dictionary<string, string>();
                            loggingConfig.GetSection("Logging:LogLevel").Bind(levels);

                            foreach (var level in levels)
                            {
                                builder.AddFilter(level.Key, TelemetryHub.ParseLogLevel(level.Value));
                            }

                            if (options.LogEnrichers != null)
                            {
                                foreach (var enricher in options.LogEnrichers)
                                {
                                    builder.Services.AddLogEnricher(enricher);
                                }
                            }
                        });

                    NeonHelper.ServiceContainer.Add(new ServiceDescriptor(typeof(ILoggerFactory), loggerFactory));

                    TelemetryHub.LoggerFactory = loggerFactory;
                    this.Logger                = loggerFactory.CreateLogger<NeonService>();
                }

                //-------------------------------------------------------------
                // Configure the tracing pipeline.

                TelemetryHub.ActivitySource ??= new ActivitySource(Name, Version);
                this.ActivitySource           = TelemetryHub.ActivitySource;

                if (options.TracerProvider != null)
                {
                    // The tracing pipeline has already been configured by the user.
                }
                else
                {
                    // Start the [CollectorChecker] which polls to ensure that the
                    // OpenTelemetry Collector service is running in the same namespace
                    // as the service and then enables/disables trace sampling based
                    // as necessary to avoid trying to send traces to a black hole.

                    if (traceCollectorUri != null)
                    {
                        OtlpCollectorChecker.Start(this, collectorUri: traceCollectorUri);
                    }
                    else
                    {
                        OtlpCollectorChecker.Start(this);
                    }

                    // Built-in tracing configuration.

                    var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
                        .AddSource(name, version)
                        .SetSampler(OtlpCollectorChecker.Sampler)
                        .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(name, serviceVersion: version));

                    // Give the derived service a chance to customize the trace pipeline.

                    if (!OnTracerConfig(tracerProviderBuilder) && traceCollectorUri != null)
                    {
                        // Use a built-in configuration when the the derived class allows it
                        // and we have a target collection URI.
#if NET6_0_OR_GREATER
                        tracerProviderBuilder.AddOtlpExporter(
                            options =>
                            {
                                options.ExportProcessorType         = ExportProcessorType.Batch;
                                options.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>();
                                options.Endpoint                    = traceCollectorUri;
                                options.Protocol                    = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                            });
#endif
                    }

                    tracerProviderBuilder.Build();
                }

                // Initialize the metrics prefix and counters.

                this.runtimeCount   = Metrics.CreateCounter($"{NeonHelper.NeonMetricsPrefix}_service_runtime_seconds", "Service runtime in seconds.");
                this.unhealthyCount = Metrics.CreateCounter($"{NeonHelper.NeonMetricsPrefix}_service_unhealthy_transitions_total", "Service [unhealthy] transitions.");

                // Detect unhandled application exceptions and log them.

                AppDomain.CurrentDomain.UnhandledException +=
                    (s, a) =>
                    {
                        var exception = (Exception)a.ExceptionObject;

                        Logger.LogCriticalEx(exception, () => $"Unhandled exception [terminating={a.IsTerminating}]");
                    };

                // Update the Prometheus metrics port from the service description if present.

                if (Description != null)
                {
                    MetricsOptions.Port = Description.MetricsPort;
                }

                // Initialize the [neon_service_info] gauge.

                infoGauge.WithLabels(Version).Set(1);
            }
            catch (Exception e)
            {
                // We shouldn't see any exceptions here but we're going to log any we see to
                // be defensive.  Note that it's possible that the exception happened before
                // we initialized the logger; we're going to special case that situation.

                if (Logger != null)
                {
                    Logger.LogErrorEx(e, () => $"Error initializing [{nameof(NeonService)}].");
                }
                else
                {
                    Console.WriteLine($"{NeonHelper.ExceptionError(e)}: stacktrace: {e.StackTrace}");
                }

                throw;
            }
        }

        /// <summary>
        /// <para>
        /// Used to specify other services that must be reachable via the network before a
        /// <see cref="NeonService"/> will be allowed to start.  This is exposed via the
        /// <see cref="NeonService.Dependencies"/> where these values can be configured in
        /// code before <see cref="NeonService.RunAsync(bool)"/> is called or they can
        /// also be configured via environment variables as described in <see cref="ServiceDependencies"/>.
        /// </para>
        /// <note>
        /// Service dependencies are currently waited for when the service status is <see cref="NeonServiceStatus.Starting"/>,
        /// which means that they will need to complete before the startup or liveliness probes time out
        /// resulting in service termination.  This behavior may change in the future.
        /// </note>
        /// </summary>
        public ServiceDependencies Dependencies { get; set; } = new ServiceDependencies();

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~NeonService()
        {
            Dispose(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Releases all associated resources.
        /// </summary>
        /// <param name="disposing">Pass <c>true</c> if we're disposing, <c>false</c> if we're finalizing.</param>
        protected virtual void Dispose(bool disposing)
        {
            lock (syncLock)
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
            }

            Stop();

            lock (syncLock)
            {
                foreach (var item in configFiles.Values)
                {
                    item.Dispose();
                }

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the service is running in production,
        /// when the <b>DEV_WORKSTATION</b> environment variable is
        /// <b>not defined</b>.  The <c>NeonServiceFixure</c> will set this
        /// to <c>true</c> explicitly as well.
        /// </summary>
        public bool InProduction { get; internal set; }

        /// <summary>
        /// Returns <c>true</c> when the service is running in development
        /// or test mode, when the <b>DEV_WORKSTATION</b> environment variable 
        /// is <b>defined</b>.
        /// </summary>
        public bool InDevelopment => !InProduction;

        /// <summary>
        /// Returns the service name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Returns the service version or <b>"unknown"</b>.
        /// </summary>
        public string Version { get; private set; }

        /// <summary>
        /// Provides support for retrieving environment variables as well as
        /// parsing common value types as well as custom value parsers.  We
        /// recommend that services use this rather than <see cref="GetEnvironmentVariable(string, string, bool)"/>
        /// when possible as a way to standardize on how settings are formatted,
        /// parsed and validated.
        /// </summary>
        public EnvironmentParser Environment { get; private set; }

        /// <summary>
        /// Returns <c>true</c> when the service is running in <b>debug mode</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Services use the presence of the <b>DEV_WORKSTATION</b> environment variable
        /// or a <b>DEBUG</b> environment set to one of <b>DEBUG</b>, <b>true</b>, <b>yes</b>,
        /// <b>on</b>, or <b>1</b>.
        /// </para>
        /// <para>
        /// If the <b>DEBUG</b> environment variable isn't present then debug mode will
        /// be set when <b>DEV_WORKSTATION</b> exists, otherwise the <b>DEBUG</b> variable
        /// value determines the mode.
        /// </para>
        /// </remarks>
        public bool DebugMode { get; private set; }

        /// <summary>
        /// Returns the service map (if any).
        /// </summary>
        public ServiceMap ServiceMap { get; private set; }

        /// <summary>
        /// Returns the service description for this service (if any).
        /// </summary>
        public ServiceDescription Description { get; private set; }

        /// <summary>
        /// Controls whether any Istio sidecars (Envoy) will be terminated automatically
        /// when the service exits normally.  This is useful for services like CRON jobs
        /// that don't get rescheduled after they exit.  This defaults to <c>false</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// You should consider setting this property to <c>true</c> for services implementing
        /// a Kubernetes CRON job that has injected Istio sidecars.  The problem here is that
        /// after the main pod container exits, the Envoy sidecar containers reamin running and
        /// the pod never gets terminated:
        /// </para>
        /// <para>
        /// https://github.com/istio/istio/issues/11659#issuecomment-809372980
        /// </para>
        /// <para>
        /// This isn't an general issue for other deployments because the Kubernetes scheduler
        /// will terminate and reschedule pods after the main container exists.  CRON jobs are
        /// different because they won't get rescheduled when the main container exits.
        /// </para>
        /// <para>
        /// When you set <see cref="AutoTerminateIstioSidecar"/><c>true</c>, the <see cref="NeonService"/>
        /// class will POST a request to the Envoy sidecar's admin API at <b>http://localhost:15000/quitquitquit</b>
        /// to explicitly terminate any sidecars when the service exits normally.  Note that we won't
        /// post this request when the service receives a termination signal.
        /// </para>
        /// <para>
        /// We recommend that you set this property before calling <see cref="StartedAsync"/>
        /// in your service initialization code.
        /// </para>
        /// <note>
        /// This class generally tolerates the situation where the service does not have injected
        /// Istio sidecars by ignoring any network errors when posting to the Envoy admin endpoint.
        /// </note>
        /// </remarks>
        public bool AutoTerminateIstioSidecar { get; set; } = false;

        /// <summary>
        /// Returns the dictionary mapping case sensitive service endpoint names to endpoint information.
        /// </summary>
        public Dictionary<string, ServiceEndpoint> Endpoints => Description.Endpoints;

        /// <summary>
        /// <para>
        /// For services with exactly one network endpoint, this returns the base
        /// URI to be used to access the service.
        /// </para>
        /// <note>
        /// This will throw a <see cref="InvalidOperationException"/> if the service
        /// defines no endpoints or has multiple endpoints.
        /// </note>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the service does not define exactly one endpoint or <see cref="Description"/> is not set.
        /// </exception>
        public Uri BaseUri
        {
            get
            {
                if (Description == null)
                {
                    throw new InvalidOperationException($"The {nameof(BaseUri)} property requires that [{nameof(Description)} be set and have exactly one endpoint.");
                }

                if (Description.Endpoints.Count == 1)
                {
                    return Description.Endpoints.First().Value.Uri;
                }
                else
                {
                    throw new InvalidOperationException($"The {nameof(BaseUri)} property requires that the service be defined with exactly one endpoint.");
                }
            }
        }

        /// <summary>
        /// <para>
        /// Prometheus metrics options.  To enable metrics collection for non-ASPNET applications,
        /// we recommend that you simply set <see cref="MetricsOptions.Mode"/><c>==</c><see cref="MetricsMode.Scrape"/>
        /// before calling <see cref="OnRunAsync"/>.
        /// </para>
        /// <para>
        /// See <see cref="MetricsOptions"/> for more details.
        /// </para>
        /// </summary>
        public MetricsOptions MetricsOptions { get; set; } = new MetricsOptions();

        /// <summary>
        /// Returns the <see cref="ILoggerFactory"/> used by the service.  This is used to create
        /// the default service <see cref="Logger"/> and may also be used by user code to create
        /// custom loggers when necessary.
        /// </summary>
        public ILoggerFactory LoggerFactory { get; private set; }

        /// <summary>
        /// <para>
        /// Configured as the <see cref="ILogger"/> used by the service for logging.
        /// </para>
        /// <note>
        /// You can create additional loggers via <see cref="TelemetryHub.CreateLogger(string, LogAttributes, bool, bool)"/>
        /// and <see cref="TelemetryHub.CreateLogger{T}(LogAttributes, bool, bool)"/>.
        /// </note>
        /// </summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// Returns the service current running status.  Use <see cref="SetStatusAsync(NeonServiceStatus)"/>
        /// to update the service status.
        /// </summary>
        public NeonServiceStatus Status { get; private set; }

        /// <summary>
        /// <para>
        /// Configured as the activity source used by the service for recording traces.
        /// </para>
        /// <note>
        /// <see cref="NeonService"/> also sets the same value as <see cref="TelemetryHub.ActivitySource"/>
        /// to enable tracing from library code.
        /// </note>
        /// </summary>
        public ActivitySource ActivitySource { get; set; }

        /// <summary>
        /// Returns the service's <see cref="ProcessTerminator"/>.  This can be used
        /// to handle termination signals.
        /// </summary>
        public ProcessTerminator Terminator { get; private set; }

        /// <summary>
        /// Returns the list of command line arguments passed to the service.  This
        /// defaults to an empty list.
        /// </summary>
        public List<string> Arguments { get; private set; } = new List<string>();

        /// <summary>
        /// <para>
        /// Called by the constructor, giving the derived service implementation a chance to
        /// customize the logger configuration using the <paramref name="options"/> parameter.
        /// This is a good place add log exporters and processors required by your service.
        /// </para>
        /// <para>
        /// This is called <b>before</b> the base <see cref="NeonService"/> class performs
        /// its default configuration.
        /// </para>
        /// </summary>
        /// <param name="options">Passed as the logger options.</param>
        /// <returns>
        /// <c>false</c> when the base <see cref="NeonService"/> class should continue with
        /// its default logger configuration or <c>true</c> to just use the configuration
        /// set by the method implementation.  The base implementation does nothing and
        /// returns <c>false</c>.
        /// </returns>
        /// <remarks>
        /// <note>
        /// This method is not called when the <see cref="NeonServiceOptions.LoggerFactory"/>
        /// property is set when constructing the service, indicating that the logging pipeline
        /// has already been configured.
        /// </note>
        /// </remarks>
        protected virtual bool OnLoggerConfg(OpenTelemetryLoggerOptions options) => false;

        /// <summary>
        /// <para>
        /// Called by the constructor, giving the derived service implementation a chance to
        /// customize the tracer builder using the <paramref name="builder"/> parameter.
        /// This is a good place to add <c>OpenTelemetry.Instrumentation.AspNetCore</c>
        /// and other trace instrumentation required by your service.
        /// </para>
        /// <para>
        /// This is called <b>before</b> the base <see cref="NeonService"/> class performs
        /// its default configuration.
        /// </para>
        /// </summary>
        /// <param name="builder">Passed as the tracer builder.</param>
        /// <returns>
        /// <c>false</c> when the base <see cref="NeonService"/> class should continue with
        /// its default tracer configuration or <c>true</c> to just use the configuration
        /// set by the method implementation.  The base implementation does nothing and
        /// returns <c>false</c>.
        /// </returns>
        /// <remarks>
        /// <note>
        /// This method is not called when the <see cref="NeonServiceOptions.TracerProvider"/>
        /// property is set when constructing the service, indicating that the tracing pipeline
        /// has already been configured.
        /// </note>
        /// </remarks>
        protected virtual bool OnTracerConfig(TracerProviderBuilder builder) => false;

        /// <summary>
        /// Updates the service status.  This is typically called internally by this
        /// class but service code may set this to <see cref="NeonServiceStatus.Unhealthy"/>
        /// when there's a problem and back to <see cref="NeonServiceStatus.Running"/>
        /// when the service is healthy again.  This may also be set to <see cref="NeonServiceStatus.NotReady"/>
        /// to indicate that the service is running but is not ready to accept external
        /// traffic.
        /// </summary>
        /// <param name="newStatus">The new status.</param>
        public async Task SetStatusAsync(NeonServiceStatus newStatus)
        {
            await SyncContext.Clear;

            var orgStatus       = this.Status;
            var newStatusString = NeonHelper.EnumToString(newStatus);

            using (await asyncMutex.AcquireAsync())
            {
                if (newStatus == orgStatus)
                {
                    // Status is unchanged.

                    return;
                }
                else if (orgStatus == NeonServiceStatus.Terminated)
                {
                    throw new InvalidOperationException($"Service status cannot be set to [{newStatus}] when the service status is [{NeonServiceStatus.Terminated}].");
                }

                this.Status = newStatus;

                if (newStatus == NeonServiceStatus.Unhealthy)
                {
                    unhealthyCount.Inc();
                    Logger.LogWarningEx(() => $"[{Name}] health status: [{newStatusString}]");
                }
                else
                {
                    Logger.LogInformationEx(() => $"[{Name}] health status: [{newStatusString}]");
                }

                if (healthStatusPath != null)
                {
                    // We're going to use a retry policy to handle the rare situations
                    // where the [health-check] or [ready-check] binaries and this method
                    // try to access the [health-status] file at the exact same moment.

                    healthRetryPolicy.Invoke(() => File.WriteAllText(healthStatusPath, newStatusString));
                }
            }
        }

        /// <summary>
        /// Returns the exit code returned by the service.
        /// </summary>
        public int ExitCode { get; private set; }

        /// <summary>
        /// Returns any abnormal exception thrown by the derived <see cref="OnRunAsync"/> method.
        /// </summary>
        public Exception ExitException { get; private set; }

        /// <summary>
        /// Initializes <see cref="Arguments"/> with the command line arguments passed.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        public NeonService SetArguments(IEnumerable<string> args)
        {
            Covenant.Requires<ArgumentNullException>(args != null, nameof(args));

            Arguments.Clear();

            foreach (var arg in args)
            {
                Arguments.Add(arg);
            }

            return this;
        }

        /// <summary>
        /// Waits until the service indicate that it's started when the <see cref="OnRunAsync"/> method
        /// has called <see cref="StartedAsync(NeonServiceStatus)"/>.  This is typically used by unit tests
        /// to wait until the service is ready before performing tests.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task WaitUntilStarted()
        {
            await startedEvent.WaitAsync();
        }

        /// <summary>
        /// Called by your <see cref="OnRunAsync"/> implementation to indicate that the service
        /// is either <see cref="NeonServiceStatus.Running"/> (the default) or <see cref="NeonServiceStatus.NotReady"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For most situations, the default <see cref="NeonServiceStatus.Running"/> argument is
        /// appropriate.  This indicates that the service will satisfy all of the probes: startup,
        /// liveliness, and readiness.
        /// </para>
        /// <para>
        /// Advanced services that may take some time to perform additional initialization 
        /// before being ready to service requests may pass <see cref="NeonServiceStatus.NotReady"/>.
        /// This means that the startup and liveliness probes will pass, preventing Kubernetes
        /// from terminating the container but that the readiness probe will fail, preventing
        /// Kubernetes from forwarding traffic to the container until <see cref="NeonServiceStatus.Running"/>
        /// is passed to <see cref="SetStatusAsync(NeonServiceStatus)"/>.
        /// </para>
        /// </remarks>
        public async Task StartedAsync(NeonServiceStatus status = NeonServiceStatus.Running)
        {
            await SyncContext.Clear;
            Covenant.Requires<ArgumentException>(status == NeonServiceStatus.Running || status == NeonServiceStatus.NotReady, nameof(status));

            startedEvent.Set();
            await SetStatusAsync(status);
        }

        /// <summary>
        /// Called by your <see cref="OnRunAsync"/> implementation to indicate that the service
        /// is either <see cref="NeonServiceStatus.Running"/> (the default) or <see cref="NeonServiceStatus.NotReady"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For most situations, the default <see cref="NeonServiceStatus.Running"/> argument is
        /// appropriate.  This indicates that the service will satisfy all of the probes: startup,
        /// liveliness, and readiness.
        /// </para>
        /// <para>
        /// Advanced services that may take some time to perform additional initialization 
        /// before being ready to service requests may pass <see cref="NeonServiceStatus.NotReady"/>.
        /// This means that the startup and liveliness probes will pass, preventing Kubernetes
        /// from terminating the container but that the readiness probe will fail, preventing
        /// Kubernetes from forwarding traffic to the container until <see cref="NeonServiceStatus.Running"/>
        /// is passed to <see cref="SetStatusAsync(NeonServiceStatus)"/>.
        /// </para>
        /// </remarks>
        public void Started(NeonServiceStatus status = NeonServiceStatus.Running)
        {
            // $note(jefflill):
            //
            // We're going to do and async fire-and-forget call here on the off chance
            // that the NeonService is being hosted and initialized in a UI app on a
            // UI thread.  The fact that we're not waiting on the task to complete
            // shouldn't be a problem.

            _ = SetStatusAsync(status);
        }

        /// <summary>
        /// Starts the service if it's not already running.  This will call <see cref="OnRunAsync"/>,
        /// which is where you'll actually implement the service.  Note that any service dependencies
        /// specified by <see cref="Dependencies"/> will be verified as ready before <see cref="OnRunAsync"/>
        /// will be called.
        /// </summary>
        /// <param name="disableProcessExit">
        /// Optionally specifies that the hosting process should not be terminated 
        /// when the service exists.  This is typically used for testing or debugging.
        /// This defaults to <c>false</c>.
        /// </param>
        /// <remarks>
        /// <note>
        /// For production, this method will not return until the service is expicitly 
        /// stopped via a call to <see cref="Stop"/> or the <see cref="Terminator"/> 
        /// handles a stop signal.  For test environments, this method will call
        /// <see cref="OnRunAsync"/> on a new thread and returns immediately while the
        /// service continues to run in parallel.
        /// </note>
        /// <para>
        /// Service implementations must honor <see cref="Terminator"/> termination
        /// signals by exiting the <see cref="OnRunAsync"/> method reasonably quickly (within
        /// 30 seconds by default) when these occur.  They can do this by passing 
        /// <see cref="ProcessTerminator.CancellationToken"/> for <c>async</c> calls
        /// and then catching the <see cref="TaskCanceledException"/> and returning
        /// from <see cref="OnRunAsync"/>.
        /// </para>
        /// <para>
        /// Another technique for synchronous code is to explicitly check the 
        /// <see cref="ProcessTerminator.CancellationToken"/> token's  
        /// <see cref="CancellationToken.IsCancellationRequested"/> property and 
        /// return from your <see cref="OnRunAsync"/> method when this is <c>true</c>.
        /// You'll need to perform this check frequently so you may need
        /// to use timeouts to prevent blocking code from blocking for too long.
        /// </para>
        /// </remarks>
        /// <returns>The service exit code.</returns>
        /// <remarks>
        /// <note>
        /// It is not possible to restart a service after it's been stopped.
        /// </note>
        /// </remarks>
        public async virtual Task<int> RunAsync(bool disableProcessExit = false)
        {
            lock (syncLock)
            {
                if (isRunning)
                {
                    throw new InvalidOperationException($"Service [{Name}] is already running.");
                }

                if (isDisposed)
                {
                    throw new InvalidOperationException($"Service [{Name}] cannot be restarted after it's been stopped.");
                }

                isRunning = true;
            }

            // [disableProcessExit] will be typically passed as true when testing or
            // debugging.  We'll let the terminator know so it won't actually terminate
            // the current process (which will actually be the unit test framework).

            if (disableProcessExit)
            {
                Terminator.DisableProcessExit = true;
            }

            if (!string.IsNullOrEmpty(Version))
            {
                Logger.LogInformationEx(() => $"Starting [{Name}:{Version}]");
            }
            else
            {
                Logger.LogInformationEx(() => $"Starting [{Name}]");
            }

            Logger.LogInformationEx(() => $".NET Runtime: {System.Environment.Version}");

            // Initialize the health status paths when enabled on Linux and
            // deploy the health and ready check tools.  We'll log any
            // errors and disable status generation if we have trouble
            // accessing the folder.

            if (string.IsNullOrWhiteSpace(healthFolder))
            {
                healthFolder = string.Empty;
            }

            if (NeonHelper.IsLinux && NeonHelper.Is64BitOS && !NeonHelper.IsARM && !healthFolder.Equals(disableHealthChecks, StringComparison.InvariantCultureIgnoreCase))
            {
                if (string.IsNullOrEmpty(healthFolder))
                {
                    healthFolder = $"/";
                }

                Logger.LogInformationEx(() => $"Deploying health checkers to: {healthFolder}");
                
                healthStatusPath = Path.Combine(healthFolder, "health-status");
                healthCheckPath  = Path.Combine(healthFolder, "health-check");
                readyCheckPath   = Path.Combine(healthFolder, "ready-check");

                try
                {
                    // Create the health status file, set its permissions as well
                    // as its initial STARTING state.

                    Directory.CreateDirectory(healthFolder);
                    File.WriteAllText(healthStatusPath, NeonHelper.EnumToString(NeonServiceStatus.Starting));

                    NeonHelper.Execute("/bin/chmod",
                        new object[]
                        {
                            "664",
                            healthStatusPath
                        });

                    // Copy the [health-check] and [ready-check] binaries into the same folder
                    // as the [health-status] file.

                    var resources = Assembly.GetExecutingAssembly().GetResourceFileSystem("Neon.Service.Resources");

                    using (var toolStream = resources.GetFile("/health-check").OpenStream())
                    {
                        using (var output = File.OpenWrite(healthCheckPath))
                        {
                            toolStream.CopyTo(output);
                        }

                        NeonHelper.Execute("/bin/chmod",
                            new object[]
                            {
                                "755",
                                healthCheckPath
                            });
                    }

                    using (var toolStream = resources.GetFile("/ready-check").OpenStream())
                    {
                        using (var output = File.OpenWrite(readyCheckPath))
                        {
                            toolStream.CopyTo(output);
                        }

                        NeonHelper.Execute("/bin/chmod",
                            new object[]
                            {
                                "755",
                                readyCheckPath
                            });
                    }
                }
                catch (IOException e)
                {
                    // This may happen if the health folder path is invalid, the process
                    // doesn't have permissions for the folder or perhaps when the file
                    // system is read-only.  We're going to log this and disable the health
                    // status feature in this case. 

                    Logger.LogErrorEx(e, () => $"Cannot initialize the health folder [{healthFolder}].  The health status feature will be disabled.");

                    healthFolder     = null;
                    healthStatusPath = null;
                    healthCheckPath  = null;
                }
            }
            else
            {
                if (healthFolder.Equals(disableHealthChecks, StringComparison.InvariantCultureIgnoreCase))
                {
                    Logger.LogInformationEx("Built-in health check executables are disabled.");
                }
                else
                {
                    Logger.LogWarningEx("NeonService health checking is currently only supported on Linux/AMD64.");
                }

                healthFolder = null;
            }

            // Initialize Prometheus metrics when enabled.

            MetricsOptions = MetricsOptions ?? new MetricsOptions();
            MetricsOptions.Validate();

            try
            {
                switch (MetricsOptions.Mode)
                {
                    case MetricsMode.Disabled:

                        break;

                    case MetricsMode.Scrape:
                    case MetricsMode.ScrapeIgnoreErrors:

                        metricServer = new KestrelMetricServer(MetricsOptions.Port, MetricsOptions.Path);
                        metricServer.Start();
                        break;

                    case MetricsMode.Push:

                        metricPusher = new MetricPusher(MetricsOptions.PushUrl, job: Name, intervalMilliseconds: (long)MetricsOptions.PushInterval.TotalMilliseconds, additionalLabels: MetricsOptions.PushLabels);
                        metricPusher.Start();
                        break;

                    default:

                        throw new NotImplementedException();
                }

                if (MetricsOptions.GetCollector != null)
                {
                    metricCollector = MetricsOptions.GetCollector();
                }
            }
            catch (NotImplementedException)
            {
                throw;
            }
            catch
            {
                if (MetricsOptions.Mode != MetricsMode.ScrapeIgnoreErrors)
                {
                    throw;
                }
            }

            // Verify that any required service dependencies are ready.

            var dnsOptions = new LookupClientOptions()
            {
                ContinueOnDnsError      = false,
                ContinueOnEmptyResponse = false,
                Retries                 = 0,
                ThrowDnsErrors          = false,
                Timeout                 = TimeSpan.FromSeconds(2),
                UseTcpFallback          = true
            };

            var dnsClient         = new LookupClient(dnsOptions);
            var dnsAvailable      = Dependencies.DisableDnsCheck;
            var readyServices     = new HashSet<Uri>();
            var notReadyUri       = (Uri)null;
            var notReadyException = (Exception)null;

            try
            {
                await NeonHelper.WaitForAsync(
                    async () =>
                    {
                        // Verify DNS availability first because services won't be available anyway
                        // when there's no DNS.

                        if (!dnsAvailable)
                        {
                            try
                            {
                                await dnsClient.QueryAsync(ServiceDependencies.DnsCheckHostName, QueryType.A);

                                dnsAvailable = true;
                            }
                            catch (DnsResponseException e)
                            {
                                return e.Code == DnsResponseCode.ConnectionTimeout;
                            }
                        }

                        // Verify the service dependencies next.

                        foreach (var uri in Dependencies.Uris)
                        {
                            if (readyServices.Contains(uri))
                            {
                                continue;   // This one is already ready
                            }

                            switch (uri.Scheme.ToUpperInvariant())
                            {
                                case "HTTP":
                                case "HTTPS":
                                case "TCP":

                                    using (var socket = new Socket(SocketType.Stream, ProtocolType.Tcp))
                                    {
                                        try
                                        {
                                            var addresses = await Dns.GetHostAddressesAsync(uri.Host);

                                            socket.Connect(new IPEndPoint(addresses.First(), uri.Port));
                                        }
                                        catch (SocketException e)
                                        {
                                            // Remember these so we can log something useful if we end up timing out.

                                            notReadyUri       = uri;
                                            notReadyException = e;

                                            return false;
                                        }
                                    }
                                    break;

                                default:

                                    Logger.LogWarningEx(() => $"Service Dependency: [{uri}] has an unsupported scheme and will be ignored.  Only HTTP, HTTPS, and TCP URIs are allowed.");
                                    readyServices.Add(uri);     // Add the bad URI so we won't try it again.
                                    break;
                            }
                        }

                        return true;
                    },
                    timeout:      Dependencies.TestTimeout ?? Dependencies.Timeout,
                    pollInterval: TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                // Report the problem and exit the service.

                Logger.LogErrorEx(notReadyException, () => $"Service Dependency: [{notReadyUri}] is still not ready after waiting [{Dependencies.Timeout}].");

                if (metricServer != null)
                {
                    await metricServer.StopAsync();
                    metricServer = null;
                }

                if (metricPusher != null)
                {
                    await metricPusher.StopAsync();
                    metricPusher = null;
                }

                if (metricCollector != null)
                {
                    metricCollector.Dispose();
                    metricCollector = null;
                }

                TerminateAnySidecars();

                return ExitCode = 1;
            }

            await Task.Delay(Dependencies.Wait);

            // Start the service runtime counter task.

            var runtimerCts  = new CancellationTokenSource();
            var runtimerTask = Runtimer(runtimerCts.Token);

            // Call the user code to start the service.

            try
            {
                await SetStatusAsync(NeonServiceStatus.Starting);
                await OnRunAsync();

                ExitCode = 0;
            }
            catch (TaskCanceledException)
            {
                // These are thrown as a normal consequence of a service being signalled
                // to terminate.  Kubernetes may write a termination message to the file
                // system.  We'll log this when present when running on Linux.

                if (NeonHelper.IsLinux && !string.IsNullOrEmpty(terminationMessagePath))
                {
                    try
                    {
                        if (File.Exists(terminationMessagePath))
                        {
                            Logger.LogInformationEx(() => $"Kubernetes termination: {File.ReadAllText(terminationMessagePath)}");
                        }
                    }
                    catch (IOException)
                    {
                        // Ignore any file read errors.
                    }
                }

                ExitCode = 0;
            }
            catch (ProgramExitException e)
            {
                // Don't override a non-zero exit code that was set earlier
                // with a zero exit code.

                if (e.ExitCode != 0)
                {
                    ExitCode = e.ExitCode;
                }

                TerminateAnySidecars();
            }
            catch (Exception e)
            {
                // We're going to consider any exceptions caught here to be errors
                // and return a non-zero exit code.  The service's [main()] method
                // can examine the [ExceptionException] property to decide whether
                // the exception should be considered an error or whether to return
                // a custom error code.

                ExitException = e;
                ExitCode      = 1;

                Logger.LogErrorEx(e);
                TerminateAnySidecars();
            }

            // Perform last rights for the service before it passes away.

            Logger.LogInformationEx(() => $"Exiting [{Name}] with [exitcode={ExitCode}].");

            runtimerCts.Cancel();
            await runtimerTask;

            if (metricServer != null)
            {
                await metricServer.StopAsync();
                metricServer = null;
            }

            if (metricPusher != null)
            {
                await metricPusher.StopAsync();
                metricPusher = null;
            }

            if (metricCollector != null)
            {
                metricCollector.Dispose();
                metricCollector = null;
            }

            Terminator.ReadyToExit();
            await SetStatusAsync(NeonServiceStatus.Terminated);

            return ExitCode;
        }

        /// <summary>
        /// Handles incrementing the <b>runtime</b> metrics counter.
        /// </summary>
        /// <param name="cancellationToken">Specifies the cancellation token used to stop the timer.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <remarks>
        /// This method loops, incrementing <see cref="runtimeCount"/> periodically
        /// until <paramref name="cancellationToken"/> requests a cancellation.
        /// </remarks>
        private async Task Runtimer(CancellationToken cancellationToken)
        {
            await SyncContext.Clear;

            // We're going to loop and compute the runtime every 5 seconds.
            // We could just add 5 seconds to the counter every time but 
            // we're going to use [SysTime] instead for a tiny bit more
            // accuracy.

            var delay    = TimeSpan.FromSeconds(5);
            var startSys = SysTime.Now;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(delay);

                var runSeconds = Math.Floor((SysTime.Now - startSys).TotalSeconds);

                if (runSeconds > runtimeCount.Value)
                {
                    runtimeCount.IncTo(runSeconds);
                }
            }
        }

        /// <summary>
        /// <para>
        /// Stops the service if it's not already stopped.  This is intended to be called by
        /// external things like unit test fixtures and is not intended to be called by the
        /// service itself.
        /// </para>
        /// </summary>
        /// <exception cref="TimeoutException">
        /// Thrown if the service did not exit gracefully in time before it would have 
        /// been killed (e.g. by Kubernetes or Docker).
        /// </exception>
        /// <remarks>
        /// <note>
        /// It is not possible to restart a service after it's been stopped.
        /// </note>
        /// <para>
        /// This is intended for internal use or managing unit test execution and is not intended 
        /// for use by the service to stop itself.
        /// </para>
        /// </remarks>
        public virtual void Stop()
        {
            lock (syncLock)
            {
                if (stopPending || !isRunning)
                {
                    return;
                }

                stopPending = true;
            }

            Terminator.Signal();
        }

        /// <summary>
        /// <para>
        /// Calls the Envoy sidecar admin API when <see cref="AutoTerminateIstioSidecar"/><c>=true</c>
        /// to ensure that the sidecar containers are terminated.  This tolerates situations where no
        /// sidecars have been injected.
        /// </para>
        /// </summary>
        private void TerminateAnySidecars()
        {
            if (!AutoTerminateIstioSidecar)
            {
                return;
            }

            using (var httpClient = new HttpClient())
            {
                try
                {
                    httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{NetworkPorts.IstioEnvoyAdmin}/quitquitquit")).WaitWithoutAggregate();
                }
                catch 
                {
                    // Ignore any errors. 
                }
            }
        }

        /// <summary>
        /// Used by services to stop themselves, specifying an optional process exit code.
        /// </summary>
        /// <param name="exitCode">The optional exit code (defaults to <b>0</b>).</param>
        /// <remarks>
        /// This works by setting <see cref="ExitCode"/> if <paramref name="exitCode"/> is non-zero,
        /// signalling process termination on another thread and then throwing a <see cref="ProgramExitException"/> 
        /// on the current thread.  This will generally cause the current thread or task to terminate
        /// immediately and any other properly implemented threads and tasks to terminate gracefully
        /// when they receive the termination signal.
        /// </remarks>
        public virtual void Exit(int exitCode = 0)
        {
            lock (syncLock)
            {
                if (exitCode != 0)
                {
                    ExitCode = exitCode;
                }

                NeonHelper.StartThread(
                    () =>
                    {
                        // $hack(jefflill):
                        //
                        // Give the Exit() method a bit of time to throw the 
                        // [ProgramExitException] to make termination handling
                        // a bit more deterministic.

                        Thread.Sleep(TimeSpan.FromSeconds(0.5));

                        try
                        {
                            Stop();
                        }
                        catch
                        {
                            // Ignoring any errors.
                        }
                    });

                throw new ProgramExitException(ExitCode);
            }
        }

        /// <summary>
        /// Called to actually implement the service.
        /// </summary>
        /// <returns>The the progam exit code.</returns>
        /// <remarks>
        /// <para>
        /// Services should perform any required initialization and then they must call <see cref="StartedAsync(NeonServiceStatus)"/>
        /// to indicate that the service should transition into the <see cref="NeonServiceStatus.Running"/>
        /// state.  This is very important because the service test fixture requires the service to be
        /// in the running state before it allows tests to proceed.  This is necessary to avoid unit test 
        /// race conditions.
        /// </para>
        /// <para>
        /// This method should return the program exit code or throw a <see cref="ProgramExitException"/>
        /// to exit with the program exit code.
        /// </para>
        /// </remarks>
        protected abstract Task<int> OnRunAsync();

        /// <summary>
        /// Used by the <see cref="EnvironmentParser"/> to retrieve environment variables
        /// via <see cref="GetEnvironmentVariable(string, string, bool)"/>.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The variable value or <c>null</c>.</returns>
        private string VariableSource(string name)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(name), nameof(name));

            lock (syncLock)
            {
                if (InProduction)
                {
                    return global::System.Environment.GetEnvironmentVariable(name) ?? (string)null;
                }

                if (environmentVariables.TryGetValue(name, out var value))
                {
                    return value;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// <para>
        /// Loads environment variables formatted as <c>NAME=VALUE</c> from a text file as service
        /// environment variables.  The file will be decrypted using <see cref="NeonVault"/> if necessary.
        /// </para>
        /// <note>
        /// Blank lines and lines beginning with '#' will be ignored.
        /// </note>
        /// </summary>
        /// <param name="path">The input file path.</param>
        /// <param name="passwordProvider">
        /// Optionally specifies the password provider function to be used to locate the
        /// password required to decrypt the source file when necessary.  The password will 
        /// use a default password provider <paramref name="passwordProvider"/> is <c>null</c>.
        /// See the remarks below.
        /// </param>
        /// <exception cref="FileNotFoundException">Thrown if the file doesn't exist.</exception>
        /// <exception cref="FormatException">Thrown for file formatting problems.</exception>
        /// <remarks>
        /// <para>
        /// The default password provider assumes that you have NeonDESKTOP installed and may be
        /// specifying passwords in the <b>~/.neonkube/passwords</b> folder (relative to the current
        /// user's home directory).  This will be harmless if you don't have NeonDESKTOP installed;
        /// it just probably won't find any passwords.
        /// </para>
        /// <para>
        /// Implement a custom password provider function if you need something different.
        /// </para>
        /// </remarks>
        public void LoadEnvironmentVariableFile(string path, Func<string, string> passwordProvider = null)
        {
            passwordProvider = passwordProvider ?? LookupPassword;

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"File not found: {path}");
            }

            var vault = new NeonVault(passwordProvider);
            var bytes = vault.Decrypt(path);

            using (var ms = new MemoryStream(bytes))
            {
                using (var reader = new StreamReader(ms))
                {
                    var lineNumber = 1;

                    foreach (var rawLine in reader.Lines())
                    {
                        var line = rawLine.Trim();

                        if (line.Length == 0 || line.StartsWith("#"))
                        {
                            continue;
                        }

                        var fields = line.Split(equalArray, 2);

                        if (fields.Length != 2)
                        {
                            throw new FormatException($"[{path}:{lineNumber}]: Invalid input: {line}");
                        }

                        var name  = fields[0].Trim();
                        var value = fields[1].Trim();

                        if (name.Length == 0)
                        {
                            throw new FormatException($"[{path}:{lineNumber}]: Setting name cannot be blank.");
                        }

                        SetEnvironmentVariable(name, value);
                    }
                }
            }
        }

        /// <summary>
        /// LOads the ambient process environment variables.
        /// </summary>
        private void LoadEnvironmentVariables()
        {
            lock (syncLock)
            {
                var variables = System.Environment.GetEnvironmentVariables();

                foreach (string key in variables.Keys)
                {
                    SetEnvironmentVariable(key, (string)variables[key]);
                }
            }
        }

        /// <summary>
        /// Sets or deletes a service environment variable.
        /// </summary>
        /// <param name="name">The variable name (case sensitive).</param>
        /// <param name="value">The variable value or <c>null</c> to remove the variable.</param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        /// <remarks>
        /// <note>
        /// Environment variable names are to be considered to be case sensitive since
        /// this is how Linux treats them and it's very common to be deploying services
        /// to Linux.
        /// </note>
        /// </remarks>
        public NeonService SetEnvironmentVariable(string name, string value)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(name), nameof(name));

            lock (syncLock)
            {
                if (value == null)
                {
                    if (environmentVariables.ContainsKey(name))
                    {
                        environmentVariables.Remove(name);
                    }
                }
                else
                {
                    environmentVariables[name] = value;
                }
            }

            return this;
        }

        /// <summary>
        /// Returns the value of an environment variable.
        /// </summary>
        /// <param name="name">The environment variable name (case sensitive).</param>
        /// <param name="def">The value to be returned when the environment variable doesn't exist (defaults to <c>null</c>).</param>
        /// <param name="redact">Optionally redact log output of the variable.</param>
        /// <returns>The variable value or <paramref name="def"/> if the variable doesn't exist.</returns>
        public string GetEnvironmentVariable(string name, string def = null, bool redact = false)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(name), nameof(name));

            return Environment.Get(name, def, redact: redact);
        }

        /// <summary>
        /// Returns all loaded enviroment variables.
        /// </summary>
        /// <returns>A dctionary mapping variable names to their values.</returns>
        public Dictionary<string, string> GetEnvironmentVariables()
        {
            lock (syncLock)
            {
                var clonedVariables = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

                foreach (var variable in environmentVariables)
                {
                    clonedVariables.Add(variable.Key, variable.Value);
                }

                return clonedVariables;
            }
        }

        /// <summary>
        /// Clears any loaded environment variables.
        /// </summary>
        public void ClearEnvironmentVariables()
        {
            lock (syncLock)
            {
                environmentVariables.Clear();
            }
        }

        /// <summary>
        /// Maps a logical configuration file path to an actual file on the
        /// local machine.  This is used for unit testing to map a file on
        /// the local workstation to the path where the service expects the
        /// find to be.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <param name="physicalPath">The physical path to the file on the local workstation.</param>
        /// <param name="passwordProvider">
        /// Optionally specifies the password provider function to be used to locate the
        /// password required to decrypt the source file when necessary.  The password will 
        /// use a default password provider <paramref name="passwordProvider"/> is <c>null</c>.
        /// See the remarks below.
        /// </param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        /// <exception cref="FileNotFoundException">Thrown if there's no file at <paramref name="physicalPath"/>.</exception>
        /// <remarks>
        /// <para>
        /// The default password provider assumes that you have NeonDESKTOP installed and may be
        /// specifying passwords in the <b>~/.neonkube/passwords</b> folder (relative to the current
        /// user's home directory).  This will be harmless if you don't have NeonDESKTOP installed;
        /// it just probably won't find any passwords.
        /// </para>
        /// <para>
        /// Implement a custom password provider function if you need something different.
        /// </para>
        /// </remarks>
        public NeonService SetConfigFilePath(string logicalPath, string physicalPath, Func<string, string> passwordProvider = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(logicalPath), nameof(logicalPath));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(physicalPath), nameof(physicalPath));

            if (!File.Exists(physicalPath))
            {
                throw new FileNotFoundException($"Physical configuration file [{physicalPath}] does not exist.");
            }

            passwordProvider = passwordProvider ?? LookupPassword;

            var vault = new NeonVault(passwordProvider);
            var bytes = vault.Decrypt(physicalPath);

            SetConfigFile(logicalPath, bytes);

            return this;
        }

        /// <summary>
        /// Maps a logical configuration file path to a temporary file holding the
        /// string contents passed encoded as UTF-8.  This is typically used for
        /// initializing confguration files for unit testing.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <param name="contents">The content string.</param>
        /// <param name="linuxLineEndings">
        /// Optionally convert any Windows style line endings (CRLF) into Linux 
        /// style endings (LF).  This defaults to <c>false</c>.
        /// </param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        public NeonService SetConfigFile(string logicalPath, string contents, bool linuxLineEndings = false)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(logicalPath), nameof(logicalPath));
            Covenant.Requires<ArgumentNullException>(contents != null, nameof(contents));

            if (linuxLineEndings)
            {
                contents = NeonHelper.ToLinuxLineEndings(contents);
            }

            lock (syncLock)
            {
                if (configFiles.TryGetValue(logicalPath, out var fileInfo))
                {
                    fileInfo.Dispose();
                }

                var tempFile = new TempFile();

                File.WriteAllText(tempFile.Path, contents);

                configFiles[logicalPath] = new FileInfo()
                {
                    PhysicalPath = tempFile.Path,
                    TempFile     = tempFile
                };
            }

            return this;
        }

        /// <summary>
        /// Maps a logical configuration file path to a temporary file holding the
        /// byte contents passed.  This is typically used initializing confguration
        /// files for unit testing.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <param name="contents">The content bytes.</param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        public NeonService SetConfigFile(string logicalPath, byte[] contents)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(logicalPath), nameof(logicalPath));
            Covenant.Requires<ArgumentNullException>(contents != null, nameof(contents));

            lock (syncLock)
            {
                if (configFiles.TryGetValue(logicalPath, out var fileInfo))
                {
                    fileInfo.Dispose();
                }

                var tempFile = new TempFile();

                File.WriteAllBytes(tempFile.Path, contents);

                configFiles[logicalPath] = new FileInfo()
                {
                    PhysicalPath = tempFile.Path,
                    TempFile     = tempFile
                };
            }

            return this;
        }

        /// <summary>
        /// Returns the physical path for the confguration file whose logical path is specified.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <returns>The physical path for the configuration file or <c>null</c> if the logical file path is not present.</returns>
        /// <remarks>
        /// <note>
        /// This method does not verify that the physical file actually exists.
        /// </note>
        /// </remarks>
        public string GetConfigFilePath(string logicalPath)
        {
            lock (syncLock)
            {
                if (InProduction)
                {
                    return logicalPath;
                }

                if (!configFiles.TryGetValue(logicalPath, out var fileInfo))
                {
                    return null;
                }

                return fileInfo.PhysicalPath;
            }
        }
    }
}
