﻿//-----------------------------------------------------------------------------
// FILE:	    IGeneratedServiceClient.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright © 2005-2023 by NEONFORGE LLC.  All rights reserved.
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
using System.ComponentModel;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;

namespace Neon.Data
{
    /// <summary>
    /// Used to identify a generated ASP.NET service client.
    /// </summary>
    public interface IGeneratedServiceClient
    {
        /// <summary>
        /// <para>
        /// Returns the version of the <b>Neon.ModelGen</b> assembly that generated
        /// this code plus the generated code schema version.  This is formatted like:
        /// </para>
        /// <code>
        /// SEMANTIC-VERSION:SCHEMA
        /// </code>
        /// <para>
        /// where SCHEMA-VERSION is the <b>Neon.ModelGen</b> assembly version and
        /// SCHEMA is a simple integer schema version number.  The version will be
        /// incremented if or when the code generated by future versions of the
        /// <b>Neon.ModelGen</b> assembly changes enough to become incompatible
        /// with older versions of the <b>Neon.Xunit.XunitExtensions.ValidateController()</b>
        /// method.  This is likely to never change, but future proofing is always
        /// a good idea.
        /// </para>
        /// </summary>
        string GeneratorVersion { get; }
    }
}
