//-----------------------------------------------------------------------------
// FILE:        VersionJsonConverter.cs
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
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Neon.Data
{
    /// <summary>
    /// Implements a type converter for <see cref="Version"/>.
    /// </summary>
    public class VersionJsonConverter : JsonConverter<Version>, IEnhancedJsonConverter
    {
        /// <inheritdoc/>
        public Type Type => typeof(Version);

        /// <inheritdoc/>
        public override Version ReadJson(JsonReader reader, Type objectType, Version existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return Version.Parse((string)reader.Value);
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, Version value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        /// <inheritdoc/>
        public string ToSimpleString(object instance)
        {
            Covenant.Requires<ArgumentNullException>(instance != null, nameof(instance));

            return ((Version)instance).ToString();
        }
    }
}
