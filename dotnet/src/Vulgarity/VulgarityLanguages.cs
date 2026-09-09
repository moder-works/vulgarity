using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Vulgarity
{
    /// <summary>The optional language packs this build carries.</summary>
    /// <remarks>
    /// English is hand-curated and always loads. Every other pack comes from a
    /// community list that nobody vetted term by term, and it carries a default
    /// category and severity. Load a pack only when you handle text in that
    /// language. Expect false positives.
    /// </remarks>
    public static class VulgarityLanguages
    {
        /// <summary>The language of the built-in term list.</summary>
        public const string Default = "en";

        private static readonly string[] Optional =
        {
            "ar", "de", "es", "fa", "fr", "hi", "it", "ko",
            "nl", "pl", "ru", "th", "vi", "zh",
        };

        /// <summary>Every language code this build carries, English included.</summary>
        public static IReadOnlyList<string> Available
        {
            get
            {
                List<string> all = new List<string>();
                all.Add(Default);
                all.AddRange(Optional);
                all.Sort(StringComparer.Ordinal);
                return all;
            }
        }

        /// <summary>Reports whether this build carries a pack for the code.</summary>
        /// <remarks>
        /// The code is matched without regard to case, so "EN", "En" and "en" all
        /// resolve. A language tag arrives upper-cased often enough — from a
        /// culture, a header, a database column — that refusing one would be a trap.
        /// </remarks>
        public static bool Has(string code)
        {
            return Normalize(code) != null;
        }

        /// <summary>The lower-case code this build carries, or null when it carries none.</summary>
        private static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            string key = code.ToLowerInvariant();
            if (key == Default)
            {
                return key;
            }

            for (int i = 0; i < Optional.Length; i++)
            {
                if (Optional[i] == key)
                {
                    return key;
                }
            }

            return null;
        }

        /// <summary>Reads one bundled term list.</summary>
        /// <param name="code">A language code, for example "en" or "es".</param>
        /// <returns>
        /// The list as base64 pack text. Pass it straight to
        /// <see cref="VulgarityFilterBuilder.AddSeed(string)"/> or to a preset's
        /// language resolver, which both read the format from the text itself.
        /// </returns>
        /// <remarks>
        /// This used to return the seed JSON. It now returns a pack, so the
        /// assembly carries no readable term. The type is unchanged, so every
        /// caller that only forwards the value keeps working.
        /// </remarks>
        public static string ReadSeed(string code)
        {
            return Convert.ToBase64String(ReadSeedPack(code));
        }

        /// <summary>Reads one bundled term list as raw pack bytes.</summary>
        /// <remarks>
        /// The builder uses this, so the default path does no base64 work.
        /// </remarks>
        internal static byte[] ReadSeedPack(string code)
        {
            string key = Normalize(code);
            if (key == null)
            {
                throw new ArgumentException(
                    "This build carries no pack for '" + code + "'. Available: " +
                    string.Join(", ", new List<string>(Available).ToArray()), "code");
            }

            string resource = "Vulgarity.seed-" + key + ".vpk";
            Assembly assembly = typeof(VulgarityLanguages).GetTypeInfo().Assembly;

            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Missing embedded resource: " + resource);
                }

                using (MemoryStream buffer = new MemoryStream())
                {
                    byte[] chunk = new byte[8192];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                    }

                    return buffer.ToArray();
                }
            }
        }
    }
}
