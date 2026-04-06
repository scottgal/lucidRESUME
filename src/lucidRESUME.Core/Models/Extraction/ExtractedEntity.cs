namespace lucidRESUME.Core.Models.Extraction;

public sealed class ExtractedEntity
{
    public Guid EntityId { get; set; }
    public string Value { get; set; } = "";
    public string NormalizedValue { get; set; } = "";
    public string Classification { get; set; } = "";
    public double Confidence { get; set; }
    public DetectionSource Source { get; set; }
    public int PageNumber { get; set; }
    public int? CharOffset { get; set; }
    public int? CharLength { get; set; }
    public string? Label { get; set; }
    public string? Section { get; set; }

    public ExtractedEntity() { }

    public static ExtractedEntity Create(string value, string classification,
        DetectionSource source, double confidence, int pageNumber) => new()
    {
        EntityId = Guid.NewGuid(),
        Value = value,
        NormalizedValue = value.Trim().ToLowerInvariant(),
        Classification = classification,
        Source = source,
        Confidence = confidence,
        PageNumber = pageNumber
    };

    public void SetLabel(string label, string? section = null)
    {
        Label = label;
        Section = section;
    }
}
