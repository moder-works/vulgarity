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

        /// <summary>How many characters of the source stream each one stands for.</summary>
        /// <remarks>
        /// Only a squeezed stream carries this; a plain fold leaves it null, where
        /// every character stands for exactly itself. A plain flag would say only
        /// that a run collapsed here, and the matcher needs the size of the run: a
        /// term spelled with a doubled letter may only match text that doubled that
        /// same letter, so "heel" must not stand in for "hell".
        /// </remarks>
        public readonly int[] RunLength;

        public NormalizedText(int[] chars, int[] srcStart, int[] srcEnd, bool[] hard, bool[] gap)
            : this(chars, srcStart, srcEnd, hard, gap, null)
        {
        }

        public NormalizedText(
            int[] chars,
            int[] srcStart,
            int[] srcEnd,
            bool[] hard,
            bool[] gap,
            int[] runLength)
        {
            Chars = chars;
            SrcStart = srcStart;
            SrcEnd = srcEnd;
            Hard = hard;
            Gap = gap;
            RunLength = runLength;
        }

        public int Length
        {
            get { return Chars.Length; }
        }

        /// <summary>How long the run at <paramref name="i"/> was before the squeeze. One on a plain fold.</summary>
        public int RunLengthAt(int i)
        {
            return RunLength == null ? 1 : RunLength[i];
        }

        /// <summary>True when the character at <paramref name="i"/> stands for a run that was collapsed.</summary>
        public bool CollapsedAt(int i)
        {
            return RunLengthAt(i) > 1;
        }
    }
}
