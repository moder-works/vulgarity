using System;
using System.Collections.Generic;

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
