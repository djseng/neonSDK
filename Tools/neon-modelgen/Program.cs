﻿//-----------------------------------------------------------------------------
// FILE:	    Program.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright © 2005-2022 by NEONFORGE LLC.  All rights reserved.
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
using System.Reflection;
using System.Threading.Tasks;

using Neon;
using Neon.Common;
using Neon.ModelGen;

namespace NeonModelGen
{
    /// <summary>
    /// Implements the <c>neon-modelgen</c> tool.
    /// </summary>
    public static class Program
    {
        private static readonly string usage = $@"
neon-modelgen v{Build.NeonSdkVersion}
-----------------------------------------
Generates C# source code for data and service models defined as interfaces
within a compiled assembly.

USAGE:

    neon-modelgen [OPTIONS] ASSEMBLY-PATH [OUTPUT-PATH]

ARGUMENTS:

    ASSEMBLY-PATH       - Path to the assembly being scanned.

    OUTPUT-PATH         - Optional path to the output file, otherwise
                          the generated code will be written to STDOUT.

OPTIONS:

    --source-namespace=VALUE    - Specifies the namespace to be used when
                                  scanning for models.  By default, all
                                  classes within the assembly wll be scanned.

    --target-namespace=VALUE    - Specifies the namespace to be used when
                                  generating the models.  This overrides 
                                  the original type namespaces as scanned
                                  from the source assembly.

    --persisted                 - Generate database persistence related code.

                                  NOTE: This only supports Couchbase

    --ux=xaml                   - Generate additional code for the specified
                                  UX framework.  Currently, only [xaml] is
                                  supported

    --no-services               - Don't generate any service clients.

    --targets=LIST              - Specifies the comma separated list of target 
                                  names.  Any input models that are not tagged
                                  with one of these names will not be generated.

    --debug-allow-stepinto      - Indicates that generated class methods will
                                  not include the [DebuggerStepThrough]
                                  attribute.  This will allow the debugger to
                                  step into the generated methods.

    --v1compatible              - Generates models using the v1.x compatible
                                  ""__T"" property name rather than ""T$$""
                                  which is generated by ModelGen v2+.

    --log=PATH                  - Optionally outputs any errors to the specified 
                                  log file and supresses potentially spurious
                                  from the standard output and exit code.

REMARKS:

This command is used to generate enhanced JSON based data models and
REST API clients suitable for applications based on flexible noSQL
style design conventions.  See this GitHub issue for more information:

    https://github.com/nforgeio/neonKUBE/issues/463

";
        private static bool     suppressSpurious = false;
        private static string   logPath          = null;

        /// <summary>
        /// Prints the command help.
        /// </summary>
        public static void Help()
        {
            Console.WriteLine(usage);
        }

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>The program exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            var commandLine = new CommandLine(args).Preprocess();

            if (commandLine.HasHelpOption)
            {
                Help();
                Environment.Exit(0);
            }

            if (commandLine.Arguments.Length == 0)
            {
                Help();
                Environment.Exit(1);
            }

            logPath          = commandLine.GetOption("--log");
            suppressSpurious = !string.IsNullOrEmpty(logPath);

            try
            {
                ClearLogFile();
            }
            catch (Exception e)
            {
                // Something must be wrong with the log path so explicitly
                // return the error.

                Console.Error.WriteLine(NeonHelper.ExceptionError(e));
                Environment.Exit(1);
            }

            var assemblyPath = commandLine.Arguments.ElementAtOrDefault(0);
            var outputPath   = commandLine.Arguments.ElementAtOrDefault(1);
            var targets      = new List<string>();

            var targetOption = commandLine.GetOption("--targets");

            if (!string.IsNullOrEmpty(targetOption))
            {
                foreach (var target in targetOption.Split( ',', StringSplitOptions.RemoveEmptyEntries))
                {
                    targets.Add(target);
                }
            }

            var settings = new ModelGeneratorSettings(targets.ToArray())
            {
                SourceNamespace       = commandLine.GetOption("--source-namespace"),
                TargetNamespace       = commandLine.GetOption("--target-namespace"),
                Persisted             = commandLine.HasOption("--persisted"),
                NoServiceClients      = commandLine.HasOption("--no-services"),
                AllowDebuggerStepInto = commandLine.HasOption("--debug-allow-stepinto"),
                V1Compatible          = commandLine.HasOption("--v1compatible")
            };

            var ux = commandLine.GetOption("--ux");

            if (ux != null)
            {
                if (ux.Equals("xaml", StringComparison.InvariantCultureIgnoreCase))
                {
                    settings.UxFramework = UxFrameworks.Xaml;
                }
                else
                {
                    LogError($"*** ERROR: [--ux={ux}] does not specify one of the supported UX frameworks: XAML", critical: true);
                    Program.Exit(1);
                }
            }

            var assembly       = (Assembly)null;
            var modelGenerator = (ModelGenerator)null;
            var output         = (ModelGeneratorOutput)null;

            try
            {
                assembly = Assembly.LoadFile(Path.GetFullPath(assemblyPath));
            }
            catch (Exception e)
            {
                // These are going to be spurious errors when the project is 
                // configured correctly due to a chicken-and-the-egg issue
                // We're going to consider these as non-critical so they won't 
                // show up in the Visual Studio error list and be annoying.

                LogError(NeonHelper.ExceptionError(e), critical: false);
                Program.Exit(1);
            }

            try
            {
                modelGenerator = new ModelGenerator(settings);
                output         = modelGenerator.Generate(assembly);
            }
            catch (Exception e)
            {
                LogError(NeonHelper.ExceptionError(e), critical: true);
                Program.Exit(1);
            }

            if (output.HasErrors)
            {
                foreach (var error in output.Errors)
                {
                    LogError(error);
                }

                Program.Exit(1);
            }

            try
            {
                if (!string.IsNullOrEmpty(outputPath))
                {
                    // Ensure that all of the parent folders exist.

                    var folderPath = Path.GetDirectoryName(outputPath);

                    Directory.CreateDirectory(folderPath);

                    // Don't write the output file if its contents are already
                    // the same as the generated output.  This will help reduce
                    // wear on SSDs and also make things a tiny bit easier for
                    // source control.

                    if (!File.Exists(outputPath) || File.ReadAllText(outputPath) != output.SourceCode)
                    {
                        File.WriteAllText(outputPath, output.SourceCode);
                    }
                }
                else
                {
                    Console.Write(output.SourceCode);
                }
            }
            catch (ProgramExitException e)
            {
                return e.ExitCode;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"*** ERROR: {NeonHelper.ExceptionError(e)}");
                Console.Error.WriteLine(e.StackTrace);
                Console.Error.WriteLine(string.Empty);
                return 1;
            }

            return await Task.FromResult(0);
        }

        /// <summary>
        /// Writes an error message to the console or log file, if one is specified.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="critical">Optionally forces the error message to be written to STDOUT even for supression mode.</param>
        private static void LogError(string message, bool critical = false)
        {
            if (suppressSpurious)
            {
                if (critical)
                {
                    Console.Error.WriteLine(message);
                }

                LogToFile(message);
            }
            else
            {
                Console.Error.WriteLine(message);
            }
        }

        /// <summary>
        /// Removes the log file if it exists.
        /// </summary>
        private static void ClearLogFile()
        {
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }

        /// <summary>
        /// Logs an error message to the log file, creating the directory and file if necessary.
        /// </summary>
        /// <param name="message">The error message.</param>
        private static void LogToFile(string message)
        {
            if (!string.IsNullOrEmpty(logPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"{message}{Environment.NewLine}");
            }
        }

        /// <summary>
        /// Exits the program returning the specified process exit code.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public static void Exit(int exitCode)
        {
            throw new ProgramExitException(exitCode);
        }
    }
}
