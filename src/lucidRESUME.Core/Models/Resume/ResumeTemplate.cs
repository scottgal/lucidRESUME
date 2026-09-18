namespace lucidRESUME.Core.Models.Resume;

/// <summary>
/// Single-column output templates. All templates preserve a predictable reading order
/// and differ only in typography, density, spacing, and restrained colour.
/// </summary>
public sealed record ResumeTemplate(
    string Id,
    string Name,
    string Description,
    string FontFamily,
    string AccentHex,
    float PageMarginPoints,
    float BodyFontSize,
    float SectionFontSize,
    bool Compact)
{
    public override string ToString() => Name;
}

public static class ResumeTemplateCatalog
{
    public const string AtsClassicId = "ats-classic";
    public const string ModernProfessionalId = "modern-professional";
    public const string CompactTechnicalId = "compact-technical";

    public static IReadOnlyList<ResumeTemplate> All { get; } =
    [
        new(AtsClassicId, "ATS Classic",
            "Conservative Arial, black text, clear headings, generous spacing.",
            "Arial", "1F2937", 50, 10, 13, false),
        new(ModernProfessionalId, "Modern Professional",
            "Aptos with a restrained blue accent and balanced spacing.",
            "Aptos", "245B78", 46, 9.5f, 12.5f, false),
        new(CompactTechnicalId, "Compact Technical",
            "Dense Aptos layout for evidence-heavy technical careers.",
            "Aptos", "374151", 38, 9, 11.5f, true)
    ];

    public static ResumeTemplate Get(string? id) =>
        All.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All[0];
}
