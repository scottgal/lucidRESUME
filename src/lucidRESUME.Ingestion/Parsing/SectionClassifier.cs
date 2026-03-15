namespace lucidRESUME.Ingestion.Parsing;

public static class SectionClassifier
{
    private static readonly (string[] Keywords, string Section)[] SectionMap =
    [
        (["experience", "employment", "work history", "career history", "professional experience"], "Experience"),
        (["education", "academic", "qualifications"], "Education"),
        (["skills", "technical skills", "core competencies", "technologies", "expertise"], "Skills"),
        (["certifications", "certificates", "accreditations", "credentials"], "Certifications"),
        (["projects", "personal projects", "side projects", "open source"], "Projects"),
        (["summary", "profile", "objective", "about me", "professional summary"], "Summary"),
    ];

    public static string? ClassifyHeading(string heading)
    {
        var clean = heading.TrimStart('#').Trim().ToLowerInvariant();
        foreach (var (keys, section) in SectionMap)
            if (keys.Any(k => clean.Contains(k)))
                return section;
        return null;
    }
}
