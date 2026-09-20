using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Ingestion.Parsing;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

/// <summary>
/// Renders a role-specific projection of the ingestion ledger. This class never extracts
/// claims or infers evidence relationships from output prose.
/// </summary>
public sealed class ResumeArtifactBuilder
{
    public ResumeDocument Build(
        ResumeDocument source,
        ResumeDocument projection,
        JobDescription job,
        string templateId)
    {
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        var projectionInfo = projection.Projection
            ?? throw new InvalidOperationException("Output must be a deterministic resume projection with ledger bindings.");
        if (projectionInfo.SourceResumeId != source.ResumeId)
            throw new InvalidOperationException("The projection belongs to a different source resume.");
        if (!string.Equals(projectionInfo.SourceRevision, ledger.SourceRevision, StringComparison.Ordinal))
            throw new InvalidOperationException("The evidence ledger changed after this projection was created. Re-project before exporting.");

        var prose = RemoveExistingJobMl(projection.RawMarkdown ?? projection.PlainText ?? "").Trim();
        var jobMl = BuildProjectedJobMl(prose, source, projectionInfo, ledger, job);
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
        result.TargetRole = job.Title;
        result.MarkTailoredFor(job.JobId);

        result.Personal = projection.Personal;
        result.Experience = projection.Experience;
        result.Education = projection.Education;
        result.Skills = projection.Skills;
        result.Projects = projection.Projects;
        result.Certifications = projection.Certifications;
        result.EvidenceLedger = ledger;
        result.Projection = projectionInfo;

        foreach (var entity in source.Entities) result.AddEntity(entity);
        return result;
    }

    private static JobMlFile BuildProjectedJobMl(string markdown, ResumeDocument source,
        ResumeProjectionInfo projection, EvidenceLedger ledger, JobDescription job)
    {
        var index = MarkdownEvidenceIndex.Create(markdown);
        var root = new JobMlRoot
        {
            Document = new JobMlDocumentMetadata
            {
                Id = EvidenceLedgerBuilder.Slug(source.Personal.FullName ?? "resume"),
                Language = "en-GB",
                CompleteLedger = Uri.TryCreate(source.CompleteJobMlUri, UriKind.Absolute, out _)
                    ? source.CompleteJobMlUri
                    : TryReadCompleteLedger(source.JobMlSource)
            },
            Job = new JobMlJob { Id = EvidenceLedgerBuilder.Slug(job.Title ?? "target-role") }
        };

        var claimsById = ledger.Claims.ToDictionary(claim => claim.Id, StringComparer.OrdinalIgnoreCase);
        var evidenceById = ledger.Evidence.ToDictionary(evidence => evidence.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var binding in projection.Blocks)
        {
            if (!claimsById.TryGetValue(binding.ClaimId, out var ledgerClaim))
                throw new InvalidOperationException($"Projection references missing ledger claim '{binding.ClaimId}'.");
            if (!index.TryGet(binding.ProseRef, out var passage))
                throw new InvalidOperationException($"Projection prose reference '{binding.ProseRef}' is missing or ambiguous.");

            var claim = new JobMlClaim
            {
                Id = ledgerClaim.Id,
                Subject = ledgerClaim.SubjectId ?? "resume",
                Statement = ledgerClaim.Statement,
                Origin = "projected",
                Review = ledgerClaim.Review,
                Concepts = new JobMlClaimConcepts { Skills = ledgerClaim.Concepts.Select(EvidenceLedgerBuilder.Slug).ToList() },
                Evidence =
                [
                    new JobMlEvidence
                    {
                        Type = "prose",
                        Ref = passage.Reference,
                        Fingerprint = new JobMlFingerprint { Text = passage.Fingerprint },
                        Selector = new JobMlTextSelector { Exact = passage.Text }
                    }
                ]
            };

            foreach (var evidenceId in ledgerClaim.EvidenceIds)
            {
                if (!evidenceById.TryGetValue(evidenceId, out var evidence))
                    throw new InvalidOperationException($"Ledger claim '{ledgerClaim.Id}' references missing evidence '{evidenceId}'.");
                claim.Evidence.Add(!string.IsNullOrWhiteSpace(evidence.ExternalUri)
                    ? new JobMlEvidence
                    {
                        Id = evidence.Id,
                        Type = EvidenceType(evidence),
                        Uri = evidence.ExternalUri,
                        Title = evidence.Title ?? EvidenceTitle(evidence),
                        Authors = [.. evidence.Authors],
                        Publisher = evidence.Publisher ?? PublisherFromUri(evidence.ExternalUri),
                        Published = evidence.PublishedOn?.ToString("yyyy-MM-dd"),
                        Accessed = evidence.AccessedOn?.ToString("yyyy-MM-dd")
                    }
                    : new JobMlEvidence
                    {
                        Id = evidence.Id,
                        Type = "source_ledger",
                        Ref = $"ledger://{evidence.Id}",
                        // cJobML cites the imported source document, not a copy of the
                        // passage. Exact text, locator and drift hash remain in full JobML.
                        Title = evidence.SourceName.EndsWith("-merged.md", StringComparison.OrdinalIgnoreCase)
                            ? "Canonical career ledger"
                            : evidence.SourceName,
                        Fingerprint = new JobMlFingerprint { Text = evidence.FastHash },
                        Selector = new JobMlTextSelector { Exact = evidence.Text }
                    });
            }

            root.Claims.Add(claim);
        }

        root.Concepts.AddRange(root.Claims.SelectMany(claim => claim.Concepts.Skills)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new JobMlConcept
            {
                Id = id,
                Type = "skill",
                Name = ledger.Claims.SelectMany(claim => claim.Concepts)
                    .FirstOrDefault(name => EvidenceLedgerBuilder.Slug(name) == id) ?? id
            }));

        foreach (var experience in source.Experience.Where(experience =>
                     root.Claims.Any(claim => claim.Subject == $"experience:{experience.Id:N}")))
        {
            root.Entities.Add(new JobMlEntity
            {
                Id = $"experience:{experience.Id:N}",
                Type = "experience",
                Name = experience.Company ?? experience.Title ?? "Experience",
                Source = $"#experience-{experience.Id:N}"
            });
        }

        foreach (var project in source.Projects.Where(project =>
                     root.Claims.Any(claim => claim.Subject == $"project:{project.Id:N}")))
        {
            root.Entities.Add(new JobMlEntity
            {
                Id = $"project:{project.Id:N}",
                Type = "project",
                Name = project.Name,
                Source = "#projects"
            });
        }

        foreach (var education in source.Education.Where(education =>
                     root.Claims.Any(claim => claim.Subject == $"education:{education.Id:N}")))
        {
            root.Entities.Add(new JobMlEntity
            {
                Id = $"education:{education.Id:N}",
                Type = "education",
                Name = education.Institution ?? education.Degree ?? "Education",
                Source = "#education"
            });
        }

        if (root.Claims.Any(claim => claim.Subject == "personal"))
            root.Entities.Add(new JobMlEntity { Id = "personal", Type = "person", Name = source.Personal.FullName ?? "Candidate", Source = "#document:p1" });
        if (root.Claims.Any(claim => claim.Subject == "resume"))
            root.Entities.Add(new JobMlEntity { Id = "resume", Type = "document", Name = source.Personal.FullName ?? "Resume", Source = "#document:p1" });

        root.Requirements.AddRange(job.RequiredSkills.Select(skill => new JobMlRequirement
        {
            Id = $"req-{EvidenceLedgerBuilder.Slug(skill)}",
            Concept = EvidenceLedgerBuilder.Slug(skill),
            Importance = "required"
        }));
        root.Requirements.AddRange(job.PreferredSkills.Select(skill => new JobMlRequirement
        {
            Id = $"req-{EvidenceLedgerBuilder.Slug(skill)}",
            Concept = EvidenceLedgerBuilder.Slug(skill),
            Importance = "preferred"
        }));
        return new JobMlFile(markdown, root);
    }

    private static string? TryReadCompleteLedger(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        return new JobMlParser().TryParse(source, out var file, out _)
            ? file!.Data.Document.CompleteLedger
            : null;
    }

    private static string EvidenceType(EvidenceRecord evidence)
    {
        if (string.Equals(evidence.Kind, "article", StringComparison.OrdinalIgnoreCase)) return "article";
        if (Uri.TryCreate(evidence.ExternalUri, UriKind.Absolute, out var uri) &&
            uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return "repository";
        return string.IsNullOrWhiteSpace(evidence.Kind) ? "external" : evidence.Kind;
    }

    private static string EvidenceTitle(EvidenceRecord evidence)
    {
        var separator = evidence.Text.IndexOf(':');
        return separator > 0 ? evidence.Text[..separator].Trim() : evidence.Kind;
    }

    private static string? PublisherFromUri(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase) : null;

    private static string RemoveExistingJobMl(string markdown)
    {
        if (new JobMlParser().TryParse(markdown, out var parsed, out _)) return parsed!.Markdown;
        return markdown;
    }

    private static string SafeFileName(string value)
    {
        var safe = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "tailored-resume" : safe;
    }
}
