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
        private VulgarityOptions _presetOptions;

        /// <summary>Adds the bundled English term list.</summary>
        public VulgarityFilterBuilder UseDefaultSeed()
        {
            return AddSeed(VulgarityLanguages.ReadSeedPack(VulgarityLanguages.Default));
        }

        /// <summary>Adds one bundled language pack.</summary>
        /// <param name="code">A language code, for example "es".</param>
        public VulgarityFilterBuilder UseLanguage(string code)
        {
            return AddSeed(VulgarityLanguages.ReadSeedPack(code));
        }

        /// <summary>Adds every term from one term list.</summary>
        /// <param name="document">
        /// A seed document as JSON, or a pack as base64 text. This reads the
        /// format from the text itself, so a caller never has to say which it
        /// is. Build a pack with: python3 tool/pack.py my-list.json
        /// </param>
        public VulgarityFilterBuilder AddSeed(string document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            byte[] pack = PackText.TryRead(document);
            if (pack != null)
            {
                return AddSeed(pack);
            }

            if (!LooksLikeJson(document))
            {
                throw new FormatException(
                    "This is neither a seed document nor a pack. A seed document " +
                    "starts with '{'. A pack is base64 text starting with 'VlBLMQ'.");
            }

            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();
            SeedLoader.Load(document, FoldTableData.Profile, terms, allow);
            return Absorb(terms, allow);
        }

        /// <summary>Adds every term from one pack.</summary>
        /// <remarks>
        /// Use this when you host your own list and want no readable term in
        /// transit or in a cache. Build one with: python3 tool/pack.py
        /// </remarks>
        public VulgarityFilterBuilder AddSeed(byte[] pack)
        {
            if (pack == null)
            {
                throw new ArgumentNullException("pack");
            }

            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();
            PackReader.Load(pack, FoldTableData.Profile, terms, allow);
            return Absorb(terms, allow);
        }

        private VulgarityFilterBuilder Absorb(List<VulgarityTerm> terms, List<string> allow)
        {
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

        /// <summary>True when the first character that is not blank is an opening brace.</summary>
        private static bool LooksLikeJson(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]) && text[i] != '\uFEFF')
                {
                    return text[i] == '{';
                }
            }

            return false;
        }

        /// <summary>Adds a whole policy: its language packs, terms, allowlist and options.</summary>
        /// <remarks>
        /// The preset's options apply when you call <see cref="Build()"/> with
        /// no options of your own. Adding a second preset replaces the first
        /// one's options; the terms of both stay.
        /// </remarks>
        public VulgarityFilterBuilder AddPreset(string json)
        {
            return AddPreset(VulgarityPreset.Parse(json), null);
        }

        /// <summary>Adds a whole policy: its language packs, terms, allowlist and options.</summary>
        public VulgarityFilterBuilder AddPreset(VulgarityPreset preset)
        {
            return AddPreset(preset, null);
        }

        /// <summary>Adds a whole policy, resolving its language codes yourself.</summary>
        /// <param name="preset">The policy to apply.</param>
        /// <param name="languageResolver">
        /// Turns a language code into a seed document. Pass null to read the
        /// packs bundled with this build.
        /// </param>
        public VulgarityFilterBuilder AddPreset(VulgarityPreset preset, Func<string, string> languageResolver)
        {
            if (preset == null)
            {
                throw new ArgumentNullException("preset");
            }

            Func<string, string> resolve = languageResolver ?? VulgarityLanguages.ReadSeed;

            // Order matters. Load the lists, add on top, then take away.
            foreach (string code in preset.Languages)
            {
                AddSeed(resolve(code));
            }

            foreach (VulgarityTerm term in preset.Entries)
            {
                Register(term);
            }

            foreach (string word in preset.Allow)
            {
                AddAllow(word);
            }

            foreach (string term in preset.Remove)
            {
                RemoveTerm(term);
            }

            _presetOptions = preset.Options;
            return this;
        }

        /// <summary>Drops one term, if it is present.</summary>
        /// <remarks>
        /// A term that is absent is not an error. That keeps a remote policy
        /// working against an older term list.
        /// </remarks>
        public VulgarityFilterBuilder RemoveTerm(string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return this;
            }

            string folded = FoldToString(term);
            if (_terms.Remove(folded))
            {
                _termOrder.Remove(folded);
            }

            return this;
        }

        /// <summary>Reports whether the builder currently holds a term.</summary>
        public bool HasTerm(string term)
        {
            return !string.IsNullOrEmpty(term) && _terms.ContainsKey(FoldToString(term));
        }

        /// <summary>How many terms the builder currently holds.</summary>
        public int TermCount
        {
            get { return _termOrder.Count; }
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
            // Your own options win. Otherwise the last preset's options apply.
            VulgarityOptions effective = options != null
                ? options.Clone()
                : (_presetOptions == null ? new VulgarityOptions() : _presetOptions.Clone());
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
