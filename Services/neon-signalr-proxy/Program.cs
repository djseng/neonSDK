//-----------------------------------------------------------------------------
// FILE:        Program.cs
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Diagnostics;
using Neon.Service;

using CommandLine;

using Prometheus.DotNetRuntime;

namespace NeonSignalRProxy
{
    /// <summary>
    /// Holds the global program state.
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// Returns the program's service implementation.
        /// </summary>
        public static Service Service { get; private set; }

        /// <summary>
        /// Command line options.
        /// </summary>
        public class Options
        {
            /// <summary>
            /// Optional <b>--config-file</b> command line option.
            /// </summary>
            [Option('c', "config-file", Required = false, HelpText = "Set the config file.")]
            public string ConfigFile { get; set; }
        }

        /// <summary>
        /// The program entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        public static async Task Main(string[] args)
        {
            var options = Parser.Default.ParseArguments<Options>(args).Value;

            try
            {
                Service = new Service(Service.ServiceName);

                if (!string.IsNullOrEmpty(options.ConfigFile))
                {
                    using (StreamReader reader = new StreamReader(new FileStream(options.ConfigFile, FileMode.Open, FileAccess.Read)))
                    {
                        Service.SetConfigFile(Service.ConfigFile, await reader.ReadToEndAsync());
                    }
                }

                if (!NeonHelper.IsDevWorkstation)
                {
                    Service.MetricsOptions.Mode         = MetricsMode.Scrape;
                    Service.MetricsOptions.GetCollector =
                        () =>
                        {
                            return DotNetRuntimeStatsBuilder
                                .Default()
                                .StartCollecting();
                        };
                }   

                Environment.Exit(await Service.RunAsync());
            }
            catch (Exception e)
            {
                if (Service?.Logger != null)
                {
                    Service.Logger.LogCriticalEx(e);
                }
                else
                {
                    // Logging isn't initialized, so fallback to just writing to SDTERR.

                    Console.Error.WriteLine("CRITICAL: " + NeonHelper.ExceptionError(e, stackTrace: true));

                    if (e.StackTrace != null)
                    {
                        Console.Error.WriteLine("STACK TRACE:");
                        Console.Error.WriteLine(e.StackTrace);
                    }
                }

                Environment.Exit(1);
            }
        }
    }
}
