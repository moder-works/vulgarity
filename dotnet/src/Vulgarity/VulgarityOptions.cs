using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Vulgarity
{
    /// <summary>Controls what the filter reports and how it masks text.</summary>
    public sealed class VulgarityOptions
    {
        /// <summary>Ignore any term below this severity. The range is 1 to 5.</summary>
        /// <remarks>Raise this to 2 to keep clinical anatomy out of the results.</remarks>
        public int MinSeverity { get; set; }

        /// <summary>Match only these categories. Null means match every category.</summary>
        public ISet<VulgarityCategory> Categories { get; set; }

        /// <summary>The character that <see cref="VulgarityFilter.Filter"/> repeats.</summary>
        public char MaskChar { get; set; }

        /// <summary>A fixed replacement string. It overrides <see cref="MaskChar"/>.</summary>
        public string MaskToken { get; set; }

        /// <summary>Scan a second time with repeated letters collapsed, so "daaamn" matches "damn".</summary>
        public bool RepeatTolerance { get; set; }

        /// <summary>Drop a match that sits fully inside a longer match.</summary>
        public bool CollapseContained { get; set; }

        /// <summary>How <see cref="VulgarityFilter.Score"/> combines severities.</summary>
        public ScoreMode ScoreMode { get; set; }

        public VulgarityOptions()
        {
            MinSeverity = 1;
            Categories = null;
            MaskChar = '*';
            MaskToken = null;
            RepeatTolerance = true;
            CollapseContained = true;
            ScoreMode = ScoreMode.Total;
        }

        /// <summary>Returns an independent copy.</summary>
        public VulgarityOptions Clone()
        {
            return new VulgarityOptions
            {
                MinSeverity = MinSeverity,
                Categories = Categories == null ? null : new HashSet<VulgarityCategory>(Categories),
                MaskChar = MaskChar,
                MaskToken = MaskToken,
                RepeatTolerance = RepeatTolerance,
                CollapseContained = CollapseContained,
                ScoreMode = ScoreMode,
            };
        }

        /// <summary>Reads options from the JSON shape a preset uses.</summary>
        /// <remarks>
        /// <para>
        /// Every field is optional. A missing field, or one that is explicitly
        /// null, keeps its default. A field of the wrong type, or one out of range,
        /// throws <see cref="FormatException"/> naming the field.
        /// </para>
        /// <para>
        /// The fields are checked in a fixed order — minSeverity, maskChar,
        /// maskToken, repeatTolerance, collapseContained, scoreMode, categories —
        /// so a document with two bad fields names the same one in both ports.
        /// </para>
        /// </remarks>
        public static VulgarityOptions FromJson(string json)
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
                throw new FormatException("The options are not valid JSON. " + error.Message, error);
            }

            using (document)
            {
                return FromElement(document.RootElement);
            }
        }

        internal static VulgarityOptions FromElement(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException(
                    "'options' must be a JSON object. The document states " + JsonRead.TypeName(element) + ".");
            }

            VulgarityOptions options = new VulgarityOptions();

            int minSeverity = JsonRead.ReadOptionalInt(element, "minSeverity") ?? 1;
            if (minSeverity < 1 || minSeverity > 5)
            {
                throw new FormatException("'minSeverity' must be 1 to 5. It states " + minSeverity + ".");
            }

            options.MinSeverity = minSeverity;

            string maskChar = JsonRead.ReadOptionalString(element, "maskChar") ?? "*";
            if (maskChar.Length != 1)
            {
                throw new FormatException(
                    "'maskChar' must be exactly one character. It states '" + maskChar + "'.");
            }

            options.MaskChar = maskChar[0];

            string maskToken = JsonRead.ReadOptionalString(element, "maskToken");
            if (maskToken != null && maskToken.Length == 0)
            {
                throw new FormatException(
                    "'maskToken' must not be empty. Leave it out to mask by character.");
            }

            options.MaskToken = maskToken;
            options.RepeatTolerance = JsonRead.ReadOptionalBool(element, "repeatTolerance") ?? true;
            options.CollapseContained = JsonRead.ReadOptionalBool(element, "collapseContained") ?? true;

            string mode = JsonRead.ReadOptionalString(element, "scoreMode");
            if (mode != null && mode != "total" && mode != "max")
            {
                throw new FormatException("'scoreMode' must be 'total' or 'max'. It states '" + mode + "'.");
            }

            options.ScoreMode = mode == "max" ? ScoreMode.Max : ScoreMode.Total;
            options.Categories = ReadCategories(element);

            // The checks above cover every rule Validate knows, so this is a net
            // rather than a second opinion: a rule added to Validate later must
            // still reach a caller of FromJson as a FormatException, never as an
            // ArgumentOutOfRangeException.
            try
            {
                options.Validate();
            }
            catch (ArgumentException error)
            {
                // ArgumentOutOfRangeException is an ArgumentException, so this
                // catches both. The error names the field it rejected.
                throw new FormatException("These options are not usable. " + error.Message, error);
            }

            return options;
        }

        /// <summary>Reads the category filter.</summary>
        /// <remarks>
        /// An empty array stays an empty set. The two mean different things — an
        /// empty set matches no category at all, and a missing field matches every
        /// one — so mapping one onto the other would invert the policy on a round
        /// trip through JSON.
        /// </remarks>
        private static ISet<VulgarityCategory> ReadCategories(JsonElement element)
        {
            JsonElement names;
            if (!element.TryGetProperty("categories", out names) || names.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (names.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException(
                    "'categories' must be an array of names. The document states "
                    + JsonRead.TypeName(names) + ".");
            }

            HashSet<VulgarityCategory> parsed = new HashSet<VulgarityCategory>();
            foreach (JsonElement name in names.EnumerateArray())
            {
                // An unknown name here would silently match nothing, so it fails
                // loudly. A term with an unknown category is different: that one
                // maps to Other.
                string text = name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                VulgarityCategory? category = text == null ? null : CategoryNames.TryParse(text);
                if (category == null)
                {
                    throw new FormatException(
                        "'categories' names '" + (text ?? name.ToString()) +
                        "', which this build does not know. Valid names: "
                        + string.Join(", ", CategoryNames.All) + ".");
                }

                parsed.Add(category.Value);
            }

            return parsed;
        }

        /// <summary>Writes these options in the JSON shape a preset uses.</summary>
        /// <remarks>
        /// A null <see cref="Categories"/> leaves the key out altogether, because
        /// writing "categories": null and writing "categories": [] would read back
        /// as opposite policies.
        /// </remarks>
        public string ToJson()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(
                    stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    WriteTo(writer);
                    writer.WriteEndObject();
                }

                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteNumber("minSeverity", MinSeverity);

            if (Categories != null)
            {
                writer.WriteStartArray("categories");
                foreach (string name in CategoryNames.All)
                {
                    VulgarityCategory? parsed = CategoryNames.TryParse(name);
                    if (parsed != null && Categories.Contains(parsed.Value))
                    {
                        writer.WriteStringValue(name);
                    }
                }

                writer.WriteEndArray();
            }

            writer.WriteString("maskChar", MaskChar.ToString());
            if (MaskToken == null)
            {
                writer.WriteNull("maskToken");
            }
            else
            {
                writer.WriteString("maskToken", MaskToken);
            }

            writer.WriteBoolean("repeatTolerance", RepeatTolerance);
            writer.WriteBoolean("collapseContained", CollapseContained);
            writer.WriteString("scoreMode", ScoreMode == ScoreMode.Max ? "max" : "total");
        }

        internal void Validate()
        {
            if (MinSeverity < 1 || MinSeverity > 5)
            {
                throw new ArgumentOutOfRangeException("MinSeverity", "MinSeverity must be 1 to 5.");
            }

            if (MaskToken != null && MaskToken.Length == 0)
            {
                throw new ArgumentException("MaskToken must not be empty. Use null to mask by character.");
            }
        }
    }
}
