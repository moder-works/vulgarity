using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>
    /// Checks every case in data/preset-vectors.json. The Dart port reads the
    /// same file, so this is the preset parity contract.
    /// </summary>
    public class PresetTests
    {
        private static string ReadPreset(string name)
        {
            return File.ReadAllText(Path.Combine(TestData.Directory, "presets", name + ".json"));
        }

        public static IEnumerable<object[]> Cases()
        {
            using JsonDocument doc = TestData.ReadJson("preset-vectors.json");
            foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                yield return new object[]
                {
                    c.GetProperty("preset").GetString() + ": " + c.GetProperty("text").GetString(),
                    c.GetRawText(),
                };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void MatchesTheContract(string name, string raw)
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement c = doc.RootElement;
            string text = c.GetProperty("text").GetString();

            VulgarityFilter filter = VulgarityFilter.FromPreset(
                ReadPreset(c.GetProperty("preset").GetString()));

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
                Assert.Equal(m.GetProperty("sev").GetInt32(), actual[i].Severity);
                i++;
            }
        }

        [Fact]
        public void TheDefaultPresetEqualsCreateDefault()
        {
            VulgarityFilter fromPreset = VulgarityFilter.FromPreset(ReadPreset("default"));
            VulgarityFilter fromFactory = VulgarityFilter.CreateDefault();
            Assert.Equal(fromFactory.TermCount, fromPreset.TermCount);
        }

        [Fact]
        public void APresetCarriesItsPolicy()
        {
            VulgarityPreset preset = VulgarityPreset.Parse(ReadPreset("hate-only"));
            Assert.Equal("hate-only", preset.Name);
            Assert.Equal(4, preset.Options.MinSeverity);
            Assert.Equal('#', preset.Options.MaskChar);
            Assert.Contains(VulgarityCategory.Hate, preset.Options.Categories);
            Assert.DoesNotContain(VulgarityCategory.Profanity, preset.Options.Categories);
        }

        [Fact]
        public void RemoveDropsATermFromTheBaseList()
        {
            Assert.True(VulgarityFilter.CreateDefault().Detect("oh damn"));
            Assert.False(VulgarityFilter.FromPreset(ReadPreset("brand")).Detect("oh damn"));
        }

        [Fact]
        public void RemovingAnAbsentTermIsNotAnError()
        {
            // A remote policy must keep working against an older term list.
            var builder = new VulgarityFilterBuilder().UseDefaultSeed();
            int before = builder.TermCount;
            builder.RemoveTerm("thistermwasneverhere");
            Assert.Equal(before, builder.TermCount);
        }

        [Fact]
        public void YourOwnOptionsBeatThePreset()
        {
            VulgarityPreset preset = VulgarityPreset.Parse(ReadPreset("hate-only"));
            VulgarityFilter loose = new VulgarityFilterBuilder()
                .AddPreset(preset)
                .Build(new VulgarityOptions { MinSeverity = 1 });

            Assert.True(loose.Detect("oh damn"));
        }

        [Fact]
        public void APresetRoundTripsThroughJson()
        {
            VulgarityPreset original = VulgarityPreset.Parse(ReadPreset("brand"));
            VulgarityPreset again = VulgarityPreset.Parse(original.ToJson());

            Assert.Equal(original.Name, again.Name);
            Assert.Equal(original.Languages.Count, again.Languages.Count);
            Assert.Equal(original.Entries.Count, again.Entries.Count);
            Assert.Equal(original.Remove.Count, again.Remove.Count);
            Assert.Equal(original.Options.MinSeverity, again.Options.MinSeverity);
            Assert.Equal(original.Options.MaskChar, again.Options.MaskChar);
        }

        [Fact]
        public void OptionsRoundTripThroughJson()
        {
            VulgarityOptions original = new VulgarityOptions
            {
                MinSeverity = 3,
                MaskToken = "[x]",
                ScoreMode = ScoreMode.Max,
                RepeatTolerance = false,
                CollapseContained = false,
                Categories = new HashSet<VulgarityCategory> { VulgarityCategory.Hate, VulgarityCategory.Drug },
            };

            VulgarityOptions again = VulgarityOptions.FromJson(original.ToJson());

            Assert.Equal(original.MinSeverity, again.MinSeverity);
            Assert.Equal(original.MaskToken, again.MaskToken);
            Assert.Equal(original.ScoreMode, again.ScoreMode);
            Assert.Equal(original.RepeatTolerance, again.RepeatTolerance);
            Assert.Equal(original.CollapseContained, again.CollapseContained);
            Assert.Equal(original.Categories, again.Categories);
        }

        // ---- A preset from the network is untrusted input. ----

        [Theory]
        [InlineData("not json at all", "not valid JSON")]
        [InlineData("[]", "must be a JSON object")]
        [InlineData("{\"schema\":99}", "preset schema")]
        [InlineData("{\"profile\":\"fold-v9\"}", "fold profile")]
        [InlineData("{\"languages\":[\"xx\"]}", "does not carry")]
        [InlineData("{\"languages\":\"en\"}", "must be an array")]
        // A field out of range is as malformed as a field of the wrong type. It
        // used to leak an ArgumentOutOfRangeException through the FormatException
        // contract.
        [InlineData("{\"options\":{\"minSeverity\":9}}", "minSeverity")]
        [InlineData("{\"options\":{\"scoreMode\":\"sideways\"}}", "scoreMode")]
        [InlineData("{\"options\":{\"categories\":[\"nonsense\"]}}", "does not know")]
        [InlineData("{\"options\":{\"maskChar\":\"toolong\"}}", "exactly one character")]
        [InlineData("{\"entries\":[{\"t\":\"x\",\"sev\":77}]}", "Severity must be 1 to 5")]
        [InlineData("{\"entries\":[{\"cat\":\"hate\"}]}", "must hold a non-empty 't' term")]
        [InlineData("{\"entries\":\"nope\"}", "must be an array")]
        [InlineData("{\"allow\":[5]}", "must hold strings only")]
        public void AMalformedPresetIsRefused(string json, string expectedMessage)
        {
            Exception error = Record.Exception(() => VulgarityPreset.Parse(json));
            Assert.NotNull(error);
            Assert.True(error is FormatException,
                "expected a FormatException, got " + error.GetType().Name);
            if (expectedMessage.Length > 0)
            {
                Assert.Contains(expectedMessage, error.Message);
            }
        }

        [Fact]
        public void AnUnknownTermCategoryIsToleratedAsOther()
        {
            // A term category stays lenient, so an older client keeps working
            // when a server adds a category.
            VulgarityPreset preset = VulgarityPreset.Parse(
                "{\"schema\":1,\"profile\":\"fold-v1\",\"languages\":[\"en\"],"
                + "\"entries\":[{\"t\":\"blorp\",\"cat\":\"brandnew\",\"sev\":3}]}");

            Assert.Equal(VulgarityCategory.Other, preset.Entries[0].Category);
            Assert.Equal("brandnew", preset.Entries[0].CategoryName);
        }

        [Fact]
        public void AnEmptyPresetStillNeedsTerms()
        {
            Assert.Throws<InvalidOperationException>(
                () => VulgarityFilter.FromPreset("{\"schema\":1,\"profile\":\"fold-v1\"}"));
        }

        [Fact]
        public void ACustomLanguageResolverIsUsed()
        {
            const string seed = "{\"schema\":1,\"profile\":\"fold-v1\","
                + "\"entries\":[{\"t\":\"blorp\",\"cat\":\"profanity\",\"sev\":3,\"w\":true}]}";

            VulgarityFilter filter = VulgarityFilter.FromPreset(
                VulgarityPreset.Parse("{\"schema\":1,\"profile\":\"fold-v1\",\"languages\":[\"en\"]}"),
                code => seed);

            Assert.True(filter.Detect("blorp"));
            Assert.False(filter.Detect("what the fuck"));   // the resolver replaced English
        }
    }
}
