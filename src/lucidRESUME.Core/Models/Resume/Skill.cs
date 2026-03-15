namespace lucidRESUME.Core.Models.Resume;

public sealed class Skill
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }  // e.g. "Language", "Framework", "Tool"
    public int? YearsExperience { get; set; }
}
