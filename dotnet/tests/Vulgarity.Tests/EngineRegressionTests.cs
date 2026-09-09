using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Regression cover for the matching engine.</summary>
    /// <remarks>
    /// Each group of cases names the defect it pins down. The shared vectors carry
    /// the same cases, so both ports stay honest; these tests catch a break earlier
    /// and say more about why.
    ///
    /// dart/test/engine_regression_test.dart is the mirror of this file.
    /// </remarks>
    public class EngineRegressionTests
    {
        private static readonly VulgarityFilter Filter = VulgarityFilter.CreateDefault();

        private static string Hits(string text)
        {
            List<string> found = new List<string>();
            foreach (VulgarityMatch match in Filter.Scan(text))
            {
                found.Add(match.Text);
            }

            return string.Join(",", found);
        }

        // ---- A term must not span two innocent words. ----

        [Theory]
        [InlineData("wash it down")]
        [InlineData("push it harder")]
        [InlineData("polish items")]
        [InlineData("shop ornament")]
        [InlineData("this ape shot up")]
        [InlineData("a fresh it item")]
        public void ATermDoesNotSpanTwoInnocentWords(string text)
        {
            Assert.True(Filter.Scan(text).Count == 0, "flagged " + Hits(text));
        }

        [Theory]
        [InlineData("a55 hole")]
        [InlineData("f u c k")]
        [InlineData("ape-shit")]
        [InlineData("a-s-s")]
        [InlineData(".f uc k")]
        public void ATermWrittenAcrossSeparatorsStillMatches(string text)
        {
            Assert.NotEmpty(Filter.Scan(text));
        }

        [Fact]
        public void TheWholeMatchIsReportedNotTheTail()
        {
            IReadOnlyList<VulgarityMatch> found = Filter.Scan("you fuck off");
            Assert.Single(found);
            Assert.Equal(4, found[0].Start);
            Assert.Equal(8, found[0].End);
        }

        // ---- An invisible character is not a word boundary. ----

        [Theory]
        [InlineData("cl\u00ADass", "soft hyphen")]
        [InlineData("cl\u200Bass", "zero width space")]
        [InlineData("gr\u2060ass", "word joiner")]
        [InlineData("cl\u200Dass", "zero width joiner")]
        [InlineData("cl\uFEFFass", "zero width no-break space")]
        public void AnInvisibleCharacterIsNotAWordBoundary(string text, string name)
        {
            Assert.True(Filter.Scan(text).Count == 0, name + " flagged " + Hits(text));
        }

        [Fact]
        public void ARealSeparatorStillBreaksAWord()
        {
            Assert.NotEmpty(Filter.Scan("cl ass"));
        }

        // ---- Repeat tolerance reaches a term with a doubled letter. ----

        [Theory]
        [InlineData("asss")]
        [InlineData("assshole")]
        [InlineData("pusssy")]
        [InlineData("bolllock")]
        [InlineData("fuuuck")]
        [InlineData("what an assssshole")]
        public void RepeatToleranceReachesATermWithADoubledLetter(string text)
        {
            Assert.NotEmpty(Filter.Scan(text));
        }

        /// <summary>
        /// A squeezed spelling must not stand in for a different real word. Text may
        /// repeat a letter more often than the term does, never less.
        /// </summary>
        [Theory]
        [InlineData("was")]
        [InlineData("class")]
        [InlineData("bass line")]
        [InlineData("the heel of the boot")]
        [InlineData("conn the ship")]
        [InlineData("a contrafagotto solo")]
        public void ASqueezedSpellingDoesNotStandInForARealWord(string text)
        {
            Assert.True(Filter.Scan(text).Count == 0, "flagged " + Hits(text));
        }

        [Fact]
        public void AnAllowlistedWordSurvivesTheSqueezePass()
        {
            Assert.Empty(Filter.Scan("I live in Scunnthorpe"));
        }

        // ---- The squeeze pass never doubles up a plain match. ----

        [Fact]
        public void BullshitterReportsOneTerm()
        {
            IReadOnlyList<VulgarityMatch> found = Filter.Scan("bullshitter");
            Assert.Single(found);
            Assert.Equal("bullshit", found[0].Text);
            Assert.Equal(3, Filter.Score("bullshitter"));
        }

        [Fact]
        public void ASqueezedCandidateThatOverlapsNothingIsKept()
        {
            IReadOnlyList<VulgarityMatch> found = Filter.Scan("damn the fuuuck");
            Assert.Equal(2, found.Count);
            Assert.Equal("damn", found[0].Text);
            Assert.Equal("fuck", found[1].Text);
        }

        // ---- A large text scans in linear time. ----

        /// <remarks>
        /// Both cases used to be quadratic: every match was compared against every
        /// allow span, and every squeezed candidate against every plain match. A
        /// megabyte took seconds.
        /// </remarks>
        [Theory]
        [InlineData("scunthorpe ")]
        [InlineData("damn daamn ")]
        public void AMegabyteScansInLinearTime(string unit)
        {
            StringBuilder buffer = new StringBuilder();
            while (buffer.Length < 1024 * 1024)
            {
                buffer.Append(unit);
            }

            string text = buffer.ToString();

            // Warm the code up on the whole text first. The runtime starts every
            // method interpreted and only promotes a hot loop once it has run, so a
            // short warm-up would time the tiering and not the algorithm.
            Filter.Scan(text);

            // Then take the best of three. The Dart mirror times one run, because
            // its test runner gives a test the machine to itself. The xunit host
            // runs whole test classes in parallel inside one process, so a single
            // reading here measures whatever else was scheduled beside it. A
            // quadratic scan is seconds on every run and fails all three.
            long best = long.MaxValue;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                Filter.Scan(text);
                watch.Stop();

                if (watch.ElapsedMilliseconds < best)
                {
                    best = watch.ElapsedMilliseconds;
                }
            }

            Assert.True(best < 500, best + " ms");
        }
    }
}
