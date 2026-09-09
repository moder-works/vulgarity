using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Vulgarity
{
    /// <summary>Reads a seed document into terms and allowlist words.</summary>
    /// <remarks>
    /// Throws <see cref="FormatException"/> on any malformed field. Nothing is
    /// coerced: a <c>sev</c> that is not a whole number, a <c>cat</c> that is not a
    /// string and a <c>w</c> that is not a boolean are all errors, not defaults.
    /// </remarks>
    internal static class SeedLoader
    {
        public const int SupportedSchema = 1;

        /// <summary>The severity a seed entry takes when it states none.</summary>
        /// <remarks>
        /// A seed list is bulk-authored and mostly mild, so an entry that says
        /// nothing is treated as mild. A preset entry defaults to 3 instead,
        /// because a preset is written one term at a time and its terms are the
        /// ones somebody cared enough to add. Both ports use these two numbers.
        /// </remarks>
        public const int DefaultSeverity = 1;

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

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException error)
            {
                throw new FormatException("The seed document is not valid JSON. " + error.Message, error);
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("A seed document must be a JSON object.");
                }

                int? schema = JsonRead.ReadOptionalInt(root, "schema");
                if (schema != null && schema.Value != SupportedSchema)
                {
                    throw new FormatException(
                        "This build reads seed schema " + SupportedSchema +
                        ". The file states schema " + schema.Value + ".");
                }

                // The profile pins the fold table the seed was built with. A stale
                // file then fails here instead of matching silently wrong.
                string profile = JsonRead.ReadOptionalString(root, "profile");
                if (profile != null && profile != expectedProfile)
                {
                    throw new FormatException(
                        "This build implements fold profile '" + expectedProfile +
                        "'. The seed file states '" + profile + "'.");
                }

                JsonElement value;
                if (!root.TryGetProperty("entries", out value) || value.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("A seed document must hold an 'entries' array.");
                }

                foreach (JsonElement entry in value.EnumerateArray())
                {
                    terms.Add(ReadEntry(entry));
                }

                allow.AddRange(JsonRead.ReadStrings(root, "allow"));
            }
        }

        private static VulgarityTerm ReadEntry(JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Every seed entry must be a JSON object.");
            }

            JsonElement value;
            if (!entry.TryGetProperty("t", out value) || value.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("Every seed entry must hold a non-empty 't' term.");
            }

            string term = value.GetString();
            if (string.IsNullOrEmpty(term))
            {
                throw new FormatException("Every seed entry must hold a non-empty 't' term.");
            }

            string category = JsonRead.ReadOptionalString(entry, "cat") ?? "other";

            int severity = JsonRead.ReadOptionalInt(entry, "sev") ?? DefaultSeverity;
            if (severity < 1 || severity > 5)
            {
                throw new FormatException(
                    "Severity must be 1 to 5. Term '" + term + "' states " + severity + ".");
            }

            return new VulgarityTerm(term, category, severity, JsonRead.ReadOptionalBool(entry, "w") ?? false);
        }
    }
}
