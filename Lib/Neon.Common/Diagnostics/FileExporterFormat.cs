//-----------------------------------------------------------------------------
// FILE:        FileLogExporterFormat.cs
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

namespace Neon.Diagnostics
{
    /// <summary>
    /// Enumerates <see cref="FileLogExporter"/> output formats.
    /// </summary>
    public enum FileLogExporterFormat
    {
        /// <summary>
        /// Outputs logs in a human readable format.  This is the
        /// default.
        /// </summary>
        Human = 0,

        /// <summary>
        /// Outputs logs as single-line JSON.
        /// </summary>
        Json
    }
}
