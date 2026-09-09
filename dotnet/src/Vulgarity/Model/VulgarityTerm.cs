namespace Vulgarity
{
    /// <summary>One term in the list, together with how the matcher must treat it.</summary>
    public sealed class VulgarityTerm
    {
        public VulgarityTerm(string text, string categoryName, int severity, bool requireBoundary)
        {
            Text = text;
            CategoryName = categoryName;
            Category = CategoryNames.Parse(categoryName);
            Severity = severity;
            RequireBoundary = requireBoundary;
        }

        /// <summary>The term, folded to profile fold-v1.</summary>
        public string Text { get; private set; }

        /// <summary>The category as the seed file spells it.</summary>
        public string CategoryName { get; private set; }

        /// <summary>The category as an enum. A name the base list does not define maps to Other.</summary>
        public VulgarityCategory Category { get; private set; }

        /// <summary>How bad the term is, from 1 to 5.</summary>
        public int Severity { get; private set; }

        /// <summary>True when a match must sit on a word boundary.</summary>
        public bool RequireBoundary { get; private set; }

        public override string ToString()
        {
            return Text + " (" + CategoryName + ", " + Severity + ")";
        }
    }
}
