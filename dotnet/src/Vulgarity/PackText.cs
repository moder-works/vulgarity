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

        /// <summary>Drops blank characters and folds the URL-safe alphabet to the standard one.</summary>
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

            return built.ToString();
        }
    }
}
