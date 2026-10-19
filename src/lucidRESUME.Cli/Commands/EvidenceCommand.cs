using System.CommandLine;
using System.Text.Json;
using lucidRESUME.Cli.Infrastructure;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Cli.Commands;

/// <summary>
/// lucidresume evidence --resume cv.docx [--output evidence.json]
/// Surfaces the full skill ledger with provenance chain.
/// </summary>
public static class EvidenceCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Command Build()
    {
        var resumeOpt = new Option<FileInfo?>("--resume") { Description = "Resume file" };
        var careerStoreOpt = new Option<FileInfo?>("--career-store") { Description = "Saved complete-career data.db (read-only)" };
        var auditOpt = new Option<bool>("--audit") { Description = "Compare ledger concepts with source text before and after refreshing deterministic links" };
        resumeOpt.Aliases.Add("-r");
        var outputOpt = new Option<FileInfo?>("--output") { Description = "Output JSON file" };
        outputOpt.Aliases.Add("-o");
        var configOpt = new Option<FileInfo?>("--config") { Description = "Config file" };

        var cmd = new Command("evidence", "Surface the skill ledger with full evidence provenance")
        {
            resumeOpt, careerStoreOpt, auditOpt, outputOpt, configOpt
        };

        cmd.SetAction(async (result, ct) =>
        {
            var file = result.GetValue(resumeOpt);
            var output = result.GetValue(outputOpt);
            var config = result.GetValue(configOpt);

            await using var sp = ServiceBootstrap.Build(config?.FullName);
            var ledgerBuilder = sp.GetRequiredService<SkillLedgerBuilder>();

            var resume = result.GetValue(careerStoreOpt) is { } database
                ? await ResumeInputHelper.LoadSavedCareerAsync(database, file, null, ct)
                : await ResumeInputHelper.LoadAsync(sp, file, null, ct);
            var beforeAudit = result.GetValue(auditOpt) ? Core.Models.Evidence.EvidenceLedgerAudit.Analyse(resume) : null;

            Console.Error.WriteLine("Building skill ledger...");
            var ledger = await ledgerBuilder.BuildAsync(resume, ct);
            var jobAudits = new List<object>();
            if (result.GetValue(auditOpt) && result.GetValue(careerStoreOpt) is { } jobDatabase)
            {
                var jobParser = sp.GetRequiredService<IJobSpecParser>();
                foreach (var job in await Core.Persistence.SavedCareerReader.ReadJobsAsync(jobDatabase.FullName, ct))
                {
                    if (string.IsNullOrWhiteSpace(job.RawText)) continue;
                    var refreshed = await jobParser.ParseFromTextAsync(job.RawText, ct);
                    jobAudits.Add(new
                    {
                        job.JobId, job.Title, job.Company,
                        before = new { job.RequiredSkills, job.PreferredSkills, job.MentionedSkills, job.SkillAnalysisVersion },
                        after = new { refreshed.RequiredSkills, refreshed.PreferredSkills, refreshed.MentionedSkills,
                            refreshed.Competencies, refreshed.SkillAnalysisVersion, refreshed.SkillDecisions },
                        removedRequired = job.RequiredSkills.Except(refreshed.RequiredSkills, StringComparer.OrdinalIgnoreCase),
                        addedRequired = refreshed.RequiredSkills.Except(job.RequiredSkills, StringComparer.OrdinalIgnoreCase)
                    });
                }
            }

            var evidenceOutput = new
            {
                qualityBeforeRefresh = beforeAudit,
                qualityAfterRefresh = result.GetValue(auditOpt) ? Core.Models.Evidence.EvidenceLedgerAudit.Analyse(resume) : null,
                savedJobAudit = jobAudits,
                basics = new
                {
                    name = resume.Personal.FullName,
                    email = resume.Personal.Email,
                    location = resume.Personal.Location,
                    github = resume.Personal.GitHubUrl,
                    linkedin = resume.Personal.LinkedInUrl,
                },
                skills = ledger.Entries.Select(e => new
                {
                    name = e.SkillName,
                    category = e.Category,
                    strength = Math.Round(e.Strength, 3),
                    calculatedYears = Math.Round(e.CalculatedYears, 1),
                    isCurrent = e.IsCurrent,
                    evidenceCount = e.Evidence.Count,
                    evidence = e.Evidence.Select(ev => new
                    {
                        source = ev.Source.ToString(),
                        company = ev.Company,
                        jobTitle = ev.JobTitle,
                        sourceText = ev.SourceText,
                        confidence = Math.Round(ev.Confidence, 2),
                        startDate = ev.StartDate?.ToString("yyyy-MM"),
                        endDate = ev.EndDate?.ToString("yyyy-MM"),
                    }),
                }),
                signals = new
                {
                    totalSkills = ledger.Entries.Count,
                    strongSkills = ledger.StrongSkills.Count,
                    weakSkills = ledger.WeakSkills.Count,
                    unsubstantiated = ledger.UnsubstantiatedSkills.Count,
                    consistencyIssues = ledger.Issues.Count,
                },
                experience = resume.Experience.Select(e => new
                {
                    company = e.Company,
                    title = e.Title,
                    startDate = e.StartDate?.ToString("yyyy-MM"),
                    endDate = e.IsCurrent ? "present" : e.EndDate?.ToString("yyyy-MM"),
                    technologies = e.Technologies,
                    importSources = e.ImportSources,
                }),
                education = resume.Education.Select(e => new
                {
                    institution = e.Institution,
                    degree = e.Degree,
                    fieldOfStudy = e.FieldOfStudy,
                    level = e.Level.ToString(),
                }),
            };

            var json = JsonSerializer.Serialize(evidenceOutput, JsonOpts);

            if (output is not null)
            {
                await File.WriteAllTextAsync(output.FullName, json, ct);
                Console.Error.WriteLine($"Written to {output.FullName}");
            }
            else
            {
                Console.Write(json);
            }

            Console.Error.WriteLine($"\n{ledger.Entries.Count} skills, {ledger.StrongSkills.Count} strong, {ledger.Issues.Count} issues");
        });

        return cmd;
    }
}
