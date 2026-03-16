namespace lucidRESUME.Core.Models.Profile;

public class ProfileFormDefinition
{
    public string SchemaVersion { get; set; } = "1.0";
    public List<ProfileFieldGroup> Groups { get; set; } = [];
}
