using System;
using System.Collections.Generic;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Covers the public surface and its edge cases.</summary>
    public class FilterApiTests
    {
        private static readonly VulgarityFilter Filter = VulgarityFilter.CreateDefault();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyInputIsHandled(string text)
        {
            Assert.False(Filter.Detect(text));
            Assert.Empty(Filter.Scan(text));
            Assert.Equal(0, Filter.Score(text));
            Assert.Equal(text, Filter.Filter(text));
        }

        [Fact]
        public void OffsetsPointIntoTheOriginalText()
        {
            const string text = "café 🙂 f.u.c.k end";
            IReadOnlyList<VulgarityMatch> hits = Filter.Scan(text);
            VulgarityMatch hit = Assert.Single(hits);

            Assert.Equal("f.u.c.k", hit.Excerpt(text));
            Assert.Equal(hit.End - hit.Start, hit.Length);
            Assert.Equal("fuck", hit.Text);
        }

        [Fact]
        public void MaskingNeverChangesTheLengthWithAMaskCharacter()
        {
            const string text = "oh fuck that shit";
            string masked = Filter.Filter(text);
            Assert.Equal(text.Length, masked.Length);
            Assert.DoesNotContain("fuck", masked);
            Assert.DoesNotContain("shit", masked);
        }

        [Fact]
        public void MaskTokenReplacesTheWholeSpan()
        {
            VulgarityFilter f = Filter.WithOptions(new VulgarityOptions { MaskToken = "[x]" });
            Assert.Equal("oh [x]", f.Filter("oh fuck"));
        }

        [Fact]
        public void ScoreModesDiffer()
        {
            const string text = "fuck this shit";
            Assert.Equal(7, Filter.WithOptions(new VulgarityOptions { ScoreMode = ScoreMode.Total }).Score(text));
            Assert.Equal(4, Filter.WithOptions(new VulgarityOptions { ScoreMode = ScoreMode.Max }).Score(text));
        }

        [Fact]
        public void CategoryFilterRestrictsTheResult()
        {
            VulgarityFilter f = Filter.WithOptions(new VulgarityOptions
            {
                Categories = new HashSet<VulgarityCategory> { VulgarityCategory.Hate },
            });

            Assert.False(f.Detect("what the fuck"));
            Assert.True(f.Detect("that faggot"));
        }

        [Fact]
        public void MinSeverityRemovesClinicalTerms()
        {
            VulgarityFilter f = Filter.WithOptions(new VulgarityOptions { MinSeverity = 2 });
            Assert.False(f.Detect("the penis and the vagina"));
            Assert.True(Filter.Detect("the penis and the vagina"));
        }

        [Fact]
        public void OptionsAreValidated()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Filter.WithOptions(new VulgarityOptions { MinSeverity = 0 }));
            Assert.Throws<ArgumentException>(
                () => Filter.WithOptions(new VulgarityOptions { MaskToken = "" }));
            Assert.Throws<ArgumentNullException>(() => Filter.WithOptions(null));
        }

        [Fact]
        public void OptionsAreCopiedNotShared()
        {
            VulgarityOptions o = new VulgarityOptions { MaskChar = '#' };
            VulgarityFilter f = Filter.WithOptions(o);
            o.MaskChar = '!';
            Assert.Equal("oh ####", f.Filter("oh fuck"));
        }

        [Fact]
        public void ABuilderAcceptsCustomTermsAndAnAllowlist()
        {
            VulgarityFilter f = new VulgarityFilterBuilder()
                .AddTerm("Blorp", "profanity", 3, true)
                .AddAllow("blorpshire")
                .Build();

            Assert.True(f.Detect("that is blorp"));
            Assert.True(f.Detect("BL0RP"));          // the builder folds the term
            Assert.False(f.Detect("I live in Blorpshire"));
        }

        [Fact]
        public void ABuilderNeedsAtLeastOneTerm()
        {
            Assert.Throws<InvalidOperationException>(() => new VulgarityFilterBuilder().Build());
        }

        [Fact]
        public void ADuplicateTermKeepsTheWorseRating()
        {
            VulgarityFilter f = new VulgarityFilterBuilder()
                .AddTerm("blorp", "profanity", 2, true)
                .AddTerm("blorp", "hate", 5, true)
                .Build();

            Assert.Equal(1, f.TermCount);
            VulgarityMatch hit = Assert.Single(f.Scan("blorp"));
            Assert.Equal(5, hit.Severity);
            Assert.Equal(VulgarityCategory.Hate, hit.Category);
        }

        [Fact]
        public void MatchesArriveInOrder()
        {
            IReadOnlyList<VulgarityMatch> hits = Filter.Scan("fuck this shit and that cunt");
            for (int i = 1; i < hits.Count; i++)
            {
                Assert.True(hits[i - 1].Start <= hits[i].Start, "matches are out of order");
            }

            Assert.Equal(3, hits.Count);
        }

        [Fact]
        public void RepeatToleranceCanBeTurnedOff()
        {
            VulgarityFilter f = Filter.WithOptions(new VulgarityOptions { RepeatTolerance = false });
            Assert.True(Filter.Detect("fuuuck"));
            Assert.False(f.Detect("fuuuck"));
            Assert.True(f.Detect("fuck"));
        }

        [Fact]
        public void TheSqueezePassNeverWidensASpanStreamAAlreadyFound()
        {
            // "this shit" squeezes to "thishit", which would report "s shit".
            IReadOnlyList<VulgarityMatch> hits = Filter.Scan("fuck this shit");
            Assert.Equal(2, hits.Count);
            Assert.Equal("shit", hits[1].Excerpt("fuck this shit"));
        }

        [Fact]
        public void LongTextStaysFast()
        {
            string text = string.Concat(new string('a', 50000), " fuck ", new string('b', 50000));
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(Filter.Detect(text));
            watch.Stop();
            Assert.True(watch.ElapsedMilliseconds < 2000, "a 100 KB scan took " + watch.ElapsedMilliseconds + " ms");
        }
    }
}
