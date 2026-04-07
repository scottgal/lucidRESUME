using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.ViewModels.Pages.MyData;

public sealed class SkillEvidenceVm
{
    private readonly SkillEvidence _evidence;

    public SkillEvidenceVm(SkillEvidence evidence)
    {
        _evidence = evidence;
    }

    public string SourceLabel => _evidence.Source switch
    {
        EvidenceSource.SkillsSection => "Skills",
        EvidenceSource.AchievementBullet => "Achievement",
        EvidenceSource.JobTechnology => "Tech Stack",
        EvidenceSource.Summary => "Summary",
        EvidenceSource.Education => "Education",
        EvidenceSource.NerExtracted => "NER",
        EvidenceSource.LlmExtracted => "LLM",
        EvidenceSource.GitHubRepository => "GitHub",
        EvidenceSource.Manual => "Manual",
        _ => "Unknown"
    };

    public string SourceColor => _evidence.Source switch
    {
        EvidenceSource.SkillsSection => "#2196F3",
        EvidenceSource.AchievementBullet => "#4CAF50",
        EvidenceSource.JobTechnology => "#00BCD4",
        EvidenceSource.Summary => "#FF9800",
        EvidenceSource.Education => "#9C27B0",
        EvidenceSource.NerExtracted => "#E91E63",
        EvidenceSource.LlmExtracted => "#795548",
        EvidenceSource.GitHubRepository => "#333333",
        EvidenceSource.Manual => "#607D8B",
        _ => "#9E9E9E"
    };

    public string? Company => _evidence.Company;
    public string? JobTitle => _evidence.JobTitle;
    public string SourceText => _evidence.SourceText;
    public double Confidence => _evidence.Confidence;
    public string? DateRange =>
        _evidence.StartDate.HasValue
            ? $"{_evidence.StartDate:MMM yyyy} – {_evidence.EndDate?.ToString("MMM yyyy") ?? "Present"}"
            : null;

    public SkillEvidence GetEvidence() => _evidence;
}
