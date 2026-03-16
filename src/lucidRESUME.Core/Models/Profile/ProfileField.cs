namespace lucidRESUME.Core.Models.Profile;

public class ProfileField
{
    public string FieldId { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public ProfileFieldDataType DataType { get; init; }
    public bool Required { get; set; }
    public List<string>? AllowedValues { get; set; }
    public string? DefaultValue { get; set; }
    public int DisplayOrder { get; set; }
    // Hint used by CV extraction pipeline to auto-populate this field
    public string? CvMappingHint { get; set; }
}
