using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Vulgarity.Normalization;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Checks the normalizer against data/fold-vectors.json.</summary>
    public class NormalizerTests
    {
        public static IEnumerable<object[]> Vectors()
        {
            using JsonDocument doc = TestData.ReadJson("fold-vectors.json");
            foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                yield return new object[]
                {
                    c.GetProperty("text").GetString(),
                    c.GetProperty("chars").GetString(),
                    c.GetProperty("hard").GetString(),
                    c.GetProperty("gap").GetString(),
                    c.GetProperty("srcStart").GetString(),
                    c.GetProperty("srcEnd").GetString(),
                    c.GetProperty("squeezed").ValueKind == JsonValueKind.Null
                        ? null
                        : c.GetProperty("squeezed").GetString(),
                };
            }
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public void FoldsToTheContract(
            string text, string chars, string hard, string gap, string srcStart, string srcEnd, string squeezed)
        {
            NormalizedText n = TextNormalizer.Normalize(text);

            Assert.Equal(chars, AsString(n));
            Assert.Equal(hard, Flags(n.Hard, n.Length));
            Assert.Equal(gap, Flags(n.Gap, n.Length));
            Assert.Equal(srcStart, Numbers(n.SrcStart, n.Length));
            Assert.Equal(srcEnd, Numbers(n.SrcEnd, n.Length));

            NormalizedText s = TextNormalizer.Squeeze(n);
            if (squeezed == null)
            {
                Assert.Null(s);
            }
            else
            {
                Assert.NotNull(s);
                Assert.Equal(squeezed, AsString(s));
            }
        }

        [Fact]
        public void OffsetsAlwaysPointInsideTheOriginal()
        {
            const string text = "a🙂b.c‍d ẞ é";
            NormalizedText n = TextNormalizer.Normalize(text);
            for (int i = 0; i < n.Length; i++)
            {
                Assert.InRange(n.SrcStart[i], 0, text.Length - 1);
                Assert.InRange(n.SrcEnd[i], 1, text.Length);
                Assert.True(n.SrcStart[i] < n.SrcEnd[i]);
                if (i > 0)
                {
                    Assert.True(n.SrcStart[i] >= n.SrcStart[i - 1], "offsets must not go backwards");
                }
            }
        }

        [Fact]
        public void SqueezeKeepsTheFirstOffsetAndExtendsTheLast()
        {
            NormalizedText n = TextNormalizer.Normalize("fuuuck");
            NormalizedText s = TextNormalizer.Squeeze(n);

            Assert.Equal("fuck", AsString(s));
            Assert.Equal(1, s.SrcStart[1]);   // the run starts at the first 'u'
            Assert.Equal(4, s.SrcEnd[1]);     // and ends after the last 'u'
        }

        [Fact]
        public void FoldingATermIsIdempotent()
        {
            // Seed terms arrive already folded. Folding again must not change them.
            using JsonDocument seed = TestData.ReadJson("seed.json");
            foreach (JsonElement entry in seed.RootElement.GetProperty("entries").EnumerateArray())
            {
                string term = entry.GetProperty("t").GetString();
                Assert.Equal(term, AsString(TextNormalizer.Normalize(term)));
            }
        }

        private static string AsString(NormalizedText n)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                b.Append(char.ConvertFromUtf32(n.Chars[i]));
            }

            return b.ToString();
        }

        private static string Flags(bool[] values, int length)
        {
            StringBuilder b = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                b.Append(values[i] ? '1' : '0');
            }

            return b.ToString();
        }

        private static string Numbers(int[] values, int length)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                {
                    b.Append(',');
                }

                b.Append(values[i]);
            }

            return b.ToString();
        }
    }
}
