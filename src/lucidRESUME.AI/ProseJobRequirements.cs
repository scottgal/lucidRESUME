using System.Text.RegularExpressions;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

public static class ProseJobRequirements
{
    public static IReadOnlyList<CompilerRequirement> Build(JobDescription job)
    {
        var requirements = new List<(string Text, RequirementKind Kind)>();
        requirements.AddRange(job.RequiredSkills.Select(text => (text, RequirementKind.Required)));
        requirements.AddRange(job.Competencies.Where(item => item.Priority == RequirementPriority.Required)
            .Select(item => (item.Name + ": " + item.Description, RequirementKind.Required)));
        requirements.AddRange(job.Responsibilities.Select(text => (text, RequirementKind.Responsibility)));
        requirements.AddRange(job.Competencies.Where(item => item.Priority != RequirementPriority.Required && item.Priority != RequirementPriority.Preferred)
            .Select(item => (item.Name + ": " + item.Description, RequirementKind.Responsibility)));
        // Preserve relevant clauses beyond the bounded top-level brief, even when extraction was partial.
        requirements.AddRange(JobTextSections.Read(job.RawText).Where(section => section.Section != "ignore")
            .SelectMany(section => Regex.Split(section.Text, @"(?<=[.!?])\s+(?=[A-Z])")
                .Select(text => (Text: text.Trim(), section.Section)))
            .Where(item => item.Text.Length >= 12 &&
                !Regex.IsMatch(item.Text, @"\b(?:don['’]t|do not) need\b|\bnot required\b", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(item.Text, @"\b(build|design|own|evaluate|lead|implement|define|operate|improve|deploy|maintain|experience|required|must)\b", RegexOptions.IgnoreCase))
            .Select(item => (item.Text, item.Section == "preferred" || Regex.IsMatch(item.Text, @"\b(preferred|preferably|ideally|bonus|nice to have)\b", RegexOptions.IgnoreCase)
                ? RequirementKind.Preferred : item.Section == "required" || Regex.IsMatch(item.Text, @"\b(required|must|essential)\b", RegexOptions.IgnoreCase)
                    ? RequirementKind.Required : RequirementKind.Responsibility)));
        requirements.AddRange(job.PreferredSkills.Select(text => (text, RequirementKind.Preferred)));
        requirements.AddRange(job.Competencies.Where(item => item.Priority == RequirementPriority.Preferred)
            .Select(item => (item.Name + ": " + item.Description, RequirementKind.Preferred)));
        return requirements.Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .DistinctBy(item => Regex.Replace(item.Text, @"\s+", " ").Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(item => new CompilerRequirement("job-" + CareerProseLexicalIndex.Fingerprint(item.Text.Trim().ToLowerInvariant())[..16],
                item.Text.Trim(), item.Kind, item.Text.Trim())).ToList();
    }

    public static ProseCompositionPlan Plan(IReadOnlyList<CompilerRequirement> requirements, IReadOnlyList<SelectedClaim> claims, CareerProseIndex index)
    {
        var sourceIds = claims.Select(claim => claim.Claim.Id).ToHashSet();
        var segments = index.Segments.Where(segment => segment.ClaimIds.Any(sourceIds.Contains)).ToList();
        var focus = requirements.Select(requirement =>
        {
            var queryTerms = CareerProseLexicalIndex.Terms(requirement.Text).Distinct().ToHashSet();
            var relevant = claims.Where(claim =>
            {
                var overlap = CareerProseLexicalIndex.Terms(claim.Claim.Statement).Distinct().Intersect(queryTerms).Count();
                return overlap >= Math.Min(2, Math.Max(1, queryTerms.Count));
            }).ToList();
            var terms = relevant.SelectMany(claim => CareerProseLexicalIndex.Terms(claim.Claim.Statement)).ToHashSet();
            return new ProseRequirementFocus(requirement.Id, requirement.Text, requirement.Kind, relevant.Select(claim => claim.Claim.Id).ToList(),
                queryTerms.Intersect(terms).Order().ToList(), queryTerms.Except(terms).Order().ToList());
        }).Where(item => item.SourceClaimIds.Count > 0)
            .OrderBy(item => item.Kind == RequirementKind.Required ? 0 : item.Kind == RequirementKind.Responsibility ? 1 : 2)
            .ThenByDescending(item => item.MatchedTerms.Count / (double)Math.Max(1, item.MatchedTerms.Count + item.UnmatchedTerms.Count))
            .Take(4).ToList();
        return new(focus, claims.Select(claim => claim.Claim.Id).ToList(), segments.SelectMany(segment => segment.MissingDimensions).Distinct().ToList(),
            segments.Select(segment => segment.Id).ToList());
    }

    public static IReadOnlyList<CompilerRequirement> SelectForRetrieval(JobDescription job, IReadOnlyList<CompilerRequirement> requirements,
        CareerProseIndex index, int maximum = 48)
    {
        var structured = job.RequiredSkills.Concat(job.Responsibilities).Concat(job.PreferredSkills)
            .Concat(job.Competencies.Select(item => item.Name + ": " + item.Description)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        double Distinctiveness(string text)
        {
            var terms = CareerProseLexicalIndex.Terms(text).Distinct().ToArray();
            return terms.Where(index.DocumentFrequency.ContainsKey).Sum(term =>
                1 + Math.Log((1d + index.Segments.Count) / (1 + index.DocumentFrequency[term]))) / Math.Sqrt(Math.Max(1, terms.Length));
        }
        // All requirements remain in composition planning. Bound expensive query work,
        // giving explicit must-haves/responsibilities precedence over noisy raw clauses.
        return requirements.OrderBy(item => structured.Contains(item.Text) && item.Kind == RequirementKind.Required ? 0 :
                structured.Contains(item.Text) && item.Kind == RequirementKind.Responsibility ? 1 :
                item.Kind == RequirementKind.Required ? 2 : item.Kind == RequirementKind.Responsibility ? 3 : 4)
            .ThenByDescending(item => Distinctiveness(item.Text)).ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, maximum)).ToList();
    }
}
