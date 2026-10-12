using System.Globalization;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>Passage retrieval only. Scores never establish skill proficiency or employment.</summary>
public static class RelatedWorkSelector
{
    private static readonly HashSet<string> Stop = new("the and for with from that this your have will build built design own lead experience required strong work working platform system systems engineering production project development skills responsibilities".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> Terms(string text) => Regex.Matches(text.ToLowerInvariant(), @"[a-z][a-z0-9+#.-]*")
        .Select(match => match.Value.TrimEnd('.')).Where(word => word.Length >= 2 && !Stop.Contains(word)).ToHashSet();

    public static IReadOnlyList<RelatedWorkLink> Select(ResumeDocument source, JobDescription job,
        IEnumerable<Guid> excludedProjects, int maximum)
    {
        if (maximum <= 0) return [];
        var ledger = EvidenceLedgerBuilder.EnsureCurrent(source);
        var excluded = excludedProjects.ToHashSet();
        var requirements = job.Competencies.Select(item => item.Name + ": " + item.Description)
            .Concat(job.RequiredSkills).Concat(job.Responsibilities).Concat(job.PreferredSkills)
            .Where(text => !string.IsNullOrWhiteSpace(text)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requirements.Count == 0) requirements = Regex.Split(job.RawText, @"[\r\n]+|(?<=[.!?])\s+").Where(text => Terms(text).Count > 0).ToList();
        var candidates = new List<(RelatedWorkLink Link, int Coverage, int Specificity, double Attribution, DateOnly? Date)>();
        foreach (var (project, kind) in source.Projects.Select(project => (project, "project"))
                     .Concat(source.Publications.Select(project => (project, "publication"))))
        {
            if (excluded.Contains(project.Id) || !Uri.TryCreate(project.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            if (project.EvidenceMetadata.GetValueOrDefault("eligible_for_personal_evidence") == "false") continue;
            if (project.EvidenceMetadata.GetValueOrDefault("provider") == "blog" && project.EvidenceMetadata.GetValueOrDefault("review") != "accepted") continue;
            var subject = $"{kind}:{project.Id:N}";
            var support = ledger.Claims.Where(claim => claim.SubjectId == subject && claim.Review == "accepted" &&
                (claim.Kind == "project" || claim.Kind == "publication")).ToList();
            var best = support.Select(claim =>
            {
                var words = Terms(claim.Statement);
                var matched = requirements.Where(requirement =>
                {
                    var terms = Terms(requirement);
                    return terms.Count > 0 && words.Intersect(terms).Count() >= Math.Min(2, terms.Count);
                }).ToList();
                return (Claim: claim, Requirements: matched, Specificity: words.Intersect(requirements.SelectMany(Terms)).Count());
            }).OrderByDescending(item => item.Requirements.Count).ThenByDescending(item => item.Specificity).FirstOrDefault();
            if (best.Claim is null || best.Requirements.Count == 0) continue;
            var attribution = project.EvidenceMetadata.GetValueOrDefault("main_contributor") == "true" ? "main contributor (observed)"
                : project.EvidenceMetadata.GetValueOrDefault("contributions_observed") == "true" ? "contribution observed" : "unknown";
            double.TryParse(project.EvidenceMetadata.GetValueOrDefault("observed_commit_share"), NumberStyles.Float, CultureInfo.InvariantCulture, out var share);
            var relevance = string.Join("; ", best.Requirements.Select(text => text.Split(':')[0]).Distinct().Take(2));
            if (relevance.Length > 140) relevance = relevance[..137] + "…";
            candidates.Add((new RelatedWorkLink
            {
                SourceId = project.Id,
                Kind = kind,
                Name = project.Name,
                Url = uri.ToString(),
                Relevance = relevance,
                SupportingPassage = best.Claim.Statement,
                SourceRevision = project.EvidenceMetadata.GetValueOrDefault("revision")
                    ?? project.EvidenceMetadata.GetValueOrDefault("content_hash") ?? ledger.SourceRevision,
                Requirements = best.Requirements,
                ClaimIds = [best.Claim.Id],
                EvidenceIds = [.. best.Claim.EvidenceIds],
                Attribution = attribution
            }, best.Requirements.Count, best.Specificity, share, project.Date));
        }
        return candidates.OrderByDescending(item => item.Coverage).ThenByDescending(item => item.Specificity)
            .ThenByDescending(item => item.Attribution).ThenByDescending(item => item.Date).ThenBy(item => item.Link.Url, StringComparer.Ordinal)
            .DistinctBy(item => item.Link.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).Take(maximum).Select(item => item.Link).ToList();
    }
}
