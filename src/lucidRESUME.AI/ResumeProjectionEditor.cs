using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace lucidRESUME.AI;

/// <summary>Edits selected, source-bound prose without changing selection, dates, names or evidence identities.</summary>
public sealed class ResumeProjectionEditor(ResumeCompositionOrchestrator composer, IOptions<TailoringOptions> options)
{
    internal static string BuildJobBrief(JobDescription job)
    {
        static string Short(string text, int maximum = 300)
        {
            var clean = Regex.Replace(text, @"\s+", " ").Trim();
            return clean.Length > maximum ? clean[..maximum] : clean;
        }
        var requirements = job.RequiredSkills.Concat(job.Competencies.Where(c =>
                c.Priority == Core.Models.Coverage.RequirementPriority.Required).Select(c => c.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).Select(text => Short(text));
        var responsibilities = job.Competencies.Where(c => c.Priority != Core.Models.Coverage.RequirementPriority.Preferred)
            .Select(c => c.Name + ": " + c.Description).Concat(job.Responsibilities)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).Select(text => Short(text));
        var preferred = job.PreferredSkills.Take(6).Select(text => Short(text));
        var brief = $"Target: {Short(job.Title ?? "")}\nCompany: {Short(job.Company ?? "")}\nRequired: {string.Join("; ", requirements)}\nResponsibilities: {string.Join("; ", responsibilities)}\nPreferred: {string.Join("; ", preferred)}\nJob context: {Short(job.RawText, 2000)}";
        // Include the complete input identity even when a long brief is bounded;
        // different jobs with identical headings must not share job-specific prose.
        var identity = string.Join('\n', new[] { job.Title ?? "", job.Company ?? "", job.RawText }
            .Concat(job.RequiredSkills).Concat(job.PreferredSkills).Concat(job.Responsibilities)
            .Concat(job.Competencies.Select(c => $"{c.Priority}: {c.Name}: {c.Description}")));
        return Short(brief, 3000) + "\nJob fingerprint: " + MarkdownEvidenceIndex.Fingerprint(identity);
    }

    public async Task EditAsync(ResumeDocument source, ResumeDocument projection, JobDescription job, CancellationToken ct = default, IProgress<string>? progress = null)
    {
        if (!options.Value.ComposeProse || projection.Projection is null) return;
        var markdown = (projection.RawMarkdown ?? "").Replace("\r\n", "\n");
        var index = MarkdownEvidenceIndex.Create(markdown);
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        var claims = ledger.Claims.ToDictionary(claim => claim.Id);
        var requirements = job.RequiredSkills.Concat(job.Responsibilities).Concat(job.PreferredSkills)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select((text, i) => new CompilerRequirement($"requirement-{i}", text,
                job.RequiredSkills.Contains(text) ? RequirementKind.Required : RequirementKind.Preferred, text)).ToList();
        var packets = new List<EvidencePacket>();
        var preferences = projection.GenerationOptions;
        var budget = ResumeGenerationBudget.Resolve(job, preferences);
        var writingInstruction = ResumeWritingPolicy.Describe(preferences.Tone, preferences.Detail);
        foreach (var group in projection.Projection.Blocks.GroupBy(binding => binding.ProseRef))
        {
            if (group.Key.StartsWith("#publications:", StringComparison.Ordinal) || group.Key.StartsWith("#related-projects:", StringComparison.Ordinal)) continue;
            if (!index.TryGet(group.Key, out var passage)) continue;
            var selected = group.Where(binding => claims.ContainsKey(binding.ClaimId)).Select(binding => claims[binding.ClaimId])
                .Where(claim => claim.Kind is "achievement" or "summary" or "project" || group.Key.StartsWith("#summary:")).Select(claim => new SelectedClaim(
                    new JobMlClaim
                    {
                        Id = claim.Id,
                        Subject = claim.SubjectId ?? "career",
                        Statement = claim.Statement,
                        Concepts = new JobMlClaimConcepts
                        {
                            Skills = [.. claim.Concepts]
                        }
                    },
                    passage.Heading ?? "", passage.Text.TrimStart('-', ' '), claim.EvidenceIds, 1, [])).ToList();
            if (selected.Count == 0) continue;
            var words = Regex.Matches(passage.Text, @"\S+").Count;
            packets.Add(new(group.Key, passage.Heading ?? "", writingInstruction,
                group.Key.StartsWith("#summary:") ? budget.SummaryWords : Math.Max((int)Math.Ceiling(words * (preferences.Detail == ResumeDetail.Thorough ? 1.4 : 1.15)), 40), selected, requirements.Select(requirement => requirement.Id).ToList(),
                group.Key.StartsWith("#summary:") ? "summary" : "experience")
            {
                Accounts = source.ProseIndex.Segments.Where(segment => selected.Any(claim => segment.ClaimIds.Contains(claim.Claim.Id)))
                    .SelectMany(segment => segment.Accounts).DistinctBy(account => account.InterviewId).ToList()
            });
        }
        if (packets.Count == 0) return;
        var manifest = new ProjectionManifest(ledger.SourceRevision, MarkdownEvidenceIndex.Fingerprint(job.RawText),
            DateTimeOffset.UtcNow, requirements, packets, [], [], "source-bound")
        { TargetTitle = job.Title };
        var result = await composer.ComposeAsync(manifest, BuildJobBrief(job) + $"\nLength: {preferences.Length}; Detail: {preferences.Detail}; Tone: {preferences.Tone}", new CompilationOptions
        { ComposeProse = true, CompositionProvider = options.Value.Provider }, ct, progress);
        ct.ThrowIfCancellationRequested();
        projection.GenerationWarnings.AddRange(result.Warnings);
        var replacements = new List<(ProsePassage Passage, string Text)>();
        foreach (var block in result.Blocks)
        {
            if (!index.TryGet(block.SectionId, out var passage)) continue;
            // Keep one paragraph per binding, so evidence references cannot drift.
            var text = Regex.Replace(block.Text.Trim(), @"\s+", " ").TrimStart('-', ' ');
            text = Regex.Replace(text, @"\basp\.net\b", "ASP.NET", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(text, @"(^|\s)(#{1,6}\s|```|\{#[^}]+\})") || text.Contains("<p", StringComparison.OrdinalIgnoreCase))
            {
                projection.GenerationWarnings.Add($"Retained source prose for {block.SectionId}: edit introduced document structure.");
                continue;
            }
            var raw = markdown.Substring(passage.SourceStart, passage.SourceLength);
            replacements.Add((passage, raw.TrimStart().StartsWith("- ") ? "- " + text : text));
            if (block.SectionId.StartsWith("#summary:")) projection.Personal.Summary = text;
            var subjects = projection.Projection.Blocks.Where(binding => binding.ProseRef == block.SectionId)
                .Select(binding => claims[binding.ClaimId].SubjectId).ToHashSet(StringComparer.Ordinal);
            foreach (var role in projection.Experience.Where(role => subjects.Contains($"experience:{role.Id:N}")))
                for (var i = 0; i < role.Achievements.Count; i++)
                    if (MarkdownEvidenceIndex.NormalizeText(role.Achievements[i]).TrimEnd('.') == passage.Text.TrimStart('-', ' ').TrimEnd('.'))
                    { role.Achievements[i] = text; break; }
            foreach (var project in projection.Projects.Where(project => subjects.Contains($"project:{project.Id:N}")))
                if (MarkdownEvidenceIndex.NormalizeText(project.Description ?? "").TrimEnd('.') == passage.Text.TrimEnd('.')) project.Description = text;
        }
        foreach (var replacement in replacements.OrderByDescending(item => item.Passage.SourceStart))
            markdown = markdown.Remove(replacement.Passage.SourceStart, replacement.Passage.SourceLength)
                .Insert(replacement.Passage.SourceStart, replacement.Text);
        var editedIndex = MarkdownEvidenceIndex.Create(markdown);
        if (projection.Projection.Blocks.Any(binding => !editedIndex.TryGet(binding.ProseRef, out _)))
            throw new InvalidDataException("Edited prose lost a source binding.");
        projection.SetDoclingOutput(markdown, null, markdown);
    }
}
