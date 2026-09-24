using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;
using lucidRESUME.Matching;

namespace lucidRESUME.Export;

/// <summary>
/// Exports the canonical career transcript as the JobML career_record profile.
/// This is a deterministic projection of already-ingested data. It performs no claim inference.
/// </summary>
public sealed class CareerRecordJobMlBuilder(
    SkillLedgerBuilder skillLedgerBuilder,
    SkillTaxonomyService taxonomy,
    IEmbeddingService embeddings)
{
    private static readonly string[] DefaultRoleCentroids =
        ["Lead Developer", "Head of Engineering", "CTO", "VP of Engineering"];

    public async Task<JobMlFile> BuildAsync(ResumeDocument transcript,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(transcript);
        var skillLedger = await skillLedgerBuilder.BuildAsync(transcript, cancellationToken);
        var claims = ledger.Claims
            .Where(claim => claim.EvidenceIds.Count > 0)
            .OrderBy(claim => claim.SubjectId)
            .ThenBy(claim => claim.Id)
            .ToList();
        var evidenceById = ledger.Evidence.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var sources = BuildSources(ledger);
        var sourceIdByResume = sources
            .Where(source => source.ResumeId.HasValue)
            .ToDictionary(source => source.ResumeId!.Value, source => source.Value.Id);
        var sourceIdByUri = sources
            .Where(source => Uri.TryCreate(source.Value.Uri, UriKind.Absolute, out _))
            .ToDictionary(source => source.Value.Uri!, source => source.Value.Id, StringComparer.OrdinalIgnoreCase);

        var subjects = claims.Select(claim => claim.SubjectId ?? "career-record")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entityIds = subjects.ToDictionary(subject => subject, subject => StableId("entity", subject),
            StringComparer.OrdinalIgnoreCase);
        var claimIds = claims.ToDictionary(claim => claim.Id, claim => StableId("claim", claim.Id),
            StringComparer.OrdinalIgnoreCase);

        var markdown = RenderTranscript(transcript, claims, evidenceById, entityIds, claimIds);
        var conceptNames = claims.SelectMany(claim => claim.Concepts)
            .Concat(skillLedger.Entries.Select(entry => entry.SkillName))
            .Concat(DefaultRoleCentroids.SelectMany(taxonomy.GetRoleSkills))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var conceptIds = conceptNames.ToDictionary(name => name, name => StableId("concept", name),
            StringComparer.OrdinalIgnoreCase);
        var conceptVectors = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var concept in conceptNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            conceptVectors[concept] = await embeddings.EmbedAsync(concept, cancellationToken);
        }

        var dimensions = conceptVectors.Values.Select(vector => vector.Length).FirstOrDefault(length => length > 0);
        var descriptor = embeddings as IEmbeddingSpaceDescriptor;
        var spaceId = StableId("space", descriptor?.ModelId ?? embeddings.GetType().Name);
        var root = new JobMlRoot
        {
            Header = new JobMlHeader
            {
                Profile = "career_record",
                Purpose = "Portable, full-resolution projection of a reviewed career transcript, its claims, sources, and optional derived semantic index."
            },
            Document = new JobMlDocumentMetadata
            {
                Id = StableId("career", transcript.Personal.FullName ?? transcript.ResumeId.ToString("N")),
                Language = "en-GB"
            },
            Sources = sources.Select(source => source.Value).ToList(),
            SemanticSpaces = dimensions == 0
                ? []
                :
                [
                    new JobMlSemanticSpace
                    {
                        Id = spaceId,
                        Model = descriptor?.ModelId ?? embeddings.GetType().FullName ?? "configured-embedding",
                        Dimensions = dimensions,
                        Normalization = descriptor?.Normalization ?? "l2",
                        ModelDigest = descriptor?.ModelDigest
                    }
                ]
        };

        root.Entities.AddRange(subjects.Select(subject => new JobMlEntity
        {
            Id = entityIds[subject],
            Type = EntityType(subject),
            Name = SubjectName(subject, claims, evidenceById, transcript),
            Source = $"#{entityIds[subject]}"
        }));
        root.Concepts.AddRange(conceptNames.Select(name => new JobMlConcept
        {
            Id = conceptIds[name],
            Type = "skill",
            Name = name,
            Embedding = dimensions > 0 && conceptVectors[name].Length == dimensions
                ? new JobMlEmbedding { Space = spaceId, Vector = [.. conceptVectors[name]] }
                : null
        }));

        foreach (var ledgerClaim in claims)
        {
            var proseId = claimIds[ledgerClaim.Id];
            var prose = MarkdownEvidenceIndex.NormalizeText(ledgerClaim.Statement);
            var evidence = new List<JobMlEvidence>
            {
                new()
                {
                    Id = $"{proseId}-prose",
                    Type = "prose",
                    Ref = $"#{proseId}",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(prose) },
                    Selector = new JobMlTextSelector { Exact = prose }
                }
            };
            foreach (var evidenceId in ledgerClaim.EvidenceIds)
            {
                if (!evidenceById.TryGetValue(evidenceId, out var record)) continue;
                string? sourceId;
                if (!string.IsNullOrWhiteSpace(record.ExternalUri))
                    sourceIdByUri.TryGetValue(record.ExternalUri, out sourceId);
                else
                    sourceIdByResume.TryGetValue(record.SourceResumeId, out sourceId);
                evidence.Add(new JobMlEvidence
                {
                    Id = record.Id,
                    Type = EvidenceType(record),
                    Ref = $"ledger://{record.Id}",
                    Uri = record.ExternalUri,
                    SourceId = sourceId,
                    Title = record.Title ?? record.SourceName,
                    Authors = [.. record.Authors],
                    Publisher = record.Publisher,
                    Published = record.PublishedOn?.ToString("yyyy-MM-dd"),
                    Accessed = record.AccessedOn?.ToString("yyyy-MM-dd"),
                    Fingerprint = new JobMlFingerprint { Text = record.FastHash },
                    Selector = new JobMlTextSelector { Exact = record.Text }
                });
            }
            root.Claims.Add(new JobMlClaim
            {
                Id = claimIds[ledgerClaim.Id],
                Subject = entityIds[ledgerClaim.SubjectId ?? "career-record"],
                Type = ClaimType(ledgerClaim, evidenceById),
                Statement = ledgerClaim.Statement,
                Origin = ledgerClaim.Origin,
                Review = ledgerClaim.Review,
                Concepts = new JobMlClaimConcepts
                {
                    Skills = ledgerClaim.Concepts.Where(conceptIds.ContainsKey).Select(name => conceptIds[name]).ToList()
                },
                Evidence = evidence
            });
        }

        if (dimensions > 0)
            foreach (var role in DefaultRoleCentroids)
            {
                var seeds = taxonomy.GetRoleSkills(role).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
                var vector = await taxonomy.GetRoleCentroidAsync(role, cancellationToken);
                if (vector.Length != dimensions) continue;
                root.RoleCentroids.Add(new JobMlRoleCentroid
                {
                    Id = StableId("role", role),
                    Name = role,
                    Space = spaceId,
                    DerivedFrom = seeds.Select(seed => conceptIds[seed]).ToList(),
                    Vector = [.. vector]
                });
            }

        return new JobMlFile(markdown, root);
    }

    private static string RenderTranscript(ResumeDocument transcript, IReadOnlyList<LedgerClaim> claims,
        IReadOnlyDictionary<string, EvidenceRecord> evidenceById,
        IReadOnlyDictionary<string, string> entityIds, IReadOnlyDictionary<string, string> claimIds)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(transcript.Personal.FullName ?? "Complete Career Transcript").AppendLine();
        var contact = new[] { transcript.Personal.Email, transcript.Personal.Phone, transcript.Personal.Location,
                transcript.Personal.LinkedInUrl, transcript.Personal.GitHubUrl, transcript.Personal.WebsiteUrl }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        builder.AppendLine(string.Join(" · ", contact));

        foreach (var group in claims.GroupBy(claim => claim.SubjectId ?? "career-record", StringComparer.OrdinalIgnoreCase))
        {
            var subject = group.Key;
            builder.AppendLine().Append("## ")
                .Append(SubjectName(subject, claims, evidenceById, transcript))
                .Append(" {#").Append(entityIds[subject]).AppendLine("}").AppendLine();
            foreach (var claim in group)
                builder.Append("<p id=\"").Append(claimIds[claim.Id]).AppendLine("\">")
                    .AppendLine(claim.Statement.Trim()).AppendLine("</p>").AppendLine();
        }
        return builder.ToString().TrimEnd();
    }

    private static List<(Guid? ResumeId, JobMlSource Value)> BuildSources(EvidenceLedger ledger)
    {
        var result = ledger.Evidence.GroupBy(record => (record.SourceResumeId, record.SourceName))
            .Select(group => ((Guid?)group.Key.SourceResumeId, new JobMlSource
            {
                Id = StableId("source", group.Key.SourceResumeId.ToString("N")),
                Type = "resume",
                Name = group.Key.SourceName,
                Summary = $"Imported career source containing {group.Count()} retained evidence passages.",
                Fingerprint = EvidenceLedgerBuilder.FastHash(string.Join('\n', group.Select(record => record.FastHash)))
            })).ToList();
        result.AddRange(ledger.Evidence.Where(record => Uri.TryCreate(record.ExternalUri, UriKind.Absolute, out _))
            .GroupBy(record => record.ExternalUri!, StringComparer.OrdinalIgnoreCase)
            .Select(group => ((Guid?)null, new JobMlSource
            {
                Id = StableId("source", group.Key),
                Type = group.Key.Contains("github.com", StringComparison.OrdinalIgnoreCase) ? "repository" : "external",
                Name = group.First().Title ?? group.Key,
                Uri = group.Key,
                Summary = group.OrderByDescending(record => record.Text.Length).First().Text,
                Fingerprint = EvidenceLedgerBuilder.FastHash(string.Join('\n', group.Select(record => record.FastHash)))
            })));
        return result;
    }

    private static string SubjectName(string subject, IReadOnlyList<LedgerClaim> claims,
        IReadOnlyDictionary<string, EvidenceRecord> evidence, ResumeDocument transcript)
    {
        if (subject == "career-record") return "Career Details";
        if (subject.Equals("personal", StringComparison.OrdinalIgnoreCase)) return "Professional Summary";
        var role = claims.FirstOrDefault(claim =>
            string.Equals(claim.SubjectId, subject, StringComparison.OrdinalIgnoreCase) && claim.Kind == "experience");
        if (role is not null) return role.Statement.Split('|').Take(2).Aggregate((left, right) => $"{left.Trim()} · {right.Trim()}");
        var first = claims.FirstOrDefault(claim => string.Equals(claim.SubjectId, subject, StringComparison.OrdinalIgnoreCase));
        if (first is null) return subject;
        return first.EvidenceIds.Select(id => evidence.GetValueOrDefault(id)?.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? transcript.Personal.FullName ?? subject;
    }

    private static string EntityType(string subject) => subject.Split(':', 2)[0] switch
    {
        "experience" => "experience",
        "project" => "project",
        "education" => "education",
        _ => "person"
    };

    private static string EvidenceType(EvidenceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ExternalUri))
            return record.ExternalUri.Contains("github.com", StringComparison.OrdinalIgnoreCase) ? "repository" : record.Kind;
        return "source_ledger";
    }

    private static string ClaimType(LedgerClaim claim, IReadOnlyDictionary<string, EvidenceRecord> evidence)
    {
        if (claim.Kind == "personal" && claim.EvidenceIds.Any(id =>
                evidence.TryGetValue(id, out var record) && record.Locator == "personal:summary"))
            return "summary";
        return claim.Kind;
    }

    private static string StableId(string prefix, string value) =>
        $"{prefix}-{EvidenceLedgerBuilder.Slug(value)}-{EvidenceLedgerBuilder.FastHash(value)[^8..]}";
}
