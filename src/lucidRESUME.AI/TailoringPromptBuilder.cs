using System.Text;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

public static class TailoringPromptBuilder
{
    public static string Build(ResumeDocument resume, JobDescription job, UserProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a professional CV editor. Your task is to tailor the candidate's resume for a specific job.");
        sb.AppendLine("CRITICAL RULES:");
        sb.AppendLine("- NEVER invent, fabricate, or exaggerate any facts, skills, or experiences.");
        sb.AppendLine("- Only reorder, rephrase, or emphasise information that already exists in the resume.");
        sb.AppendLine("- Do not add skills the candidate does not have.");
        sb.AppendLine();
        sb.AppendLine($"## Target Role: {job.Title} at {job.Company}");
        sb.AppendLine($"## Required Skills: {string.Join(", ", job.RequiredSkills)}");
        sb.AppendLine($"## Preferred Skills: {string.Join(", ", job.PreferredSkills)}");
        sb.AppendLine();

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
}
