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
        public static bool Has(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return false;
            }

            if (code == Default)
            {
                return true;
            }

            for (int i = 0; i < Optional.Length; i++)
            {
                if (Optional[i] == code)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Reads one bundled seed document.</summary>
        /// <param name="code">A language code, for example "en" or "es".</param>
        public static string ReadSeed(string code)
        {
            if (!Has(code))
            {
                throw new ArgumentException(
                    "This build carries no pack for '" + code + "'. Available: " +
                    string.Join(", ", new List<string>(Available).ToArray()), "code");
            }

            string resource = code == Default ? "Vulgarity.seed.json" : "Vulgarity.seed." + code + ".json";
            Assembly assembly = typeof(VulgarityLanguages).GetTypeInfo().Assembly;

            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Missing embedded resource: " + resource);
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
