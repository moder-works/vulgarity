using System.Collections.Generic;
using Vulgarity.Normalization;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Folding has to reach a fixed point in one pass.</summary>
    /// <remarks>
    /// Terms are stored folded and text is folded at scan time, so a character that
    /// keeps changing would make its own term unreachable. Fullwidth digits used to
    /// fold onto ASCII digits, which then folded again onto soft letters.
    ///
    /// dart/test/fold_idempotence_test.dart is the mirror of this file.
    /// </remarks>
    public class FoldIdempotenceTests
    {
        [Fact]
        public void FoldingEveryCodePointIsIdempotent()
        {
            List<string> offenders = new List<string>();

            for (int cp = 0; cp <= 0x1FFFF; cp++)
            {
                if (cp >= 0xD800 && cp <= 0xDFFF)
                {
                    continue; // A lone surrogate is not a character.
                }

                string source = char.ConvertFromUtf32(cp);
                string once = TextNormalizer.FoldToString(source);
                string twice = TextNormalizer.FoldToString(once);

                if (once != twice)
                {
                    offenders.Add("U+" + cp.ToString("X4") + " folds to '" + once
                        + "', then to '" + twice + "'");
                }
            }

            Assert.True(offenders.Count == 0,
                offenders.Count + " code points fold twice: "
                + string.Join("; ", offenders.GetRange(0, offenders.Count < 10 ? offenders.Count : 10)));
        }

        [Theory]
        [InlineData("\uFF10", "o")]
        [InlineData("\uFF11", "i")]
        [InlineData("\uFF13", "e")]
        [InlineData("\uFF14", "a")]
        [InlineData("\uFF15", "s")]
        [InlineData("\uFF17", "t")]
        [InlineData("\uFF12", "2")]
        [InlineData("\uFF16", "6")]
        [InlineData("\uFF18", "8")]
        [InlineData("\uFF19", "9")]
        public void AFullwidthDigitFoldsLikeItsAsciiDigit(string wide, string expected)
        {
            Assert.Equal(expected, TextNormalizer.FoldToString(wide));
        }

        [Fact]
        public void APresetThatFoldsTwoTermsTogetherBuilds()
        {
            // "sh\uFF10t" and "shot" both fold to "shot". The builder merges them; it
            // used to throw, because the first one folded to "sh0t" and then to
            // "shot".
            VulgarityFilter filter = VulgarityFilter.FromPreset(
                "{\"languages\":[\"en\"],\"entries\":["
                + "{\"t\":\"sh\uFF10t\",\"sev\":2},{\"t\":\"shot\",\"sev\":2}]}");

            Assert.True(filter.Detect("what a shot"));
            Assert.True(filter.Detect("what a \uFF53\uFF48\uFF10\uFF54"));
        }
    }
}
