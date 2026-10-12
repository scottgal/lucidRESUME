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
        jobMl.Data.Extensions ??= new();
        jobMl.Data.Extensions["lucidresume.related_work"] = new Dictionary<string, object?>
        {
            ["version"] = "0.1",
            ["links"] = projection.RelatedWork
        };
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
        result.GenerationOptions = projection.GenerationOptions;
        result.MinimumOutputPages = projection.MinimumOutputPages;
        result.TargetRole = job.Title;
        result.MarkTailoredFor(job.JobId);

        result.Personal = projection.Personal;
        result.Experience = projection.Experience;
        result.Education = projection.Education;
        result.Skills = projection.Skills;
        result.Projects = projection.Projects;
        result.Publications = projection.Publications;
        result.RelatedWork = projection.RelatedWork;
        var selectedClaims = projectionInfo.Blocks.Select(block => block.ClaimId).ToHashSet(StringComparer.Ordinal);
        result.ProseIndex = new CareerProseIndex
        {
            SourceRevision = source.ProseIndex.SourceRevision,
            AcceptedClaimsRevision = source.ProseIndex.AcceptedClaimsRevision,
            EmbeddingSpace = source.ProseIndex.EmbeddingSpace,
            PolicyVersion = source.ProseIndex.PolicyVersion,
            AnalysedAt = source.ProseIndex.AnalysedAt,
            Segments = source.ProseIndex.Segments.Where(segment => segment.ClaimIds.Any(selectedClaims.Contains)).ToList()
        };
        result.Certifications = projection.Certifications;
        result.EvidenceLedger = ledger;
        result.Projection = projectionInfo;
        result.GenerationWarnings.AddRange(projection.GenerationWarnings);

        foreach (var entity in source.Entities) result.AddEntity(entity);
        return result;
    }

    private static JobMlFile BuildProjectedJobMl(string markdown, ResumeDocument source,
        ResumeProjectionInfo projection, EvidenceLedger ledger, JobDescription job)
    {
        var index = MarkdownEvidenceIndex.Create(markdown);
        var fullJobMl = Uri.TryCreate(source.CompleteJobMlUri, UriKind.Absolute, out var configuredFullJobMl)
            ? configuredFullJobMl
            : Uri.TryCreate(TryReadFullJobMl(source.JobMlSource), UriKind.Absolute, out var embeddedFullJobMl)
                ? embeddedFullJobMl
                : null;
        var root = new JobMlRoot
        {
            Document = new JobMlDocumentMetadata
            {
                Id = EvidenceLedgerBuilder.Slug(source.Personal.FullName ?? "resume"),
                Language = "en-GB",
                FullJobMl = fullJobMl?.ToString()
            },
            Job = new JobMlJob { Id = EvidenceLedgerBuilder.Slug(job.Title ?? "target-role") }
        };

        var claimsById = ledger.Claims.ToDictionary(claim => claim.Id, StringComparer.OrdinalIgnoreCase);
        var evidenceById = ledger.Evidence.ToDictionary(evidence => evidence.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var binding in projection.Blocks)
        {
            if (!claimsById.TryGetValue(binding.ClaimId, out var ledgerClaim))
                throw new InvalidOperationException($"Projection references missing ledger claim '{binding.ClaimId}'.");
            if (ledgerClaim.Review != "accepted")
                throw new InvalidOperationException($"Projection references unaccepted claim '{binding.ClaimId}'. Re-project after reviewing the career record.");
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
                        Type = "career_transcript",
                        Ref = $"ledger://{evidence.Id}",
                        // The compact document points to the complete prose section. Exact
                        // passage identity, locator and drift hash remain in full JobML.
                        Uri = FullTranscriptSectionUri(fullJobMl, ledgerClaim.SubjectId),
                        Title = $"Complete transcript: {TranscriptSectionTitle(ledgerClaim.SubjectId, source)}",
                        Fingerprint = new JobMlFingerprint { Text = evidence.FastHash },
                        Selector = new JobMlTextSelector { Exact = evidence.Text }
                    });
            }

            var existing = root.Claims.FirstOrDefault(item => item.Id == claim.Id);
            if (existing is null) root.Claims.Add(claim);
            else existing.Evidence.Add(claim.Evidence[0]); // The same source claim can support multiple passages.
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
                Source = $"#experience-{experience.Id:N}",
                Projection = experience.IsCareerAnchor
                    ? new JobMlProjectionPreference
                    {
                        Include = "always",
                        Reason = "Career anchor selected by the author."
                    }
                    : null
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

    private static string? TryReadFullJobMl(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        return new JobMlParser().TryParse(source, out var file, out _)
            ? file!.Data.Document.EffectiveFullJobMl
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

    private static string? FullTranscriptSectionUri(Uri? fullJobMl, string? subject)
    {
        if (fullJobMl is null || string.IsNullOrWhiteSpace(subject)) return null;
        var builder = new UriBuilder(fullJobMl)
        {
            Fragment = CareerRecordJobMlBuilder.StableEntityId(subject)
        };
        return builder.Uri.ToString();
    }

    private static string TranscriptSectionTitle(string? subject, ResumeDocument source)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject == "career-record" || subject == "resume")
            return "Career details";
        if (subject.Equals("personal", StringComparison.OrdinalIgnoreCase))
            return "Professional summary";
        var separator = subject.IndexOf(':');
        if (separator < 0 || !Guid.TryParse(subject[(separator + 1)..], out var id)) return subject;
        return subject[..separator] switch
        {
            "experience" => source.Experience.FirstOrDefault(item => item.Id == id) is { } experience
                ? $"{experience.Title} · {experience.Company}".Trim(' ', '·')
                : "Experience",
            "project" => source.Projects.FirstOrDefault(item => item.Id == id)?.Name ?? "Project",
            "education" => source.Education.FirstOrDefault(item => item.Id == id) is { } education
                ? string.Join(" · ", new[] { education.Degree, education.Institution }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                : "Education",
            _ => "Career details"
        };
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
