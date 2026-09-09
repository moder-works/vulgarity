using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>
    /// Exercises the filter against a community corpus of real spellings.
    /// </summary>
    /// <remarks>
    /// The corpus never seeds the filter. It is unvetted, and it holds ordinary
    /// words such as "aunt", "cool" and "acct". It earns its place as a test
    /// fixture, because it carries thousands of genuine evasion spellings.
    /// </remarks>
    public class CorpusTests
    {
        /// <summary>The count detected when this fixture was recorded.</summary>
        private const int DetectionBaseline = 2894;

        /// <summary>Evasions the cross-word boundary rule gives up on, and why.</summary>
        /// <remarks>
        /// A term with no boundary rule may span a dropped separator only when it
        /// starts its own word. Without that, the tail of one innocent word plus
        /// the head of the next spells a term, and "wash it down" reports one.
        /// These entries pay for that: the term sits in the middle of a longer word
        /// AND straddles a separator, which is exactly the shape the rule rejects.
        /// Every other one of the thousand-odd evasions still matches.
        /// </remarks>
        private static readonly HashSet<string> KnownBoundaryLosses =
            new HashSet<string> { "m.otherf.ucker" };

        [Fact]
        public void EveryKnownEvasionIsDetected()
        {
            VulgarityFilter filter = VulgarityFilter.CreateDefault();
            using JsonDocument doc = ReadFixture("evasions-en.json");

            List<string> missed = new List<string>();
            int total = 0;

            foreach (JsonElement term in doc.RootElement.GetProperty("terms").EnumerateArray())
            {
                total++;
                string text = term.GetString();
                if (!filter.Detect(text) && !KnownBoundaryLosses.Contains(text))
                {
                    missed.Add(text);
                }
            }

            Assert.True(total > 900, "the evasion fixture shrank unexpectedly");
            Assert.True(missed.Count == 0,
                "The filter missed " + missed.Count + " of " + total + " known evasions: "
                + string.Join(", ", missed.GetRange(0, missed.Count < 20 ? missed.Count : 20)));
        }

        [Fact]
        public void CorpusDetectionDoesNotRegress()
        {
            VulgarityFilter filter = VulgarityFilter.CreateDefault();
            using JsonDocument doc = ReadFixture("corpus-en.json");

            int detected = 0;
            foreach (JsonElement term in doc.RootElement.GetProperty("terms").EnumerateArray())
            {
                if (filter.Detect(term.GetString()))
                {
                    detected++;
                }
            }

            // The corpus is mostly noise, so full coverage is neither possible
            // nor wanted. This guards against a drop.
            Assert.True(detected >= DetectionBaseline - 20,
                "Corpus detection fell to " + detected + " from a baseline of " + DetectionBaseline + ".");
        }

        [Theory]
        [InlineData("$h!t", "shit")]
        [InlineData("4r5e", "arse")]
        [InlineData("5h1t", "shit")]
        [InlineData("a$$hole", "asshole")]
        [InlineData("a-s-s", "ass")]
        [InlineData("@rse", "arse")]
        [InlineData("f u c k", "fuck")]
        [InlineData("phuck", "phuck")]
        [InlineData("b!tch", "bitch")]
        [InlineData("c0ck", "cock")]
        [InlineData("d1ck", "dick")]
        [InlineData("pu$$y", "pussy")]
        [InlineData("p.u.s.s.y", "pussy")]
        [InlineData("fuuuuuuck", "fuck")]
        [InlineData("SHIT", "shit")]
        [InlineData("Ｓｈｉｔ", "shit")]
        public void EvasionFoldsOntoTheRightTerm(string text, string term)
        {
            VulgarityFilter filter = VulgarityFilter.CreateDefault();
            IReadOnlyList<VulgarityMatch> hits = filter.Scan(text);
            Assert.True(hits.Count > 0, "'" + text + "' was not detected");
            Assert.Equal(term, hits[0].Text);
        }

        private static JsonDocument ReadFixture(string name)
        {
            return JsonDocument.Parse(File.ReadAllText(Path.Combine(TestData.Directory, "testdata", name)));
        }
    }
}
