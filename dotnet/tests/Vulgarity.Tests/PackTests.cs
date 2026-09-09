using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Checks that a pack carries exactly what its JSON seed carries.</summary>
    /// <remarks>
    /// The packs are what ships. The JSON stays the authored source. These two
    /// must never drift, so every bundled list is read both ways and compared.
    /// </remarks>
    public class PackTests
    {
        public static IEnumerable<object[]> Languages()
        {
            yield return new object[] { "en" };
            foreach (string code in VulgarityLanguages.Available)
            {
                if (code != "en")
                {
                    yield return new object[] { code };
                }
            }
        }

        private static string SeedName(string code)
        {
            return code == "en" ? "seed.json" : "seed." + code + ".json";
        }

        private static string PackName(string code)
        {
            return Path.Combine("packs", "seed-" + code + ".vpk");
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void APackReadsBackAsItsSeedReads(string code)
        {
            List<VulgarityTerm> fromJson = new List<VulgarityTerm>();
            List<string> allowFromJson = new List<string>();
            SeedLoader.Load(TestData.Read(SeedName(code)), VulgarityFilter.Profile, fromJson, allowFromJson);

            List<VulgarityTerm> fromPack = new List<VulgarityTerm>();
            List<string> allowFromPack = new List<string>();
            PackReader.Load(TestData.ReadBytes(PackName(code)), VulgarityFilter.Profile, fromPack, allowFromPack);

            Assert.Equal(fromJson.Count, fromPack.Count);
            Assert.Equal(allowFromJson, allowFromPack);

            for (int i = 0; i < fromJson.Count; i++)
            {
                VulgarityTerm want = fromJson[i];
                VulgarityTerm got = fromPack[i];
                Assert.Equal(want.Text, got.Text);
                Assert.Equal(want.CategoryName, got.CategoryName);
                Assert.Equal(want.Category, got.Category);
                Assert.Equal(want.Severity, got.Severity);
                Assert.Equal(want.RequireBoundary, got.RequireBoundary);
            }
        }

        [Fact]
        public void EveryBundledPackIsPresent()
        {
            foreach (object[] row in Languages())
            {
                string path = Path.Combine(TestData.Directory, PackName((string)row[0]));
                Assert.True(File.Exists(path), "missing pack: " + path);
            }
        }

        [Fact]
        public void APackCarriesNoReadableTerm()
        {
            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();
            SeedLoader.Load(TestData.Read("seed.json"), VulgarityFilter.Profile, terms, allow);

            byte[] pack = TestData.ReadBytes(PackName("en"));
            string raw = System.Text.Encoding.Latin1.GetString(pack);

            foreach (VulgarityTerm term in terms)
            {
                if (term.Text.Length >= 4)
                {
                    Assert.False(raw.Contains(term.Text), "the pack still holds '" + term.Text + "'");
                }
            }
        }

        [Fact]
        public void SomethingThatIsNotAPackIsRefused()
        {
            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();

            Assert.False(PackReader.Looks(new byte[0]));
            Assert.False(PackReader.Looks(System.Text.Encoding.UTF8.GetBytes("{}")));
            Assert.False(PackReader.Looks(System.Text.Encoding.UTF8.GetBytes("VPK")));

            Assert.Throws<System.FormatException>(
                () => PackReader.Load(System.Text.Encoding.UTF8.GetBytes("{\"entries\":[]}"),
                    VulgarityFilter.Profile, terms, allow));
        }

        [Fact]
        public void APackBuiltForAnotherProfileIsRefused()
        {
            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();

            Assert.Throws<System.FormatException>(
                () => PackReader.Load(TestData.ReadBytes(PackName("en")), "fold-v0", terms, allow));
        }
    }
}
