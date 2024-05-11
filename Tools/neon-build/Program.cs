//-----------------------------------------------------------------------------
// FILE:        Program.cs
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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using Neon.Common;
using Neon.Deployment;

namespace NeonBuild
{
    /// <summary>
    /// Hosts the program entry point.
    /// </summary>
    public static partial class Program
    {
        private const string version = "1.12";

        private static readonly string usage =
$@"
Internal NEONSDK project build related utilities: v{version}

NOTE: Command line arguments and options may include references to 
      profile values, secrets and environment variables, like:

      ${{profile:NAME}}                   - profile value
      ${{secret:NAME}}                    - ""password"" property value of NAME secret
      ${{secret:NAME:SOURCE}}             - ""password""  property value of NAME secret at SOURCE
      ${{secret:NAME[PROPERTY}}           - PROPERTY value from NAME secret
      ${{secret:NAME[PROPERTY]:SOURCE}}   - PROPERTY value from NAME secret at SOURCE
      ${{env:NAME}}                       - environment variable

      For Linux, you'll need to surround these references with single quotes
      to prevent Bash from interpreting them as Bash variable references.

---------------------------------------------------------------------
neon-build version

Outputs the tool's version number.

-----------------------
neon-build clean [-all] REPO-PATH

Deletes all of the [bin] and [obj] folders within the repo and
also clears the [Build] folder as well as [.version] files located
amywhere within the repo's [$\Images] folder.

ARGUMENTS:

    REPO-PATH       - Path to the GitHub repository root folder.

OPTIONS:

    --build-cache   - Clears the [Build-cache] folder too.
    --version       - Clears the Docker image [.version] files
    --all           - Implies [--build-cache] and [--version]

---------------------------------------------------------------------
neon-build clean-generated-cs TARGET-FOLDER

Deletes any [**/obj/**/*.cs] files present in a project or solution.

Pass TARGET-FOLDER as the directory holding the project or solution.

These files are generated for projects by custom build targets and may result in duplicate
symbol definition compiler errors because the C# compiler include these for all build
configurations, not just the current config and unfortunately, cleaning the solution or
project doesn't remove these files either.

---------------------------------------------------------------------
neon-build kill-vs

Kills any Visual Studio (devenv.exe) processes.  This is useful because
some devenv processes run for maybe a minute after Visual Studio is closed,
and waiting for this to happen before running build/nuget related scripts
is annoying.

WARNING! Any unsaved changes in VS will be lost.

---------------------------------------------------------------------
neon-build gzip SOURCE TARGET

Compresses a file using GZIP if the target doesn't exist or is
older than the source.

ARGUMENTS:

    SOURCE          - Path to the (uncompressed) source file
    TARGET          - Path to the (compressed) target file

---------------------------------------------------------------------
neon-build copy SOURCE TARGET

Copies a file if the target doesn't exist or is older than the source.

ARGUMENTS:

    SOURCE          - Path to the (uncompressed) source file
    TARGET          - Path to the (compressed) target file

---------------------------------------------------------------------
neon-build replace PATH OLD NEW

Searches and replaces instances of case sensitive OLD string and
replaces them with the NEW string in the file at PATH, writing
any changes to the same file.

---------------------------------------------------------------------
neon-build read-version [-n] CSPATH CONSTANT

Used to read a version constant from a C# source file.

ARGUMENTS:

    CSPATH          - Path to the source file defining the version constant
    CONSTANT        - Name of the the constant to be read

OPTIONS:

    -n              - Omit the line terminator when writing the output

REMARKS:

The value of the constant read is written to STDOUT.

NOTE: The constant must be formatted exactly like:

    public const string NAME = ""VERSION"";

within the C# source file to be parsed correctly where [NAME] is the constant
name and [VERSION] will be returned as value.

---------------------------------------
neon-build dotnet-version GLOBAL-JSON-PATH

Calls <b>dotnet --info</b> with the working directory holding the <b>global.json</b> 
file specified on the command line and parses the .NET SDK version (like ""7.0.102"")
as well as the .NET runtime version (like ""7.0.2"").

The command writes the SDK version to the first output line and the corresponding
runtime version to the second line.

----------------------------------------------------
neon-build pack-version CSPATH CONSTANT CSPROJ

Updates the specified library CSPROJ file's package version to the value
of a constant named CONSTANT from the C# source file at CSPATH.

NOTE: The constant must be formatted exactly like:

    public const string NAME = ""VALUE"";

ARGUMENTS:

    CSPATH      - Path to the C# file defining the version constant
    CONSTANT    - Constant name
    CSPROJ      - Path to the CSPROJ file being modified
    
---------------------------------------------------
neon-build shfb SHFB-FOLDER SITE-FOLDER [OPTIONS]

Munges the web help files generated by Sandcastle Helper File Builder (SHFB)
by renaming HTML files named with GUIDs, inserting Google Analytics [gtag.js]
files if requested, relocating HTML files to the same directory as the index
file and then fixing up all of the relative links.

ARGUMENTS:

    SHFB-FOLDER     - Path to the SHFB project directory in the source repo
    SITE-FOLDER     - Path to the SHFB site output directory

OPTIONS:

    --dryrun        - Don't actually modify the help content.  Just verify
                      that everything looks ready.

    --gtag=PATH     - Optionally specifies the path to the Google Analytics
                      [gtag.js] file to insert into the help files for
                      visitor tracking purposes.

    --styles=FOLDER - Optionally specifies a folder with CSS style files
                      that will be copied to the site [styles] folder.

-------------------------------------------
neon-build embed-check PROJECT EMBED-FOLDER

ARGUMENTS:

    PROJECT         - Path to a [*.csproj] file
    EMBED-FOLDER    - Path to the folder with embedded resource files

Verifies that a C# project file includes embedded resource file references
to all of the files within EMBED-FOLDER (recurively).  This is handy for
ensuring that no files are present that aren't being embeded.

----------------------------------------
neon-build dotnet [OPTIONS] ARGS

Executes the [dotnet] command with most of the environment variables 
removed.  This allows [dotnet] commands to be executed within the 
context of a Visual Studio build.

Any options and/or arguments are passed thru as-is to [dotnet].

----------------------------------------
neon-build hexdump

Reads standard input as binary, converts it to hex and writes it to
standard output as one line.

----------------------------------------
neon-build download URI TARGET-PATH

Downloads a file from a URI and writes it to a file.  Note that
S3 URIs are not supported.

ARGUMENTS:

    URI         - The S3 source URI (formatted as http://... or https://...
    TARGET-PATH - Path to the output file

----------------------------------------
neon-build download-const-uri ASSEMBLY-PATH TYPE-NAME CONST-NAME TARGET-PATH

Extracts a constant value from an assembly file and then downloads
the URI and writes it to a file.  Note that S3 URIs are not supported.

ARGUMENTS:

    ASSEMBLY-PATH   - Path to the assembly with the constant
    TYPE-NAME       - Fully qualified name of the type with the constant
    CONST-NAME      - Name of the constant within the class
    TARGET-PATH     - Path to the output file

----------------------------------------
neon-build publish-folder SOURCE-FOLDER TARGET-FOLDER

Recurisively copies the files in the SOURCE-FOLDER to the TARGET-FOLDER,
removing any existing target files or creating the TARGET-FOLDER if it
doesn't already exist.

ARGUMENTS:

    SOURCE-FOLDER   - Path to the source folder
    TARGET-FOLDER   - Path to the target folder

----------------------------------------
neon-build publish-files SOURCE-PATTERN TARGET-FOLDER [--exclude-kustomize] [--no-delete]

Copies files matching the SOURCE-PATTERN to TARGET-FOLDER, removing any
existing files there or creating the target folder when it doesn't exist.

ARGUMENTS:

    SOURCE-PATTERN  - Path and source file pattern
    TARGET-FOLDER   - Path to the target folder

OPTIONS:

    --exclude-kustomize     - Exclude [kustomization.yaml] files
    --no-delete             - Don't delete the folder

----------------------------------------
neon-build rm FILE-PATH

Removes files matching a pattern if they exist.

ARGUMENTS:

    FILE-PATH       - Path to the file being deleted, optionally including
                      ""?"" and/or ""*"" wildcards

----------------------------------------
neon-build rmdir FOLDER-PATH

Removes a directory (rercursively) if it exists.

ARGUMENTS:

    FOLDER-PATH     - Path to the folder being deleted

----------------------------------------
neon-build kustomize build SOURCE-FOLDER TARGET-PATH

Runs [kustomize build SOURCE-FOLDER] and writes the output to TARGET-PATH,
creating any parent directories as required.

ARGUMENTS:

    SOURCE-FOLDER   - Path to the source folder
    TARGET-PATH     - Path to the output file
";
        private static CommandLine commandLine;

        /// <summary>
        /// This is the program entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        public static void Main(string[] args)
        {
            string      repoRoot;
            string      buildFolder;

            // $hack(jefflill):
            //
            // We hardcoding our own profile client for the time being.  Eventually,
            // we'll need to support custom or retail profile clients somehow.
            //
            // This is required by: CommandLine.Preprocess()

            NeonHelper.ServiceContainer.AddSingleton<IProfileClient>(new MaintainerProfile());

            commandLine = new CommandLine(args).Preprocess();

            var command = commandLine.Arguments.FirstOrDefault();

            if (command != null)
            {
                command = command.ToLowerInvariant();
            }

            if (commandLine.Arguments.Length == 0 || commandLine.HasHelpOption)
            {
                Console.WriteLine(usage);
                Program.Exit(commandLine.HasHelpOption ? 0 : -1);
            }

            try
            {
                Program.NeonSdkRepoPath = Environment.GetEnvironmentVariable("NF_ROOT");

                if (string.IsNullOrEmpty(Program.NeonSdkRepoPath) || !Directory.Exists(Program.NeonSdkRepoPath))
                {
                    Console.Error.WriteLine("*** ERROR: NF_ROOT environment variable does not reference the local NEONSDK repostory.");
                    Program.Exit(1);
                }

                // Handle the commands.

                switch (command)
                {
                    case "version":

                        Console.WriteLine(version);
                        break;

                    case "clean":

                        repoRoot = commandLine.Arguments.ElementAtOrDefault(1);

                        if (string.IsNullOrEmpty(repoRoot))
                        {
                            Console.Error.WriteLine("*** ERROR: REPO-ROOT argument is required.");
                            Program.Exit(-1);
                        }

                        // Esure that the folder specified includes at least one solution file (*.sln).
                        // This will help prevent developers from clean their whole drive or other
                        // important folders by accident.
                        //
                        // $todo(jefflill): This almost happened to me but I was able to the kill
                        //                  the process in time.

                        if (!Directory.Exists(repoRoot))
                        {
                            Console.Error.WriteLine($"*** ERROR: [{repoRoot}] folder does not exist.");
                            Program.Exit(1);
                        }

                        if (!Directory.GetFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly).Any())
                        {
                            Console.Error.WriteLine($"*** ERROR: [{repoRoot}] folder does not include a Visual Studio solution.");
                            Console.Error.WriteLine($"           This is required for your safety.");
                            Program.Exit(1);
                        }

                        // Perform the operation.

                        buildFolder = Path.Combine(repoRoot, "Build");

                        if (Directory.Exists(buildFolder))
                        {
                            NeonHelper.DeleteFolderContents(buildFolder);
                        }

                        if (commandLine.HasOption("--build-cache") || commandLine.HasOption("--all"))
                        {
                            var buildCacheFolder = Path.Combine(repoRoot, "Build-cache");

                            if (Directory.Exists(buildCacheFolder))
                            {
                                NeonHelper.DeleteFolderContents(buildCacheFolder);
                            }
                        }

                        var cadenceResourcesPath = Path.Combine(repoRoot, "Lib", "Neon.Cadence", "Resources");

                        if (Directory.Exists(cadenceResourcesPath))
                        {
                            NeonHelper.DeleteFolder(cadenceResourcesPath);
                        }

                        foreach (var folder in Directory.EnumerateDirectories(repoRoot, "bin", SearchOption.AllDirectories))
                        {
                            if (Directory.Exists(folder))
                            {
                                NeonHelper.DeleteFolder(folder);
                            }
                        }

                        foreach (var folder in Directory.EnumerateDirectories(repoRoot, "obj", SearchOption.AllDirectories))
                        {
                            if (Directory.Exists(folder))
                            {
                                NeonHelper.DeleteFolder(folder);
                            }
                        }

                        foreach (var folder in Directory.EnumerateDirectories(repoRoot, "node_modules", SearchOption.AllDirectories))
                        {
                            if (Directory.Exists(folder))
                            {
                                NeonHelper.DeleteFolder(folder);
                            }

                            var parentDir = Directory.GetParent(folder);

                            if (parentDir.ToString().Contains(repoRoot))
                            {
                                var packageLock = Path.Combine(parentDir.ToString(), "package-lock.json");

                                if (File.Exists(packageLock))
                                {
                                    NeonHelper.DeleteFile(packageLock);
                                }
                            }
                        }

                        // Delete any [$/Images/**/.version] files.  These files are generated for NeonCLOUD
                        // during cluster image builds.  These files are within [.gitignore] and should really
                        // be cleaned before builds etc.

                        // $hack(jefflill):
                        // 
                        // This is a bit of a hack since only the NeonCLOUD repo currently generates these files,
                        // but this is somewhat carefully coded to not cause problems for the other repos.  Just
                        // be sure that those repos don't include a root [$/Images] folder that has [.version]
                        // files beneath it used for purposes other than building Docker images.

                        if (commandLine.HasOption("--version") || commandLine.HasOption("--all"))
                        {
                            var imageFolder = Path.Combine(repoRoot, "Images");

                            if (Directory.Exists(imageFolder))
                            {
                                foreach (var file in Directory.GetFiles(imageFolder, ".version", SearchOption.AllDirectories))
                                {
                                    NeonHelper.DeleteFile(file);
                                }
                            }
                        }
                        break;

                    case "clean-generated-cs":

                        var targetFolder = commandLine.Arguments.ElementAtOrDefault(1);

                        // Remove any trailing "/" or "\\".

                        if (targetFolder.EndsWith('/') || targetFolder.EndsWith("\\"))
                        {
                            targetFolder = targetFolder.Substring(0, targetFolder.Length - 1);
                        }

                        if (string.IsNullOrEmpty(targetFolder))
                        {
                            Console.Error.WriteLine("*** ERROR: TARGET-FOLDER argument is required.");
                            Program.Exit(-1);
                        }

                        // $hack(jefflill): Remove any double quotes (I'm not sure why these are being added to the EXEC args).

                        targetFolder = targetFolder.Replace("\"", string.Empty);

                        // Normalize backslashes to forward slashes.

                        targetFolder = targetFolder.Replace("\\", "/");

                        // Delete matching files.

                        var globPattern = GlobPattern.Parse($"{targetFolder}/**/obj/**/*.cs", caseInsensitive: true);

                        foreach (var file in Directory.GetFiles(targetFolder, "*.cs", SearchOption.AllDirectories))
                        {
                            var normalizedFile = file.Replace("\\", "/");

                            // Note that the glob pattern will match files named like:
                            //
                            //      C:/src/NEONSDK/Lib/Neon.Common/Collections/ObjectDictionary.cs
                            //
                            // which we don't want to remove.  We're going to mitigate this by 
                            // ensuring that the file name includes an "/obj/" directory in
                            // its path.  We're going to make this a case sensitive check since
                            // .NET builds always names these folders in lowercase.

                            if (globPattern.IsMatch(normalizedFile) && normalizedFile.Contains("/obj/"))
                            {
                                try
                                {
                                    File.Delete(normalizedFile);
                                }
                                catch
                                {
                                    // It seems that sometimes these files can be open at times, perhaps
                                    // when another build task is generating them or perhaps an analyzer
                                    // has it open.
                                    //
                                    // We're just going to ignore this and hope for the best.
                                }
                            }
                        }
                        break;

                    case "kill-vs":

                        foreach (var process in Process.GetProcessesByName("devenv"))
                        {
                            try
                            {
                                process.Kill(entireProcessTree: true);
                            }
                            catch
                            {
                                // Ignore any errors here because processes may have
                                // already exited before we had the chance to kill them.
                            }
                        }
                        break;

                    case "copy":

                        {
                            var sourcePath = commandLine.Arguments.ElementAtOrDefault(1);
                            var targetPath = commandLine.Arguments.ElementAtOrDefault(2);

                            if (sourcePath == null)
                            {
                                Console.Error.WriteLine("*** ERROR: SOURCE argument is required.");
                                Program.Exit(-1);
                            }

                            if (targetPath == null)
                            {
                                Console.Error.WriteLine("*** ERROR: TARGET argument is required.");
                                Program.Exit(-1);
                            }

                            if (!File.Exists(sourcePath))
                            {
                                Console.Error.WriteLine($"*** ERROR: SOURCE file [{sourcePath}] does not exist.");
                                Program.Exit(1);
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                            if (File.Exists(targetPath) && File.GetLastWriteTimeUtc(targetPath) > File.GetLastWriteTimeUtc(sourcePath))
                            {
                                Console.WriteLine($"File [{targetPath}] is up to date.");
                                Program.Exit(0);
                            }

                            Console.WriteLine($"COPY: [{sourcePath}] --> [{targetPath}].");
                            File.Copy(sourcePath, targetPath);
                        }
                        break;

                    case "gzip":

                        Gzip(commandLine);
                        break;

                    case "read-version":

                        ReadVersion(commandLine);
                        break;

                    case "dotnet-version":

                        DotnetVersion(commandLine);
                        break;

                    case "pack-version":

                        PackVersion(commandLine);
                        break;

                    case "shfb":

                        Shfb(commandLine);
                        break;

                    case "embed-check":

                        EmbedCheck(commandLine);
                        break;

                    case "dotnet":

                        Dotnet(commandLine);
                        break;

                    case "hexdump":

                        using (var input = Console.OpenStandardInput())
                        {
                            var buffer = new byte[1];

                            while (input.Read(buffer, 0, 1) > 0)
                            {
                                Console.Write(NeonHelper.ToHex(buffer[0]));
                            }
                        }
                        break;

                    case "download":

                        Download(commandLine);
                        break;

                    case "download-const-uri":

                        DownloadConstUri(commandLine);
                        break;

                    case "publish-folder":

                        PublishFolder(commandLine);
                        break;

                    case "publish-files":

                        PublishFiles(commandLine);
                        break;

                    case "rm":

                        Rm(commandLine);
                        break;

                    case "rmdir":

                        Rmdir(commandLine);
                        break;

                    case "kustomize":

                        Kustomize(commandLine);
                        break;

                    case "replace":

                        Replace(commandLine);
                        break;

                    default:

                        Console.Error.WriteLine($"*** ERROR: Unexpected command [{command}].");
                        Program.Exit(-1);
                        break;
                }

                Program.Exit(0);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"*** ERROR: {NeonHelper.ExceptionError(e)}");
                Program.Exit(1);
            }
        }

        /// <summary>
        /// Returns the path to the NEONSDK local repository root folder.
        /// </summary>
        public static string NeonSdkRepoPath { get; private set; }

        /// <summary>
        /// Terminates the program with a specified exit code.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public static void Exit(int exitCode)
        {
            Environment.Exit(exitCode);
        }

        /// <summary>
        /// Reads a version number from a C# source file.
        /// </summary>
        /// <param name="csPath">Path to the C# source file.</param>
        /// <param name="constant">Name of the version constant.</param>
        /// <returns>The version string.</returns>
        private static string ReadVersion(string csPath, string constant)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(csPath), nameof(csPath));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(constant), nameof(constant));

            // We're simply going to scan the source file for the first line 
            // that looks like the constant definition.

            var match = $"public const string {constant} =";

            using (var reader = new StreamReader(csPath))
            {
                foreach (var line in reader.Lines())
                {
                    if (line.Trim().StartsWith(match))
                    {
                        var fields  = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var version = fields[5];

                        // Strip off the double quotes and semicolon

                        return version.Substring(1, version.Length - 3);
                    }
                }
            }

            throw new Exception($" Cannot locate the constant [{constant}] in [{csPath}].");
        }
    }
}
