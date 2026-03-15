namespace lucidRESUME.Core.Models.Extraction;

public sealed class ExtractedEntity
{
    public Guid EntityId { get; private set; }
    public string Value { get; private set; } = "";
    public string NormalizedValue { get; private set; } = "";
    public string Classification { get; private set; } = "";
    public double Confidence { get; private set; }
    public DetectionSource Source { get; private set; }
    public int PageNumber { get; private set; }
    public int? CharOffset { get; private set; }
    public int? CharLength { get; private set; }
    public string? Label { get; private set; }
    public string? Section { get; private set; }

    private ExtractedEntity() { }

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
