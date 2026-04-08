using System.Text.Json.Serialization;

namespace lucidRESUME.Core.Models.Jobs;

public sealed class JobDescription
{
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Source
    public JobSource Source { get; set; } = new();

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
    public string RawText { get; set; } = "";

    /// <summary>Classified employer type - populated by ResumeCoverageAnalyser on first analysis.</summary>
    public CompanyType CompanyType { get; set; } = CompanyType.Unknown;

    /// <summary>True if this is a role the user is hiring for (employer mode), false if applying to.</summary>
    public bool IsEmployerRole { get; set; }

    // Application tracking
    public double? MatchScore { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }

    /// <summary>
    /// Set when the URL could not be scraped automatically (login wall, bot detection, etc.)
    /// and the user must manually paste the job description text.
    /// </summary>
    public bool NeedsManualInput { get; private set; }

    [JsonConstructor]
    public JobDescription() { }

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

    public void MarkNeedsManualInput() => NeedsManualInput = true;
}