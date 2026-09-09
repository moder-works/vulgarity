using System;
using System.IO;
using System.Text.Json;

namespace Vulgarity.Tests
{
    /// <summary>Finds the shared data directory that both ports read.</summary>
    internal static class TestData
    {
        private static readonly Lazy<string> Root = new Lazy<string>(Locate);

        public static string Directory
        {
            get { return Root.Value; }
        }

        private static string Locate()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "seed.json")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Cannot find the shared data directory.");
        }

        public static string Read(string name)
        {
            return File.ReadAllText(Path.Combine(Directory, name));
        }

        public static byte[] ReadBytes(string name)
        {
            return File.ReadAllBytes(Path.Combine(Directory, name));
        }

        public static JsonDocument ReadJson(string name)
        {
            return JsonDocument.Parse(Read(name));
        }

        /// <summary>The number of terms the English seed holds.</summary>
        /// <remarks>
        /// A filter built from seed.json must report exactly this many. Read it
        /// here rather than writing the number into a test: the seed is
        /// generated, it grows, and a literal only records what it happened to
        /// be on the day.
        /// </remarks>
        public static int SeedEntryCount()
        {
            using (JsonDocument doc = ReadJson("seed.json"))
            {
                return doc.RootElement.GetProperty("entries").GetArrayLength();
            }
        }

        /// <summary>The system word list, or null when this machine has none.</summary>
        public static string[] EnglishWords()
        {
            const string path = "/usr/share/dict/words";
            return File.Exists(path) ? File.ReadAllLines(path) : null;
        }
    }
}
