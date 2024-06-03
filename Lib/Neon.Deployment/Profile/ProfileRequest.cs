//-----------------------------------------------------------------------------
// FILE:        ProfileRequest.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;

namespace Neon.Deployment
{
    /// <summary>
    /// Abstracts Neon Profile Service named pipe command requests.
    /// </summary>
    public class ProfileRequest : IProfileRequest
    {
        //---------------------------------------------------------------------
        // Static members

        private static readonly char[]      commaArray = new char[] { ',' };

        /// <summary>
        /// Creates a command with optional arguments.
        /// </summary>
        /// <param name="command">
        /// <para>
        /// The command name.
        /// </para>
        /// <note>
        /// Argument names and values may not include commas.
        /// </note>
        /// </param>
        /// <param name="args">The optional arguments.</param>
        /// <returns>The <see cref="ProfileRequest"/>.</returns>
        public static ProfileRequest Create(string command, Dictionary<string, string> args = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(command), nameof(command));

            args = args ?? new Dictionary<string, string>();

            // $note(jefflill):
            //
            // We need to enforce some somewhat arbitrary restrictions on the command
            // and arguments because I didn't encode theses as JSON and its not possible
            // to use the new [IProfileClient.Call()] to pass values that might not fit
            // into our original message format.

            if (command.Contains(':'))
            {
                throw new ArgumentException($"Command [{command}] may not include a ':'.");
            }

            foreach (var item in args)
            {
                if (item.Key == null)
                {
                    throw new ArgumentNullException("NULL argument names are not allowed.");
                }

                if (item.Value == null)
                {
                    throw new ArgumentNullException($"Argument [{item.Key}] has a NULL value.");
                }

                if (item.Key.Contains('='))
                {
                    throw new ArgumentException($"Argument name [{item.Key}] is invalid because it includes a '='.");
                }

                if (item.Value.Contains(','))
                {
                    throw new ArgumentException($"Argument [{item.Key}={item.Value}] is invalid because the value includes a comma.");
                }
            }

            return new ProfileRequest()
            {
                Command = command,
                Args    = args
            };
        }

        /// <summary>
        /// Parses a request from a line of text read from the named pipe.
        /// </summary>
        /// <param name="commandLine">The command line.</param>
        /// <returns>The <see cref="ProfileRequest"/>.</returns>
        /// <exception cref="FormatException">Thrown for invalid command lines.</exception>
        public static ProfileRequest Parse(string commandLine)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(commandLine), nameof(commandLine));

            var colonPos = commandLine.IndexOf(':');

            if (colonPos == -1)
            {
                throw new FormatException("Invalid profile service command line: Command colon is missing.");
            }

            var command = commandLine.Substring(0, colonPos).Trim();

            if (command == string.Empty)
            {
                throw new FormatException("Invalid profile service command line: Command is empty.");
            }

            var request = new ProfileRequest() { Command = command };
            var args    = commandLine.Substring(colonPos + 1).Split(commaArray, StringSplitOptions.RemoveEmptyEntries);

            foreach (var arg in args)
            {
                var fields = arg.Split('=');

                if (fields.Length != 2)
                {
                    throw new FormatException("Invalid profile service command line: Malformed argument");
                }

                request.Args[fields[0].Trim()] = fields[1].Trim();
            }

            return request;
        }

        //---------------------------------------------------------------------
        // Instance members

        /// <summary>
        /// Private constructor.
        /// </summary>
        private ProfileRequest()
        {
        }

        /// <summary>
        /// Returns the command.
        /// </summary>
        public string Command { get; private set; }

        /// <summary>
        /// Returns the standard command arguments.
        /// </summary>
        public Dictionary<string, string> Args { get; private set; } = new Dictionary<string, string>();

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder();

            if (Args != null && this.Args.Count > 0)
            {
                foreach (var arg in Args)
                {
                    sb.AppendWithSeparator($"{arg.Key}={arg.Value}", ", ");
                }
            }

            return $"{Command}:" + (sb.Length > 0 ? " " : "") + sb.ToString();
        }
    }
}
