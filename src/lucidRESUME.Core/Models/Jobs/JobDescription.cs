namespace lucidRESUME.Core.Models.Jobs;

public sealed class JobDescription
{
    public Guid JobId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Source
    public JobSource Source { get; private set; } = new();

    // Core fields
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? Location { get; set; }
    public bool? IsRemote { get; set; }
    public bool? IsHybrid { get; set; }
    public SalaryRange? Salary { get; set; }

    // Extracted requirements
    public List<string> RequiredSkills { get; set; } = [];
    public List<string> PreferredSkills { get; set; } = [];
    public int? RequiredYearsExperience { get; set; }
    public string? RequiredEducation { get; set; }
    public List<string> Responsibilities { get; set; } = [];
    public List<string> Benefits { get; set; } = [];

    // Raw content always preserved
    public string RawText { get; private set; } = "";

    // Application tracking
    public double? MatchScore { get; private set; }
    public bool IsBlocked { get; private set; }
    public string? BlockReason { get; private set; }

    private JobDescription() { }

    public static JobDescription Create(string rawText, JobSource source) => new()
    {
        JobId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        RawText = rawText,
        Source = source
    };

    public void SetMatchScore(double score) => MatchScore = score;

    public void Block(string reason)
    {
        IsBlocked = true;
        BlockReason = reason;
    }
}
