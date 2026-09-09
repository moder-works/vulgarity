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

        /// <summary>Every term again, with its runs collapsed. The repeat pass scans with it.</summary>
        private readonly AhoCorasick _squeezedTrie;

        /// <summary>Maps a pattern in <see cref="_squeezedTrie"/> to every term that squeezes onto it.</summary>
        private readonly int[][] _squeezedTerms;

        /// <summary>The run lengths of each term's folded spelling, one list per term.</summary>
        private readonly int[][] _termRuns;

        internal VulgarityFilter(
            VulgarityTerm[] terms,
            AhoCorasick termTrie,
            AhoCorasick allowTrie,
            VulgarityOptions options,
            AhoCorasick squeezedTrie,
            int[][] squeezedTerms,
            int[][] termRuns)
        {
            _terms = terms;
            _termTrie = termTrie;
            _allowTrie = allowTrie;
            _options = options;
            _squeezedTrie = squeezedTrie;
            _squeezedTerms = squeezedTerms;
            _termRuns = termRuns;
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
            return new VulgarityFilter(
                _terms,
                _termTrie,
                _allowTrie,
                options.Clone(),
                _squeezedTrie,
                _squeezedTerms,
                _termRuns);
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

            SpanIndex allow = SpanIndex.Of(allowStart, allowEnd);

            List<VulgarityMatch> matches = new List<VulgarityMatch>();
            CollectTerms(streamA, _termTrie, null, allow, matches, stopAtFirst);

            if (streamB != null && !(stopAtFirst && matches.Count > 0))
            {
                // The squeeze pass is a fallback, not a second opinion. It widens
                // a span across the letters it collapsed, so "damn the music
                // crap" would report "c crap" for the second term. Stream A already
                // holds the tight span, so drop every loose candidate that lands
                // on one.
                List<VulgarityMatch> squeezed = new List<VulgarityMatch>();
                CollectTerms(streamB, _squeezedTrie, _squeezedTerms, allow, squeezed, stopAtFirst);

                if (squeezed.Count > 0)
                {
                    SpanIndex fromStreamA = SpanIndex.OfMatches(matches);
                    for (int i = 0; i < squeezed.Count; i++)
                    {
                        if (!fromStreamA.Overlaps(squeezed[i].Start, squeezed[i].End))
                        {
                            matches.Add(squeezed[i]);
                        }
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

        /// <summary>Turns raw hits into matches.</summary>
        /// <remarks>
        /// <paramref name="trie"/> carries the patterns, and
        /// <paramref name="patternTerms"/> maps a pattern back to every term that
        /// produced it. Pass null for the plain pass, where a pattern id is already
        /// a term index. A non-null map marks the squeezed pass, where a term that
        /// actually lost a letter has to land on runs at least as long as its own.
        /// </remarks>
        private void CollectTerms(
            NormalizedText stream,
            AhoCorasick trie,
            int[][] patternTerms,
            SpanIndex allow,
            List<VulgarityMatch> matches,
            bool stopAtFirst)
        {
            List<RawHit> hits = new List<RawHit>();
            trie.Scan(stream, hits);
            ISet<VulgarityCategory> categories = _options.Categories;

            for (int i = 0; i < hits.Count; i++)
            {
                int patternId = hits[i].PatternId;
                int patternLength = trie.LengthOf(patternId);
                int endIndex = hits[i].End;
                int startIndex = endIndex - patternLength + 1;
                if (startIndex < 0)
                {
                    continue;
                }

                // This holds for every term the pattern stands for, so pay for it once.
                bool interiorGap = HasInteriorGap(stream, startIndex, endIndex);

                int[] mapped = patternTerms == null ? null : patternTerms[patternId];
                int termCount = mapped == null ? 1 : mapped.Length;

                for (int n = 0; n < termCount; n++)
                {
                    int termIndex = mapped == null ? patternId : mapped[n];
                    VulgarityTerm term = _terms[termIndex];

                    if (term.Severity < _options.MinSeverity)
                    {
                        continue;
                    }

                    if (categories != null && !categories.Contains(term.Category))
                    {
                        continue;
                    }

                    // A term that squeezes onto a shorter spelling only matches
                    // where the stream really did collapse the same runs. Text may
                    // repeat a letter more often than the term does, never less, so
                    // a term spelled with a doubled letter cannot stand in for a
                    // different real word that shares its squeezed spelling.
                    if (mapped != null
                        && patternLength != _termTrie.LengthOf(termIndex)
                        && !RunsCover(stream, startIndex, _termRuns[termIndex]))
                    {
                        continue;
                    }

                    if (term.RequireBoundary)
                    {
                        if (!HasLeftBoundary(stream, startIndex) || !HasRightBoundary(stream, endIndex))
                        {
                            continue;
                        }
                    }
                    else if (interiorGap && !HasLeftBoundary(stream, startIndex))
                    {
                        // The match swallowed a separator, so it spans two words.
                        // Only a term that starts its own word may do that.
                        // Otherwise the tail of one ordinary word joined to the head
                        // of the next spells a term, and a harmless sentence gets
                        // flagged. The right edge stays free, so a term written with
                        // a hyphen or a space between every letter still matches.
                        continue;
                    }

                    int start = stream.SrcStart[startIndex];
                    int end = stream.SrcEnd[endIndex];

                    if (allow.Covers(start, end))
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
        }

        /// <summary>True when a separator was dropped inside the match, not just before it.</summary>
        private static bool HasInteriorGap(NormalizedText stream, int startIndex, int endIndex)
        {
            for (int k = startIndex + 1; k <= endIndex; k++)
            {
                if (stream.Gap[k])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when every run the match landed on is as long as the term's own.</summary>
        private static bool RunsCover(NormalizedText stream, int startIndex, int[] runs)
        {
            for (int k = 0; k < runs.Length; k++)
            {
                if (stream.RunLengthAt(startIndex + k) < runs[k])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tests the left edge of a match for a word boundary.
        /// </summary>
        /// <remarks>
        /// An edge is a boundary when the match reaches the edge of the text, when
        /// a separator was dropped there, or when the neighbouring character is
        /// not a real word character. The gap test is what lets "a hell"
        /// match while "shell" does not.
        /// </remarks>
        private static bool HasLeftBoundary(NormalizedText stream, int startIndex)
        {
            return startIndex == 0
                || stream.Gap[startIndex]
                || !stream.Hard[startIndex - 1];
        }

        /// <summary>Tests the right edge of a match for a word boundary.</summary>
        private static bool HasRightBoundary(NormalizedText stream, int endIndex)
        {
            int after = endIndex + 1;
            return after >= stream.Length
                || stream.Gap[after]
                || !stream.Hard[after];
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

        /// <summary>A set of spans that answers containment and overlap in O(log n).</summary>
        /// <remarks>
        /// A scan of a large text finds a great many spans, and every match is
        /// judged against all of them. Walking the list makes that quadratic: a
        /// megabyte of an allowlisted word took seconds. The spans are sorted by
        /// start, and each entry carries the furthest end seen up to it, so one
        /// binary search settles a question that used to need a full sweep.
        /// </remarks>
        private sealed class SpanIndex
        {
            private static readonly SpanIndex EmptyIndex = new SpanIndex(new int[0], new int[0]);

            private readonly int[] _start;
            private readonly int[] _maxEnd;

            private SpanIndex(int[] start, int[] maxEnd)
            {
                _start = start;
                _maxEnd = maxEnd;
            }

            /// <summary>Builds an index over parallel start and end lists.</summary>
            public static SpanIndex Of(List<int> starts, List<int> ends)
            {
                int count = starts.Count;
                if (count == 0)
                {
                    return EmptyIndex;
                }

                int[] order = new int[count];
                for (int i = 0; i < count; i++)
                {
                    order[i] = i;
                }

                List<int> byStart = starts;
                List<int> byEnd = ends;
                Array.Sort(order, delegate(int a, int b)
                {
                    int difference = byStart[a] - byStart[b];
                    return difference != 0 ? difference : byEnd[a] - byEnd[b];
                });

                int[] start = new int[count];
                int[] end = new int[count];
                for (int i = 0; i < count; i++)
                {
                    start[i] = starts[order[i]];
                    end[i] = ends[order[i]];
                }

                return Build(start, end);
            }

            /// <summary>Builds an index over the span of every match.</summary>
            public static SpanIndex OfMatches(List<VulgarityMatch> matches)
            {
                int count = matches.Count;
                if (count == 0)
                {
                    return EmptyIndex;
                }

                VulgarityMatch[] sorted = matches.ToArray();
                Array.Sort(sorted, delegate(VulgarityMatch a, VulgarityMatch b)
                {
                    int difference = a.Start - b.Start;
                    return difference != 0 ? difference : a.End - b.End;
                });

                int[] start = new int[count];
                int[] end = new int[count];
                for (int i = 0; i < count; i++)
                {
                    start[i] = sorted[i].Start;
                    end[i] = sorted[i].End;
                }

                return Build(start, end);
            }

            private static SpanIndex Build(int[] start, int[] end)
            {
                int[] maxEnd = new int[start.Length];
                int running = end[0];

                for (int i = 0; i < start.Length; i++)
                {
                    if (end[i] > running)
                    {
                        running = end[i];
                    }

                    maxEnd[i] = running;
                }

                return new SpanIndex(start, maxEnd);
            }

            /// <summary>True when one span holds the whole of [start, end).</summary>
            public bool Covers(int start, int end)
            {
                int i = LastStartAtOrBefore(start);
                return i >= 0 && _maxEnd[i] >= end;
            }

            /// <summary>True when one span shares a character with [start, end).</summary>
            public bool Overlaps(int start, int end)
            {
                int i = LastStartBefore(end);
                return i >= 0 && _maxEnd[i] > start;
            }

            /// <summary>The last entry whose start is at or before the value, or -1.</summary>
            private int LastStartAtOrBefore(int value)
            {
                int low = 0;
                int high = _start.Length - 1;
                int found = -1;

                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);
                    if (_start[mid] <= value)
                    {
                        found = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                return found;
            }

            /// <summary>The last entry whose start is strictly before the value, or -1.</summary>
            private int LastStartBefore(int value)
            {
                int low = 0;
                int high = _start.Length - 1;
                int found = -1;

                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);
                    if (_start[mid] < value)
                    {
                        found = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                return found;
            }
        }
    }
}
