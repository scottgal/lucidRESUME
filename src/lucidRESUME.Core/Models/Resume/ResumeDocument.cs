using System.Text.Json.Serialization;
using lucidRESUME.Core.Models.Extraction;

namespace lucidRESUME.Core.Models.Resume;

public sealed class ResumeDocument
{
    public Guid ResumeId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }

    // Docling output
    public string? RawMarkdown { get; set; }
    public string? RawJson { get; set; }
    public string? PlainText { get; set; }

    /// <summary>Filesystem cache key for page images (null if not yet cached).</summary>
    public string? ImageCacheKey { get; set; }

    /// <summary>Total number of pages cached (0 if unknown).</summary>
    public int PageCount { get; set; }

    // Parsed sections
    public PersonalInfo Personal { get; set; } = new();
    public List<WorkExperience> Experience { get; set; } = [];
    public List<Education> Education { get; set; } = [];
    public List<Skill> Skills { get; set; } = [];
    public List<Certification> Certifications { get; set; } = [];
    public List<Project> Projects { get; set; } = [];

    // Extraction metadata
    public List<ExtractedEntity> Entities { get; set; } = [];

    // Tailoring metadata
    public Guid? TailoredForJobId { get; set; }

    [JsonIgnore]
    public bool IsTailored => TailoredForJobId.HasValue;

    /// <summary>
    /// Background task that fills missing fields via LLM.
    /// Null if no LLM enhancement was triggered.  Await before persisting to disk
    /// if you need guaranteed-complete data.
    /// </summary>
    [JsonIgnore]
    public Task? LlmEnhancementTask { get; set; }

    [JsonConstructor]
    public ResumeDocument() { }

    public static ResumeDocument Create(string fileName, string contentType, long fileSizeBytes) => new()
    {
        ResumeId = Guid.NewGuid(),
        FileName = fileName,
        ContentType = contentType,
        FileSizeBytes = fileSizeBytes,
        CreatedAt = DateTimeOffset.UtcNow
    };

    public void SetDoclingOutput(string markdown, string? json, string? plainText)
    {
        RawMarkdown = markdown;
        RawJson = json;
        PlainText = plainText;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public void AddEntity(ExtractedEntity entity) => Entities.Add(entity);

    public void MarkTailoredFor(Guid jobId) => TailoredForJobId = jobId;
}
