using System;
using System.Text;

namespace Vulgarity
{
    /// <summary>Reads base64 pack text the same way both ports read it.</summary>
    /// <remarks>
    /// The two runtimes disagree, and they disagree in opposite directions:
    ///
    ///   input                     Dart base64.decode   Convert.FromBase64String
    ///   line-wrapped at 76        throws               accepts
    ///   spaces or tabs            throws               accepts
    ///   URL-safe '-' and '_'      accepts              throws
    ///
    /// So `base64 &lt; pack.vpk`, which wraps at 76 columns, would load on .NET and
    /// fail on Dart. This normalises first, so a caller gets one answer on both.
    /// dart/lib/src/pack_text.dart is the mirror of this file.
    /// </remarks>
    internal static class PackText
    {
        /// <summary>Decodes base64 pack text. Returns null when the text is not a pack.</summary>
        public static byte[] TryRead(string text)
        {
            if (text == null)
            {
                return null;
            }

            string cleaned = Normalize(text);
            if (cleaned.Length == 0)
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(cleaned);
            }
            catch (FormatException)
            {
                return null;
            }

            return PackReader.Looks(bytes) ? bytes : null;
        }

        /// <summary>Puts base64 pack text into the one shape both decoders accept.</summary>
        /// <remarks>
        /// <para>
        /// It drops blank characters, drops a leading byte-order mark, folds the
        /// URL-safe alphabet to the standard one, and puts back any '=' padding the
        /// sender left off. base64url is normally served unpadded and both decoders
        /// demand a multiple of four, so without the last step a URL-safe pack loads
        /// only when its length happens to divide.
        /// </para>
        /// <para>
        /// A byte-order mark anywhere but the front is left alone: that is
        /// corruption, not an encoding choice, and the decode must fail on it.
        /// </para>
        /// </remarks>
        public static string Normalize(string text)
        {
            StringBuilder built = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\v' || c == '\f')
                {
                    continue;
                }

                if (c == '\uFEFF' && built.Length == 0)
                {
                    continue;
                }

                if (c == '-')
                {
                    built.Append('+');
                }
                else if (c == '_')
                {
                    built.Append('/');
                }
                else
                {
                    built.Append(c);
                }
            }

            string cleaned = built.ToString();
            switch (cleaned.Length % 4)
            {
                case 2:
                    return cleaned + "==";
                case 3:
                    return cleaned + "=";
                default:
                    // A remainder of 1 is no base64 at all. Hand it on and let the
                    // decoder say so, rather than padding it into something that
                    // looks valid.
                    return cleaned;
            }
        }
    }
}
