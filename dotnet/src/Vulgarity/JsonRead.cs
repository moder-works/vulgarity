using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Vulgarity
{
    /// <summary>Field readers shared by the seed, preset and options parsers.</summary>
    /// <remarks>
    /// <para>
    /// A document from the network is untrusted, so no reader here ever coerces.
    /// <c>"minSeverity": "3"</c> is a document the sender got wrong, and saying so
    /// is more useful than guessing what was meant.
    /// </para>
    /// <para>
    /// Two rules hold for every reader. A missing field and an explicit
    /// <c>null</c> mean the same thing: both leave the caller's default in place.
    /// A field that is present with the wrong type throws
    /// <see cref="FormatException"/>, naming the field and the type the schema
    /// wants.
    /// </para>
    /// <para>dart/lib/src/json_read.dart is the mirror of this file.</para>
    /// </remarks>
    internal static class JsonRead
    {
        /// <summary>Names the JSON type of a value for an error message.</summary>
        public static string TypeName(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Undefined:
                case JsonValueKind.Null:
                    return "null";
                case JsonValueKind.String:
                    return "a string";
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return "a boolean";
                case JsonValueKind.Number:
                    return "a number";
                case JsonValueKind.Array:
                    return "an array";
                case JsonValueKind.Object:
                    return "an object";
                default:
                    return "a value";
            }
        }

        /// <summary>Reads a whole number. Returns null when the field is absent.</summary>
        /// <remarks>
        /// A fractional number is refused, so <c>1.0</c> is not a stand-in for
        /// <c>1</c>.
        /// </remarks>
        public static int? ReadOptionalInt(JsonElement json, string field)
        {
            JsonElement value;
            if (!json.TryGetProperty(field, out value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            int result;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out result))
            {
                throw new FormatException(
                    "'" + field + "' must be a whole number. The document states " + TypeName(value) + ".");
            }

            return result;
        }

        /// <summary>Reads a boolean. Returns null when the field is absent.</summary>
        public static bool? ReadOptionalBool(JsonElement json, string field)
        {
            JsonElement value;
            if (!json.TryGetProperty(field, out value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            {
                throw new FormatException(
                    "'" + field + "' must be true or false. The document states " + TypeName(value) + ".");
            }

            return value.ValueKind == JsonValueKind.True;
        }

        /// <summary>Reads a string. Returns null when the field is absent.</summary>
        public static string ReadOptionalString(JsonElement json, string field)
        {
            JsonElement value;
            if (!json.TryGetProperty(field, out value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new FormatException(
                    "'" + field + "' must be a string. The document states " + TypeName(value) + ".");
            }

            return value.GetString();
        }

        /// <summary>Reads an array of strings. A missing field returns an empty list.</summary>
        /// <remarks>
        /// An empty string carries no meaning in any of these lists, so it drops out
        /// rather than failing. A value of another type is a malformed document.
        /// </remarks>
        public static List<string> ReadStrings(JsonElement json, string field)
        {
            List<string> result = new List<string>();

            JsonElement value;
            if (!json.TryGetProperty(field, out value) || value.ValueKind == JsonValueKind.Null)
            {
                return result;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException(
                    "'" + field + "' must be an array of strings. The document states " + TypeName(value) + ".");
            }

            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException(
                        "'" + field + "' must hold strings only. It holds " + TypeName(item) + ".");
                }

                string text = item.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    result.Add(text);
                }
            }

            return result;
        }
    }
}
