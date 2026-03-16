namespace lucidRESUME.Core.Models.Profile;

public class ProfileFieldGroup
{
    public string GroupId { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? AccentColor { get; set; }  // hex e.g. "#89B4FA"
    public string? Icon { get; set; }          // e.g. "👤"
    public int DisplayOrder { get; set; }
    public List<ProfileField> Fields { get; set; } = [];
}
