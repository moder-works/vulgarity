using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Vulgarity.Tests
{
    /// <summary>The point of the pack format, written as a test.</summary>
    /// <remarks>
    /// Before the packs, `strings Vulgarity.dll` printed 418 of the 526 English
    /// terms. It must now print none. The packs are a pure function of the files
    /// in data/, so this test is exact and repeatable, not probabilistic.
    /// </remarks>
    public class LeakTests
    {
        private static List<string> AllTerms()
        {
            List<string> all = new List<string>();
            foreach (string code in VulgarityLanguages.Available)
            {
                List<VulgarityTerm> terms = new List<VulgarityTerm>();
                PackReader.Load(VulgarityLanguages.ReadSeedPack(code), VulgarityFilter.Profile,
                    terms, new List<string>());
                foreach (VulgarityTerm term in terms)
                {
                    all.Add(term.Text);
                }
            }

            return all;
        }

        /// <summary>True when the term appears with no letter or digit on either side.</summary>
        private static bool HasWholeWord(string haystack, string term)
        {
            int at = haystack.IndexOf(term, StringComparison.Ordinal);
            while (at >= 0)
            {
                bool leftClear = at == 0 || !IsWordCharacter(haystack[at - 1]);
                int after = at + term.Length;
                bool rightClear = after >= haystack.Length || !IsWordCharacter(haystack[after]);
                if (leftClear && rightClear)
                {
                    return true;
                }

                at = haystack.IndexOf(term, at + 1, StringComparison.Ordinal);
            }

            return false;
        }

        private static bool IsWordCharacter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        [Fact]
        public void NoEmbeddedPackHoldsAReadableTerm()
        {
            // The packs are the region that used to leak, so hold them to every
            // term, however short.
            List<string> terms = AllTerms();

            foreach (string code in VulgarityLanguages.Available)
            {
                string pack = Encoding.Latin1.GetString(VulgarityLanguages.ReadSeedPack(code));
                foreach (string term in terms)
                {
                    Assert.False(pack.Contains(term),
                        "the '" + code + "' pack holds the readable term '" + term + "'");
                }
            }
        }

        [Fact]
        public void TheAssemblyHoldsNoReadableTerm()
        {
            string location = typeof(VulgarityFilter).Assembly.Location;
            Assert.False(string.IsNullOrEmpty(location),
                "Cannot read the assembly, so this test cannot check it.");

            byte[] assembly = File.ReadAllBytes(location);

            // .NET keeps the user-string heap as UTF-16, so a term added back as a
            // C# literal would not show up in a UTF-8 scan. Check both.
            string asUtf8 = Encoding.Latin1.GetString(assembly);
            string asUtf16 = Encoding.Unicode.GetString(assembly);

            // Match on a word boundary. An assembly is full of ordinary English
            // identifiers, and a plain substring search reports every one that
            // happens to contain a term: 'arse' inside 'Parse', 'arato' inside
            // 'Separator', 'bugger' inside 'DebuggerBrowsableState'. A leaked
            // term would sit between separator bytes, so a boundary test keeps
            // every real hit and drops the noise.
            List<string> leaked = new List<string>();
            foreach (string term in AllTerms())
            {
                if (term.Length >= 4 && (HasWholeWord(asUtf8, term) || HasWholeWord(asUtf16, term)))
                {
                    leaked.Add(term);
                }
            }

            Assert.True(leaked.Count == 0,
                "The assembly holds " + leaked.Count + " readable terms: " +
                string.Join(", ", leaked.ToArray()));
        }
    }
}
