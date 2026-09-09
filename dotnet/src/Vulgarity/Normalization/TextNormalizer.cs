using System.Collections.Generic;
using System.Text;

namespace Vulgarity.Normalization
{
    /// <summary>Folds text to profile fold-v1 and keeps a map back to the original offsets.</summary>
    internal static class TextNormalizer
    {
        public const int KindDropBreak = 0;
        public const int KindDropSilent = 1;
        public const int KindSoft = 2;
        public const int KindHard = 3;

        /// <summary>The lowest code point that any algorithmic range covers.</summary>
        private static readonly int MinRangeStart = ComputeMinRangeStart();

        private static int ComputeMinRangeStart()
        {
            int min = int.MaxValue;
            int[][] ranges = FoldTableData.Ranges;
            for (int i = 0; i < ranges.Length; i++)
            {
                if (ranges[i][0] < min)
                {
                    min = ranges[i][0];
                }
            }

            return min;
        }

        /// <summary>Classifies one code point and reports what it folds to.</summary>
        /// <param name="cp">The code point to fold.</param>
        /// <param name="single">The single folded code point, or -1 when <paramref name="multi"/> applies.</param>
        /// <param name="multi">The folded string when the fold produces more than one character.</param>
        /// <returns>One of the four Kind constants.</returns>
        public static int Classify(int cp, out int single, out string multi)
        {
            multi = null;

            // The common case first. Neither fold map holds an ASCII letter.
            if (cp >= 'a' && cp <= 'z')
            {
                single = cp;
                return KindHard;
            }

            if (cp >= 'A' && cp <= 'Z')
            {
                single = cp + 32;
                return KindHard;
            }

            string value;
            if (FoldTableData.Soft.TryGetValue(cp, out value))
            {
                return Split(value, KindSoft, out single, out multi);
            }

            if (FoldTableData.Hard.TryGetValue(cp, out value))
            {
                return Split(value, KindHard, out single, out multi);
            }

            // Digits that carry no leetspeak meaning stay as themselves.
            if (cp >= '0' && cp <= '9')
            {
                single = cp;
                return KindHard;
            }

            if (cp >= MinRangeStart)
            {
                int[][] ranges = FoldTableData.Ranges;
                for (int i = 0; i < ranges.Length; i++)
                {
                    int[] r = ranges[i];
                    if (cp >= r[0] && cp <= r[1])
                    {
                        single = r[2] + (cp - r[0]);
                        return KindHard;
                    }
                }
            }

            if (InRanges(FoldTableData.DropBreak, cp))
            {
                single = -1;
                return KindDropBreak;
            }

            if (InRanges(FoldTableData.DropSilent, cp))
            {
                single = -1;
                return KindDropSilent;
            }

            // An unmapped script, for example CJK or Arabic. Keep it. It then
            // blocks a match instead of joining two words together.
            single = cp;
            return KindHard;
        }

        private static int Split(string value, int kind, out int single, out string multi)
        {
            if (value.Length == 1)
            {
                single = value[0];
                multi = null;
            }
            else
            {
                single = -1;
                multi = value;
            }

            return kind;
        }

        private static bool InRanges(int[][] ranges, int cp)
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                int[] r = ranges[i];
                if (cp >= r[0] && cp <= r[1])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Folds a whole string and records where each folded character came from.</summary>
        public static NormalizedText Normalize(string text)
        {
            int capacity = text.Length;
            List<int> chars = new List<int>(capacity);
            List<int> srcStart = new List<int>(capacity);
            List<int> srcEnd = new List<int>(capacity);
            List<bool> hard = new List<bool>(capacity);
            List<bool> gap = new List<bool>(capacity);

            // The start of the text counts as a word break.
            bool pendingGap = true;
            int i = 0;

            while (i < text.Length)
            {
                int width = 1;
                int cp = text[i];
                if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < text.Length)
                {
                    int low = text[i + 1];
                    if (low >= 0xDC00 && low <= 0xDFFF)
                    {
                        cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                        width = 2;
                    }
                }

                int single;
                string multi;
                int kind = Classify(cp, out single, out multi);

                if (kind == KindDropBreak)
                {
                    pendingGap = true;
                }
                else if (kind != KindDropSilent)
                {
                    bool isHard = kind == KindHard;
                    int start = i;
                    int end = i + width;

                    if (multi == null)
                    {
                        chars.Add(single);
                        srcStart.Add(start);
                        srcEnd.Add(end);
                        hard.Add(isHard);
                        gap.Add(pendingGap);
                        pendingGap = false;
                    }
                    else
                    {
                        for (int k = 0; k < multi.Length; k++)
                        {
                            chars.Add(multi[k]);
                            srcStart.Add(start);
                            srcEnd.Add(end);
                            hard.Add(isHard);
                            gap.Add(k == 0 && pendingGap);
                        }

                        pendingGap = false;
                    }
                }

                i += width;
            }

            return new NormalizedText(
                chars.ToArray(),
                srcStart.ToArray(),
                srcEnd.ToArray(),
                hard.ToArray(),
                gap.ToArray());
        }

        /// <summary>
        /// Collapses every run of one character down to a single character.
        /// </summary>
        /// <returns>
        /// The squeezed text, or null when the input holds no run. A null result
        /// means the caller can skip the second scan.
        /// </returns>
        public static NormalizedText Squeeze(NormalizedText source)
        {
            int length = source.Length;
            bool hasRun = false;
            for (int i = 1; i < length; i++)
            {
                if (source.Chars[i] == source.Chars[i - 1])
                {
                    hasRun = true;
                    break;
                }
            }

            if (!hasRun)
            {
                return null;
            }

            List<int> chars = new List<int>(length);
            List<int> srcStart = new List<int>(length);
            List<int> srcEnd = new List<int>(length);
            List<bool> hard = new List<bool>(length);
            List<bool> gap = new List<bool>(length);
            List<int> runLength = new List<int>(length);

            for (int i = 0; i < length; i++)
            {
                int last = chars.Count - 1;
                if (last >= 0 && chars[last] == source.Chars[i])
                {
                    // Extend the run. The kept character now spans the whole run.
                    srcEnd[last] = source.SrcEnd[i];
                    runLength[last]++;
                    continue;
                }

                chars.Add(source.Chars[i]);
                srcStart.Add(source.SrcStart[i]);
                srcEnd.Add(source.SrcEnd[i]);
                hard.Add(source.Hard[i]);
                gap.Add(source.Gap[i]);
                runLength.Add(1);
            }

            return new NormalizedText(
                chars.ToArray(),
                srcStart.ToArray(),
                srcEnd.ToArray(),
                hard.ToArray(),
                gap.ToArray(),
                runLength.ToArray());
        }

        /// <summary>Folds a term down to the plain character sequence the trie stores.</summary>
        /// <remarks>
        /// Callers use this on a term they add at run time, so caller input and
        /// seed data reach the trie in the same form.
        /// </remarks>
        public static int[] FoldTerm(string term)
        {
            NormalizedText normalized = Normalize(term);
            return normalized.Chars;
        }

        /// <summary>Folds a term and collapses every run down to a single character.</summary>
        /// <remarks>
        /// This is the form a term takes in the squeezed trie, so that a squeezed
        /// stream is scanned with squeezed patterns. Without it no term holding a
        /// doubled letter could ever match in the repeat-tolerant pass.
        /// </remarks>
        public static int[] SqueezeTerm(string term)
        {
            int[] folded = FoldTerm(term);
            List<int> result = new List<int>(folded.Length);
            for (int i = 0; i < folded.Length; i++)
            {
                if (result.Count == 0 || result[result.Count - 1] != folded[i])
                {
                    result.Add(folded[i]);
                }
            }

            return result.ToArray();
        }

        /// <summary>The length of each run in a folded term, aligned with <see cref="SqueezeTerm"/>.</summary>
        /// <remarks>
        /// The squeezed pass compares these against the runs the squeeze collapsed.
        /// Text may repeat a letter more often than the term does, never less, so
        /// one squeezed spelling cannot stand in for a different real word.
        /// </remarks>
        public static int[] TermRuns(string term)
        {
            int[] folded = FoldTerm(term);
            List<int> runs = new List<int>(folded.Length);
            for (int i = 0; i < folded.Length; i++)
            {
                if (i > 0 && folded[i] == folded[i - 1])
                {
                    runs[runs.Count - 1]++;
                }
                else
                {
                    runs.Add(1);
                }
            }

            return runs.ToArray();
        }

        /// <summary>Folds a term and returns it as a string.</summary>
        public static string FoldToString(string term)
        {
            int[] folded = FoldTerm(term);
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
