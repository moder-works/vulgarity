using System;
using System.Collections.Generic;
using Vulgarity.Normalization;
using Vulgarity.Trie;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>Unit tests for the prefix trie with failure links.</summary>
    public class TrieTests
    {
        [Fact]
        public void FindsEveryPatternInOnePass()
        {
            AhoCorasick trie = new AhoCorasick();
            int he = trie.Add(Chars("he"));
            int she = trie.Add(Chars("she"));
            int his = trie.Add(Chars("his"));
            int hers = trie.Add(Chars("hers"));
            trie.Build();

            List<RawHit> hits = Scan(trie, "ushers");

            // "ushers" holds "she" at 1..3, "he" at 2..3 and "hers" at 2..5.
            Assert.Contains(hits, h => h.PatternId == she && h.End == 3);
            Assert.Contains(hits, h => h.PatternId == he && h.End == 3);
            Assert.Contains(hits, h => h.PatternId == hers && h.End == 5);
            Assert.DoesNotContain(hits, h => h.PatternId == his);
        }

        [Fact]
        public void ReportsAPatternThatIsAlsoASuffix()
        {
            AhoCorasick trie = new AhoCorasick();
            int shit = trie.Add(Chars("shit"));
            int bullshit = trie.Add(Chars("bullshit"));
            trie.Build();

            List<RawHit> hits = Scan(trie, "bullshit");
            Assert.Contains(hits, h => h.PatternId == shit);
            Assert.Contains(hits, h => h.PatternId == bullshit);
        }

        [Fact]
        public void GivesADuplicatePatternTheSameId()
        {
            AhoCorasick trie = new AhoCorasick();
            Assert.Equal(trie.Add(Chars("abc")), trie.Add(Chars("abc")));
            Assert.Equal(1, trie.PatternCount);
        }

        [Fact]
        public void ReportsThePatternLength()
        {
            AhoCorasick trie = new AhoCorasick();
            int id = trie.Add(Chars("hello"));
            trie.Build();
            Assert.Equal(5, trie.LengthOf(id));
        }

        [Fact]
        public void RefusesAnEmptyPattern()
        {
            AhoCorasick trie = new AhoCorasick();
            Assert.Throws<ArgumentException>(() => trie.Add(new int[0]));
        }

        [Fact]
        public void RefusesAPatternAfterBuild()
        {
            AhoCorasick trie = new AhoCorasick();
            trie.Add(Chars("a"));
            trie.Build();
            Assert.Throws<InvalidOperationException>(() => trie.Add(Chars("b")));
        }

        [Fact]
        public void RefusesAScanBeforeBuild()
        {
            AhoCorasick trie = new AhoCorasick();
            trie.Add(Chars("a"));
            Assert.Throws<InvalidOperationException>(() => Scan(trie, "a"));
        }

        [Fact]
        public void AnEmptyTrieFindsNothing()
        {
            AhoCorasick trie = new AhoCorasick();
            trie.Build();
            Assert.Empty(Scan(trie, "anything"));
        }

        private static int[] Chars(string s)
        {
            return TextNormalizer.FoldTerm(s);
        }

        private static List<RawHit> Scan(AhoCorasick trie, string text)
        {
            List<RawHit> hits = new List<RawHit>();
            trie.Scan(TextNormalizer.Normalize(text), hits);
            return hits;
        }
    }
}
