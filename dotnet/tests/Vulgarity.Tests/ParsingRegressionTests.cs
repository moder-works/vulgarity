using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>
    /// Guards the input-parsing contract: a malformed document throws
    /// <see cref="FormatException"/>, names the field, and never applies in part.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every case here stood for a real defect. A document from the network is
    /// untrusted, and each of these either leaked the wrong exception type, quietly
    /// coerced a bad value into a good one, or left the builder holding half a
    /// policy.
    /// </para>
    /// <para>dart/test/parsing_regression_test.dart is the mirror of this file.</para>
    /// </remarks>
    public class ParsingRegressionTests
    {
        // -------------------------------------------------------------------
        // A malformed field is a FormatException that names the field.
        // -------------------------------------------------------------------

        // The second argument is the text the message must carry: the field name.
        [Theory]
        // Options. These used to be GetInt32, GetBoolean and GetString calls, so a
        // bad value threw InvalidOperationException instead.
        [InlineData("{\"options\":{\"minSeverity\":\"3\"}}", "minSeverity")]
        [InlineData("{\"options\":{\"minSeverity\":1.5}}", "minSeverity")]
        [InlineData("{\"options\":{\"minSeverity\":true}}", "minSeverity")]
        [InlineData("{\"options\":{\"repeatTolerance\":\"yes\"}}", "repeatTolerance")]
        [InlineData("{\"options\":{\"collapseContained\":1}}", "collapseContained")]
        [InlineData("{\"options\":{\"maskToken\":5}}", "maskToken")]
        [InlineData("{\"options\":{\"maskChar\":5}}", "maskChar")]
        [InlineData("{\"options\":{\"scoreMode\":7}}", "scoreMode")]
        [InlineData("{\"options\":{\"categories\":\"hate\"}}", "categories")]
        // Out of range, which used to leak ArgumentOutOfRangeException.
        [InlineData("{\"options\":{\"minSeverity\":9}}", "minSeverity")]
        [InlineData("{\"options\":{\"minSeverity\":0}}", "minSeverity")]
        [InlineData("{\"options\":{\"maskToken\":\"\"}}", "maskToken")]
        // The document's own fields.
        [InlineData("{\"name\":5}", "name")]
        [InlineData("{\"description\":[1,2]}", "description")]
        [InlineData("{\"schema\":\"1\"}", "schema")]
        [InlineData("{\"profile\":5}", "profile")]
        // Entries.
        [InlineData("{\"entries\":[{\"t\":\"x\",\"sev\":\"3\"}]}", "sev")]
        [InlineData("{\"entries\":[{\"t\":\"x\",\"cat\":5}]}", "cat")]
        [InlineData("{\"entries\":[{\"t\":\"x\",\"w\":\"yes\"}]}", "w")]
        public void AFieldOfTheWrongTypeIsRefused(string json, string field)
        {
            FormatException error = Assert.Throws<FormatException>(() => VulgarityPreset.Parse(json));
            Assert.Contains(field, error.Message);
        }

        [Fact]
        public void AFractionalSchemaIsNotAWholeNumber()
        {
            // 1.0 is not 1. The reader refuses the fractional token rather than
            // rounding it into a schema number nobody wrote.
            Assert.Throws<FormatException>(() => VulgarityPreset.Parse("{\"schema\":1.0}"));
        }

        /// <summary>
        /// Both ports must name the same field for the same bad document, or a
        /// server cannot tell an operator what to fix. The order is minSeverity,
        /// maskChar, maskToken, repeatTolerance, collapseContained, scoreMode,
        /// categories.
        /// </summary>
        [Theory]
        [InlineData("{\"options\":{\"minSeverity\":9,\"maskChar\":\"toolong\"}}", "minSeverity")]
        [InlineData("{\"options\":{\"maskChar\":\"toolong\",\"maskToken\":\"\"}}", "maskChar")]
        [InlineData("{\"options\":{\"maskToken\":\"\",\"repeatTolerance\":\"yes\"}}", "maskToken")]
        [InlineData("{\"options\":{\"repeatTolerance\":\"yes\",\"collapseContained\":\"no\"}}", "repeatTolerance")]
        [InlineData("{\"options\":{\"collapseContained\":\"no\",\"scoreMode\":\"sideways\"}}", "collapseContained")]
        [InlineData("{\"options\":{\"scoreMode\":\"sideways\",\"categories\":[\"nonsense\"]}}", "scoreMode")]
        public void TheFieldsAreCheckedInOneFixedOrder(string json, string field)
        {
            FormatException error = Assert.Throws<FormatException>(() => VulgarityPreset.Parse(json));
            Assert.Contains(field, error.Message);
        }

        // -------------------------------------------------------------------
        // categories: an empty set and no set at all are opposite policies.
        // -------------------------------------------------------------------

        [Fact]
        public void AnEmptyCategorySetStaysAnEmptySet()
        {
            VulgarityOptions original = new VulgarityOptions
            {
                Categories = new HashSet<VulgarityCategory>(),
            };

            string json = original.ToJson();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement categories = doc.RootElement.GetProperty("categories");
                Assert.Equal(JsonValueKind.Array, categories.ValueKind);
                Assert.Equal(0, categories.GetArrayLength());
            }

            ISet<VulgarityCategory> again = VulgarityOptions.FromJson(json).Categories;
            Assert.NotNull(again);
            Assert.Empty(again);
        }

        [Fact]
        public void NoCategoryFilterLeavesTheKeyOutEntirely()
        {
            string json = new VulgarityOptions().ToJson();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement ignored;
                Assert.False(doc.RootElement.TryGetProperty("categories", out ignored));
            }

            Assert.Null(VulgarityOptions.FromJson(json).Categories);
        }

        [Fact]
        public void AnEmptySetAndNoSetMeanOppositeThings()
        {
            VulgarityFilter none = VulgarityFilter.CreateDefault(new VulgarityOptions
            {
                Categories = new HashSet<VulgarityCategory>(),
            });
            VulgarityFilter all = VulgarityFilter.CreateDefault();

            Assert.False(none.Detect("oh damn"), "an empty set matches no category");
            Assert.True(all.Detect("oh damn"), "no filter matches every category");
        }

        [Fact]
        public void AChosenCategorySetSurvivesAWholePresetRoundTrip()
        {
            VulgarityOptions original = new VulgarityOptions
            {
                Categories = new HashSet<VulgarityCategory> { VulgarityCategory.Hate },
            };

            VulgarityPreset preset = new VulgarityPreset(
                null, null, new string[0], original, new VulgarityTerm[0], new string[0], new string[0]);
            VulgarityPreset again = VulgarityPreset.Parse(preset.ToJson());

            Assert.Equal(original.Categories, again.Options.Categories);
        }

        // -------------------------------------------------------------------
        // AddPreset applies everything or nothing.
        // -------------------------------------------------------------------

        [Fact]
        public void ALanguageThePresetCannotServeUnloadsTheFirst()
        {
            // The document form never gets this far on .NET: Parse refuses a code
            // the assembly does not carry. That is the documented divergence from
            // Dart, which defers the question to the builder.
            Assert.Throws<FormatException>(() => VulgarityPreset.Parse("{\"languages\":[\"en\",\"pt\"]}"));

            // A preset built in code still reaches the builder, and the staging is
            // what has to hold: English must not survive the failure on "pt".
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder();
            VulgarityPreset preset = new VulgarityPreset(
                null,
                null,
                new[] { "en", "pt" },
                new VulgarityOptions(),
                new VulgarityTerm[0],
                new string[0],
                new string[0]);

            Assert.ThrowsAny<ArgumentException>(() => builder.AddPreset(preset));
            Assert.Equal(0, builder.TermCount);
        }

        [Fact]
        public void AResolverThatThrowsLeavesNothingBehind()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder();

            Assert.Throws<InvalidOperationException>(() => builder.AddPreset(
                VulgarityPreset.Parse("{\"languages\":[\"en\",\"es\"]}"),
                delegate(string code)
                {
                    if (code == "es")
                    {
                        throw new InvalidOperationException("the network is down");
                    }

                    return VulgarityLanguages.ReadSeed(code);
                }));

            Assert.Equal(0, builder.TermCount);
        }

        [Fact]
        public void ABadEntryUndoesNeitherTheListsNorTheRemovals()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder().UseDefaultSeed();
            int before = builder.TermCount;

            Assert.Throws<FormatException>(() => builder.AddPreset(
                "{\"languages\":[\"en\"],\"remove\":[\"damn\"],\"entries\":[{\"t\":\"\u200B\"}]}"));

            Assert.Equal(before, builder.TermCount);
            Assert.True(builder.Build().Detect("oh damn"),
                "the preset's remove list must not have run");
        }

        [Fact]
        public void APresetThatIsNothingAtAllIsAnArgumentError()
        {
            // C# settles the type at the call site, so the Dart test's "neither a
            // string nor a preset" case cannot arise. Null is the one value that
            // reaches these entry points untyped.
            Assert.Throws<ArgumentNullException>(() => VulgarityPreset.Parse(null));
            Assert.Throws<ArgumentNullException>(() => new VulgarityFilterBuilder().AddPreset((VulgarityPreset)null));
        }

        // -------------------------------------------------------------------
        // languages: the parser checks the shape, then the supply.
        // -------------------------------------------------------------------

        [Theory]
        [InlineData("EN")]
        [InlineData("e")]
        [InlineData("toolongcode")]
        [InlineData("en-US")]
        [InlineData("e1")]
        [InlineData("en ")]
        public void ALanguageCodeIsShapeCheckedAtParseTime(string code)
        {
            FormatException error = Assert.Throws<FormatException>(
                () => VulgarityPreset.Parse("{\"languages\":[\"" + code + "\"]}"));
            Assert.Contains("languages", error.Message);
        }

        [Fact]
        public void ACodeThisBuildDoesNotCarryIsRefusedAtParseTime()
        {
            // The documented divergence. Dart parses "pt" and lets the builder
            // decide, because a resolver may serve it. This port carries its packs
            // in the assembly, so it can answer at parse time and does.
            FormatException error = Assert.Throws<FormatException>(
                () => VulgarityPreset.Parse("{\"languages\":[\"pt\"]}"));
            Assert.Contains("does not carry", error.Message);
        }

        [Fact]
        public void ACustomResolverCanServeACodeNoPackCovers()
        {
            VulgarityPreset preset = new VulgarityPreset(
                null,
                null,
                new[] { "pt" },
                new VulgarityOptions(),
                new VulgarityTerm[0],
                new string[0],
                new string[0]);

            VulgarityFilter filter = VulgarityFilter.FromPreset(
                preset,
                delegate { return "{\"profile\":\"fold-v1\",\"entries\":[{\"t\":\"blorp\",\"sev\":3}]}"; });

            Assert.True(filter.Detect("blorp"));
        }

        // -------------------------------------------------------------------
        // Who is allowed to widen a term, and who is not.
        // -------------------------------------------------------------------

        /// <summary>
        /// A list is a source the app author chose, so the wider rule wins. The
        /// Spanish list carries "shit" with a boundary and the English list carries
        /// it without one; loading Spanish must not narrow English behind the
        /// author's back.
        /// </summary>
        [Fact]
        public void ALanguagePackDoesNotNarrowTheBundledList()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder()
                .UseDefaultSeed()
                .AddSeed(VulgarityLanguages.ReadSeed("es"));

            Assert.True(builder.HasTerm("shit"));

            IReadOnlyList<VulgarityMatch> found = builder.Build().Scan("shitty");
            Assert.True(found.Count > 0, "\"shitty\" stopped matching");
            Assert.Equal("shit", found[0].Text);
        }

        [Fact]
        public void TheSameHoldsForAPackNamedByAPreset()
        {
            VulgarityFilter filter = VulgarityFilter.FromPreset(
                VulgarityPreset.Parse("{\"languages\":[\"en\",\"es\"]}"));

            Assert.NotEmpty(filter.Scan("shitty"));
        }

        [Fact]
        public void TheFrenchPackDoesNotNarrowItEither()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder()
                .UseDefaultSeed()
                .AddSeed(VulgarityLanguages.ReadSeed("fr"));

            Assert.NotEmpty(builder.Build().Scan("biatches"));
        }

        [Fact]
        public void AddTermMayWidenBecauseThatIsTheAppAuthorSpeaking()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder().UseDefaultSeed();
            Assert.False(builder.Build().Detect("the class"));

            builder.AddTerm("ass", "profanity", 1, false);
            Assert.True(builder.Build().Detect("the class"),
                "a term named in code must be able to widen a bundled one");
        }

        [Fact]
        public void APresetCannotDropTheBoundaryOffABundledTerm()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder()
                .AddTerm("ass", "profanity", 3, true);

            // The preset repeats the term without "w". It used to widen the rule.
            builder.AddPreset("{\"entries\":[{\"t\":\"ass\",\"sev\":1}]}");
            VulgarityFilter filter = builder.Build();

            Assert.False(filter.Detect("the class assessment"));
            Assert.True(filter.Detect("you ass"));
        }

        [Fact]
        public void ABoundaryTheSecondSourceAsksForIsKeptToo()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder()
                .AddTerm("ass", "profanity", 3, false);
            builder.AddPreset("{\"entries\":[{\"t\":\"ass\",\"sev\":1,\"w\":true}]}");

            Assert.False(builder.Build().Detect("the class"));
        }

        [Fact]
        public void TheHigherSeverityStillWinsAndTheCategoryFollowsIt()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder()
                .AddTerm("blorp", "profanity", 2, true);
            builder.AddPreset("{\"entries\":[{\"t\":\"blorp\",\"cat\":\"hate\",\"sev\":5}]}");

            IReadOnlyList<VulgarityMatch> found = builder.Build().Scan("you blorp");
            Assert.Single(found);
            Assert.Equal(5, found[0].Severity);
            Assert.Equal(VulgarityCategory.Hate, found[0].Category);
            Assert.False(builder.Build().Detect("xblorpx"),
                "the boundary from the first source is still on");
        }

        [Fact]
        public void ALowerSeverityLeavesTheRatingAlone()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder()
                .AddTerm("blorp", "hate", 5, false);
            builder.AddPreset("{\"entries\":[{\"t\":\"blorp\",\"cat\":\"drug\",\"sev\":1}]}");

            IReadOnlyList<VulgarityMatch> found = builder.Build().Scan("blorp");
            Assert.Single(found);
            Assert.Equal(5, found[0].Severity);
            Assert.Equal(VulgarityCategory.Hate, found[0].Category);
        }

        // -------------------------------------------------------------------
        // The seed reader and the preset reader agree on every field but one.
        // -------------------------------------------------------------------

        private static List<VulgarityTerm> SeedTerms(string json)
        {
            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            SeedLoader.Load(json, VulgarityFilter.Profile, terms, new List<string>());
            return terms;
        }

        [Fact]
        public void AMissingSeverityKeepsEachDefault()
        {
            // These two numbers are the one deliberate difference, and they match
            // the Dart port exactly.
            Assert.Equal(1, SeedTerms("{\"entries\":[{\"t\":\"blorp\"}]}")[0].Severity);
            Assert.Equal(3, VulgarityPreset.Parse("{\"entries\":[{\"t\":\"blorp\"}]}").Entries[0].Severity);
        }

        [Fact]
        public void ASeverityThatIsNotAWholeNumberIsRefusedByBoth()
        {
            // The seed reader used to swallow this and call it severity 1, while the
            // preset reader called the same document severity 3.
            Assert.Throws<FormatException>(() => SeedTerms("{\"entries\":[{\"t\":\"x\",\"sev\":3.0}]}"));
            Assert.Throws<FormatException>(
                () => VulgarityPreset.Parse("{\"entries\":[{\"t\":\"x\",\"sev\":3.0}]}"));
        }

        [Theory]
        [InlineData("{\"schema\":\"1\",\"entries\":[]}", "schema")]
        [InlineData("{\"schema\":1.0,\"entries\":[]}", "schema")]
        [InlineData("{\"profile\":5,\"entries\":[]}", "profile")]
        [InlineData("{\"entries\":[{\"t\":\"x\",\"cat\":5}]}", "cat")]
        [InlineData("{\"entries\":[{\"t\":\"x\",\"w\":\"yes\"}]}", "w")]
        [InlineData("{\"entries\":[{\"t\":\"x\",\"sev\":\"3\"}]}", "sev")]
        [InlineData("{\"entries\":[],\"allow\":[5]}", "allow")]
        [InlineData("{\"entries\":[],\"allow\":\"blorp\"}", "allow")]
        public void ASeedWithABadFieldIsRefused(string json, string field)
        {
            FormatException error = Assert.Throws<FormatException>(() => SeedTerms(json));
            Assert.Contains(field, error.Message);
        }

        [Fact]
        public void AMissingCategoryIsStillOtherOnBoth()
        {
            Assert.Equal("other", SeedTerms("{\"entries\":[{\"t\":\"blorp\"}]}")[0].CategoryName);
            Assert.Equal("other",
                VulgarityPreset.Parse("{\"entries\":[{\"t\":\"blorp\"}]}").Entries[0].CategoryName);
        }

        [Fact]
        public void AnUnknownCategoryNameIsStillToleratedOnBoth()
        {
            // A NAME the build does not know stays lenient. A category of the wrong
            // TYPE does not. The two are different problems.
            Assert.Equal(VulgarityCategory.Other,
                SeedTerms("{\"entries\":[{\"t\":\"blorp\",\"cat\":\"brandnew\"}]}")[0].Category);
        }

        [Fact]
        public void ASeedThatIsNotJsonIsAFormatException()
        {
            // JsonDocument.Parse throws JsonException, which used to leak straight
            // out of FromSeed.
            Assert.Throws<FormatException>(() => VulgarityFilter.FromSeed("{oops"));
        }

        // -------------------------------------------------------------------
        // A term that folds to nothing.
        // -------------------------------------------------------------------

        [Fact]
        public void ATermThatFoldsToNothingIsAFormatExceptionFromASeedDocument()
        {
            Assert.Throws<FormatException>(() => VulgarityFilter.FromSeed("{\"entries\":[{\"t\":\"...\"}]}"));
        }

        [Fact]
        public void ATermThatFoldsToNothingIsAFormatExceptionFromAPreset()
        {
            Assert.Throws<FormatException>(
                () => VulgarityFilter.FromPreset("{\"entries\":[{\"t\":\"\u200B\"}]}"));
        }

        [Fact]
        public void ATermThatFoldsToNothingIsAFormatExceptionFromAPack()
        {
            List<byte> body = new List<byte> { 1, 0 };
            body.AddRange(PackString("fold-v1"));
            body.Add(1);
            body.AddRange(PackString("other"));
            body.Add(1);
            body.AddRange(PackString("..."));
            body.Add(0x01);

            Assert.Throws<FormatException>(
                () => new VulgarityFilterBuilder().AddSeed(BuildPack(body)));
        }

        [Fact]
        public void ATermThatFoldsToNothingIsAnArgumentErrorFromAddTerm()
        {
            // A caller who names a term in code got the argument wrong. That is not
            // a malformed document.
            Assert.ThrowsAny<ArgumentException>(
                () => new VulgarityFilterBuilder().AddTerm("...", "profanity", 1, true));
        }

        // -------------------------------------------------------------------
        // Varints are capped at 31 bits, so every runtime reads the same pack.
        // -------------------------------------------------------------------

        /// <remarks>
        /// Left uncapped, the shift overflows an int here and truncates to 32 bits
        /// on the Dart web compilers, so a crafted pack read as a different number
        /// on every runtime. These bytes used to raise OverflowException,
        /// ArgumentOutOfRangeException or OutOfMemoryException.
        /// </remarks>
        [Theory]
        [InlineData("85 80 80 80 10")]
        [InlineData("FF FF FF FF 0F")]
        public void APackNumberWiderThan31BitsIsRefused(string bytes)
        {
            List<byte> body = new List<byte> { 1, 0 };
            body.AddRange(PackString("fold-v1"));
            body.AddRange(Hex(bytes)); // the category count

            FormatException error = Assert.Throws<FormatException>(
                () => new VulgarityFilterBuilder().AddSeed(BuildPack(body)));
            Assert.Contains("runs too long", error.Message);
        }

        [Fact]
        public void AHostileCategoryCountDoesNotAllocate()
        {
            // 0x7FFFFFFF is the largest number the cap still admits. The reader used
            // to size an array from it and die with OutOfMemoryException; it now
            // grows a list and runs out of pack instead.
            List<byte> body = new List<byte> { 1, 0 };
            body.AddRange(PackString("fold-v1"));
            body.AddRange(Hex("FF FF FF FF 07")); // 0x7FFFFFFF categories

            Assert.Throws<FormatException>(() => new VulgarityFilterBuilder().AddSeed(BuildPack(body)));
        }

        [Fact]
        public void AHostileEntryCountDoesNotAllocate()
        {
            List<byte> body = new List<byte> { 1, 0 };
            body.AddRange(PackString("fold-v1"));
            body.Add(1);
            body.AddRange(PackString("other"));
            body.AddRange(Hex("FF FF FF FF 07")); // 0x7FFFFFFF entries

            Assert.Throws<FormatException>(() => new VulgarityFilterBuilder().AddSeed(BuildPack(body)));
        }

        [Fact]
        public void AStringLengthOfMinusOneIsRefused()
        {
            // -1 as a 32-bit integer is FF FF FF FF 0F in LEB128. It used to read
            // back as a negative length and throw ArgumentOutOfRangeException out of
            // the UTF-8 decoder.
            List<byte> body = new List<byte> { 1, 0 };
            body.AddRange(Hex("FF FF FF FF 0F")); // the profile string's length

            Assert.Throws<FormatException>(() => new VulgarityFilterBuilder().AddSeed(BuildPack(body)));
        }

        [Fact]
        public void AnOverLongStringLengthIsRefused()
        {
            // A length inside the cap but past the end of the pack.
            List<byte> body = new List<byte> { 1, 0 };
            body.AddRange(Hex("FF FF FF 7F")); // 0x0FFFFFFF bytes of profile

            FormatException error = Assert.Throws<FormatException>(
                () => new VulgarityFilterBuilder().AddSeed(BuildPack(body)));
            Assert.Contains("ends inside a string", error.Message);
        }

        [Fact]
        public void APackTheEncoderWouldWriteStillReads()
        {
            // The control. If this failed, the cap would be refusing real packs.
            List<byte> body = new List<byte> { 1, 0 };
            body.AddRange(PackString("fold-v1"));
            body.Add(1);
            body.AddRange(PackString("profanity"));
            body.Add(1);
            body.AddRange(PackString("blorp"));
            body.Add(0x03);

            VulgarityFilterBuilder builder = new VulgarityFilterBuilder().AddSeed(BuildPack(body));
            Assert.Equal(1, builder.TermCount);
            Assert.Equal(3, builder.Build().Scan("blorp")[0].Severity);
        }

        // -------------------------------------------------------------------
        // Pack text: the shapes a sender actually produces.
        // -------------------------------------------------------------------

        private static string PackTextEn()
        {
            return VulgarityLanguages.ReadSeed("en");
        }

        private static void ExpectLoads(string text, string shape)
        {
            Assert.True(PackText.TryRead(text) != null, "refused " + shape);

            VulgarityFilterBuilder builder = new VulgarityFilterBuilder().AddSeed(text);
            Assert.True(builder.TermCount > 0, "AddSeed refused " + shape);
        }

        [Fact]
        public void PaddingLeftOff()
        {
            ExpectLoads(PackTextEn().Replace("=", ""), "base64 with the padding cut");
        }

        [Fact]
        public void UrlSafeAndUnpadded()
        {
            // This is what base64url produces, and what a pack in a URL looks like.
            ExpectLoads(
                PackTextEn().Replace("+", "-").Replace("/", "_").Replace("=", ""),
                "unpadded base64url");
        }

        [Fact]
        public void ALeadingByteOrderMark()
        {
            ExpectLoads("\uFEFF" + PackTextEn(), "a pack behind a BOM");
        }

        [Fact]
        public void AByteOrderMarkAndANewline()
        {
            // What a text editor writes when it saves the pack as UTF-8 with a BOM.
            ExpectLoads("\uFEFF\n" + PackTextEn() + "\n", "a pack behind a BOM and a newline");
        }

        [Fact]
        public void ABomInTheMiddleIsStillCorruption()
        {
            string text = PackTextEn();
            int half = text.Length / 2;
            Assert.Null(PackText.TryRead(text.Substring(0, half) + "\uFEFF" + text.Substring(half)));
        }

        [Fact]
        public void ASeedDocumentBehindABomStillTakesTheJsonPath()
        {
            VulgarityFilterBuilder builder = new VulgarityFilterBuilder().AddSeed(
                "\uFEFF\n{\"profile\":\"fold-v1\",\"entries\":[{\"t\":\"blorp\",\"sev\":3}]}");
            Assert.Equal(1, builder.TermCount);
        }

        [Fact]
        public void TextThatIsNeitherIsStillRefusedClearly()
        {
            Assert.Throws<FormatException>(
                () => new VulgarityFilterBuilder().AddSeed("VlBLMQ but not really"));
            Assert.Throws<FormatException>(() => new VulgarityFilterBuilder().AddSeed(""));
        }

        // -------------------------------------------------------------------
        // Language codes arrive upper-cased more often than anyone expects.
        // -------------------------------------------------------------------

        [Fact]
        public void EnglishInAnyCase()
        {
            Assert.Equal(VulgarityLanguages.ReadSeed("en"), VulgarityLanguages.ReadSeed("EN"));
            Assert.Equal(VulgarityLanguages.ReadSeed("en"), VulgarityLanguages.ReadSeed("En"));
        }

        [Fact]
        public void APackInAnyCase()
        {
            Assert.Equal(VulgarityLanguages.ReadSeed("es"), VulgarityLanguages.ReadSeed("ES"));
            Assert.Equal(VulgarityLanguages.ReadSeed("zh"), VulgarityLanguages.ReadSeed("Zh"));
            Assert.True(VulgarityLanguages.Has("FR"));
        }

        [Fact]
        public void ACodeNoPackCoversIsStillAnArgumentError()
        {
            Assert.ThrowsAny<ArgumentException>(() => VulgarityLanguages.ReadSeed("XX"));
        }

        // -------------------------------------------------------------------
        // Helpers that write a pack no encoder would ever produce.
        // -------------------------------------------------------------------

        /// <summary>Encodes text as a pack string: a varint length, then the UTF-8 bytes.</summary>
        private static byte[] PackString(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text);
            byte[] result = new byte[raw.Length + 1];
            result[0] = (byte)raw.Length;
            Array.Copy(raw, 0, result, 1, raw.Length);
            return result;
        }

        private static byte[] Hex(string spaced)
        {
            string[] parts = spaced.Split(' ');
            byte[] result = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = Convert.ToByte(parts[i], 16);
            }

            return result;
        }

        /// <summary>Wraps a pack body in the magic and the RC4 mask.</summary>
        /// <remarks>
        /// This mirrors tool/packlib.py, so a test can hand the reader bytes no
        /// encoder would ever write.
        /// </remarks>
        private static byte[] BuildPack(List<byte> body)
        {
            byte[] key = Encoding.ASCII.GetBytes("vulgarity-pack-v1");

            byte[] box = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                box[i] = (byte)i;
            }

            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + box[i] + key[i % key.Length]) & 0xFF;
                byte swap = box[i];
                box[i] = box[j];
                box[j] = swap;
            }

            List<byte> result = new List<byte> { 0x56, 0x50, 0x4B, 0x31 }; // "VPK1"
            int a = 0;
            int b = 0;
            for (int i = 0; i < body.Count; i++)
            {
                a = (a + 1) & 0xFF;
                b = (b + box[a]) & 0xFF;
                byte swap = box[a];
                box[a] = box[b];
                box[b] = swap;
                result.Add((byte)(body[i] ^ box[(box[a] + box[b]) & 0xFF]));
            }

            return result.ToArray();
        }
    }
}
