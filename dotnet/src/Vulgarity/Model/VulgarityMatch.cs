using System;

namespace Vulgarity
{
    /// <summary>One term found in the text.</summary>
    /// <remarks>
    /// <see cref="Start"/> and <see cref="End"/> index the ORIGINAL text the
    /// caller passed in, not the folded form. A caller can highlight the real
    /// span with them.
    /// </remarks>
    public sealed class VulgarityMatch : IComparable<VulgarityMatch>
    {
        internal VulgarityMatch(int start, int end, VulgarityTerm term, int termIndex)
        {
            Start = start;
            End = end;
            Term = term;
            TermIndex = termIndex;
        }

        /// <summary>Where the match starts in the original text.</summary>
        public int Start { get; private set; }

        /// <summary>Where the match ends in the original text. Exclusive.</summary>
        public int End { get; private set; }

        /// <summary>How many characters of the original text the match covers.</summary>
        public int Length
        {
            get { return End - Start; }
        }

        /// <summary>The term that matched.</summary>
        public VulgarityTerm Term { get; private set; }

        /// <summary>The position of the term in the filter's term list.</summary>
        internal int TermIndex { get; private set; }

        /// <summary>The matched term, folded to profile fold-v1.</summary>
        public string Text
        {
            get { return Term.Text; }
        }

        public VulgarityCategory Category
        {
            get { return Term.Category; }
        }

        public string CategoryName
        {
            get { return Term.CategoryName; }
        }

        public int Severity
        {
            get { return Term.Severity; }
        }

        /// <summary>Returns the exact text the match covers.</summary>
        public string Excerpt(string original)
        {
            return original.Substring(Start, End - Start);
        }

        /// <summary>Orders matches by start, then by the longer match, then by term.</summary>
        public int CompareTo(VulgarityMatch other)
        {
            if (other == null)
            {
                return 1;
            }

            if (Start != other.Start)
            {
                return Start < other.Start ? -1 : 1;
            }

            if (End != other.End)
            {
                return End > other.End ? -1 : 1;
            }

            return TermIndex.CompareTo(other.TermIndex);
        }

        public override string ToString()
        {
            return Term.Text + "@" + Start + ".." + End;
        }
    }
}
