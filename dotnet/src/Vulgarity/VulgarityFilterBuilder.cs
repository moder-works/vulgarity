using System;
using System.Collections.Generic;
using System.Text;
using Vulgarity.Normalization;
using Vulgarity.Trie;

namespace Vulgarity
{
    /// <summary>Collects terms from one or more sources, then compiles a filter.</summary>
    /// <example>
    /// <code>
    /// var filter = new VulgarityFilterBuilder()
    ///     .UseDefaultSeed()
    ///     .UseLanguage("es")
    ///     .AddTerm("brandname", "profanity", 2, true)
    ///     .AddAllow("scunthorpe")
    ///     .Build();
    /// </code>
    /// </example>
    public sealed class VulgarityFilterBuilder
    {
        private readonly Dictionary<string, VulgarityTerm> _terms =
            new Dictionary<string, VulgarityTerm>(StringComparer.Ordinal);

        private readonly List<string> _termOrder = new List<string>();
        private readonly Dictionary<string, bool> _allow = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Adds the bundled English term list.</summary>
        public VulgarityFilterBuilder UseDefaultSeed()
        {
            return AddSeed(VulgarityLanguages.ReadSeed(VulgarityLanguages.Default));
        }

        /// <summary>Adds one bundled language pack.</summary>
        /// <param name="code">A language code, for example "es".</param>
        public VulgarityFilterBuilder UseLanguage(string code)
        {
            return AddSeed(VulgarityLanguages.ReadSeed(code));
        }

        /// <summary>Adds every term from one seed document.</summary>
        public VulgarityFilterBuilder AddSeed(string json)
        {
            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();
            SeedLoader.Load(json, FoldTableData.Profile, terms, allow);

            for (int i = 0; i < terms.Count; i++)
            {
                Register(terms[i]);
            }

            for (int i = 0; i < allow.Count; i++)
            {
                AddAllow(allow[i]);
            }

            return this;
        }

        /// <summary>Adds one term.</summary>
        /// <param name="term">The term. The builder folds it, so any spelling works.</param>
        /// <param name="category">A category name, for example "profanity".</param>
        /// <param name="severity">How bad the term is, from 1 to 5.</param>
        /// <param name="requireBoundary">True to demand a word boundary around a match.</param>
        public VulgarityFilterBuilder AddTerm(string term, string category, int severity, bool requireBoundary)
        {
            if (severity < 1 || severity > 5)
            {
                throw new ArgumentOutOfRangeException("severity", "Severity must be 1 to 5.");
            }

            Register(new VulgarityTerm(term, category ?? "other", severity, requireBoundary));
            return this;
        }

        /// <summary>Adds one innocent word that holds a term, so the filter never flags it.</summary>
        public VulgarityFilterBuilder AddAllow(string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return this;
            }

            string folded = FoldToString(word);
            if (folded.Length > 0)
            {
                _allow[folded] = true;
            }

            return this;
        }

        /// <summary>Compiles the trie and returns the filter.</summary>
        public VulgarityFilter Build()
        {
            return Build(null);
        }

        /// <summary>Compiles the trie and returns the filter.</summary>
        public VulgarityFilter Build(VulgarityOptions options)
        {
            VulgarityOptions effective = options == null ? new VulgarityOptions() : options.Clone();
            effective.Validate();

            if (_termOrder.Count == 0)
            {
                throw new InvalidOperationException(
                    "Add at least one term. Call UseDefaultSeed to load the bundled list.");
            }

            VulgarityTerm[] terms = new VulgarityTerm[_termOrder.Count];
            AhoCorasick termTrie = new AhoCorasick();

            for (int i = 0; i < _termOrder.Count; i++)
            {
                VulgarityTerm term = _terms[_termOrder[i]];
                terms[i] = term;

                int id = termTrie.Add(TextNormalizer.FoldTerm(term.Text));
                if (id != i)
                {
                    throw new InvalidOperationException(
                        "The trie assigned an unexpected id. Term '" + term.Text + "' is a duplicate.");
                }
            }

            termTrie.Build();

            AhoCorasick allowTrie = new AhoCorasick();
            foreach (KeyValuePair<string, bool> entry in _allow)
            {
                allowTrie.Add(TextNormalizer.FoldTerm(entry.Key));
            }

            allowTrie.Build();

            return new VulgarityFilter(terms, termTrie, allowTrie, effective);
        }

        /// <summary>Stores a term under its folded form, keeping the worse of any pair.</summary>
        private void Register(VulgarityTerm term)
        {
            string folded = FoldToString(term.Text);
            if (folded.Length == 0)
            {
                throw new ArgumentException("Term '" + term.Text + "' folds to nothing.");
            }

            VulgarityTerm normalized = folded == term.Text
                ? term
                : new VulgarityTerm(folded, term.CategoryName, term.Severity, term.RequireBoundary);

            VulgarityTerm existing;
            if (_terms.TryGetValue(folded, out existing))
            {
                // Two sources gave the same term. Keep the worse rating, and keep
                // the wider match rule, so no source loses coverage.
                if (normalized.Severity > existing.Severity || !normalized.RequireBoundary)
                {
                    int severity = Math.Max(existing.Severity, normalized.Severity);
                    bool boundary = existing.RequireBoundary && normalized.RequireBoundary;
                    string category = existing.Severity >= normalized.Severity
                        ? existing.CategoryName
                        : normalized.CategoryName;
                    _terms[folded] = new VulgarityTerm(folded, category, severity, boundary);
                }

                return;
            }

            _terms[folded] = normalized;
            _termOrder.Add(folded);
        }

        private static string FoldToString(string text)
        {
            int[] folded = TextNormalizer.FoldTerm(text);
            StringBuilder builder = new StringBuilder(folded.Length);
            for (int i = 0; i < folded.Length; i++)
            {
                int cp = folded[i];
                if (cp > 0xFFFF)
                {
                    // An unmapped character outside the basic plane needs a surrogate pair.
                    cp -= 0x10000;
                    builder.Append((char)(0xD800 + (cp >> 10)));
                    builder.Append((char)(0xDC00 + (cp & 0x3FF)));
                }
                else
                {
                    builder.Append((char)cp);
                }
            }

            return builder.ToString();
        }
    }
}
