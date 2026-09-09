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

        /// <summary>The severity a preset entry takes when it states none.</summary>
        /// <remarks>
        /// A preset entry is added one at a time and by hand, so an entry that says
        /// nothing sits in the middle of the range. A seed entry defaults to 1
        /// instead. Both ports use these two numbers.
        /// </remarks>
        public const int DefaultSeverity = 3;

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

                int? schema = JsonRead.ReadOptionalInt(root, "schema");
                if (schema != null && schema.Value != SupportedSchema)
                {
                    throw new FormatException(
                        "This build reads preset schema " + SupportedSchema +
                        ". The document states schema " + schema.Value + ".");
                }

                // The profile pins the fold table. A preset built against an
                // older table would match differently, so it fails here.
                string profile = JsonRead.ReadOptionalString(root, "profile");
                if (profile != null && profile != FoldTableData.Profile)
                {
                    throw new FormatException(
                        "This build implements fold profile '" + FoldTableData.Profile +
                        "'. The preset states '" + profile + "'.");
                }

                List<VulgarityTerm> entries = new List<VulgarityTerm>();
                if (root.TryGetProperty("entries", out value) && value.ValueKind != JsonValueKind.Null)
                {
                    if (value.ValueKind != JsonValueKind.Array)
                    {
                        throw new FormatException(
                            "'entries' must be an array. The document states " + JsonRead.TypeName(value) + ".");
                    }

                    foreach (JsonElement entry in value.EnumerateArray())
                    {
                        entries.Add(ReadEntry(entry));
                    }
                }

                JsonElement rawOptions;
                bool hasOptions = root.TryGetProperty("options", out rawOptions)
                    && rawOptions.ValueKind != JsonValueKind.Null;
                if (hasOptions && rawOptions.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException(
                        "'options' must be a JSON object. The document states "
                        + JsonRead.TypeName(rawOptions) + ".");
                }

                string name = JsonRead.ReadOptionalString(root, "name");
                string description = JsonRead.ReadOptionalString(root, "description");
                List<string> languages = ReadLanguages(root);

                VulgarityOptions options = hasOptions
                    ? VulgarityOptions.FromElement(rawOptions)
                    : new VulgarityOptions();

                return new VulgarityPreset(
                    name,
                    description,
                    languages,
                    options,
                    entries,
                    JsonRead.ReadStrings(root, "allow"),
                    JsonRead.ReadStrings(root, "remove"));
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

        /// <summary>Reads and shape-checks the language codes.</summary>
        /// <remarks>
        /// The shape check mirrors the Dart port: a code is 2 to 8 lower-case
        /// letters. This port then goes one step further and asks whether the
        /// assembly carries the pack, because a .NET caller who wants to serve a
        /// code no pack covers passes a resolver to
        /// <see cref="VulgarityFilterBuilder.AddPreset(VulgarityPreset, Func{string, string})"/>
        /// with a preset object rather than a document. Dart defers that question
        /// to the builder instead; the divergence is documented on both sides.
        /// </remarks>
        private static List<string> ReadLanguages(JsonElement root)
        {
            List<string> codes = JsonRead.ReadStrings(root, "languages");
            foreach (string code in codes)
            {
                if (!IsLanguageCode(code))
                {
                    throw new FormatException(
                        "'languages' names '" + code + "', which is not a language code. " +
                        "A code is 2 to 8 lower-case letters.");
                }

                if (!VulgarityLanguages.Has(code))
                {
                    throw new FormatException(
                        "'languages' names '" + code + "', which this build does not carry. Available: "
                        + string.Join(", ", new List<string>(VulgarityLanguages.Available).ToArray()) + ".");
                }
            }

            return codes;
        }

        /// <summary>True when the code is 2 to 8 lower-case ASCII letters.</summary>
        private static bool IsLanguageCode(string code)
        {
            if (code == null || code.Length < 2 || code.Length > 8)
            {
                return false;
            }

            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] < 'a' || code[i] > 'z')
                {
                    return false;
                }
            }

            return true;
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
                throw new FormatException("Every entry in 'entries' must hold a non-empty 't' term.");
            }

            string term = value.GetString();
            if (string.IsNullOrEmpty(term))
            {
                throw new FormatException("Every entry in 'entries' must hold a non-empty 't' term.");
            }

            // A term category the build does not know maps to Other. That keeps an
            // older client working when a server adds a category. A category that is
            // not a string is a different thing: that is a malformed document.
            string category = JsonRead.ReadOptionalString(entry, "cat") ?? "other";

            int severity = JsonRead.ReadOptionalInt(entry, "sev") ?? DefaultSeverity;
            if (severity < 1 || severity > 5)
            {
                throw new FormatException(
                    "Severity must be 1 to 5. Term '" + term + "' states " + severity + ".");
            }

            return new VulgarityTerm(term, category, severity, JsonRead.ReadOptionalBool(entry, "w") ?? false);
        }

        public override string ToString()
        {
            return "preset " + (Name ?? "(unnamed)")
                + ": " + Languages.Count + " languages, " + Entries.Count + " added, "
                + Remove.Count + " removed";
        }
    }
}
