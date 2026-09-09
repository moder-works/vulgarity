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

        /// <summary>Scan a second time with repeated letters collapsed, so "fuuuck" matches "fuck".</summary>
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
        /// <remarks>Every field is optional. A missing field keeps its default.</remarks>
        public static VulgarityOptions FromJson(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                return FromElement(document.RootElement);
            }
        }

        internal static VulgarityOptions FromElement(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("'options' must be a JSON object.");
            }

            VulgarityOptions options = new VulgarityOptions();
            JsonElement value;

            if (element.TryGetProperty("minSeverity", out value) && value.ValueKind != JsonValueKind.Null)
            {
                options.MinSeverity = value.GetInt32();
            }

            if (element.TryGetProperty("maskChar", out value) && value.ValueKind != JsonValueKind.Null)
            {
                string mask = value.GetString();
                if (mask == null || mask.Length != 1)
                {
                    throw new FormatException("'maskChar' must be exactly one character.");
                }

                options.MaskChar = mask[0];
            }

            if (element.TryGetProperty("maskToken", out value) && value.ValueKind != JsonValueKind.Null)
            {
                options.MaskToken = value.GetString();
            }

            if (element.TryGetProperty("repeatTolerance", out value) && value.ValueKind != JsonValueKind.Null)
            {
                options.RepeatTolerance = value.GetBoolean();
            }

            if (element.TryGetProperty("collapseContained", out value) && value.ValueKind != JsonValueKind.Null)
            {
                options.CollapseContained = value.GetBoolean();
            }

            if (element.TryGetProperty("scoreMode", out value) && value.ValueKind != JsonValueKind.Null)
            {
                string mode = value.GetString();
                if (mode == "total")
                {
                    options.ScoreMode = ScoreMode.Total;
                }
                else if (mode == "max")
                {
                    options.ScoreMode = ScoreMode.Max;
                }
                else
                {
                    throw new FormatException("'scoreMode' must be 'total' or 'max'. It states '" + mode + "'.");
                }
            }

            if (element.TryGetProperty("categories", out value) && value.ValueKind != JsonValueKind.Null)
            {
                if (value.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("'categories' must be an array of names.");
                }

                HashSet<VulgarityCategory> set = new HashSet<VulgarityCategory>();
                foreach (JsonElement name in value.EnumerateArray())
                {
                    string text = name.GetString();

                    // An unknown name here would silently match nothing, so it
                    // fails loudly instead. A term with an unknown category is
                    // different: that one maps to Other.
                    VulgarityCategory? parsed = CategoryNames.TryParse(text);
                    if (parsed == null)
                    {
                        throw new FormatException(
                            "'categories' names '" + text + "', which this build does not know. Valid names: "
                            + string.Join(", ", CategoryNames.All) + ".");
                    }

                    set.Add(parsed.Value);
                }

                options.Categories = set.Count == 0 ? null : set;
            }

            options.Validate();
            return options;
        }

        /// <summary>Writes these options in the JSON shape a preset uses.</summary>
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

            if (Categories == null)
            {
                writer.WriteNull("categories");
            }
            else
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
