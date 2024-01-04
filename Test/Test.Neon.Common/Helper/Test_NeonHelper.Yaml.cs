//-----------------------------------------------------------------------------
// FILE:        Test_NeonHelper.Yaml.cs
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
using System.Dynamic;
using System.Runtime.Serialization;

using Neon.Common;
using Neon.Xunit;

using YamlDotNet.Core;
using Xunit;
using System.IO;

namespace TestCommon
{
    public partial class Test_NeonHelper
    {
        public enum YamlGender
        {
            Unknown = 0,

            [EnumMember(Value = "gender-female")]
            Female,

            [EnumMember(Value = "gender-male")]
            Male,
        }

        public class YamlPerson
        {
            public string Name { get; set; }
            public int Age { get; set; }
            public YamlGender Gender { get; set; }
        }

        [Fact]
        public void YamlSerialize()
        {
            var before =
                new YamlPerson()
                {
                    Name = "Jeff",
                    Age = 56,
                    Gender = YamlGender.Unknown
                };

            // Verify that the property names were converted to lowercase.

            var yaml = NeonHelper.YamlSerialize(before);

            Assert.Contains("name: Jeff", yaml);
            Assert.Contains("age: 56", yaml);
            Assert.Contains("gender: Unknown", yaml);

            // Verify that we can deserialize.

            var after = NeonHelper.YamlDeserialize<YamlPerson>(yaml);

            Assert.Equal("Jeff", after.Name);
            Assert.Equal(56, after.Age);
            Assert.Equal(YamlGender.Unknown, after.Gender);
        }

        [Fact]
        public void YamlDoubleSpaced()
        {
            // The YamlDotNet serializer serializes multi-lined strings as double-spaced for
            // for reason.  Our NeonHelper YAML serializer has been modified not to do this.
            //
            //      https://stackoverflow.com/questions/58431796/change-the-scalar-style-used-for-all-multi-line-strings-when-serialising-a-dynam
            //
            // We're going to verify our YAML serializer's behavior here.

            var person = new YamlPerson() { Name = "line 1\nline 2" };
            var yaml = NeonHelper.YamlSerialize(person);

            // Confirm that there's no blank line between "line 1" and "line 2".

            var lines = new List<string>();

            using (var reader = new StringReader(yaml))
            {
                foreach (var line in reader.Lines())
                {
                    lines.Add(line.Trim());
                }
            }

            var line1Index = -1;

            for (int i = 0;i<lines.Count;i++)
            {
                if (lines[i] == "line 1")
                {
                    line1Index = i;
                    break;
                }
            }

            Assert.NotEqual(-1, line1Index);
            Assert.Equal("line 1", lines[line1Index]);
            Assert.NotEqual(string.Empty, lines[line1Index + 1]);
            Assert.Equal("line 2", lines[line1Index + 1]);
        }

        [Fact]
        public void YamlEnumMember()
        {
            // Verify that we recognize [EnumMember] attributes.

            var before =
                new YamlPerson()
                {
                    Name = "Jeff",
                    Age = 56,
                    Gender = YamlGender.Male
                };

            var yaml = NeonHelper.YamlSerialize(before);

            Assert.Contains("name: Jeff", yaml);
            Assert.Contains("age: 56", yaml);
            Assert.Contains("gender: gender-male", yaml);

            // Verify that we can deserialize.

            var after = NeonHelper.YamlDeserialize<YamlPerson>(yaml);

            Assert.Equal("Jeff", after.Name);
            Assert.Equal(56, after.Age);
            Assert.Equal(YamlGender.Male, after.Gender);
        }

        [Fact]
        public void YamlDeserializeUnmatched()
        {
            const string normal =
@"name: Jeff
age: 56
";
            const string unmatched =
@"name: Jeff
age: 56
unmatched: Hello
";
            // Verify that we can deserialize YAML without unmatched properties.

            var person = NeonHelper.YamlDeserialize<YamlPerson>(normal);

            Assert.Equal("Jeff", person.Name);
            Assert.Equal(56, person.Age);

            // Verify that we can ignore unmatched properties.

            person = NeonHelper.YamlDeserialize<YamlPerson>(unmatched, strict: false);

            Assert.Equal("Jeff", person.Name);
            Assert.Equal(56, person.Age);

            // Verify that we see an exception when we're not ignoring unmatched
            // properties and the input has an unmatched property.

            Assert.Throws<YamlException>(() => NeonHelper.YamlDeserialize<YamlPerson>(unmatched, strict: true));
        }

        [Fact]
        public void YamlException()
        {
            // Verify that exception messages explain the issue.

            const string unmatched =
@"name: Jeff
age: 56
unmatched: Hello
";
            try
            {
                var person = NeonHelper.YamlDeserialize<YamlPerson>(unmatched, strict: true);
            }
            catch (YamlException e)
            {
                Assert.Contains("Property 'unmatched' not found", e.Message);
            }

            const string badSyntax =
@"name Jeff
age: 56
";
            try
            {
                var person = NeonHelper.YamlDeserialize<YamlPerson>(badSyntax);
            }
            catch (YamlException e)
            {
                Assert.Contains("invalid mapping", e.Message);
            }
        }

        [Fact]
        public void YamlNotJson()
        {
            // Verify that we can identify and parse YAML (over JSON).

            var before =
                new YamlPerson()
                {
                    Name = "Jeff",
                    Age = 56
                };

            // Verify that the property names were converted to lowercase.

            var yaml = NeonHelper.YamlSerialize(before);

            Assert.Contains("name: Jeff", yaml);
            Assert.Contains("age: 56", yaml);

            // Verify that we can deserialize.

            var after = NeonHelper.JsonOrYamlDeserialize<YamlPerson>(yaml);

            Assert.Equal("Jeff", after.Name);
            Assert.Equal(56, after.Age);
        }

        [Fact]
        public void YamlArray()
        {
            // Verify that we can YAML arrays.

            var before = new List<YamlPerson>();

            before.Add(
                new YamlPerson()
                {
                    Name = "Jeff",
                    Age = 56
                });

            before.Add(
                new YamlPerson()
                {
                    Name = "Darrian",
                    Age = 25
                });

            var yaml = NeonHelper.YamlSerialize(before);

            // Verify that we can deserialize.

            var after = NeonHelper.JsonOrYamlDeserialize<List<YamlPerson>>(yaml);

            Assert.Equal(2, after.Count);
            Assert.Equal("Jeff", after[0].Name);
            Assert.Equal("Darrian", after[1].Name);
        }

        [Fact]
        public void JsonToYaml_Basic()
        {
            // Verify that we can convert JSON to YAML.

            var input = new YamlPerson()
            {
                Name = "Jeff",
                Age  = 56
            };

            var jsonText = NeonHelper.JsonSerialize(input);
            var yamlText = NeonHelper.JsonToYaml(jsonText);
            var output   = NeonHelper.YamlDeserialize<YamlPerson>(yamlText);

            Assert.Equal(input.Name, output.Name);
            Assert.Equal(input.Age, output.Age);
            NeonHelper.YamlDeserialize<dynamic>(yamlText);

            // Ensure that JSON property strings that are integer values  
            // retain their "stringness".

            input = new YamlPerson()
            {
                Name = "1001",
                Age  = 56
            };

            jsonText = NeonHelper.JsonSerialize(input);
            yamlText = NeonHelper.JsonToYaml(jsonText);

            Assert.Contains("'1001'", yamlText);
            NeonHelper.YamlDeserialize<dynamic>(yamlText);
        }

        [Fact]
        public void JsonToYaml_Values()
        {
            var jsonText = @"
{
    ""int"": 10,
    ""float"": 123.4,
    ""string0"": ""hello world"",
    ""string1"": ""hello \""world\"""",
    ""string2"": ""hello world!"",
    ""string3"": ""test=value"",
    ""string4"": ""line1\nline2\n"",
    ""bool0"": true,
    ""bool1"": false,
    ""is-null"" : null,
    ""string-int"": ""3"",
    ""string-float"": ""123.4"",
    ""string-bool-true"": ""true"",
    ""string-bool-false"": ""false"",
    ""string-bool-yes"": ""yes"",
    ""string-bool-no"": ""no"",
    ""string-bool-on"": ""on"",
    ""string-bool-off"": ""off"",
    ""string-bool-yes"": ""yes"",
    ""string-bool-no"": ""no"",
}
";
            var yamlText = NeonHelper.JsonToYaml(jsonText);
            var yamlExpected =
@"int: 10
float: 123.4
string0: hello world
string1: ""hello \""world\""""
string2: ""hello world!""
string3: ""test=value""
string4: ""line1\nline2\n""
bool0: true
bool1: false
is-null: null
string-int: '3'
string-float: '123.4'
string-bool-true: 'true'
string-bool-false: 'false'
string-bool-on: 'on'
string-bool-off: 'off'
string-bool-yes: 'yes'
string-bool-no: 'no'
";
            TestHelper.AssertEqualLines(yamlExpected, yamlText);
            NeonHelper.YamlDeserialize<dynamic>(yamlText);
        }

        [Fact]
        public void JsonToYaml_Object()
        {
            var jsonText = @"
{
  ""name"": ""level0"",
  ""nested"": {
    ""property0"": ""hello"",
    ""property1"": ""world"",
    ""property2"": {
      ""hello"": ""world""
    }
  }
}
";
            var yamlText = NeonHelper.JsonToYaml(jsonText);
            var yamlExpected =
@"name: level0
nested:
  property0: hello
  property1: world
  property2:
    hello: world
";
            TestHelper.AssertEqualLines(yamlExpected, yamlText);
            NeonHelper.YamlDeserialize<dynamic>(yamlText);
        }

        [Fact]
        public void JsonToYaml_SimpleArray()
        {
            var jsonText = @"
[
    ""zero"",
    ""one"",
    ""two"",
    ""three""
]
";
            var yamlText = NeonHelper.JsonToYaml(jsonText);
            var yamlExpected =
@"- zero
- one
- two
- three
";
            TestHelper.AssertEqualLines(yamlExpected, yamlText);
            NeonHelper.YamlDeserialize<dynamic>(yamlText);
        }

        [Fact]
        public void JsonToYaml_ObjectsAndArrays()
        {
            var jsonText = @"
[
    ""zero"",
    {
      ""name"": ""jeff"",
      ""age"": 56,
      ""pets"": [
        ""lilly"",
        ""butthead"",
        ""poophead"",
        {
           ""name"": ""norman"",
           ""type"": ""pony""
        }
      ]
    }
]
";
            var yamlText = NeonHelper.JsonToYaml(jsonText);
            var yamlExpected =
@"- zero
- name: jeff
  age: 56
  pets:
    - lilly
    - butthead
    - poophead
    - name: norman
      type: pony
";
            TestHelper.AssertEqualLines(yamlExpected, yamlText);
            NeonHelper.YamlDeserialize<dynamic>(yamlText);
        }
    }
}
