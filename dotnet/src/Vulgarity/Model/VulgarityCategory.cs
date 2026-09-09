namespace Vulgarity
{
    /// <summary>The kind of term that matched.</summary>
    public enum VulgarityCategory
    {
        /// <summary>A category the base list does not define.</summary>
        Other = 0,

        /// <summary>General swearing and insults.</summary>
        Profanity = 1,

        /// <summary>Sexual and anatomical terms.</summary>
        Sexual = 2,

        /// <summary>Slurs that target a group.</summary>
        Hate = 3,

        /// <summary>Threats and violent acts.</summary>
        Violence = 4,

        /// <summary>Illegal drugs and drug use.</summary>
        Drug = 5,
    }

    /// <summary>How <see cref="VulgarityFilter.Score"/> combines severities.</summary>
    public enum ScoreMode
    {
        /// <summary>Add up the severity of every match.</summary>
        Total = 0,

        /// <summary>Take the highest severity of any match.</summary>
        Max = 1,
    }

    internal static class CategoryNames
    {
        /// <summary>The name a seed or preset file uses for a category.</summary>
        public static string ToName(VulgarityCategory category)
        {
            switch (category)
            {
                case VulgarityCategory.Profanity: return "profanity";
                case VulgarityCategory.Sexual: return "sexual";
                case VulgarityCategory.Hate: return "hate";
                case VulgarityCategory.Violence: return "violence";
                case VulgarityCategory.Drug: return "drug";
                default: return "other";
            }
        }

        /// <summary>Maps a category name, or returns null when the name is unknown.</summary>
        public static VulgarityCategory? TryParse(string name)
        {
            switch (name)
            {
                case "profanity": return VulgarityCategory.Profanity;
                case "sexual": return VulgarityCategory.Sexual;
                case "hate": return VulgarityCategory.Hate;
                case "violence": return VulgarityCategory.Violence;
                case "drug": return VulgarityCategory.Drug;
                case "other": return VulgarityCategory.Other;
                default: return null;
            }
        }

        /// <summary>Every category name a preset can name.</summary>
        public static readonly string[] All =
        {
            "profanity", "sexual", "hate", "violence", "drug", "other",
        };

        public static VulgarityCategory Parse(string name)
        {
            switch (name)
            {
                case "profanity": return VulgarityCategory.Profanity;
                case "sexual": return VulgarityCategory.Sexual;
                case "hate": return VulgarityCategory.Hate;
                case "violence": return VulgarityCategory.Violence;
                case "drug": return VulgarityCategory.Drug;
                default: return VulgarityCategory.Other;
            }
        }
    }
}
