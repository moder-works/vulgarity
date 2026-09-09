using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>
    /// Checks every case in data/vectors.json. The Dart port reads the same file
    /// and asserts the same values, so this file is the parity contract.
    /// </summary>
    public class ParityTests
    {
        public static IEnumerable<object[]> Cases()
        {
            using JsonDocument doc = TestData.ReadJson("vectors.json");
            foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                yield return new object[] { c.GetProperty("name").GetString(), c.GetRawText() };
            }
        }

        [Fact]
        public void VectorsTargetThisProfile()
        {
            using JsonDocument doc = TestData.ReadJson("vectors.json");
            Assert.Equal(doc.RootElement.GetProperty("profile").GetString(), VulgarityFilter.Profile);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void MatchesTheContract(string name, string raw)
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement c = doc.RootElement;
            string text = c.GetProperty("text").GetString();

            VulgarityFilter filter = VulgarityFilter.CreateDefault(ReadOptions(c));

            Assert.Equal(c.GetProperty("detect").GetBoolean(), filter.Detect(text));
            Assert.Equal(c.GetProperty("score").GetInt32(), filter.Score(text));
            Assert.Equal(c.GetProperty("filtered").GetString(), filter.Filter(text));

            JsonElement expected = c.GetProperty("matches");
            IReadOnlyList<VulgarityMatch> actual = filter.Scan(text);
            Assert.Equal(expected.GetArrayLength(), actual.Count);

            int i = 0;
            foreach (JsonElement m in expected.EnumerateArray())
            {
                Assert.Equal(m.GetProperty("start").GetInt32(), actual[i].Start);
                Assert.Equal(m.GetProperty("end").GetInt32(), actual[i].End);
                Assert.Equal(m.GetProperty("term").GetString(), actual[i].Text);
                Assert.Equal(m.GetProperty("cat").GetString(), actual[i].CategoryName);
                Assert.Equal(m.GetProperty("sev").GetInt32(), actual[i].Severity);
                i++;
            }
        }

        private static VulgarityOptions ReadOptions(JsonElement c)
        {
            VulgarityOptions o = new VulgarityOptions();
            if (!c.TryGetProperty("options", out JsonElement opts))
            {
                return o;
            }

            if (opts.TryGetProperty("maskToken", out JsonElement v)) o.MaskToken = v.GetString();
            if (opts.TryGetProperty("maskChar", out v)) o.MaskChar = v.GetString()[0];
            if (opts.TryGetProperty("minSeverity", out v)) o.MinSeverity = v.GetInt32();
            if (opts.TryGetProperty("repeatTolerance", out v)) o.RepeatTolerance = v.GetBoolean();
            if (opts.TryGetProperty("collapseContained", out v)) o.CollapseContained = v.GetBoolean();
            if (opts.TryGetProperty("scoreMode", out v))
            {
                o.ScoreMode = (ScoreMode)Enum.Parse(typeof(ScoreMode), v.GetString());
            }

            if (opts.TryGetProperty("categories", out v))
            {
                HashSet<VulgarityCategory> set = new HashSet<VulgarityCategory>();
                foreach (JsonElement name in v.EnumerateArray())
                {
                    set.Add((VulgarityCategory)Enum.Parse(typeof(VulgarityCategory), name.GetString()));
                }

                o.Categories = set;
            }

            return o;
        }
    }
}
