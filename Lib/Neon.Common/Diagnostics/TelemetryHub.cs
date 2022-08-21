﻿//-----------------------------------------------------------------------------
// FILE:	    TelemetryHub.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Neon.Common;

namespace Neon.Diagnostics
{
    /// <summary>
    /// <para>
    /// Provides a standard global place where libraries and applications can gain access to
    /// the application's <see cref="ActivitySource"/> and <see cref="LoggerFactory"/> for 
    /// recording traces and logs.  Applications that enable tracing and logging should set
    /// <see cref="ActivitySource"/> to the global program activity source and <see cref="LoggerFactory"/>
    /// to the program's global <see cref="ILoggerFactory"/> so that Neon (and perhaps other) 
    /// libraries can emit traces and logs.
    /// </para>
    /// <note>
    /// The <b>Neon.Service.NeonService</b> class initializes these properties by default when
    /// used by programs.
    /// </note>
    /// <para>
    /// The <see cref="ParseLogLevel(string, LogLevel)"/> utility can be used to parse a log level
    /// string obtained from an environment variable or elsewhere.
    /// </para>
    /// </summary>
    public static class TelemetryHub
    {
        /// <summary>
        /// Holds the global activity source used by Neon and perhaps other libraries for emitting
        /// traces.  This defaults to <c>null</c> which means that libraries won't emit any
        /// traces by default.  Programs should set this after configuring tracing.
        /// </summary>
        public static ActivitySource ActivitySource { private get; set; } = null;

        /// <summary>
        /// Holds the global <see cref="ILoggerFactory"/> used by the Neon and perhaps other libraries
        /// for emitting logs.  This defaults to <c>null</c> which means that libraries won't emit any
        /// logs by default.  Programs should set this after configuring logging.
        /// </summary>
        public static ILoggerFactory LoggerFactory { private get; set; } = null;

        /// <summary>
        /// <para>
        /// Returns an <see cref="ILogger"/> using the fully qualified name of the <typeparamref name="T"/>
        /// type as the logger's category name.
        /// </para>
        /// <note>
        /// This returns an internal do-nothing logger when <see cref="LoggerFactory"/> is <c>null</c>.
        /// </note>
        /// </summary>
        /// <typeparam name="T">Identifies the type whose fully-qualified name is to be used as the logger's category name.</typeparam>
        /// <param name="isLogEnabledFunc">Optionally specifies a function that controls whether a do-nothing logger should be returned.</param>
        /// <returns>The <see cref="ILogger"/>.</returns>
        public static ILogger CreateLogger<T>(Func<bool> isLogEnabledFunc = null)
        {
            if (isLogEnabledFunc != null && !isLogEnabledFunc())
            {
                return new NullLogger();
            }

            if (LoggerFactory == null)
            {
                return new NullLogger();
            }
            else
            {
                return LoggerFactory.CreateLogger<T>();
            }
        }

        /// <summary>
        /// <para>
        /// Returns an <see cref="ILogger"/> using the category name passed.
        /// </para>
        /// <note>
        /// This returns an internal do-nothing logger when <see cref="LoggerFactory"/> is <c>null</c>.
        /// </note>
        /// </summary>
        /// <param name="categoryName">Specifies the logger's category name.</param>
        /// <param name="isLogEnabledFunc">Optionally specifies a function that controls whether a do-nothing logger should be returned.</param>
        /// <returns>The <see cref="ILogger"/>.</returns>
        public static ILogger CreateLogger(string categoryName, Func<bool> isLogEnabledFunc = null)
        {
            if (isLogEnabledFunc != null && !isLogEnabledFunc())
            {
                return new NullLogger();
            }

            categoryName ??= "DEFAULT";

            if (LoggerFactory == null)
            {
                return new NullLogger();
            }
            else
            {
                return LoggerFactory.CreateLogger(categoryName);
            }
        }

        /// <summary>
        /// Parses a <see cref="LogLevel"/> from a string.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="default">The default value to return when <paramref name="input"/> is <c>null</c> or invalid.</param>
        /// <returns></returns>
        public static LogLevel ParseLogLevel(string input, LogLevel @default = LogLevel.Information)
        {
            if (input == null)
            {
                return @default;
            }

            switch (input.ToUpperInvariant())
            {
                case "CRITICAL":

                    return LogLevel.Critical;

                case "ERROR":

                    return LogLevel.Error;

                case "WARN":    // Backwards compatibility
                case "WARNING":

                    return LogLevel.Warning;

                case "INFO":    // Backwards compatibility
                case "INFORMATION":

                    return LogLevel.Information; ;

                case "DEBUG":

                    return LogLevel.Debug;

                case "TRACE":

                    return LogLevel.Trace;

                case "NONE":

                    return LogLevel.None;

                default:

                    return @default;
            }
        }
    }
}
