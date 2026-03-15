using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Export;

public sealed class MarkdownExporter : IResumeExporter
{
    public ExportFormat Format => ExportFormat.Markdown;

    public Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var p = resume.Personal;

        if (p.FullName != null) sb.AppendLine($"# {p.FullName}");

        var contacts = new List<string>();
        if (p.Email != null) contacts.Add(p.Email);
        if (p.Phone != null) contacts.Add(p.Phone);
        if (p.Location != null) contacts.Add(p.Location);
        if (p.LinkedInUrl != null) contacts.Add(p.LinkedInUrl);
        if (p.GitHubUrl != null) contacts.Add(p.GitHubUrl);
        if (contacts.Count > 0) sb.AppendLine(string.Join(" | ", contacts));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(p.Summary))
        {
            sb.AppendLine("## Summary");
            sb.AppendLine(p.Summary);
            sb.AppendLine();
        }

        if (resume.Experience.Count > 0)
        {
            sb.AppendLine("## Experience");
            foreach (var exp in resume.Experience)
            {
                sb.AppendLine($"### {exp.Title} — {exp.Company}");
                var dates = FormatDateRange(exp.StartDate, exp.EndDate, exp.IsCurrent);
                if (!string.IsNullOrEmpty(dates)) sb.AppendLine($"*{dates}*");
                foreach (var a in exp.Achievements) sb.AppendLine($"- {a}");
                sb.AppendLine();
            }
        }

        if (resume.Education.Count > 0)
        {
            sb.AppendLine("## Education");
            foreach (var edu in resume.Education)
            {
                sb.AppendLine($"### {edu.Degree} in {edu.FieldOfStudy} — {edu.Institution}");
                var dates = FormatDateRange(edu.StartDate, edu.EndDate, false);
                if (!string.IsNullOrEmpty(dates)) sb.AppendLine($"*{dates}*");
                sb.AppendLine();
            }
        }

        if (resume.Skills.Count > 0)
        {
            sb.AppendLine("## Skills");
            foreach (var g in resume.Skills.GroupBy(s => s.Category ?? "General"))
            {
                sb.AppendLine($"**{g.Key}:** {string.Join(", ", g.Select(s => s.Name))}");
            }
            sb.AppendLine();
        }

        if (resume.Certifications.Count > 0)
        {
            sb.AppendLine("## Certifications");
            foreach (var c in resume.Certifications)
                sb.AppendLine($"- **{c.Name}** — {c.Issuer} ({c.IssuedDate?.Year})");
            sb.AppendLine();
        }

        if (resume.Projects.Count > 0)
        {
            sb.AppendLine("## Projects");
            foreach (var proj in resume.Projects)
            {
                sb.AppendLine($"### {proj.Name}");
                if (!string.IsNullOrWhiteSpace(proj.Description)) sb.AppendLine(proj.Description);
                if (proj.Technologies.Count > 0)
                    sb.AppendLine($"*Technologies: {string.Join(", ", proj.Technologies)}*");
                sb.AppendLine();
            }
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string FormatDateRange(DateOnly? start, DateOnly? end, bool isCurrent)
    {
        var s = start?.ToString("MMM yyyy") ?? "";
        var e = isCurrent ? "Present" : end?.ToString("MMM yyyy") ?? "";
        return s != "" || e != "" ? $"{s} \u2013 {e}" : "";
    }
}
