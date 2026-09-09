using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Checks the shape and the health of the bundled term list.</summary>
    public class SeedTests
    {
        [Fact]
        public void SeedTargetsThisProfile()
        {
            using JsonDocument doc = TestData.ReadJson("seed.json");
            Assert.Equal(doc.RootElement.GetProperty("profile").GetString(), VulgarityFilter.Profile);
            Assert.Equal(1, doc.RootElement.GetProperty("schema").GetInt32());
        }

        [Fact]
        public void EveryTermIsPlainAndUnique()
        {
            using JsonDocument doc = TestData.ReadJson("seed.json");
            HashSet<string> seen = new HashSet<string>();

            foreach (JsonElement e in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                string term = e.GetProperty("t").GetString();
                Assert.False(string.IsNullOrEmpty(term));
                Assert.True(seen.Add(term), "duplicate term: " + term);

                foreach (char c in term)
                {
                    Assert.True((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'),
                        "term '" + term + "' is not folded to fold-v1");
                }

                Assert.InRange(e.GetProperty("sev").GetInt32(), 1, 5);
                Assert.False(string.IsNullOrEmpty(e.GetProperty("cat").GetString()));
            }
        }

        [Fact]
        public void LoadingTheSeedRefusesTheWrongProfile()
        {
            string json = "{\"schema\":1,\"profile\":\"fold-v9\",\"entries\":[{\"t\":\"x\",\"cat\":\"a\",\"sev\":1}]}";
            Assert.Throws<System.FormatException>(() => VulgarityFilter.FromSeed(json));
        }

        [Fact]
        public void LoadingTheSeedRefusesTheWrongSchema()
        {
            string json = "{\"schema\":99,\"profile\":\"fold-v1\",\"entries\":[]}";
            Assert.Throws<System.FormatException>(() => VulgarityFilter.FromSeed(json));
        }

        /// <summary>
        /// An allowlist entry that the matcher does not need is worse than
        /// useless. "therapist" sat in the list once, and it suppressed the
        /// correct match on "the rapist".
        /// </summary>
        [Fact]
        public void EveryAllowlistEntryIsNeeded()
        {
            VulgarityFilter bare = BuildWithoutAllowlist();
            using JsonDocument doc = TestData.ReadJson("seed.json");

            foreach (JsonElement word in doc.RootElement.GetProperty("allow").EnumerateArray())
            {
                string text = word.GetString();
                Assert.True(bare.Detect(text),
                    "Allowlist entry '" + text + "' is not needed. The matcher never flags it, "
                    + "so the entry only risks suppressing a real match. Remove it.");
            }
        }

        [Fact]
        public void AllowlistWordsStayClean()
        {
            VulgarityFilter filter = VulgarityFilter.CreateDefault();
            using JsonDocument doc = TestData.ReadJson("seed.json");

            foreach (JsonElement word in doc.RootElement.GetProperty("allow").EnumerateArray())
            {
                string text = word.GetString();
                Assert.False(filter.Detect(text), "Allowlist entry '" + text + "' is still flagged.");
            }
        }

        [Fact]
        public void TheBoundaryTestSeparatesThePhraseFromTheWord()
        {
            VulgarityFilter filter = VulgarityFilter.CreateDefault();

            // The gap between the two words is what makes this pair work.
            Assert.True(filter.Detect("the rapist was caught"));
            Assert.False(filter.Detect("my therapist is great"));
        }

        internal static VulgarityFilter BuildWithoutAllowlist()
        {
            using JsonDocument doc = TestData.ReadJson("seed.json");
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder();

            foreach (JsonElement e in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                builder.AddTerm(
                    e.GetProperty("t").GetString(),
                    e.GetProperty("cat").GetString(),
                    e.GetProperty("sev").GetInt32(),
                    e.TryGetProperty("w", out JsonElement w) && w.ValueKind == JsonValueKind.True);
            }

            return builder.Build();
        }
    }
}
