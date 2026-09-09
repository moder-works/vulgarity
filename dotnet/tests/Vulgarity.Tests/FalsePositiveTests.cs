using System;
using System.Collections.Generic;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Sweeps the system word list and fails on a new false positive.</summary>
    public class FalsePositiveTests
    {
        /// <summary>
        /// These two words are squeeze artifacts, and both stay flagged on
        /// purpose. Every other artifact belongs in the seed allowlist.
        /// </summary>
        private static readonly HashSet<string> KnownVulgarArtifacts =
            new HashSet<string>(StringComparer.Ordinal) { "kaffir", "bastaard" };

        [Fact]
        public void TheSqueezePassInventsNoNewMatch()
        {
            string[] lines = TestData.EnglishWords();
            if (lines == null)
            {
                return; // This machine carries no word list.
            }

            VulgarityFilter withSqueeze = VulgarityFilter.CreateDefault();
            VulgarityFilter noSqueeze = withSqueeze.WithOptions(
                new VulgarityOptions { RepeatTolerance = false });

            List<string> unexpected = new List<string>();
            foreach (string line in lines)
            {
                string word = line.Trim().ToLowerInvariant();
                if (word.Length < 4 || !IsPlainAscii(word))
                {
                    continue;
                }

                if (noSqueeze.Detect(word) || KnownVulgarArtifacts.Contains(word))
                {
                    continue;
                }

                if (withSqueeze.Detect(word))
                {
                    unexpected.Add(word);
                }
            }

            Assert.True(unexpected.Count == 0,
                "The squeeze pass flagged ordinary words. Add each one to the seed allowlist, "
                + "or mark its term with \"w\": " + string.Join(", ", unexpected));
        }

        [Theory]
        // Each of these once fired, or nearly fired, on an ordinary word.
        [InlineData("thorny problem")]
        [InlineData("the heroine of the story")]
        [InlineData("trimming the hedge")]
        [InlineData("a scrappy team")]
        [InlineData("scumming the pot")]
        [InlineData("a looser fit")]
        [InlineData("rapping on the door")]
        [InlineData("the annals of history")]
        [InlineData("cattle inbreed here")]
        [InlineData("pollack for dinner")]
        [InlineData("the poorness of the soil")]
        [InlineData("a Shiite mosque")]
        [InlineData("skeet shooting")]
        [InlineData("mushrooms on toast")]
        [InlineData("shiitake mushrooms")]
        [InlineData("I live in Scunthorpe")]
        [InlineData("he is an assassin")]
        [InlineData("the class of 2024")]
        [InlineData("an analysis of the data")]
        [InlineData("grapefruit juice")]
        [InlineData("a raccoon in the yard")]
        [InlineData("the cockpit door")]
        [InlineData("sauerkraut and sausage")]
        [InlineData("a niggardly sum")]
        [InlineData("my therapist is great")]
        [InlineData("multivibrator circuit")]
        [InlineData("spasticity in the muscle")]
        [InlineData("bass guitar")]
        [InlineData("assume the position")]
        [InlineData("Cockburn is a surname")]
        public void OrdinaryEnglishStaysClean(string text)
        {
            VulgarityFilter filter = VulgarityFilter.CreateDefault();
            IReadOnlyList<VulgarityMatch> hits = filter.Scan(text);
            Assert.True(hits.Count == 0, "'" + text + "' flagged " + Describe(hits));
        }

        private static string Describe(IReadOnlyList<VulgarityMatch> hits)
        {
            List<string> parts = new List<string>();
            foreach (VulgarityMatch h in hits)
            {
                parts.Add(h.Text);
            }

            return string.Join(", ", parts);
        }

        private static bool IsPlainAscii(string word)
        {
            foreach (char c in word)
            {
                if (c < 'a' || c > 'z')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
