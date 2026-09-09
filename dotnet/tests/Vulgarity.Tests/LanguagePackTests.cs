using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Checks that every bundled language pack loads and works.</summary>
    public class LanguagePackTests
    {
        public static IEnumerable<object[]> Packs()
        {
            foreach (string code in VulgarityLanguages.Available)
            {
                yield return new object[] { code };
            }
        }

        [Fact]
        public void EnglishIsTheDefault()
        {
            Assert.Equal("en", VulgarityLanguages.Default);
            Assert.Contains("en", VulgarityLanguages.Available);
            Assert.Equal(15, VulgarityLanguages.Available.Count);
        }

        [Theory]
        [MemberData(nameof(Packs))]
        public void PackLoadsAndTargetsThisProfile(string code)
        {
            using JsonDocument doc = JsonDocument.Parse(VulgarityLanguages.ReadSeed(code));
            Assert.Equal(VulgarityFilter.Profile, doc.RootElement.GetProperty("profile").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("schema").GetInt32());
            Assert.True(doc.RootElement.GetProperty("entries").GetArrayLength() > 0);
        }

        [Theory]
        [MemberData(nameof(Packs))]
        public void EveryTermInAPackIsDetectedWhenThatPackLoads(string code)
        {
            VulgarityFilter filter = code == VulgarityLanguages.Default
                ? VulgarityFilter.CreateDefault()
                : new VulgarityFilterBuilder().UseLanguage(code).Build();

            using JsonDocument doc = JsonDocument.Parse(VulgarityLanguages.ReadSeed(code));
            List<string> missed = new List<string>();

            foreach (JsonElement entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                string term = entry.GetProperty("t").GetString();
                if (!filter.Detect(term))
                {
                    missed.Add(term);
                }
            }

            Assert.True(missed.Count == 0,
                "Pack '" + code + "' holds " + missed.Count + " terms it cannot find: "
                + string.Join(", ", missed.GetRange(0, missed.Count < 10 ? missed.Count : 10)));
        }

        [Fact]
        public void AnOptionalPackAddsTermsToEnglish()
        {
            VulgarityFilter english = VulgarityFilter.CreateDefault();
            VulgarityFilter both = new VulgarityFilterBuilder()
                .UseDefaultSeed()
                .UseLanguage("es")
                .Build();

            Assert.True(both.TermCount > english.TermCount);

            // English still works after the pack loads.
            Assert.True(both.Detect("what the fuck"));
            Assert.False(both.Detect("Have a nice day."));
        }

        [Fact]
        public void AnUnknownPackIsRefused()
        {
            Assert.Throws<System.ArgumentException>(() => VulgarityLanguages.ReadSeed("xx"));
            Assert.False(VulgarityLanguages.Has("xx"));
            Assert.False(VulgarityLanguages.Has(null));
        }

        [Fact]
        public void EveryPackIsMarkedUnvetted()
        {
            foreach (string code in VulgarityLanguages.Available)
            {
                if (code == VulgarityLanguages.Default)
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(VulgarityLanguages.ReadSeed(code));
                Assert.False(doc.RootElement.GetProperty("vetted").GetBoolean(),
                    "Pack '" + code + "' claims it is vetted. Nobody vetted it.");
            }
        }
    }
}
