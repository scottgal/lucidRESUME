using System.CommandLine;
using System.Text;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume anonymize --resume cv.docx [--output anon.md] [--format markdown|docx|pdf]
/// Strips PII and replaces with abstractions for bias-free sharing.
/// </summary>
public static class AnonymizeCommand
{
    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo>("--resume") { Required = true, Description = "Resume file to anonymize" };
        resumeOpt.Aliases.Add("-r");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output file" };
        outputOpt.Aliases.Add("-o");
        var formatOpt = new Option<string>("--format") { Description = "Output format: markdown (default), docx, pdf" };
        
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("anonymize", "Strip PII for bias-free sharing — names, locations, specific institutions abstracted")
        {
            resumeOpt, outputOpt, formatOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(resumeOpt)!;
            var output = result.GetValue(outputOpt);
            var format = result.GetValue(formatOpt) ?? "markdown";
            var config = result.GetValue(configOpt);

            var sp = ServiceBootstrap.Build(config?.FullName);
            var parser = sp.GetRequiredService<IResumeParser>();

            Console.Error.WriteLine($"Parsing {file.Name}...");
            var resume = await ParseHelper.ParseAndAwaitAsync(parser, file.FullName, ct);

            Console.Error.WriteLine("Anonymizing...");
            var anon = Anonymize(resume);

            // Export as markdown by default
            byte[] outputBytes;
            if (format.Equals("docx", StringComparison.OrdinalIgnoreCase))
            {
                var exporter = sp.GetServices<IResumeExporter>().FirstOrDefault(e => e.Format == ExportFormat.Docx);
                outputBytes = exporter != null ? await exporter.ExportAsync(anon, ct) : Encoding.UTF8.GetBytes(ToMarkdown(anon));
            }
            else if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            {
                var exporter = sp.GetServices<IResumeExporter>().FirstOrDefault(e => e.Format == ExportFormat.Pdf);
                outputBytes = exporter != null ? await exporter.ExportAsync(anon, ct) : Encoding.UTF8.GetBytes(ToMarkdown(anon));
            }
            else
            {
                outputBytes = Encoding.UTF8.GetBytes(ToMarkdown(anon));
            }

            if (output is not null)
            {
                await File.WriteAllBytesAsync(output.FullName, outputBytes, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(Encoding.UTF8.GetString(outputBytes));
            }

            Console.Error.WriteLine($"\nAnonymized: name, email, phone, specific locations stripped. {resume.Experience.Count} positions abstracted.");
        });

        return cmd;
    }

    private static ResumeDocument Anonymize(ResumeDocument original)
    {
        var anon = ResumeDocument.Create("anonymized", "application/anonymized", 0);

        // Strip PII
        anon.Personal = new PersonalInfo
        {
            FullName = null, // stripped
            Email = null,    // stripped
            Phone = null,    // stripped
            Location = AnonymizeLocation(original.Personal.Location),
            Summary = original.Personal.Summary, // keep summary (no name in it usually)
            LinkedInUrl = null, // stripped
            GitHubUrl = null,   // stripped
            WebsiteUrl = null,  // stripped
        };

        // Anonymize experience — keep title/tech/achievements, abstract company
        foreach (var exp in original.Experience)
        {
            anon.Experience.Add(new WorkExperience
            {
                Company = AnonymizeCompany(exp.Company, exp.Location),
                Title = exp.Title,
                Location = AnonymizeLocation(exp.Location),
                StartDate = exp.StartDate,
                EndDate = exp.EndDate,
                IsCurrent = exp.IsCurrent,
                Achievements = exp.Achievements.ToList(),
                Technologies = exp.Technologies.ToList(),
            });
        }

        // Education — abstract institution to tier
        foreach (var edu in original.Education)
        {
            edu.ClassifyLevel();
            var tier = EducationEquivalence.Default.GetUniversityTier(edu.Institution);
            anon.Education.Add(new Education
            {
                Institution = tier.Tier switch
                {
                    1 => $"Tier 1 university ({tier.Group})",
                    2 => $"Well-regarded university ({tier.Group})",
                    _ => "Accredited university",
                },
                Degree = edu.Degree,
                FieldOfStudy = edu.FieldOfStudy,
                StartDate = edu.StartDate,
                EndDate = edu.EndDate,
                Level = edu.Level,
            });
        }

        // Skills — keep as-is (skills are not PII)
        anon.Skills = original.Skills.ToList();

        // Projects — keep name + tech, strip URLs
        foreach (var proj in original.Projects)
        {
            anon.Projects.Add(new Project
            {
                Name = proj.Name,
                Description = proj.Description,
                Technologies = proj.Technologies.ToList(),
                Url = null, // stripped
                Date = proj.Date,
            });
        }

        anon.Certifications = original.Certifications.ToList();

        return anon;
    }

    private static string? AnonymizeLocation(string? location)
    {
        if (string.IsNullOrEmpty(location)) return null;
        // Keep country/region, strip city
        var parts = location.Split(',').Select(p => p.Trim()).ToArray();
        return parts.Length >= 2 ? parts[^1] : "Undisclosed";
    }

    private static string? AnonymizeCompany(string? company, string? location)
    {
        if (string.IsNullOrEmpty(company)) return "Undisclosed company";
        // Abstract to description — users can override later
        var lower = company.ToLowerInvariant();
        if (lower.Contains("microsoft")) return "Major technology corporation";
        if (lower.Contains("dell")) return "Global hardware/services company";
        if (lower.Contains("consulting") || lower.Contains("self-employed") || lower.Contains("freelance"))
            return "Independent consulting";
        // Default: keep industry signal, strip name
        return "Technology company";
    }

    private static string ToMarkdown(ResumeDocument resume)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Candidate Profile (Anonymized)");
        sb.AppendLine();

        if (resume.Personal.Location != null)
            sb.AppendLine($"Location: {resume.Personal.Location}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(resume.Personal.Summary))
        {
            sb.AppendLine("## Summary");
            sb.AppendLine(resume.Personal.Summary);
            sb.AppendLine();
        }

        if (resume.Experience.Count > 0)
        {
            sb.AppendLine("## Experience");
            foreach (var exp in resume.Experience)
            {
                sb.AppendLine($"### {exp.Title} — {exp.Company}");
                var start = exp.StartDate?.ToString("MMM yyyy") ?? "";
                var end = exp.IsCurrent ? "Present" : exp.EndDate?.ToString("MMM yyyy") ?? "";
                if (start.Length > 0) sb.AppendLine($"*{start} – {end}*");
                foreach (var a in exp.Achievements) sb.AppendLine($"- {a}");
                if (exp.Technologies.Count > 0)
                    sb.AppendLine($"*Tech: {string.Join(", ", exp.Technologies)}*");
                sb.AppendLine();
            }
        }

        if (resume.Skills.Count > 0)
        {
            sb.AppendLine("## Skills");
            foreach (var g in resume.Skills.GroupBy(s => s.Category ?? "General"))
                sb.AppendLine($"**{g.Key}:** {string.Join(", ", g.Select(s => s.Name))}");
            sb.AppendLine();
        }

        if (resume.Education.Count > 0)
        {
            sb.AppendLine("## Education");
            foreach (var edu in resume.Education)
                sb.AppendLine($"- {edu.Degree} — {edu.Institution}");
        }

        return sb.ToString();
    }
}
