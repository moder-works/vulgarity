using System;
using System.Collections.Generic;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Both ports must accept the same base64 pack text.</summary>
    /// <remarks>
    /// The two runtimes disagree, and in opposite directions. Dart's
    /// base64.decode refuses every blank character but accepts the URL-safe
    /// alphabet. Convert.FromBase64String does the reverse. So `base64 &lt; x.vpk`,
    /// which wraps at 76 columns, would load here and fail there. PackText
    /// normalises first. dart/test/pack_text_test.dart is the mirror of this file.
    /// </remarks>
    public class PackTextTests
    {
        private static string PackText()
        {
            return VulgarityLanguages.ReadSeed("en");
        }

        private static void ExpectAccepted(string text, string shape)
        {
            byte[] pack = Vulgarity.PackText.TryRead(text);
            Assert.True(pack != null, "this port refused " + shape);

            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            PackReader.Load(pack, VulgarityFilter.Profile, terms, new List<string>());
            Assert.NotEmpty(terms);
        }

        [Fact]
        public void PlainBase64IsAccepted()
        {
            ExpectAccepted(PackText(), "plain base64");
        }

        [Fact]
        public void LineWrappedBase64IsAccepted()
        {
            // This is what `base64 < pack.vpk` produces.
            string text = PackText();
            System.Text.StringBuilder wrapped = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i += 76)
            {
                wrapped.Append(text.Substring(i, Math.Min(76, text.Length - i)));
                wrapped.Append('\n');
            }

            ExpectAccepted(wrapped.ToString(), "base64 wrapped at 76 columns");
        }

        [Fact]
        public void UrlSafeBase64IsAccepted()
        {
            // This is what a caller who put the pack in a URL produces.
            ExpectAccepted(PackText().Replace('+', '-').Replace('/', '_'),
                "the URL-safe base64 alphabet");
        }

        [Fact]
        public void SurroundingBlankSpaceIsAccepted()
        {
            ExpectAccepted("\n\t " + PackText() + " \r\n", "base64 with blank space around it");
        }

        [Fact]
        public void TextThatIsNotAPackIsRefused()
        {
            Assert.Null(Vulgarity.PackText.TryRead(""));
            Assert.Null(Vulgarity.PackText.TryRead("   "));
            Assert.Null(Vulgarity.PackText.TryRead(null));
            Assert.Null(Vulgarity.PackText.TryRead("{\"entries\":[]}"));
            Assert.Null(Vulgarity.PackText.TryRead("not base64 at all !!!"));
            Assert.Null(Vulgarity.PackText.TryRead(Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("this is not a pack"))));
        }

        [Fact]
        public void AJsonDocumentStillTakesTheJsonPath()
        {
            // The documented remote path must not change.
            VulgarityFilter filter = new VulgarityFilterBuilder()
                .AddSeed(TestData.Read("seed.json"))
                .Build();
            Assert.Equal(526, filter.TermCount);
        }

        [Fact]
        public void SomethingThatIsNeitherIsRefusedClearly()
        {
            FormatException error = Assert.Throws<FormatException>(
                () => new VulgarityFilterBuilder().AddSeed("hello"));
            Assert.Contains("pack", error.Message);
        }
    }
}
