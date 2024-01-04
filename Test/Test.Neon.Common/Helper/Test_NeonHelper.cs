//-----------------------------------------------------------------------------
// FILE:        Test_NeonHelper.cs
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
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Xunit;

using Xunit;

namespace TestCommon
{
    [Trait(TestTrait.Category, TestArea.NeonCommon)]
    public partial class Test_NeonHelper
    {
        [Fact]
        public void FrameworkVersionTest()
        {
#if NET8_0_OR_GREATER
            var checkVersion = SemanticVersion.Parse("8.0");
#elif NET7_0_OR_GREATER
            var checkVersion = SemanticVersion.Parse("7.0");
#elif NET6_0_OR_GREATER
            var checkVersion = SemanticVersion.Parse("6.0");
#elif NET5_0_OR_GREATER
            var checkVersion = SemanticVersion.Parse("5.0");
#elif TARGET_NETCORE_3_1
            var checkVersion = SemanticVersion.Parse("3.0");
#else
            var checkVersion = SemanticVersion.Parse("4.8");
#endif
            Assert.Equal(checkVersion.Major, NeonHelper.FrameworkVersion.Major);
            Assert.Equal(checkVersion.Minor, NeonHelper.FrameworkVersion.Minor);
        }

        [Fact]
        public void SdkFolders()
        {
            var sdkFolder        = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".neonsdk");
            var azureCacheFolder = Path.Combine(sdkFolder, "codesigning-azure");
            var usbCacheFolder   = Path.Combine(sdkFolder, "codesigning-usb");

            Assert.Equal(sdkFolder, NeonHelper.NeonSdkFolder);
            Assert.True(Directory.Exists(sdkFolder));

            Assert.Equal(azureCacheFolder, NeonHelper.NeonSdkAzureCodeSigningFolder);
            Assert.True(Directory.Exists(azureCacheFolder));

            Assert.Equal(usbCacheFolder, NeonHelper.NeonSdkUsbCodeSigningFolder);
            Assert.True(Directory.Exists(usbCacheFolder));

            // Verify that these folder are recreated after being deleted.

            Directory.Delete(NeonHelper.NeonSdkFolder, recursive: true);
            Assert.Equal(sdkFolder, NeonHelper.NeonSdkFolder);
            Assert.True(Directory.Exists(sdkFolder));

            Directory.Delete(NeonHelper.NeonSdkAzureCodeSigningFolder, recursive: true);
            Assert.Equal(azureCacheFolder, NeonHelper.NeonSdkAzureCodeSigningFolder);
            Assert.True(Directory.Exists(azureCacheFolder));

            Directory.Delete(NeonHelper.NeonSdkUsbCodeSigningFolder, recursive: true);
            Assert.Equal(usbCacheFolder, NeonHelper.NeonSdkUsbCodeSigningFolder);
            Assert.True(Directory.Exists(usbCacheFolder));
        }

        [Fact]
        public void ParseCsv()
        {
            string[] fields;

            fields = NeonHelper.ParseCsv("");
            Assert.Equal(new string[] { "" }, fields);

            fields = NeonHelper.ParseCsv("1");
            Assert.Equal(new string[] { "1" }, fields);

            fields = NeonHelper.ParseCsv("1,2,3,4");
            Assert.Equal(new string[] { "1", "2", "3", "4" }, fields);

            fields = NeonHelper.ParseCsv("abc,def");
            Assert.Equal(new string[] { "abc", "def" }, fields);

            fields = NeonHelper.ParseCsv("abc,def,");
            Assert.Equal(new string[] { "abc", "def", "" }, fields);

            fields = NeonHelper.ParseCsv("\"\"");
            Assert.Equal(new string[] { "" }, fields);

            fields = NeonHelper.ParseCsv("\"abc\"");
            Assert.Equal(new string[] { "abc" }, fields);

            fields = NeonHelper.ParseCsv("\"abc,def\"");
            Assert.Equal(new string[] { "abc,def" }, fields);

            fields = NeonHelper.ParseCsv("\"a,b\",\"c,d\"");
            Assert.Equal(new string[] { "a,b", "c,d" }, fields);

            fields = NeonHelper.ParseCsv("\"a,b\",\"c,d\",e");
            Assert.Equal(new string[] { "a,b", "c,d", "e" }, fields);

            fields = NeonHelper.ParseCsv("\"abc\r\ndef\"");
            Assert.Equal(new string[] { "abc\r\ndef" }, fields);

            fields = NeonHelper.ParseCsv("0,1,,,4");
            Assert.Equal(new string[] { "0", "1", "", "", "4" }, fields);

            fields = NeonHelper.ParseCsv(",,,,");
            Assert.Equal(new string[] { "", "", "", "", "" }, fields);

            Assert.Throws<FormatException>(() => NeonHelper.ParseCsv("\"abc"));
        }

        [Fact]
        public void DoesNotThrow()
        {
            Assert.True(NeonHelper.DoesNotThrow(() => { }));
            Assert.True(NeonHelper.DoesNotThrow<ArgumentException>(() => { }));
            Assert.True(NeonHelper.DoesNotThrow<ArgumentException>(() => { throw new FormatException(); }));

            Assert.False(NeonHelper.DoesNotThrow(() => { throw new ArgumentException(); }));
            Assert.False(NeonHelper.DoesNotThrow<ArgumentException>(() => { throw new ArgumentException(); }));
        }

        [Fact]
        public void ExpandTabs()
        {
            // Test input without line endings.

            Assert.Equal("text", NeonHelper.ExpandTabs("text"));
            Assert.Equal("    text", NeonHelper.ExpandTabs("\ttext"));
            Assert.Equal("-   text", NeonHelper.ExpandTabs("-\ttext"));
            Assert.Equal("--  text", NeonHelper.ExpandTabs("--\ttext"));
            Assert.Equal("--- text", NeonHelper.ExpandTabs("---\ttext"));
            Assert.Equal("        text", NeonHelper.ExpandTabs("\t\ttext"));
            Assert.Equal("-       text", NeonHelper.ExpandTabs("-\t\ttext"));
            Assert.Equal("--      text", NeonHelper.ExpandTabs("--\t\ttext"));
            Assert.Equal("---     text", NeonHelper.ExpandTabs("---\t\ttext"));
            Assert.Equal("            text", NeonHelper.ExpandTabs("\t\t\ttext"));

            Assert.Equal("1   2   3", NeonHelper.ExpandTabs("1\t2\t3"));

            // Verify that a zero tab stop returns and unchanged string.

            Assert.Equal("    text", NeonHelper.ExpandTabs("    text", tabStop: 0));

            // Test input with line endings.

            var sb = new StringBuilder();

            sb.Clear();
            sb.AppendLine("line1");
            sb.AppendLine("\tline2");
            sb.AppendLine("-\tline3");
            sb.AppendLine("--\tline4");
            sb.AppendLine("---\tline5");
            sb.AppendLine("\t\tline6");
            sb.AppendLine("-    \tline7");
            sb.AppendLine("--   \tline8");
            sb.AppendLine("---  \tline9");
            sb.AppendLine("\t\t\tline10");

            Assert.Equal(
@"line1
    line2
-   line3
--  line4
--- line5
        line6
-       line7
--      line8
---     line9
            line10
", NeonHelper.ExpandTabs(sb.ToString()));

            // Test a non-default tab stop.

            Assert.Equal("text", NeonHelper.ExpandTabs("text", 8));
            Assert.Equal("        text", NeonHelper.ExpandTabs("\ttext", 8));
            Assert.Equal("-       text", NeonHelper.ExpandTabs("-\ttext", 8));
            Assert.Equal("--      text", NeonHelper.ExpandTabs("--\ttext", 8));
            Assert.Equal("---     text", NeonHelper.ExpandTabs("---\ttext", 8));
            Assert.Equal("                text", NeonHelper.ExpandTabs("\t\ttext", 8));
            Assert.Equal("-               text", NeonHelper.ExpandTabs("-\t\ttext", 8));
            Assert.Equal("--              text", NeonHelper.ExpandTabs("--\t\ttext", 8));
            Assert.Equal("---             text", NeonHelper.ExpandTabs("---\t\ttext", 8));
            Assert.Equal("                        text", NeonHelper.ExpandTabs("\t\t\ttext", 8));

            Assert.Equal("1       2       3", NeonHelper.ExpandTabs("1\t2\t3", 8));

            // Verify that a negative tab stop converts spaces into TABs.

            Assert.Equal("text", NeonHelper.ExpandTabs("text", -4));
            Assert.Equal("\ttext", NeonHelper.ExpandTabs("    text", -4));
            Assert.Equal("\t\ttext", NeonHelper.ExpandTabs("        text", -4));

            // Verify that we can handle left-over spaces.

            Assert.Equal("  text", NeonHelper.ExpandTabs("  text", -4));
            Assert.Equal("\t text", NeonHelper.ExpandTabs("     text", -4));
            Assert.Equal("\t  text", NeonHelper.ExpandTabs("      text", -4));

            // Verify that we don't convert spaces after the first non-space character.

            Assert.Equal("\ttext        x", NeonHelper.ExpandTabs("    text        x", -4));
        }

        [Fact]
        public void SequenceEquals_Enumerable()
        {
            Assert.True(NeonHelper.SequenceEqual((IEnumerable<string>)null, (IEnumerable<string>)null));
            Assert.True(NeonHelper.SequenceEqual((IEnumerable<string>)Array.Empty<string>(), (IEnumerable<string>)Array.Empty<string>()));
            Assert.True(NeonHelper.SequenceEqual((IEnumerable<string>)new string[] { "0", "1" }, (IEnumerable<string>)new string[] { "0", "1" }));
            Assert.True(NeonHelper.SequenceEqual((IEnumerable<string>)new string[] { "0", null }, (IEnumerable<string>)new string[] { "0", null }));

            Assert.False(NeonHelper.SequenceEqual((IEnumerable<string>)new string[] { "0", "1" }, (IEnumerable<string>)null));
            Assert.False(NeonHelper.SequenceEqual((IEnumerable<string>)null, (IEnumerable<string>)new string[] { "0", "1" }));
            Assert.False(NeonHelper.SequenceEqual((IEnumerable<string>)new string[] { "0", "1" }, (IEnumerable<string>)new string[] { "0" }));
        }

        [Fact]
        public void SequenceEquals_Array()
        {
            Assert.True(NeonHelper.SequenceEqual((string[])null, (string[])null));
            Assert.True(NeonHelper.SequenceEqual(Array.Empty<string>(), Array.Empty<string>()));
            Assert.True(NeonHelper.SequenceEqual(new string[] { "0", "1" }, new string[] { "0", "1" }));
            Assert.True(NeonHelper.SequenceEqual(new string[] { "0", null }, new string[] { "0", null }));

            Assert.False(NeonHelper.SequenceEqual(new string[] { "0", "1" }, (string[])null));
            Assert.False(NeonHelper.SequenceEqual((string[])null, new string[] { "0", "1" }));
            Assert.False(NeonHelper.SequenceEqual(new string[] { "0", "1" }, new string[] { "0" }));
        }

        [Fact]
        public void SequenceEquals_List()
        {
            Assert.True(NeonHelper.SequenceEqual((List<string>)null, (List<string>)null));
            Assert.True(NeonHelper.SequenceEqual(new List<string>(), new List<string>()));
            Assert.True(NeonHelper.SequenceEqual(new List<string>() { "0", "1" }, new List<string>() { "0", "1" }));
            Assert.True(NeonHelper.SequenceEqual(new List<string>() { "0", null }, new List<string>() { "0", null }));

            Assert.False(NeonHelper.SequenceEqual(new List<string>() { "0", "1" }, (List<string>)null));
            Assert.False(NeonHelper.SequenceEqual((List<string>)null, new List<string>() { "0", "1" }));
            Assert.False(NeonHelper.SequenceEqual(new List<string>() { "0", "1" }, new List<string>() { "0" }));
        }

        private async static Task GetNoResultAsync()
        {
            await Task.CompletedTask;
        }

        public static async Task<string> GetResultAsync()
        {
            return await Task.FromResult("Hello World!");
        }

        [Fact]
        public async Task GetObjectResultAsync()
        {
            // We should see an ArgumentException here because the task doesn't return a result.

            await Assert.ThrowsAsync<ArgumentException>(async () => await NeonHelper.GetTaskResultAsObjectAsync(GetNoResultAsync()));

            // This should succeed.

            Assert.Equal("Hello World!", await NeonHelper.GetTaskResultAsObjectAsync(GetResultAsync()));
        }

        [Fact]
        public void Base64UrlEncoding()
        {
            // Verify that known values can be encoded with padding.

            Assert.Equal("", NeonHelper.Base64UrlEncode(Array.Empty<byte>(), retainPadding: true));
            Assert.Equal("AA%3D%3D", NeonHelper.Base64UrlEncode(new byte[] { 0 }, retainPadding: true));
            Assert.Equal("AAE%3D", NeonHelper.Base64UrlEncode(new byte[] { 0, 1 }, retainPadding: true));
            Assert.Equal("AAEC", NeonHelper.Base64UrlEncode(new byte[] { 0, 1, 2 }, retainPadding: true));
            Assert.Equal("AAECAw%3D%3D", NeonHelper.Base64UrlEncode(new byte[] { 0, 1, 2, 3 }, retainPadding: true));
            Assert.Equal("AAECAwQ%3D", NeonHelper.Base64UrlEncode(new byte[] { 0, 1, 2, 3, 4 }, retainPadding: true));

            // Verify that known values can be encoded without padding.

            Assert.Equal("", NeonHelper.Base64UrlEncode(Array.Empty<byte>()));
            Assert.Equal("AA", NeonHelper.Base64UrlEncode(new byte[] { 0 }));
            Assert.Equal("AAE", NeonHelper.Base64UrlEncode(new byte[] { 0, 1 }));
            Assert.Equal("AAEC", NeonHelper.Base64UrlEncode(new byte[] { 0, 1, 2 }));
            Assert.Equal("AAECAw", NeonHelper.Base64UrlEncode(new byte[] { 0, 1, 2, 3 }));
            Assert.Equal("AAECAwQ", NeonHelper.Base64UrlEncode(new byte[] { 0, 1, 2, 3, 4 }));

            // Verify that we can decode known values with padding.

            Assert.Equal(Array.Empty<byte>(), NeonHelper.Base64UrlDecode(""));
            Assert.Equal(new byte[] { 0 }, NeonHelper.Base64UrlDecode("AA%3D%3D"));
            Assert.Equal(new byte[] { 0, 1 }, NeonHelper.Base64UrlDecode("AAE%3D"));
            Assert.Equal(new byte[] { 0, 1, 2 }, NeonHelper.Base64UrlDecode("AAEC"));
            Assert.Equal(new byte[] { 0, 1, 2, 3 }, NeonHelper.Base64UrlDecode("AAECAw%3D%3D"));
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, NeonHelper.Base64UrlDecode("AAECAwQ%3D"));

            // Verify that we can decode known values without URL encoded padding.

            Assert.Equal(Array.Empty<byte>(), NeonHelper.Base64UrlDecode(""));
            Assert.Equal(new byte[] { 0 }, NeonHelper.Base64UrlDecode("AA"));
            Assert.Equal(new byte[] { 0, 1 }, NeonHelper.Base64UrlDecode("AAE"));
            Assert.Equal(new byte[] { 0, 1, 2 }, NeonHelper.Base64UrlDecode("AAEC"));
            Assert.Equal(new byte[] { 0, 1, 2, 3 }, NeonHelper.Base64UrlDecode("AAECAw"));
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, NeonHelper.Base64UrlDecode("AAECAwQ"));

            // Verify that we can decode known values with standard '=' padding.

            Assert.Equal(Array.Empty<byte>(), NeonHelper.Base64UrlDecode(""));
            Assert.Equal(new byte[] { 0 }, NeonHelper.Base64UrlDecode("AA=="));
            Assert.Equal(new byte[] { 0, 1 }, NeonHelper.Base64UrlDecode("AAE="));
            Assert.Equal(new byte[] { 0, 1, 2 }, NeonHelper.Base64UrlDecode("AAEC"));
            Assert.Equal(new byte[] { 0, 1, 2, 3 }, NeonHelper.Base64UrlDecode("AAECAw=="));
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, NeonHelper.Base64UrlDecode("AAECAwQ="));
        }

        [Fact]
        public void UnixEpoch()
        {
            var unixEpoch = new DateTime(1970, 1, 1).ToUniversalTime();

            Assert.Equal(unixEpoch, NeonHelper.UnixEpoch);
        }

        [Fact]
        public void UnixEpochNanoseconds()
        {
            Assert.Equal(NeonHelper.UnixEpoch, NeonHelper.UnixEpochNanosecondsToDateTimeUtc(NeonHelper.UnixEpoch.ToUnixEpochNanoseconds()));
        }

        [Fact]
        public void UnixEpochMilliseconds()
        {
            Assert.Equal(NeonHelper.UnixEpoch, NeonHelper.UnixEpochMillisecondsToDateTimeUtc(NeonHelper.UnixEpoch.ToUnixEpochMilliseconds()));
        }
    }
}
