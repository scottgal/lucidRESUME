namespace lucidRESUME.Core.Models.Profile;

/// <summary>
/// UI-layer value/reason pair used by tag chip controls.
/// Maps to/from <see cref="SkillPreference"/> for skill fields when loading/saving.
/// </summary>
public class TagItem
{
    public string Value { get; set; } = "";
    public string? Reason { get; set; }
}
