using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

public static class TailoringPromptBuilder
{
    /// <summary>
    /// Builds a tailoring prompt by embedding user-controlled content (job description,
    /// resume text, career goals). This is intentionally structured rather than freeform
    /// to reduce the prompt-injection surface, but it is not fully immune. This is acceptable
    /// for a local single-user Ollama deployment where the user controls all input.
    /// If connecting to a hosted model, add structural delimiters (e.g. XML tags) around
    /// each user-supplied section and validate/truncate inputs.
    /// </summary>
    public static string Build(ResumeDocument resume, JobDescription job, UserProfile profile,
        IReadOnlyList<TermMatch>? termMappings = null,
        CoverageReport? coverage = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a professional CV editor. Your task is to tailor the candidate's resume for a specific job.");
        sb.AppendLine("CRITICAL RULES:");
        sb.AppendLine("- NEVER invent, fabricate, or exaggerate any facts, skills, or experiences.");
        sb.AppendLine("- Only reorder, rephrase, or emphasise information that already exists in the resume.");
        sb.AppendLine("- Do not add skills the candidate does not have.");
        sb.AppendLine();

        // Company-type tone guidance
        if (coverage is not null)
        {
            var tone = CompanyTypeTone(coverage.CompanyType);
            if (tone is not null)
            {
                sb.AppendLine($"## Company Type: {coverage.CompanyType}");
                sb.AppendLine(tone);
                sb.AppendLine();
            }
        }

        sb.AppendLine($"## Target Role: {job.Title} at {job.Company}");
        sb.AppendLine();

        // Coverage: answered questions first, then gaps
        if (coverage is { Entries.Count: > 0 })
        {
            sb.AppendLine("## Requirement Coverage (structure your answer around this):");

            var covered = coverage.Covered
                .OrderBy(e => e.Requirement.Priority)
                .ToList();
            var gaps = coverage.RequiredGaps.ToList();

            if (covered.Count > 0)
            {
                sb.AppendLine("### Answered — lead with these, strongest first:");
                foreach (var e in covered)
                    sb.AppendLine($"- [{e.Requirement.Priority}] {e.Requirement.Text} → \"{e.Evidence}\"");
            }

            if (gaps.Count > 0)
            {
                sb.AppendLine("### Not covered — do NOT fabricate; omit or note as developing:");
                foreach (var e in gaps)
                    sb.AppendLine($"- {e.Requirement.Text} (not covered in resume)");
            }

            sb.AppendLine();
        }
        else
        {
            // Fallback: plain skill lists
            sb.AppendLine($"## Required Skills: {string.Join(", ", job.RequiredSkills)}");
            sb.AppendLine($"## Preferred Skills: {string.Join(", ", job.PreferredSkills)}");
            sb.AppendLine();
        }

        // Term normalization
        if (termMappings is { Count: > 0 })
        {
            var pairs = termMappings
                .Where(m => m.MatchedSourceTerm is not null &&
                            !string.Equals(m.MatchedSourceTerm, m.TargetTerm, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pairs.Count > 0)
            {
                sb.AppendLine("## Term Normalization (IMPORTANT):");
                sb.AppendLine("When the resume uses any of the following equivalent terms, USE the job description's exact phrasing:");
                foreach (var m in pairs)
                    sb.AppendLine($"- Resume says \"{m.MatchedSourceTerm}\" → use \"{m.TargetTerm}\"");
                sb.AppendLine();
            }
        }

        if (profile.SkillsToEmphasise.Count > 0)
            sb.AppendLine($"## Candidate wants to emphasise: {string.Join(", ", profile.SkillsToEmphasise.Select(s => s.SkillName))}");
        if (profile.SkillsToAvoid.Count > 0)
            sb.AppendLine($"## Candidate prefers NOT to emphasise: {string.Join(", ", profile.SkillsToAvoid.Select(s => s.SkillName))}");
        if (profile.CareerGoals != null)
            sb.AppendLine($"## Career goals: {profile.CareerGoals}");

        sb.AppendLine();
        sb.AppendLine("## Candidate's Current Resume (Markdown):");
        sb.AppendLine(resume.RawMarkdown ?? "No markdown available.");
        sb.AppendLine();
        sb.AppendLine("Output the tailored resume as clean Markdown only. No explanations or preamble.");

        return sb.ToString();
    }

    private static string? CompanyTypeTone(CompanyType type) => type switch
    {
        CompanyType.Startup     => "Tone: emphasise ownership, breadth, speed of delivery, and shipped outcomes. " +
                                   "De-emphasise process-heavy corporate language.",
        CompanyType.ScaleUp     => "Tone: emphasise building systems at scale, repeatability, and team/function growth. " +
                                   "Show you can take things from scrappy to structured.",
        CompanyType.Enterprise  => "Tone: emphasise process adherence, risk management, stakeholder communication, " +
                                   "and delivery within constraints. Use precise, professional language.",
        CompanyType.Agency      => "Tone: emphasise speed, client communication, multi-project delivery, " +
                                   "and breadth of domain exposure.",
        CompanyType.Consultancy => "Tone: emphasise structured problem-solving, stakeholder management, " +
                                   "frameworks, and on-time delivery across engagements.",
        CompanyType.Finance     => "Tone: emphasise accuracy, compliance awareness, quantified impact, " +
                                   "and regulated-environment experience. Every bullet should have a number.",
        CompanyType.Public      => "Tone: emphasise service delivery, accessibility, stakeholder diversity, " +
                                   "and policy/compliance alignment.",
        CompanyType.Academic    => "Tone: emphasise research rigour, publications, teaching, and methodological depth.",
        _                       => null
    };
}
