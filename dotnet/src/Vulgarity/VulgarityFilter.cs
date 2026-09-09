using System;
using System.Collections.Generic;
using System.Text;
using Vulgarity.Normalization;
using Vulgarity.Trie;

namespace Vulgarity
{
    /// <summary>
    /// Finds vulgar terms in text, masks them, and scores them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Build one filter and keep it. The constructor compiles the term list into
    /// a trie, which costs far more than a scan. An instance holds no mutable
    /// state, so many threads can scan through one filter at the same time.
    /// </para>
    /// <example>
    /// <code>
    /// var filter = VulgarityFilter.CreateDefault();
    /// bool dirty = filter.Detect(comment);
    /// string clean = filter.Filter(comment);
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class VulgarityFilter
    {
        private static readonly VulgarityMatch[] NoMatches = new VulgarityMatch[0];

        private readonly VulgarityTerm[] _terms;
        private readonly AhoCorasick _termTrie;
        private readonly AhoCorasick _allowTrie;
        private readonly VulgarityOptions _options;

        internal VulgarityFilter(
            VulgarityTerm[] terms,
            AhoCorasick termTrie,
            AhoCorasick allowTrie,
            VulgarityOptions options)
        {
            _terms = terms;
            _termTrie = termTrie;
            _allowTrie = allowTrie;
            _options = options;
        }

        /// <summary>The fold profile this build implements.</summary>
        public static string Profile
        {
            get { return FoldTableData.Profile; }
        }

        /// <summary>The options this filter runs with.</summary>
        public VulgarityOptions Options
        {
            get { return _options.Clone(); }
        }

        /// <summary>How many terms the filter holds.</summary>
        public int TermCount
        {
            get { return _terms.Length; }
        }

        /// <summary>Builds a filter from the bundled English term list.</summary>
        public static VulgarityFilter CreateDefault()
        {
            return CreateDefault(null);
        }

        /// <summary>Builds a filter from the bundled English term list.</summary>
        public static VulgarityFilter CreateDefault(VulgarityOptions options)
        {
            return new VulgarityFilterBuilder().UseDefaultSeed().Build(options);
        }

        /// <summary>Builds a filter from one seed document.</summary>
        public static VulgarityFilter FromSeed(string json)
        {
            return FromSeed(json, null);
        }

        /// <summary>Builds a filter from one seed document.</summary>
        public static VulgarityFilter FromSeed(string json, VulgarityOptions options)
        {
            return new VulgarityFilterBuilder().AddSeed(json).Build(options);
        }

        /// <summary>Builds a filter from a preset document.</summary>
        /// <remarks>
        /// A preset carries the policy as well as the terms, so a server can
        /// change how strict a client is without an app release. Treat a preset
        /// from the network as untrusted: this throws
        /// <see cref="FormatException"/> on a malformed document, and it never
        /// partly applies one.
        /// </remarks>
        /// <example>
        /// <code>
        /// string json = await http.GetStringAsync("https://example.com/policy.json");
        /// var filter = VulgarityFilter.FromPreset(json);
        /// </code>
        /// </example>
        public static VulgarityFilter FromPreset(string json)
        {
            return FromPreset(VulgarityPreset.Parse(json), null);
        }

        /// <summary>Builds a filter from a preset document.</summary>
        public static VulgarityFilter FromPreset(VulgarityPreset preset)
        {
            return FromPreset(preset, null);
        }

        /// <summary>Builds a filter from a preset, resolving its language codes yourself.</summary>
        public static VulgarityFilter FromPreset(VulgarityPreset preset, Func<string, string> languageResolver)
        {
            return new VulgarityFilterBuilder().AddPreset(preset, languageResolver).Build();
        }

        /// <summary>Returns a filter with different options. It reuses the compiled trie.</summary>
        public VulgarityFilter WithOptions(VulgarityOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            options.Validate();
            return new VulgarityFilter(_terms, _termTrie, _allowTrie, options.Clone());
        }

        /// <summary>Reports whether the text holds any term. It stops at the first one.</summary>
        public bool Detect(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return Collect(text, true).Count > 0;
        }

        /// <summary>Finds every term in the text.</summary>
        /// <returns>
        /// The matches, ordered by start. Each one carries offsets into the
        /// ORIGINAL text, so a caller can highlight the real span.
        /// </returns>
        public IReadOnlyList<VulgarityMatch> Scan(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return NoMatches;
            }

            return Collect(text, false);
        }

        /// <summary>Replaces every match with the mask.</summary>
        public string Filter(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            List<VulgarityMatch> matches = Collect(text, false);
            if (matches.Count == 0)
            {
                return text;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            int cursor = 0;
            int i = 0;

            while (i < matches.Count)
            {
                int start = matches[i].Start;
                int end = matches[i].End;

                // Merge the overlapping matches, so no character is masked twice.
                int j = i + 1;
                while (j < matches.Count && matches[j].Start < end)
                {
                    if (matches[j].End > end)
                    {
                        end = matches[j].End;
                    }

                    j++;
                }

                builder.Append(text, cursor, start - cursor);
                if (_options.MaskToken != null)
                {
                    builder.Append(_options.MaskToken);
                }
                else
                {
                    builder.Append(_options.MaskChar, end - start);
                }

                cursor = end;
                i = j;
            }

            builder.Append(text, cursor, text.Length - cursor);
            return builder.ToString();
        }

        /// <summary>Scores the text from the severity of its matches.</summary>
        /// <returns>Zero when the text holds no term.</returns>
        public int Score(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            List<VulgarityMatch> matches = Collect(text, false);
            int score = 0;

            for (int i = 0; i < matches.Count; i++)
            {
                int severity = matches[i].Severity;
                if (_options.ScoreMode == ScoreMode.Max)
                {
                    if (severity > score)
                    {
                        score = severity;
                    }
                }
                else
                {
                    score += severity;
                }
            }

            return score;
        }

        // -------------------------------------------------------------------
        // The scan itself
        // -------------------------------------------------------------------

        private List<VulgarityMatch> Collect(string text, bool stopAtFirst)
        {
            NormalizedText streamA = TextNormalizer.Normalize(text);
            NormalizedText streamB = _options.RepeatTolerance ? TextNormalizer.Squeeze(streamA) : null;

            // The allowlist must be complete before any term match is judged.
            List<int> allowStart = new List<int>();
            List<int> allowEnd = new List<int>();
            CollectAllow(streamA, allowStart, allowEnd);
            if (streamB != null)
            {
                CollectAllow(streamB, allowStart, allowEnd);
            }

            List<VulgarityMatch> matches = new List<VulgarityMatch>();
            CollectTerms(streamA, allowStart, allowEnd, matches, stopAtFirst);
            int fromStreamA = matches.Count;

            if (streamB != null && !(stopAtFirst && matches.Count > 0))
            {
                // The squeeze pass is a fallback, not a second opinion. It widens
                // a span across the letters it collapsed, so "fuck this shit"
                // would report "s shit" for the second term. Stream A already
                // holds the tight span, so drop the loose duplicate.
                List<VulgarityMatch> squeezed = new List<VulgarityMatch>();
                CollectTerms(streamB, allowStart, allowEnd, squeezed, stopAtFirst);

                for (int i = 0; i < squeezed.Count; i++)
                {
                    if (!OverlapsSameTerm(matches, fromStreamA, squeezed[i]))
                    {
                        matches.Add(squeezed[i]);
                    }
                }
            }

            if (matches.Count < 2)
            {
                return matches;
            }

            matches.Sort();
            RemoveDuplicates(matches);
            if (_options.CollapseContained)
            {
                RemoveContained(matches);
            }

            return matches;
        }

        private void CollectAllow(NormalizedText stream, List<int> starts, List<int> ends)
        {
            if (_allowTrie.PatternCount == 0)
            {
                return;
            }

            List<RawHit> hits = new List<RawHit>();
            _allowTrie.Scan(stream, hits);

            for (int i = 0; i < hits.Count; i++)
            {
                int length = _allowTrie.LengthOf(hits[i].PatternId);
                int startIndex = hits[i].End - length + 1;
                if (startIndex < 0)
                {
                    continue;
                }

                starts.Add(stream.SrcStart[startIndex]);
                ends.Add(stream.SrcEnd[hits[i].End]);
            }
        }

        private void CollectTerms(
            NormalizedText stream,
            List<int> allowStart,
            List<int> allowEnd,
            List<VulgarityMatch> matches,
            bool stopAtFirst)
        {
            List<RawHit> hits = new List<RawHit>();
            _termTrie.Scan(stream, hits);

            for (int i = 0; i < hits.Count; i++)
            {
                int termIndex = hits[i].PatternId;
                VulgarityTerm term = _terms[termIndex];

                if (term.Severity < _options.MinSeverity)
                {
                    continue;
                }

                if (_options.Categories != null && !_options.Categories.Contains(term.Category))
                {
                    continue;
                }

                int endIndex = hits[i].End;
                int startIndex = endIndex - _termTrie.LengthOf(termIndex) + 1;
                if (startIndex < 0)
                {
                    continue;
                }

                if (term.RequireBoundary && !HasWordBoundary(stream, startIndex, endIndex))
                {
                    continue;
                }

                int start = stream.SrcStart[startIndex];
                int end = stream.SrcEnd[endIndex];

                if (IsAllowed(allowStart, allowEnd, start, end))
                {
                    continue;
                }

                matches.Add(new VulgarityMatch(start, end, term, termIndex));
                if (stopAtFirst)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Tests whether a match sits on a word boundary.
        /// </summary>
        /// <remarks>
        /// An edge is a boundary when the match reaches the end of the text, when
        /// a separator was dropped there, or when the neighbouring character is
        /// not a real word character. The gap test is what lets "the rapist"
        /// match while "therapist" does not.
        /// </remarks>
        private static bool HasWordBoundary(NormalizedText stream, int startIndex, int endIndex)
        {
            bool leftOk = startIndex == 0
                || stream.Gap[startIndex]
                || !stream.Hard[startIndex - 1];

            if (!leftOk)
            {
                return false;
            }

            int after = endIndex + 1;
            return after >= stream.Length
                || stream.Gap[after]
                || !stream.Hard[after];
        }

        /// <summary>Reports whether stream A already found this term across this span.</summary>
        private static bool OverlapsSameTerm(List<VulgarityMatch> matches, int count, VulgarityMatch candidate)
        {
            for (int i = 0; i < count; i++)
            {
                VulgarityMatch found = matches[i];
                if (found.TermIndex != candidate.TermIndex)
                {
                    continue;
                }

                if (found.Start < candidate.End && candidate.Start < found.End)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowed(List<int> starts, List<int> ends, int start, int end)
        {
            for (int i = 0; i < starts.Count; i++)
            {
                if (starts[i] <= start && end <= ends[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveDuplicates(List<VulgarityMatch> matches)
        {
            int write = 1;
            for (int read = 1; read < matches.Count; read++)
            {
                VulgarityMatch previous = matches[write - 1];
                VulgarityMatch current = matches[read];
                if (current.Start == previous.Start
                    && current.End == previous.End
                    && current.TermIndex == previous.TermIndex)
                {
                    continue;
                }

                matches[write++] = current;
            }

            matches.RemoveRange(write, matches.Count - write);
        }

        /// <summary>Drops a match that sits fully inside an earlier, longer match.</summary>
        /// <remarks>
        /// The list arrives ordered by start, then by the longer match. So every
        /// kept match starts at or before the one under test, and comparing
        /// against the furthest end so far is enough.
        /// </remarks>
        private static void RemoveContained(List<VulgarityMatch> matches)
        {
            int write = 0;
            int furthestEnd = -1;

            for (int read = 0; read < matches.Count; read++)
            {
                if (matches[read].End <= furthestEnd)
                {
                    continue;
                }

                furthestEnd = matches[read].End;
                matches[write++] = matches[read];
            }

            matches.RemoveRange(write, matches.Count - write);
        }
    }
}
