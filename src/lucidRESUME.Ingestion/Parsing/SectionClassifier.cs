namespace lucidRESUME.Ingestion.Parsing;

public static partial class SectionClassifier
{
    private static readonly (string[] Keywords, string Section)[] SectionMap =
    [
        (["experience", "employment", "work history", "career history", "professional history", "professional background", "positions held"], "Experience"),
        (["education", "academic", "qualifications"], "Education"),
        (["skills", "technical skills", "core competencies", "technologies", "expertise"], "Skills"),
        (["certifications", "certificates", "accreditations", "credentials"], "Certifications"),
        (["projects", "personal projects", "side projects", "open source"], "Projects"),
        (["summary", "profile", "objective", "about me", "professional summary"], "Summary"),
    ];

    public static string? ClassifyHeading(string heading)
    {
        var raw = heading.TrimStart('#').Trim();
        var clean = CollapseSpacedLetters(raw).ToLowerInvariant();

        // Match section keywords that appear as the primary topic of the heading.
        // Allow one qualifier word before the keyword (e.g. "Professional Experience",
        // "Technical Skills", "Work History") but reject compound headings where the
        // keyword is buried (e.g. "Mentorship & Education", "AI Systems & Applied ML").
        foreach (var (keys, section) in SectionMap)
        {
            foreach (var k in keys)
            {
                if (clean.StartsWith(k))
                    return section;

                var idx = clean.IndexOf(k, StringComparison.Ordinal);
                if (idx <= 0 || clean[idx - 1] != ' ') continue;

                // Count words before the keyword - allow max 1 qualifying word
                var prefix = clean[..idx].Trim();
                var wordsBefore = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordsBefore <= 1)
                    return section;
            }
        }
        return null;
    }

    /// <summary>
    /// Collapses spaced-letter headings: "W O R K  E X P E R I E N C E" → "WORK EXPERIENCE"
    /// Detects by checking if the majority of space-separated tokens are single characters.
    /// </summary>
    private static string CollapseSpacedLetters(string heading)
    {
        var tokens = heading.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3) return heading;
        var singleCount = tokens.Count(t => t.Length == 1);
        if (singleCount < 3 || singleCount < tokens.Length * 0.6) return heading;

        // Merge consecutive single-char tokens into words
        var parts = new System.Collections.Generic.List<string>();
        var word = new System.Text.StringBuilder();
        foreach (var t in tokens)
        {
            if (t.Length == 1) { word.Append(t); }
            else { if (word.Length > 0) { parts.Add(word.ToString()); word.Clear(); } parts.Add(t); }
        }
        if (word.Length > 0) parts.Add(word.ToString());
        return string.Join(" ", parts);
    }
}