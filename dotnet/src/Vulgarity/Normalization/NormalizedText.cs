namespace Vulgarity.Normalization
{
    /// <summary>
    /// A folded view of some input text, with a map back to the original offsets.
    /// </summary>
    /// <remarks>
    /// Every array has the same length. Index <c>i</c> describes one folded
    /// character.
    /// </remarks>
    internal sealed class NormalizedText
    {
        /// <summary>The folded code points.</summary>
        public readonly int[] Chars;

        /// <summary>Where <see cref="Chars"/>[i] starts in the original string.</summary>
        public readonly int[] SrcStart;

        /// <summary>Where <see cref="Chars"/>[i] ends in the original string. Exclusive.</summary>
        public readonly int[] SrcEnd;

        /// <summary>True when the source character was a real word character.</summary>
        /// <remarks>
        /// A leetspeak stand-in such as <c>@</c> or <c>!</c> folds to a letter but
        /// stays false here. The matcher then treats it as a word boundary.
        /// </remarks>
        public readonly bool[] Hard;

        /// <summary>True when a word-breaking separator was dropped just before this character.</summary>
        public readonly bool[] Gap;

        public NormalizedText(int[] chars, int[] srcStart, int[] srcEnd, bool[] hard, bool[] gap)
        {
            Chars = chars;
            SrcStart = srcStart;
            SrcEnd = srcEnd;
            Hard = hard;
            Gap = gap;
        }

        public int Length
        {
            get { return Chars.Length; }
        }
    }
}
