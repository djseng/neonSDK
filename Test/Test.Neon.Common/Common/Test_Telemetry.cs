//-----------------------------------------------------------------------------
// FILE:        Test_Telemetry.cs
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Diagnostics;
using Neon.Xunit;

using Xunit;

namespace TestCommon
{
    public class Test_Telemetry
    {
        private class TestObject
        {
            public TestObject(string field1, int field2)
            {
                this.Field1 = field1;
                this.Field2 = field2;
            }

            public string Field1 { get; set; }
            public int Field2 { get; set; }
        }

        [Fact]
        public void LogAttributes()
        {
            var logAttibutes = new LogAttributes();

            // Attributes starts out empty.

            Assert.Empty(logAttibutes.Attributes);

            // Verify that we can add attributes with different value types.

            logAttibutes = new LogAttributes();

            logAttibutes.Add("bool-true", true);
            logAttibutes.Add("bool-false", false);
            logAttibutes.Add("long", 1234L);
            logAttibutes.Add("double", 123.456D);
            logAttibutes.Add("string-hello", "Hello World!");
            logAttibutes.Add("string-null", (string)null);
            logAttibutes.Add("string-empty", string.Empty);
            logAttibutes.Add("object-null", null);
            logAttibutes.Add("object-value", new TestObject("test", 123));

            Assert.NotEmpty(logAttibutes.Attributes);
            Assert.True((bool)logAttibutes.Attributes["bool-true"]);
            Assert.False((bool)logAttibutes.Attributes["bool-false"]);
            Assert.Equal(1234L, logAttibutes.Attributes["long"]);
            Assert.Equal("Hello World!", logAttibutes.Attributes["string-hello"]);
            Assert.Null(logAttibutes.Attributes["string-null"]);
            Assert.Empty((string)logAttibutes.Attributes["string-empty"]);
            Assert.Null(logAttibutes.Attributes["object-null"]);

            var obj = (TestObject)logAttibutes.Attributes["object-value"];

            Assert.Equal("test", obj.Field1);
            Assert.Equal(123, obj.Field2);

            // Attribute names cannot be NULL or empty.

            logAttibutes = new LogAttributes();

            Assert.Throws<ArgumentNullException>(() => logAttibutes.Add(null, "Hello World!"));
            Assert.Throws<ArgumentNullException>(() => logAttibutes.Add(String.Empty, "Hello World!"));
        }
    }
}
