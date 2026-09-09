using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Vulgarity
{
    /// <summary>Reads a seed document into terms and allowlist words.</summary>
    internal static class SeedLoader
    {
        public const int SupportedSchema = 1;

        public static void Load(
            string json,
            string expectedProfile,
            List<VulgarityTerm> terms,
            List<string> allow)
        {
            if (json == null)
            {
                throw new ArgumentNullException("json");
            }

            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("A seed document must be a JSON object.");
                }

                JsonElement value;

                if (root.TryGetProperty("schema", out value))
                {
                    int schema = value.GetInt32();
                    if (schema != SupportedSchema)
                    {
                        throw new FormatException(
                            "This build reads seed schema " + SupportedSchema +
                            ". The file states schema " + schema + ".");
                    }
                }

                // The profile pins the fold table the seed was built with. A stale
                // file then fails here instead of matching silently wrong.
                if (root.TryGetProperty("profile", out value))
                {
                    string profile = value.GetString();
                    if (profile != expectedProfile)
                    {
                        throw new FormatException(
                            "This build implements fold profile '" + expectedProfile +
                            "'. The seed file states '" + profile + "'.");
                    }
                }

                if (!root.TryGetProperty("entries", out value) || value.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("A seed document must hold an 'entries' array.");
                }

                foreach (JsonElement entry in value.EnumerateArray())
                {
                    terms.Add(ReadEntry(entry));
                }

                if (root.TryGetProperty("allow", out value) && value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement word in value.EnumerateArray())
                    {
                        string text = word.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            allow.Add(text);
                        }
                    }
                }
            }
        }

        private static VulgarityTerm ReadEntry(JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Every seed entry must be a JSON object.");
            }

            JsonElement value;

            if (!entry.TryGetProperty("t", out value))
            {
                throw new FormatException("Every seed entry must hold a 't' term.");
            }

            string term = value.GetString();
            if (string.IsNullOrEmpty(term))
            {
                throw new FormatException("A seed term must not be empty.");
            }

            string category = entry.TryGetProperty("cat", out value) ? value.GetString() : "other";
            int severity = entry.TryGetProperty("sev", out value) ? value.GetInt32() : 1;
            bool requireBoundary = entry.TryGetProperty("w", out value) && value.ValueKind == JsonValueKind.True;

            if (severity < 1 || severity > 5)
            {
                throw new FormatException("Severity must be 1 to 5. Term '" + term + "' states " + severity + ".");
            }

            return new VulgarityTerm(term, category ?? "other", severity, requireBoundary);
        }
    }
}
