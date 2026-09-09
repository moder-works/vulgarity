using System;
using System.Collections.Generic;
using Vulgarity;

internal static class Program
{
    private static int Main(string[] args)
    {
        VulgarityFilter filter = VulgarityFilter.CreateDefault();
        Console.WriteLine("profile: " + VulgarityFilter.Profile + ", terms: " + filter.TermCount);

        // Mild on purpose, so this file carries no strong term. The samples
        // still show every evasion the matcher defeats.
        string[] samples = args.Length > 0 ? args : new[]
        {
            "Have a nice day.",
            "you are a d.a.m.n",
            "what the daaaamn",
            "h3ll happens",
            "cr@p",
            "I live in Scunthorpe",
            "he is an assassin",
            "class of 2024",
            "a hell of a day",
            "a nice shell company",
            "thorny problem, heroine of the story, trimming the hedge",
        };

        foreach (string sample in samples)
        {
            IReadOnlyList<VulgarityMatch> hits = filter.Scan(sample);
            Console.WriteLine();
            Console.WriteLine("  in    : " + sample);
            Console.WriteLine("  detect: " + filter.Detect(sample) + "   score: " + filter.Score(sample));
            Console.WriteLine("  filter: " + filter.Filter(sample));
            foreach (VulgarityMatch hit in hits)
            {
                Console.WriteLine("    [" + hit.Start + ".." + hit.End + "] '" + hit.Excerpt(sample)
                    + "' -> " + hit.Text + " (" + hit.CategoryName + ", sev " + hit.Severity + ")");
            }
        }

        return 0;
    }
}
