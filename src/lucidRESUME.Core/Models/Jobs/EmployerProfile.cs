namespace lucidRESUME.Core.Models.Jobs;

/// <summary>
/// Employer/company profile for hiring mode.
/// Stored locally, syncs to commercial SaaS when available.
/// </summary>
public sealed class EmployerProfile
{
    public string CompanyName { get; set; } = "";
    public string? Website { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubOrgUrl { get; set; }
    public string? Industry { get; set; }
    public CompanySizeRange CompanySize { get; set; } = CompanySizeRange.Unknown;
    public string? Location { get; set; }
    public string? Description { get; set; }
}

public enum CompanySizeRange
{
    Unknown,
    Micro,    // 1-10
    Small,    // 11-50
    Medium,   // 51-200
    Large,    // 200+
}
