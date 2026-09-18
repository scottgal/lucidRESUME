using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

/// <summary>
/// Turns AI-authored prose into the portable resume artifact. The generated prose is
/// reparsed for DOCX/PDF rendering; JobML remains visibly derived until reviewed.
/// </summary>
public sealed class ResumeArtifactBuilder
{
    public ResumeDocument Build(
        ResumeDocument source,
        ResumeDocument generated,
        JobDescription job,
        string templateId)
    {
        var prose = RemoveExistingJobMl(generated.RawMarkdown ?? generated.PlainText ?? "").Trim();
        var jobMl = JobMlDraftGenerator.Generate(prose);
        AddConceptsAndExternalEvidence(jobMl, source, job);
        AddGenerationProvenance(jobMl, generated);
        var artifact = JobMlArtifactComposer.Compose(jobMl);

        var result = ResumeDocument.Create(
            $"{SafeFileName(job.Title ?? "tailored-resume")}.md",
            "text/markdown",
            System.Text.Encoding.UTF8.GetByteCount(artifact));
        result.SetDoclingOutput(jobMl.Markdown, null, jobMl.Markdown);
        result.CanonicalMarkdown = jobMl.Markdown;
        result.JobMlSource = artifact;
        result.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(jobMl.Markdown);
        result.OutputTemplateId = templateId;
        result.MarkTailoredFor(job.JobId);

        MarkdownSectionParser.PopulateSections(result, jobMl.Markdown);
        ReconcileGeneratedExperience(result, source);
        CopyContactDetails(source.Personal, result.Personal);

        foreach (var project in source.Projects.Where(project =>
                     !string.IsNullOrWhiteSpace(project.Name) &&
                     jobMl.Markdown.Contains(project.Name, StringComparison.OrdinalIgnoreCase)))
            result.Projects.Add(project);

        foreach (var certification in source.Certifications.Where(certification =>
                     !string.IsNullOrWhiteSpace(certification.Name) &&
                     jobMl.Markdown.Contains(certification.Name, StringComparison.OrdinalIgnoreCase)))
            result.Certifications.Add(certification);

        foreach (var entity in source.Entities) result.AddEntity(entity);
        result.GenerationEvidenceLinks.AddRange(generated.GenerationEvidenceLinks);
        result.GenerationWarnings.AddRange(generated.GenerationWarnings);
        return result;
    }

    private static void AddGenerationProvenance(JobMlFile file, ResumeDocument generated)
    {
        foreach (var claim in file.Data.Claims)
        {
            var matches = generated.GenerationEvidenceLinks
                .Where(link => ClaimsMatch(claim.Statement, link.OutputClaim))
                .SelectMany(link => link.EvidenceRefs)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var evidenceRef in matches)
            {
                var ledgerRef = $"ledger://{evidenceRef}";
                if (claim.Evidence.All(evidence => !string.Equals(evidence.Ref, ledgerRef, StringComparison.OrdinalIgnoreCase)))
                    claim.Evidence.Add(new JobMlEvidence { Type = "source_ledger", Ref = ledgerRef });
            }
        }
    }

    private static void ReconcileGeneratedExperience(ResumeDocument generated, ResumeDocument source)
    {
        foreach (var experience in generated.Experience)
        {
            var company = NormalizeClaim(experience.Company ?? "");
            var title = NormalizeClaim(experience.Title ?? "");
            var direct = source.Experience.FirstOrDefault(candidate =>
                EquivalentLabel(company, candidate.Company) && EquivalentLabel(title, candidate.Title));
            var reversed = direct is null
                ? source.Experience.FirstOrDefault(candidate =>
                    EquivalentLabel(company, candidate.Title) && EquivalentLabel(title, candidate.Company))
                : null;
            var match = direct ?? reversed;
            if (match is null) continue;
            experience.Company = match.Company;
            experience.Title = match.Title;
        }
    }

    private static bool EquivalentLabel(string normalized, string? candidate)
    {
        var other = NormalizeClaim(candidate ?? "");
        return normalized == other || (normalized.Length >= 5 && other.Length >= 5 &&
            (normalized.Contains(other, StringComparison.Ordinal) || other.Contains(normalized, StringComparison.Ordinal)));
    }

    private static bool ClaimsMatch(string passage, string outputClaim)
    {
        var left = NormalizeClaim(passage);
        var right = NormalizeClaim(outputClaim);
        if (left.Length == 0 || right.Length == 0) return false;
        if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal)) return true;

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var union = leftTokens.Union(rightTokens).Count();
        return union > 0 && (double)leftTokens.Intersect(rightTokens).Count() / union >= 0.72;
    }

    private static string NormalizeClaim(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9+#.]+", " ").Trim();

    private static void AddConceptsAndExternalEvidence(
        JobMlFile file,
        ResumeDocument source,
        JobDescription job)
    {
        var concepts = source.Skills.Select(skill => (skill.Name, Type: "skill"))
            .Concat(source.Experience.SelectMany(exp => exp.Technologies).Select(name => (Name: name, Type: "technology")))
            .Concat(source.Projects.SelectMany(project => project.Technologies).Select(name => (Name: name, Type: "technology")))
            .Concat(job.RequiredSkills.Select(name => (Name: name, Type: "skill")))
            .Concat(job.PreferredSkills.Select(name => (Name: name, Type: "skill")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new JobMlConcept
            {
                Id = Slug(item.Name),
                Type = item.Type,
                Name = item.Name
            })
            .ToList();
        file.Data.Concepts.AddRange(concepts);

        foreach (var claim in file.Data.Claims)
        {
            claim.Concepts.Skills.AddRange(concepts
                .Where(concept => ContainsTerm(claim.Statement, concept.Name))
                .Select(concept => concept.Id));

            foreach (var project in source.Projects.Where(project =>
                         Uri.TryCreate(project.Url, UriKind.Absolute, out _) &&
                         (ContainsTerm(claim.Statement, project.Name) ||
                          project.Technologies.Any(technology => ContainsTerm(claim.Statement, technology)))))
            {
                if (claim.Evidence.All(evidence => !string.Equals(evidence.Uri, project.Url, StringComparison.OrdinalIgnoreCase)))
                    claim.Evidence.Add(new JobMlEvidence { Type = "repository", Uri = project.Url });
            }
        }

        file.Data.Job = new JobMlJob { Id = Slug(job.Title ?? "target-role") };
        file.Data.Requirements.AddRange(job.RequiredSkills.Select(skill => new JobMlRequirement
        {
            Id = $"req-{Slug(skill)}",
            Concept = Slug(skill),
            Importance = "required"
        }));
        file.Data.Requirements.AddRange(job.PreferredSkills.Select(skill => new JobMlRequirement
        {
            Id = $"req-{Slug(skill)}",
            Concept = Slug(skill),
            Importance = "preferred"
        }));
    }

    private static void CopyContactDetails(PersonalInfo source, PersonalInfo target)
    {
        target.FullName ??= source.FullName;
        target.Email ??= source.Email;
        target.Phone ??= source.Phone;
        target.Location ??= source.Location;
        target.LinkedInUrl ??= source.LinkedInUrl;
        target.GitHubUrl ??= source.GitHubUrl;
        target.WebsiteUrl ??= source.WebsiteUrl;
    }

    private static string RemoveExistingJobMl(string markdown)
    {
        if (new JobMlParser().TryParse(markdown, out var parsed, out _)) return parsed!.Markdown;
        return markdown;
    }

    private static bool ContainsTerm(string text, string term) =>
        term.Length >= 2 && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string Slug(string value)
    {
        value = value.Replace("ASP.NET", "aspnet", StringComparison.OrdinalIgnoreCase)
            .Replace(".NET", "dotnet", StringComparison.OrdinalIgnoreCase)
            .Replace("C++", "cpp", StringComparison.OrdinalIgnoreCase)
            .Replace("C#", "csharp", StringComparison.OrdinalIgnoreCase);
        var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    private static string SafeFileName(string value)
    {
        var safe = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "tailored-resume" : safe;
    }
}
