using System;
using System.Collections.Generic;
using Vulgarity;

internal static class Program
{
    private static int Main(string[] args)
    {
        VulgarityFilter filter = VulgarityFilter.CreateDefault();
        Console.WriteLine("profile: " + VulgarityFilter.Profile + ", terms: " + filter.TermCount);

        string[] samples = args.Length > 0 ? args : new[]
        {
            "Have a nice day.",
            "you are a f.u.c.k",
            "what the fuuuuck",
            "sh!t happens",
            "I live in Scunthorpe",
            "he is an assassin",
            "an ass",
            "class of 2024",
            "the rapist was caught",
            "my therapist is great",
            "a$$hole",
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
