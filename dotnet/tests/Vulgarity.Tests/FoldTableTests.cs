using System.Collections.Generic;
using System.Text.Json;
using Vulgarity.Normalization;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>
    /// The compiled fold table must equal data/fold-v1.json. That file is the
    /// contract, and both ports compile a copy of it.
    /// </summary>
    public class FoldTableTests
    {
        [Fact]
        public void ProfileMatchesTheContract()
        {
            using JsonDocument doc = TestData.ReadJson("fold-v1.json");
            Assert.Equal(doc.RootElement.GetProperty("profile").GetString(), FoldTableData.Profile);
        }

        [Fact]
        public void SoftFoldsMatchTheContract()
        {
            AssertMap("foldSoft", FoldTableData.Soft);
        }

        [Fact]
        public void HardFoldsMatchTheContract()
        {
            AssertMap("foldHard", FoldTableData.Hard);
        }

        [Fact]
        public void RangesMatchTheContract()
        {
            using JsonDocument doc = TestData.ReadJson("fold-v1.json");
            JsonElement ranges = doc.RootElement.GetProperty("ranges");
            Assert.Equal(ranges.GetArrayLength(), FoldTableData.Ranges.Length);

            int i = 0;
            foreach (JsonElement range in ranges.EnumerateArray())
            {
                Assert.Equal(range.GetProperty("from").GetInt32(), FoldTableData.Ranges[i][0]);
                Assert.Equal(range.GetProperty("to").GetInt32(), FoldTableData.Ranges[i][1]);
                Assert.Equal(range.GetProperty("base").GetInt32(), FoldTableData.Ranges[i][2]);
                i++;
            }
        }

        [Theory]
        [InlineData("dropBreak")]
        [InlineData("dropSilent")]
        public void DropRangesMatchTheContract(string key)
        {
            using JsonDocument doc = TestData.ReadJson("fold-v1.json");
            int[][] compiled = key == "dropBreak" ? FoldTableData.DropBreak : FoldTableData.DropSilent;
            JsonElement ranges = doc.RootElement.GetProperty(key);
            Assert.Equal(ranges.GetArrayLength(), compiled.Length);

            int i = 0;
            foreach (JsonElement range in ranges.EnumerateArray())
            {
                Assert.Equal(range[0].GetInt32(), compiled[i][0]);
                Assert.Equal(range[1].GetInt32(), compiled[i][1]);
                i++;
            }
        }

        /// <summary>
        /// The normalizer reads ASCII letters before it reads either fold map.
        /// That shortcut is only safe while no map holds an ASCII letter.
        /// </summary>
        [Fact]
        public void NoFoldMapHoldsAnAsciiLetter()
        {
            foreach (int cp in FoldTableData.Soft.Keys)
            {
                Assert.False((cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z'), "soft map holds " + (char)cp);
            }

            foreach (int cp in FoldTableData.Hard.Keys)
            {
                Assert.False((cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z'), "hard map holds " + (char)cp);
            }
        }

        [Fact]
        public void EveryFoldProducesPlainAsciiLetters()
        {
            foreach (KeyValuePair<int, string> entry in FoldTableData.Soft)
            {
                AssertAscii(entry.Value);
            }

            foreach (KeyValuePair<int, string> entry in FoldTableData.Hard)
            {
                AssertAscii(entry.Value);
            }
        }

        private static void AssertAscii(string value)
        {
            Assert.NotEmpty(value);
            foreach (char c in value)
            {
                Assert.True(c >= 'a' && c <= 'z', "fold produced '" + value + "'");
            }
        }

        private static void AssertMap(string key, Dictionary<int, string> compiled)
        {
            using JsonDocument doc = TestData.ReadJson("fold-v1.json");
            JsonElement map = doc.RootElement.GetProperty(key);

            int count = 0;
            foreach (JsonProperty entry in map.EnumerateObject())
            {
                int cp = char.ConvertToUtf32(entry.Name, 0);
                Assert.True(compiled.ContainsKey(cp), key + " is missing " + entry.Name);
                Assert.Equal(entry.Value.GetString(), compiled[cp]);
                count++;
            }

            Assert.Equal(count, compiled.Count);
        }
    }
}
