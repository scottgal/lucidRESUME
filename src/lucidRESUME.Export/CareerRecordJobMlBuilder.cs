using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
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

    /// <summary>Add literal source-backed links to an existing portable graph without rewriting authored prose or review decisions.</summary>
    public static bool RefreshLiteralConceptLinks(ResumeDocument transcript)
    {
        var parser = new JobMlParser();
        if (string.IsNullOrWhiteSpace(transcript.JobMlSource) || !parser.TryParse(transcript.JobMlSource, out var file, out _)) return false;
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(transcript);
        var claims = ledger.Claims.Where(claim => claim.Kind == "achievement" && claim.Review == "accepted")
            .ToDictionary(claim => StableId("claim", claim.Id));
        var changed = false;
        foreach (var claim in file!.Data.Claims.Where(claim => claim.Review == "accepted"))
        {
            if (!claims.TryGetValue(claim.Id, out var source) || source.Statement != claim.Statement) continue;
            foreach (var name in source.Concepts.Where(name => EvidenceTerms.Mentions(source.Statement, name)))
            {
                var concept = file.Data.Concepts.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (concept is null)
                {
                    concept = new JobMlConcept { Id = StableId("concept", name), Type = "skill", Name = name };
                    if (file.Data.Concepts.Any(item => item.Id == concept.Id)) continue;
                    file.Data.Concepts.Add(concept);
                }
                claim.Concepts ??= new();
                if (claim.Concepts.Skills.Contains(concept.Id)) continue;
                claim.Concepts.Skills.Add(concept.Id);
                changed = true;
            }
        }
        if (!changed || JobMlProcessor.Validate(file).Any(item => item.Severity == JobMlDiagnosticSeverity.Error)) return false;
        transcript.JobMlSource = parser.Serialize(file);
        transcript.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(transcript.JobMlSource);
        return true;
    }

    /// <summary>Refresh derived metadata without rebuilding claims, embeddings or authored Markdown.</summary>
    public static void RefreshDerivedExtensions(ResumeDocument transcript)
    {
        var parser = new JobMlParser();
        if (string.IsNullOrWhiteSpace(transcript.JobMlSource) || !parser.TryParse(transcript.JobMlSource, out var file, out _)) return;
        file!.Data.Extensions ??= new();
        foreach (var extension in BuildEvidenceExtensions(transcript)!) file.Data.Extensions[extension.Key] = extension.Value;
        AddProseBindings(file.Data, transcript.ProseIndex);
        transcript.JobMlSource = parser.Serialize(file);
        transcript.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(transcript.JobMlSource);
    }

    public async Task<JobMlFile> BuildAsync(ResumeDocument transcript,
        CancellationToken cancellationToken = default, CareerSemanticIndex? semanticIndex = null)
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
        var entityIds = subjects.ToDictionary(subject => subject, StableEntityId,
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
        var descriptor = embeddings as IEmbeddingSpaceDescriptor;
        var reuseIndex = descriptor is not null && !string.IsNullOrWhiteSpace(descriptor.ModelDigest) && semanticIndex is not null &&
            semanticIndex.Model == descriptor.ModelId && semanticIndex.ModelDigest == descriptor.ModelDigest &&
            semanticIndex.Normalization == descriptor.Normalization;
        foreach (var concept in conceptNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            conceptVectors[concept] = reuseIndex && semanticIndex!.Concepts.TryGetValue(concept, out var cached)
                ? cached.ToArray() : await embeddings.EmbedAsync(concept, cancellationToken);
        }

        var dimensions = conceptVectors.Values.Select(vector => vector.Length).FirstOrDefault(length => length > 0);
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
            Extensions = BuildEvidenceExtensions(transcript),
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
            Source = $"#{entityIds[subject]}",
            Projection = FindExperience(subject, transcript)?.IsCareerAnchor == true
                ? new JobMlProjectionPreference
                {
                    Include = "always",
                    Reason = "Career anchor selected by the author."
                }
                : null
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
                var vectors = seeds.Select(seed => conceptVectors[seed]).Where(vector => vector.Length == dimensions).ToList();
                if (vectors.Count == 0) continue;
                var vector = new float[dimensions];
                foreach (var seedVector in vectors)
                    for (var index = 0; index < dimensions; index++) vector[index] += seedVector[index];
                var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
                if (magnitude > 1e-8f)
                    for (var index = 0; index < dimensions; index++) vector[index] /= magnitude;
                root.RoleCentroids.Add(new JobMlRoleCentroid
                {
                    Id = StableId("role", role),
                    Name = role,
                    Space = spaceId,
                    DerivedFrom = seeds.Select(seed => conceptIds[seed]).ToList(),
                    Vector = [.. vector]
                });
            }

        AddProseBindings(root, transcript.ProseIndex);
        return new JobMlFile(markdown, root);
    }

    private static void AddProseBindings(JobMlRoot root, CareerProseIndex index)
    {
        var byEvidence = root.Claims.Where(claim => claim.Review == "accepted")
            .SelectMany(claim => claim.Evidence.Where(evidence => evidence.Id is not null).Select(evidence => (evidence.Id!, Claim: claim)))
            .GroupBy(item => item.Item1).ToDictionary(group => group.Key, group => group.Select(item => item.Claim).ToList());
        var bindings = index.Segments.Select(segment =>
        {
            var claims = segment.EvidenceIds.Where(byEvidence.ContainsKey).SelectMany(id => byEvidence[id])
                .Where(claim => claim.Statement.Equals(segment.Text, StringComparison.OrdinalIgnoreCase)).DistinctBy(claim => claim.Id).ToList();
            return new Dictionary<string, object?>
            {
                ["segment_id"] = segment.Id,
                ["ledger_claim_ids"] = segment.ClaimIds,
                ["jobml_claim_ids"] = claims.Select(claim => claim.Id).ToList(),
                ["jobml_subject_ids"] = claims.Select(claim => claim.Subject).Distinct().ToList(),
                ["jobml_skill_concept_ids"] = claims.SelectMany(claim => claim.Concepts.Skills).Distinct().ToList()
            };
        }).ToList();
        if (root.Extensions?.GetValueOrDefault("lucidresume.transcript") is Dictionary<string, object?> transcript)
            transcript["prose_bindings"] = bindings;
    }

    private static Dictionary<string, object?>? BuildEvidenceExtensions(ResumeDocument transcript)
    {
        var repositories = transcript.Projects
            .Where(project => project.EvidenceMetadata.GetValueOrDefault("provider") == "github")
            .Select(project => ProjectObservation(project)).ToList();
        var packages = transcript.Projects
            .Where(project => project.EvidenceMetadata.GetValueOrDefault("provider") == "nuget")
            .Select(project => ProjectObservation(project)).ToList();
        var extensions = new Dictionary<string, object?>
        {
            ["lucidresume.publications"] = new Dictionary<string, object?>
            {
                ["version"] = "0.1",
                ["articles"] = transcript.Publications.Select(ProjectObservation).ToList()
            },
            ["lucidresume.transcript"] = new Dictionary<string, object?>
            {
                ["version"] = "0.1",
                ["retained_source_narrative"] = transcript.RawMarkdown,
                ["narrative_policy"] = "Retained source material is not automatically an accepted claim or skill.",
                ["prose_index"] = transcript.ProseIndex,
                ["role_interviews"] = transcript.Experience.Where(role => role.InterviewEntries.Count > 0)
                    .Select(role => new Dictionary<string, object?>
                    {
                        ["role_id"] = role.Id.ToString("N"),
                        ["entries"] = role.InterviewEntries.Select(entry => new Dictionary<string, object?>
                        {
                            ["id"] = entry.Id.ToString("N"),
                            ["topic"] = entry.TopicKey,
                            ["requirement"] = entry.Requirement,
                            ["context"] = entry.Context,
                            ["action"] = entry.Action,
                            ["outcome"] = entry.Outcome,
                            ["reviewed_prose"] = entry.ReviewedProse,
                            ["contribution_account"] = entry.Account,
                            ["skills"] = entry.Skills,
                            ["accepted_at"] = entry.AcceptedAt.ToString("O")
                        }).ToList()
                    }).ToList()
            }
        };
        if (repositories.Count > 0)
            extensions["lucidresume.github"] = new Dictionary<string, object?>
            {
                ["version"] = "0.1",
                ["repositories"] = repositories
            };
        if (packages.Count > 0)
            extensions["lucidresume.packages"] = new Dictionary<string, object?>
            {
                ["version"] = "0.1",
                ["families"] = packages
            };
        return extensions;
    }

    private static Dictionary<string, object?> ProjectObservation(Project project)
    {
        var provider = project.EvidenceMetadata.GetValueOrDefault("provider") ?? "external";
        var identity = project.EvidenceMetadata.GetValueOrDefault("family_key") ?? project.Url ?? project.Name;
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = StableEntityId($"{provider}:{identity}"),
            ["name"] = project.Name,
            ["uri"] = project.Url,
            ["technologies"] = project.Technologies,
            ["passages"] = project.EvidencePassages
        };
        foreach (var (key, value) in project.EvidenceMetadata)
            result[key] = ObservationValue(key, value);
        return result;
    }

    private static object ObservationValue(string key, string value)
    {
        if (BooleanObservationKeys.Contains(key) && bool.TryParse(value, out var boolean)) return boolean;
        if (IntegerObservationKeys.Contains(key) && long.TryParse(value, out var integer)) return integer;
        if (NumericObservationKeys.Contains(key) &&
            double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number)) return number;
        return value;
    }

    private static readonly HashSet<string> BooleanObservationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "fork", "archived", "tree_truncated", "tree_observed", "has_tests", "has_ci",
        "has_release_automation", "has_package_manifests", "has_documentation",
        "has_browser_extension", "eligible_for_personal_evidence", "human_prose_preserved"
    };

    private static readonly HashSet<string> IntegerObservationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_count", "stars", "forks", "package_count", "total_downloads"
    };

    private static readonly HashSet<string> NumericObservationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "repository_age_years", "active_span_years", "evidence_strength", "originality",
        "longevity", "engineering_process", "documentation", "delivery"
    };

    private static string RenderTranscript(ResumeDocument transcript, IReadOnlyList<LedgerClaim> claims,
        IReadOnlyDictionary<string, EvidenceRecord> evidenceById,
        IReadOnlyDictionary<string, string> entityIds, IReadOnlyDictionary<string, string> claimIds)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(transcript.Personal.FullName ?? "Complete Career Transcript").AppendLine();
        var displayedPhone = string.IsNullOrWhiteSpace(transcript.Personal.Phone)
            ? null
            : string.IsNullOrWhiteSpace(transcript.Personal.ContactPreference)
                ? transcript.Personal.Phone
                : $"{transcript.Personal.Phone} - {transcript.Personal.ContactPreference} -";
        var contact = new[] { transcript.Personal.Email, displayedPhone, transcript.Personal.Location,
                transcript.Personal.LinkedInUrl, transcript.Personal.GitHubUrl, transcript.Personal.WebsiteUrl }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        builder.AppendLine(string.Join(" · ", contact));

        var groups = claims.GroupBy(claim => claim.SubjectId ?? "career-record", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key == "personal" ? 0 : group.Key.StartsWith("experience:") ? 1
                : group.Key.StartsWith("project:") ? 2 : group.Key.StartsWith("publication:") ? 3
                : group.Key.StartsWith("education:") ? 4 : 5)
            .ThenByDescending(group => FindExperience(group.Key, transcript)?.StartDate);
        foreach (var group in groups)
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
        // A source's display label can change after author edits. Its stable identity
        // must still produce one catalogue entry and one lookup key.
        var result = ledger.Evidence.GroupBy(record => record.SourceResumeId)
            .Select(group => ((Guid?)group.Key, new JobMlSource
            {
                Id = StableId("source", group.Key.ToString("N")),
                Type = group.All(record => record.ExtractionMethod == "author-interview") ? "interview" : "resume",
                Name = string.Join(" / ", group.Select(record => record.SourceName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                Summary = group.All(record => record.ExtractionMethod == "author-interview")
                    ? "Author-reviewed role interview. Candidate-declared evidence, not independently verified."
                    : $"Imported career source containing {group.Count()} retained evidence passages.",
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
        var project = FindProject(subject, transcript);
        if (project is not null && !string.IsNullOrWhiteSpace(project.Name))
            return SingleLine(project.Name);
        var role = claims.FirstOrDefault(claim =>
            string.Equals(claim.SubjectId, subject, StringComparison.OrdinalIgnoreCase) && claim.Kind == "experience");
        if (role is not null)
            return SingleLine(role.Statement.Split('|').Take(2)
                .Aggregate((left, right) => $"{left.Trim()} · {right.Trim()}"));
        var first = claims.FirstOrDefault(claim => string.Equals(claim.SubjectId, subject, StringComparison.OrdinalIgnoreCase));
        if (first is null) return subject;
        return SingleLine(first.EvidenceIds.Select(id => evidence.GetValueOrDefault(id)?.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? transcript.Personal.FullName ?? subject);
    }

    private static string EntityType(string subject) => subject.Split(':', 2)[0] switch
    {
        "experience" => "experience",
        "project" => "project",
        "publication" => "project",
        "education" => "education",
        _ => "person"
    };

    private static WorkExperience? FindExperience(string subject, ResumeDocument transcript)
    {
        if (!subject.StartsWith("experience:", StringComparison.OrdinalIgnoreCase)) return null;
        return Guid.TryParse(subject["experience:".Length..], out var id)
            ? transcript.Experience.FirstOrDefault(experience => experience.Id == id)
            : null;
    }

    private static Project? FindProject(string subject, ResumeDocument transcript)
    {
        if (subject.StartsWith("publication:", StringComparison.OrdinalIgnoreCase))
            return Guid.TryParse(subject["publication:".Length..], out var publicationId)
                ? transcript.Publications.FirstOrDefault(item => item.Id == publicationId) : null;
        if (!subject.StartsWith("project:", StringComparison.OrdinalIgnoreCase)) return null;
        return Guid.TryParse(subject["project:".Length..], out var id)
            ? transcript.Projects.FirstOrDefault(project => project.Id == id)
            : null;
    }

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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

    internal static string StableEntityId(string subject) => StableId("entity", subject);

    private static string StableId(string prefix, string value) =>
        $"{prefix}-{EvidenceLedgerBuilder.Slug(value)}-{EvidenceLedgerBuilder.FastHash(value)[^8..]}";
}
