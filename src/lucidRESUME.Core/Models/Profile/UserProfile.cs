namespace lucidRESUME.Core.Models.Profile;

public sealed class UserProfile
{
    public Guid ProfileId { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    // Who they are
    public string? DisplayName { get; set; }
    public string? CurrentTitle { get; set; }
    public int? YearsOfExperience { get; set; }

    // What they want
    public WorkPreferences Preferences { get; set; } = new();

    // Skills they actively want to use
    public List<SkillPreference> SkillsToEmphasise { get; set; } = [];

    // Skills they have but want to avoid
    public List<SkillPreference> SkillsToAvoid { get; set; } = [];

    // Companies/orgs they won't work for
    public List<string> BlockedCompanies { get; set; } = [];

    // Free-form notes for the AI (context, career goals, anything)
    public string? CareerGoals { get; set; }
    public string? AdditionalContext { get; set; }

    public void BlockCompany(string company)
    {
        if (!BlockedCompanies.Contains(company, StringComparer.OrdinalIgnoreCase))
            BlockedCompanies.Add(company);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void EmphasiseSkill(string skill, string? reason = null)
    {
        SkillsToEmphasise.Add(new SkillPreference { SkillName = skill, Reason = reason });
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AvoidSkill(string skill, string? reason = null)
    {
        SkillsToAvoid.Add(new SkillPreference { SkillName = skill, Reason = reason });
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
