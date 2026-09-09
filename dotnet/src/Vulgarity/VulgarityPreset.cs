using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Vulgarity.Normalization;

namespace Vulgarity
{
    /// <summary>
    /// A whole filter policy in one JSON document: the options, the extra
    /// terms, the allowlist, and the terms to drop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A seed document carries terms alone. A preset carries the policy too, so
    /// a server can change how strict a client is without an app release.
    /// </para>
    /// <para>
    /// Treat a preset from the network as untrusted. <see cref="Parse"/>
    /// validates every field and throws <see cref="FormatException"/> with the
    /// offending field named. It never partly applies a bad document.
    /// </para>
    /// <example>
    /// <code>
    /// string json = await http.GetStringAsync("https://example.com/policy.json");
    /// var filter = VulgarityFilter.FromPreset(json);
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class VulgarityPreset
    {
        /// <summary>The preset schema this build reads.</summary>
        public const int SupportedSchema = 1;

        internal VulgarityPreset(
            string name,
            string description,
            IReadOnlyList<string> languages,
            VulgarityOptions options,
            IReadOnlyList<VulgarityTerm> entries,
            IReadOnlyList<string> allow,
            IReadOnlyList<string> remove)
        {
            Name = name;
            Description = description;
            Languages = languages;
            Options = options;
            Entries = entries;
            Allow = allow;
            Remove = remove;
        }

        /// <summary>A short name for the policy. It can be null.</summary>
        public string Name { get; private set; }

        /// <summary>What the policy is for. It can be null.</summary>
        public string Description { get; private set; }

        /// <summary>The bundled term lists to load, by language code.</summary>
        public IReadOnlyList<string> Languages { get; private set; }

        /// <summary>The policy. It is never null; a missing field keeps its default.</summary>
        public VulgarityOptions Options { get; private set; }

        /// <summary>Extra terms the preset adds.</summary>
        public IReadOnlyList<VulgarityTerm> Entries { get; private set; }

        /// <summary>Innocent words the filter must never flag.</summary>
        public IReadOnlyList<string> Allow { get; private set; }

        /// <summary>Terms to drop after the language packs load.</summary>
        public IReadOnlyList<string> Remove { get; private set; }

        /// <summary>Builds a preset that loads the bundled English list and nothing else.</summary>
        public static VulgarityPreset Default()
        {
            return new VulgarityPreset(
                "default",
                "The bundled English term list with default options.",
                new[] { VulgarityLanguages.Default },
                new VulgarityOptions(),
                new VulgarityTerm[0],
                new string[0],
                new string[0]);
        }

        /// <summary>Reads a preset document.</summary>
        /// <exception cref="FormatException">The document is malformed, or a field is out of range.</exception>
        public static VulgarityPreset Parse(string json)
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
                throw new FormatException("The preset is not valid JSON. " + error.Message, error);
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("A preset must be a JSON object.");
                }

                JsonElement value;

                if (root.TryGetProperty("schema", out value) && value.ValueKind != JsonValueKind.Null)
                {
                    int schema = value.GetInt32();
                    if (schema != SupportedSchema)
                    {
                        throw new FormatException(
                            "This build reads preset schema " + SupportedSchema +
                            ". The document states schema " + schema + ".");
                    }
                }

                // The profile pins the fold table. A preset built against an
                // older table would match differently, so it fails here.
                if (root.TryGetProperty("profile", out value) && value.ValueKind != JsonValueKind.Null)
                {
                    string profile = value.GetString();
                    if (profile != FoldTableData.Profile)
                    {
                        throw new FormatException(
                            "This build implements fold profile '" + FoldTableData.Profile +
                            "'. The preset states '" + profile + "'.");
                    }
                }

                string name = root.TryGetProperty("name", out value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
                string description =
                    root.TryGetProperty("description", out value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : null;

                List<string> languages = ReadStrings(root, "languages");
                foreach (string code in languages)
                {
                    if (!VulgarityLanguages.Has(code))
                    {
                        throw new FormatException(
                            "'languages' names '" + code + "', which this build does not carry. Available: "
                            + string.Join(", ", new List<string>(VulgarityLanguages.Available).ToArray()) + ".");
                    }
                }

                VulgarityOptions options =
                    root.TryGetProperty("options", out value) && value.ValueKind != JsonValueKind.Null
                        ? VulgarityOptions.FromElement(value)
                        : new VulgarityOptions();

                List<VulgarityTerm> entries = new List<VulgarityTerm>();
                if (root.TryGetProperty("entries", out value) && value.ValueKind != JsonValueKind.Null)
                {
                    if (value.ValueKind != JsonValueKind.Array)
                    {
                        throw new FormatException("'entries' must be an array.");
                    }

                    foreach (JsonElement entry in value.EnumerateArray())
                    {
                        entries.Add(ReadEntry(entry));
                    }
                }

                return new VulgarityPreset(
                    name,
                    description,
                    languages,
                    options,
                    entries,
                    ReadStrings(root, "allow"),
                    ReadStrings(root, "remove"));
            }
        }

        /// <summary>Writes this preset as a JSON document.</summary>
        public string ToJson()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(
                    stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("schema", SupportedSchema);
                    writer.WriteString("profile", FoldTableData.Profile);

                    if (Name != null)
                    {
                        writer.WriteString("name", Name);
                    }

                    if (Description != null)
                    {
                        writer.WriteString("description", Description);
                    }

                    writer.WriteStartArray("languages");
                    foreach (string code in Languages)
                    {
                        writer.WriteStringValue(code);
                    }

                    writer.WriteEndArray();

                    writer.WriteStartObject("options");
                    Options.WriteTo(writer);
                    writer.WriteEndObject();

                    writer.WriteStartArray("entries");
                    foreach (VulgarityTerm term in Entries)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("t", term.Text);
                        writer.WriteString("cat", term.CategoryName);
                        writer.WriteNumber("sev", term.Severity);
                        if (term.RequireBoundary)
                        {
                            writer.WriteBoolean("w", true);
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();

                    WriteStrings(writer, "allow", Allow);
                    WriteStrings(writer, "remove", Remove);
                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
        {
            writer.WriteStartArray(name);
            foreach (string value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        private static List<string> ReadStrings(JsonElement root, string name)
        {
            List<string> result = new List<string>();
            JsonElement value;
            if (!root.TryGetProperty(name, out value) || value.ValueKind == JsonValueKind.Null)
            {
                return result;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("'" + name + "' must be an array of strings.");
            }

            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("'" + name + "' must hold strings only.");
                }

                string text = item.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    result.Add(text);
                }
            }

            return result;
        }

        private static VulgarityTerm ReadEntry(JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Every entry in 'entries' must be a JSON object.");
            }

            JsonElement value;
            if (!entry.TryGetProperty("t", out value) || value.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("Every entry in 'entries' must hold a 't' term.");
            }

            string term = value.GetString();
            if (string.IsNullOrEmpty(term))
            {
                throw new FormatException("A term in 'entries' must not be empty.");
            }

            // A term category the build does not know maps to Other. That keeps
            // an older client working when a server adds a category.
            string category = entry.TryGetProperty("cat", out value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : "other";

            int severity = 3;
            if (entry.TryGetProperty("sev", out value) && value.ValueKind != JsonValueKind.Null)
            {
                severity = value.GetInt32();
                if (severity < 1 || severity > 5)
                {
                    throw new FormatException(
                        "Severity must be 1 to 5. Term '" + term + "' states " + severity + ".");
                }
            }

            bool requireBoundary = entry.TryGetProperty("w", out value) && value.ValueKind == JsonValueKind.True;
            return new VulgarityTerm(term, category, severity, requireBoundary);
        }

        public override string ToString()
        {
            return "preset " + (Name ?? "(unnamed)")
                + ": " + Languages.Count + " languages, " + Entries.Count + " added, "
                + Remove.Count + " removed";
        }
    }
}
