namespace lucidRESUME.Core.Models.Resume;

public sealed class Skill
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }  // e.g. "Language", "Framework", "Tool"
    public int? YearsExperience { get; set; }

    /// <summary>Number of LinkedIn endorsements (0 = not imported or no endorsements).</summary>
    public int EndorsementCount { get; set; }

    /// <summary>Import sources that contributed to this skill.</summary>
    public List<string> ImportSources { get; set; } = [];
}
