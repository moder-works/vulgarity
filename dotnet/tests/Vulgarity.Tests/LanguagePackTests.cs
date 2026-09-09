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

        /// <summary>Reads one bundled pack straight out of the assembly.</summary>
        private static List<VulgarityTerm> Bundled(string code)
        {
            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();
            PackReader.Load(VulgarityLanguages.ReadSeedPack(code), VulgarityFilter.Profile, terms, allow);
            return terms;
        }

        [Theory]
        [MemberData(nameof(Packs))]
        public void PackLoadsAndTargetsThisProfile(string code)
        {
            // PackReader refuses a pack built for another profile, so a load that
            // returns terms is itself the profile check.
            Assert.NotEmpty(Bundled(code));

            Assert.Throws<System.FormatException>(() =>
                PackReader.Load(VulgarityLanguages.ReadSeedPack(code), "fold-v0",
                    new List<VulgarityTerm>(), new List<string>()));
        }

        [Theory]
        [MemberData(nameof(Packs))]
        public void ReadSeedReturnsPackTextTheBuilderAccepts(string code)
        {
            // ReadSeed hands out base64 pack text, not JSON. A caller that only
            // forwards the value keeps working, which is what this proves.
            string text = VulgarityLanguages.ReadSeed(code);
            Assert.DoesNotContain("\"entries\"", text);

            VulgarityFilter filter = new VulgarityFilterBuilder().AddSeed(text).Build();
            Assert.Equal(Bundled(code).Count, filter.TermCount);
        }

        [Theory]
        [MemberData(nameof(Packs))]
        public void EveryTermInAPackIsDetectedWhenThatPackLoads(string code)
        {
            VulgarityFilter filter = code == VulgarityLanguages.Default
                ? VulgarityFilter.CreateDefault()
                : new VulgarityFilterBuilder().UseLanguage(code).Build();

            List<string> missed = new List<string>();

            foreach (VulgarityTerm entry in Bundled(code))
            {
                if (!filter.Detect(entry.Text))
                {
                    missed.Add(entry.Text);
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

                // "vetted" is metadata about the source list, not something the
                // matcher reads, so a pack does not carry it. Check the authored
                // JSON, which is where that claim lives.
                using JsonDocument doc = TestData.ReadJson("seed." + code + ".json");
                Assert.False(doc.RootElement.GetProperty("vetted").GetBoolean(),
                    "Pack '" + code + "' claims it is vetted. Nobody vetted it.");
            }
        }
    }
}
