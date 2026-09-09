using System;
using System.Collections.Generic;
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

            List<VulgarityTerm> terms = new List<VulgarityTerm>();
            List<string> allow = new List<string>();
            ReadDocument(document, terms, allow);
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

        /// <summary>Reads one term list into the two lists, whichever format it is in.</summary>
        /// <remarks>
        /// The format is settled from the first character that carries content, so
        /// a seed document never runs through the base64 normaliser first. That
        /// normaliser walks the whole string, and on a megabyte of JSON the walk
        /// was a quarter of the load.
        /// </remarks>
        private static void ReadDocument(string document, List<VulgarityTerm> terms, List<string> allow)
        {
            int start = FirstContent(document);

            if (start < document.Length && document[start] == '{')
            {
                // A JSON reader takes leading blanks but not a byte-order mark, so
                // hand it the document from its first real character. The copy only
                // happens when there was something to skip.
                SeedLoader.Load(
                    start == 0 ? document : document.Substring(start),
                    FoldTableData.Profile,
                    terms,
                    allow);
                return;
            }

            // "VPK1" in base64 is "VlBLMQ", and the magic is never masked, so every
            // pack starts with those six characters.
            if (StartsWithAt(document, "VlBLMQ", start))
            {
                byte[] pack = PackText.TryRead(document);
                if (pack != null)
                {
                    PackReader.Load(pack, FoldTableData.Profile, terms, allow);
                    return;
                }
            }

            throw new FormatException(
                "This is neither a seed document nor a pack. A seed document " +
                "starts with '{'. A pack is base64 text starting with 'VlBLMQ'.");
        }

        /// <summary>The index of the first character that is neither blank nor a BOM.</summary>
        /// <remarks>
        /// The blank set matches the one <see cref="PackText.Normalize"/> drops, so
        /// both agree on where a document begins.
        /// </remarks>
        private static int FirstContent(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                bool blank = c == ' ' || (c >= '\u0009' && c <= '\u000D') || c == '\uFEFF';
                if (!blank)
                {
                    return i;
                }

                i++;
            }

            return i;
        }

        private static bool StartsWithAt(string text, string prefix, int index)
        {
            return index + prefix.Length <= text.Length
                && string.CompareOrdinal(text, index, prefix, 0, prefix.Length) == 0;
        }

        /// <summary>Commits a whole term list.</summary>
        /// <remarks>
        /// Every term is folded first, so a list with one unusable term leaves the
        /// builder untouched.
        /// </remarks>
        private VulgarityFilterBuilder Absorb(List<VulgarityTerm> terms, List<string> allow)
        {
            List<VulgarityTerm> folded = FoldAll(terms);
            for (int i = 0; i < folded.Count; i++)
            {
                Merge(folded[i], true);
            }

            for (int i = 0; i < allow.Count; i++)
            {
                AddAllow(allow[i]);
            }

            return this;
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
        /// <remarks>
        /// <para>
        /// This is all or nothing. Every list is resolved and read, and every term
        /// is folded, before the builder is touched at all. A preset that names two
        /// languages and cannot serve the second leaves the first unloaded, and a
        /// <paramref name="languageResolver"/> that throws leaves the builder
        /// exactly as it was.
        /// </para>
        /// <para>
        /// A preset entry can make a term already loaded stricter or more severe,
        /// never wider. The language lists a preset names carry no such limit,
        /// because those are lists you chose to load.
        /// </para>
        /// </remarks>
        public VulgarityFilterBuilder AddPreset(VulgarityPreset preset, Func<string, string> languageResolver)
        {
            if (preset == null)
            {
                throw new ArgumentNullException("preset");
            }

            // Stage the whole policy first. Resolving a list, reading it and folding
            // its terms can all fail, and a policy that fails halfway would
            // otherwise leave the builder holding part of it.
            //
            // The lists stay apart from the entries, because the two commit under
            // different merge rules. See Merge.
            List<VulgarityTerm> stagedLists = new List<VulgarityTerm>();
            List<string> stagedAllow = new List<string>();

            foreach (string code in preset.Languages)
            {
                if (languageResolver == null)
                {
                    // The bundled path hands the reader raw pack bytes, so the
                    // default preset does no base64 work at all.
                    PackReader.Load(
                        VulgarityLanguages.ReadSeedPack(code),
                        FoldTableData.Profile,
                        stagedLists,
                        stagedAllow);
                }
                else
                {
                    ReadDocument(languageResolver(code), stagedLists, stagedAllow);
                }
            }

            foreach (string word in preset.Allow)
            {
                stagedAllow.Add(word);
            }

            // The last steps that can fail. Past here nothing throws.
            List<VulgarityTerm> lists = FoldAll(stagedLists);
            List<VulgarityTerm> entries = FoldAll(preset.Entries);

            // Order matters. Load the lists, add on top, then take away. A list is
            // a source the app author chose, so it may widen a term. An entry came
            // with the policy, so it may not.
            for (int i = 0; i < lists.Count; i++)
            {
                Merge(lists[i], true);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Merge(entries[i], false);
            }

            for (int i = 0; i < stagedAllow.Count; i++)
            {
                AddAllow(stagedAllow[i]);
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

            // A second trie over the squeezed spelling of every term. The repeat
            // pass scans a stream whose runs are collapsed, so it has to carry
            // patterns whose runs are collapsed too, or no term holding a doubled
            // letter could match. Several terms can share one squeezed spelling, so
            // the map is one to many.
            AhoCorasick squeezedTrie = new AhoCorasick();
            List<List<int>> squeezedTerms = new List<List<int>>();
            int[][] termRuns = new int[terms.Length][];

            for (int i = 0; i < terms.Length; i++)
            {
                int id = squeezedTrie.Add(TextNormalizer.SqueezeTerm(terms[i].Text));
                while (squeezedTerms.Count <= id)
                {
                    squeezedTerms.Add(new List<int>());
                }

                squeezedTerms[id].Add(i);
                termRuns[i] = TextNormalizer.TermRuns(terms[i].Text);
            }

            squeezedTrie.Build();

            int[][] patternTerms = new int[squeezedTerms.Count][];
            for (int i = 0; i < squeezedTerms.Count; i++)
            {
                patternTerms[i] = squeezedTerms[i].ToArray();
            }

            AhoCorasick allowTrie = new AhoCorasick();
            foreach (KeyValuePair<string, bool> entry in _allow)
            {
                allowTrie.Add(TextNormalizer.FoldTerm(entry.Key));
            }

            allowTrie.Build();

            return new VulgarityFilter(
                terms,
                termTrie,
                allowTrie,
                effective,
                squeezedTrie,
                patternTerms,
                termRuns);
        }

        /// <summary>Folds one term. Returns null when nothing is left of it.</summary>
        private static VulgarityTerm Fold(VulgarityTerm term)
        {
            string folded = FoldToString(term.Text);
            if (folded.Length == 0)
            {
                return null;
            }

            return folded == term.Text
                ? term
                : new VulgarityTerm(folded, term.CategoryName, term.Severity, term.RequireBoundary);
        }

        /// <summary>Folds a whole list, or throws before it folds any of it into the builder.</summary>
        /// <remarks>
        /// A term that folds to nothing came from a document here, not from a call
        /// to <see cref="AddTerm"/>, so it is a <see cref="FormatException"/>: the
        /// document is malformed.
        /// </remarks>
        private static List<VulgarityTerm> FoldAll(IReadOnlyList<VulgarityTerm> terms)
        {
            List<VulgarityTerm> folded = new List<VulgarityTerm>(terms.Count);
            for (int i = 0; i < terms.Count; i++)
            {
                VulgarityTerm one = Fold(terms[i]);
                if (one == null)
                {
                    throw new FormatException(
                        "The term '" + terms[i].Text + "' folds to nothing under profile " +
                        FoldTableData.Profile + ", so it could never match.");
                }

                folded.Add(one);
            }

            return folded;
        }

        /// <summary>Folds a term and stores it. Throws when the term folds to nothing.</summary>
        /// <remarks>
        /// This is the direct path, from <see cref="AddTerm"/>. A caller who names a
        /// term in code got the argument wrong, so this stays an
        /// <see cref="ArgumentException"/>.
        /// </remarks>
        private void Register(VulgarityTerm term)
        {
            VulgarityTerm folded = Fold(term);
            if (folded == null)
            {
                throw new ArgumentException("Term '" + term.Text + "' folds to nothing.");
            }

            Merge(folded, true);
        }

        /// <summary>Stores an already-folded term, keeping the worse of any pair.</summary>
        /// <remarks>
        /// <para>
        /// This never throws, which is what lets <see cref="AddPreset(VulgarityPreset, Func{string, string})"/>
        /// and the seed readers commit a staged list knowing that nothing can fail
        /// halfway.
        /// </para>
        /// <para>
        /// Two sources gave the same term. The rating always takes the worse of the
        /// two, and the category follows the higher severity. What differs is the
        /// boundary rule, and it differs by who is asking:
        /// </para>
        /// <para>
        /// <paramref name="canWiden"/> is true for a term list and for
        /// <see cref="AddTerm"/> — every source the app author chose. There the
        /// wider rule wins (existing AND incoming). Several bundled lists carry a
        /// term the English list also carries, and carry it WITH a boundary where
        /// English has none. Loading a second language must not narrow the first, or
        /// a compound the English list used to catch would go quiet the moment a
        /// language was added.
        /// </para>
        /// <para>
        /// <paramref name="canWiden"/> is false for the entries of a preset — the
        /// one source that can arrive from the network. There the boundary is sticky
        /// (existing OR incoming) and the severity may only rise, so a remote policy
        /// can make a bundled term stricter but never looser. A bundled term that
        /// demands a boundary keeps demanding one, so an entry that restates it
        /// without "w" — {"t":"hell","sev":1} — cannot go on to flag "shell".
        /// </para>
        /// </remarks>
        private void Merge(VulgarityTerm incoming, bool canWiden)
        {
            string folded = incoming.Text;

            VulgarityTerm existing;
            if (!_terms.TryGetValue(folded, out existing))
            {
                _terms[folded] = incoming;
                _termOrder.Add(folded);
                return;
            }

            bool boundary = canWiden
                ? existing.RequireBoundary && incoming.RequireBoundary
                : existing.RequireBoundary || incoming.RequireBoundary;

            if (incoming.Severity > existing.Severity)
            {
                // The category follows the higher severity.
                _terms[folded] = new VulgarityTerm(
                    folded, incoming.CategoryName, incoming.Severity, boundary);
            }
            else if (boundary != existing.RequireBoundary)
            {
                _terms[folded] = new VulgarityTerm(
                    folded, existing.CategoryName, existing.Severity, boundary);
            }
        }

        private static string FoldToString(string text)
        {
            return TextNormalizer.FoldToString(text);
        }
    }
}
