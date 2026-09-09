using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>
    /// Checks the pack reader against data/testdata/hostile-packs.json.
    /// </summary>
    /// <remarks>
    /// The Dart reader and tool/packlib.py read the same file and assert the
    /// same outcomes, so a pack one reader accepts and another refuses fails in
    /// three places at once. tool/gen_hostile_packs.py writes the file.
    /// </remarks>
    public class HostilePackTests
    {
        private static JsonDocument ReadFixture()
        {
            return JsonDocument.Parse(File.ReadAllText(
                Path.Combine(TestData.Directory, "testdata", "hostile-packs.json")));
        }

        public static IEnumerable<object[]> Cases()
        {
            using JsonDocument doc = ReadFixture();
            string profile = doc.RootElement.GetProperty("profile").GetString();
            foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                yield return new object[]
                {
                    c.GetProperty("name").GetString(),
                    c.GetProperty("pack").GetString(),
                    c.GetProperty("expect").GetString(),
                    c.TryGetProperty("terms", out JsonElement terms) ? terms.GetInt32() : -1,
                    profile,
                };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ThePackReaderAgreesWithTheSharedFixtures(
            string name, string encoded, string expect, int terms, string profile)
        {
            byte[] pack = Convert.FromBase64String(encoded);
            List<VulgarityTerm> read = new List<VulgarityTerm>();
            List<string> allow = new List<string>();

            if (expect == "error")
            {
                Exception error = Record.Exception(
                    () => PackReader.Load(pack, profile, read, allow));

                Assert.True(error != null, name + " must be refused");
                Assert.True(error is FormatException,
                    name + ": expected a FormatException, got " + error.GetType().Name);
                return;
            }

            PackReader.Load(pack, profile, read, allow);
            Assert.Equal(terms, read.Count);
        }

        [Fact]
        public void TheHostilePacksTargetThisProfile()
        {
            using JsonDocument doc = ReadFixture();
            Assert.Equal(VulgarityFilter.Profile,
                doc.RootElement.GetProperty("profile").GetString());
        }

        [Fact]
        public void APackThatLoadsStillBuildsAUsableFilter()
        {
            // The valid fixture is tiny, so this also proves a pack needs
            // nothing from the bundled list to stand on its own.
            using JsonDocument doc = ReadFixture();

            foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                if (c.GetProperty("name").GetString() != "a valid tiny pack")
                {
                    continue;
                }

                byte[] pack = Convert.FromBase64String(c.GetProperty("pack").GetString());
                VulgarityFilter filter = new VulgarityFilterBuilder().AddSeed(pack).Build();

                Assert.Equal(c.GetProperty("terms").GetInt32(), filter.TermCount);
                Assert.True(filter.Detect("blorp"));
                Assert.False(filter.Detect("nothing here"));
                return;
            }

            Assert.Fail("the fixtures hold no case named 'a valid tiny pack'");
        }
    }
}
