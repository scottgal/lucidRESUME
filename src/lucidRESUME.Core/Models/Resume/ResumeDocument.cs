using lucidRESUME.Core.Models.Extraction;

namespace lucidRESUME.Core.Models.Resume;

public sealed class ResumeDocument
{
    public Guid ResumeId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long FileSizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }

    // Docling output
    public string? RawMarkdown { get; private set; }
    public string? RawJson { get; private set; }
    public string? PlainText { get; private set; }

    // Parsed sections
    public PersonalInfo Personal { get; private set; } = new();
    public List<WorkExperience> Experience { get; private set; } = [];
    public List<Education> Education { get; private set; } = [];
    public List<Skill> Skills { get; private set; } = [];
    public List<Certification> Certifications { get; private set; } = [];
    public List<Project> Projects { get; private set; } = [];

    // Extraction metadata
    private readonly List<ExtractedEntity> _entities = [];
    public IReadOnlyList<ExtractedEntity> Entities => _entities.AsReadOnly();

    // Tailoring metadata
    public Guid? TailoredForJobId { get; private set; }
    public bool IsTailored => TailoredForJobId.HasValue;

    private ResumeDocument() { }

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

    public void AddEntity(ExtractedEntity entity) => _entities.Add(entity);

    public void MarkTailoredFor(Guid jobId) => TailoredForJobId = jobId;
}
