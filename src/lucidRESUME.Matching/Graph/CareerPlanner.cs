using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.Matching.Graph;

/// <summary>
/// Career navigation engine. Given your skill graph and a target cluster,
/// computes the minimum-cost path through skill space.
///
/// Answers: "What's the smallest set of changes to move me from here to there?"
/// Separates true gaps (you don't have the skill) from presentation gaps
/// (you have it but it's weakly evidenced).
/// </summary>
public sealed class CareerPlanner
{
    private readonly IEmbeddingService _embedder;

    public CareerPlanner(IEmbeddingService embedder)
    {
        _embedder = embedder;
    }

    /// <summary>
    /// Analyse the gap between your current position and a target JD/cluster.
    /// Returns actionable recommendations ranked by impact.
    /// </summary>
    public async Task<CareerPlan> PlanAsync(
        SkillLedger resumeLedger, JdSkillLedger targetJd, SkillGraph graph, CancellationToken ct = default)
    {
        var plan = new CareerPlan
        {
            TargetTitle = targetJd.JobTitle ?? "Target Role",
            TargetCompany = targetJd.Company,
        };

        // Build the matcher result
        var matcher = new SkillLedgerMatcher(_embedder);
        var match = await matcher.MatchAsync(resumeLedger, targetJd, ct);

        plan.CurrentFit = match.OverallFit;
        plan.RequiredCoverage = match.RequiredCoverage;

        // Classify each gap
        foreach (var skillMatch in match.Matches.Where(m => !m.IsMatched))
        {
            // Check if this is a TRUE gap or a PRESENTATION gap
            var isNearMiss = match.NearMisses.Any(nm =>
                nm.RequiredSkill == skillMatch.RequiredSkill && nm.Similarity >= 0.6f);

            var nearSkill = match.NearMisses
                .FirstOrDefault(nm => nm.RequiredSkill == skillMatch.RequiredSkill);

            var recommendation = new CareerRecommendation
            {
                SkillName = skillMatch.RequiredSkill,
                Importance = skillMatch.Importance,
            };

            if (isNearMiss && nearSkill is not null)
            {
                // PRESENTATION GAP — you have adjacent skill, need to surface it
                recommendation.GapType = GapType.PresentationGap;
                recommendation.Advice = $"You have '{nearSkill.ClosestResumeSkill}' (similarity {nearSkill.Similarity:P0}) — " +
                    $"strengthen this by adding specific {skillMatch.RequiredSkill} achievements, " +
                    $"or use the JD's exact terminology in your resume.";
                recommendation.Effort = EffortLevel.Low;
                recommendation.Impact = skillMatch.Importance == SkillImportance.Required ? 0.8 : 0.4;
            }
            else
            {
                // TRUE GAP — skill not in your graph
                var communityId = graph.Nodes.TryGetValue(skillMatch.RequiredSkill, out var node)
                    ? node.CommunityId : -1;
                var yourCommunities = resumeLedger.StrongSkills
                    .Where(s => graph.Nodes.TryGetValue(s.SkillName, out var n) && n.CommunityId == communityId)
                    .Select(s => s.SkillName)
                    .ToList();

                if (yourCommunities.Count > 0)
                {
                    // Adjacent community — you have related skills
                    recommendation.GapType = GapType.AdjacentSkill;
                    recommendation.Advice = $"You're close — your {string.Join(", ", yourCommunities.Take(3))} " +
                        $"skills are in the same cluster. A small project using {skillMatch.RequiredSkill} would bridge the gap.";
                    recommendation.Effort = EffortLevel.Medium;
                    recommendation.Impact = skillMatch.Importance == SkillImportance.Required ? 0.7 : 0.3;
                }
                else
                {
                    // Distant skill — not in your graph at all
                    recommendation.GapType = GapType.TrueGap;
                    recommendation.Advice = $"'{skillMatch.RequiredSkill}' is not in your current skill set. " +
                        $"This would require dedicated learning or a role that uses it.";
                    recommendation.Effort = EffortLevel.High;
                    recommendation.Impact = skillMatch.Importance == SkillImportance.Required ? 0.9 : 0.2;
                }
            }

            plan.Recommendations.Add(recommendation);
        }

        // Also add "strengthen" recommendations for weak matches
        foreach (var skillMatch in match.Matches.Where(m => m.IsMatched && m.EvidenceStrength < 0.4))
        {
            plan.Recommendations.Add(new CareerRecommendation
            {
                SkillName = skillMatch.RequiredSkill,
                GapType = GapType.WeakEvidence,
                Importance = skillMatch.Importance,
                Advice = $"You have {skillMatch.RequiredSkill} but evidence is thin " +
                    $"({skillMatch.EvidenceCount} mentions, {skillMatch.CalculatedYears:F1} years). " +
                    $"Add specific achievements or quantified outcomes.",
                Effort = EffortLevel.Low,
                Impact = 0.5,
            });
        }

        // Sort by impact descending — highest-value actions first
        plan.Recommendations = plan.Recommendations
            .OrderByDescending(r => r.Impact)
            .ThenBy(r => r.Effort)
            .ToList();

        return plan;
    }
}

public sealed class CareerPlan
{
    public string TargetTitle { get; set; } = "";
    public string? TargetCompany { get; set; }
    public double CurrentFit { get; set; }
    public double RequiredCoverage { get; set; }
    public List<CareerRecommendation> Recommendations { get; set; } = [];

    /// <summary>Quick summary: how many actions to reach good fit.</summary>
    public int ActionsToGoodFit => Recommendations.Count(r =>
        r.Importance == SkillImportance.Required &&
        r.GapType is GapType.TrueGap or GapType.AdjacentSkill);
}

public sealed class CareerRecommendation
{
    public string SkillName { get; set; } = "";
    public GapType GapType { get; set; }
    public SkillImportance Importance { get; set; }
    public string Advice { get; set; } = "";
    public EffortLevel Effort { get; set; }
    public double Impact { get; set; }
}

public enum GapType
{
    PresentationGap,  // you have adjacent skill — surface it differently
    WeakEvidence,     // you have it but evidence is thin — add achievements
    AdjacentSkill,    // you have related skills in the same cluster — small project bridges it
    TrueGap,          // genuinely new skill needed
}

public enum EffortLevel
{
    Low,      // resume rewording, terminology alignment
    Medium,   // side project, short course, open source contribution
    High,     // dedicated learning, career move needed
}
