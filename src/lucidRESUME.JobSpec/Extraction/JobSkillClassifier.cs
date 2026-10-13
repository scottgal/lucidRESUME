using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Jobs;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>Validates extracted labels before they enter matching. Decisions cannot invent skills or career facts.</summary>
public sealed class JobSkillClassifier(IResumeDecisionProvider? decisions = null, ILogger<JobSkillClassifier>? logger = null,
    IOptions<JobSkillClassificationOptions>? options = null)
{
    public const string PolicyVersion = "job-skills-v1";
    public bool HasDecisionProvider => decisions is not null;
    public string AnalysisVersion => PolicyVersion + (decisions is null ? ":offline" : ":nimble");
    private readonly JobSkillClassificationOptions _options = options?.Value ?? new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, JobSkillClassification> _cache = new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> Classes = new Dictionary<string, string>
    {
        ["skill"] = "A specific learnable professional skill, technology or method requested in the advert, e.g. C#, LLMs, PostgreSQL, data validation.",
        ["capability"] = "A meaningful professional capability requested in context, e.g. system design, AI evaluation, production reliability or stakeholder communication.",
        ["logistics"] = "Location, working hours, eligibility, compensation, benefits, application instructions or employer information.",
        ["noise"] = "An ordinary word, vague fragment, heading or non-skill, e.g. business, systems, support, connect, practical experience, tools, product."
    };
    // These isolated fragments cannot describe an assessable requirement, even if an optional model misclassifies one.
    private static readonly HashSet<string> Fragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "AI", "Remote", "UK", "Europe", "OR", "IT", "DO", "CAN", "CV", "Business", "Systems", "Support", "Connect",
        "Engineering", "Hiring", "Operations", "Ownership", "Workflows", "Reliable", "Processes", "Production", "Software",
        "Quality", "Developing", "Infrastructure", "Growth", "Influence", "Architecture", "Implementation", "Access", "Product",
        "Technology", "Models", "Tools", "Data", "Backend", "Services", "Evaluations", "Performance", "Requirements", "Engineers",
        "Practical experience", "Building", "Applications", "Sound judgement", "Understanding", "Confidence", "Specification",
        "Commercial", "Startup", "Technical", "Products", "Practice", "Team", "Room", "Collaboration", "Equity", "Salary",
        "Base Salary", "Competitive", "Competitive Base Salary", "Visa Sponsorship", "Engineering Experience"
    };

    private static readonly HashSet<string> Logistics = new(StringComparer.OrdinalIgnoreCase)
    { "Remote", "UK", "Europe", "CV", "Equity", "Salary", "Base Salary", "Competitive Base Salary", "Visa Sponsorship" };

    internal async Task<JobSkillClassification> ClassifyAsync(
        string text, IReadOnlyList<JdFieldCandidate> candidates, CancellationToken ct)
    {
        var labels = candidates.Where(c => c.FieldType is "skill" or "preferredskill")
            .Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var key = EvidenceLedgerBuilder.FastHash(AnalysisVersion + "\n" + text + "\n" + string.Join("\n", candidates.Select(c => $"{c.FieldType}:{c.Value}:{c.Source}:{c.Confidence}")));
        await _gate.WaitAsync(ct);
        try
        {
            if (!_cache.TryGetValue(key, out var classified))
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Unknown NER tokens have no authority on their own. Structural/taxonomy candidates remain a conservative offline fallback.
                foreach (var label in labels)
                    result[label] = Logistics.Contains(label) ? "logistics" : Fragments.Contains(label) ? "noise" : candidates.Any(c => c.Value.Equals(label, StringComparison.OrdinalIgnoreCase) &&
                        (c.Source.StartsWith("structural", StringComparison.Ordinal) || c.Source.StartsWith("taxonomy", StringComparison.Ordinal))) ? "skill" : "noise";
                var audit = result.ToDictionary(pair => pair.Key, pair => new JobSkillDecision(pair.Key, pair.Value, "conservative"), StringComparer.OrdinalIgnoreCase);
                var warnings = new List<string>();
                if (decisions is not null)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        foreach (var batch in labels.Where(label => !Fragments.Contains(label)).Chunk(16))
                        {
                            var requests = batch.Select(label => new ResumeDecisionRequest(
                                "job-skill:" + label, "job-description", key, BoundedContext(text, batch, _options.MaxStateCharacters),
                                $"Classify the extracted label '{label}' as used in this job advert. A capitalised word is not automatically a skill. Select only the supplied category; do not infer missing requirements.", Classes)).ToList();
                            var answers = await decisions.DecideBatchAsync(requests, timeout.Token);
                            if (answers.Count != requests.Count) throw new InvalidDataException("Incomplete skill classifications.");
                            for (var index = 0; index < batch.Length; index++)
                            {
                                var answer = answers[index];
                                var probability = answer.Probabilities.GetValueOrDefault(answer.SelectedCandidate);
                                var runnerUp = answer.Probabilities.Where(pair => pair.Key != answer.SelectedCandidate).Select(pair => pair.Value).DefaultIfEmpty().Max();
                                if (!Classes.ContainsKey(answer.SelectedCandidate)) throw new InvalidDataException("Unknown skill class.");
                                if (probability >= _options.AcceptanceProbability && probability - runnerUp >= _options.MinimumMargin)
                                {
                                    result[batch[index]] = Fragments.Contains(batch[index]) && answer.SelectedCandidate is "skill" or "capability" ? "noise" : answer.SelectedCandidate;
                                    audit[batch[index]] = new(batch[index], result[batch[index]], "decision", answer.Provider,
                                        answer.Model, probability, answer.Probabilities, answer.SelectedCandidate, true);
                                }
                                else audit[batch[index]] = new(batch[index], result[batch[index]], "uncertain-decision", answer.Provider,
                                    answer.Model, probability, answer.Probabilities, answer.SelectedCandidate, false);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        warnings.Add("Nimble job skill classification timed out. Conservative extraction was used for unresolved labels.");
                        logger?.LogWarning("{Warning}", warnings[^1]);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        warnings.Add("Nimble job skill classification is unavailable. Check the configured server and its /v1/systemone support; conservative extraction was used for unresolved labels.");
                        logger?.LogWarning(ex, "{Warning}", warnings[^1]);
                    }
                }
                classified = new(candidates.Where(c => c.FieldType is not ("skill" or "preferredskill") ||
                    result.GetValueOrDefault(c.Value) is "skill" or "capability").ToList(), result, audit.Values.ToList(), warnings);
                if (_cache.Count >= 128) _cache.Clear();
                if (warnings.Count == 0) _cache[key] = classified;
            }
            return classified;
        }
        finally { _gate.Release(); }
    }
    private static string BoundedContext(string text, IReadOnlyList<string> labels, int limit)
    {
        limit = Math.Clamp(limit, 256, 16_000);
        if (text.Length <= limit) return text;
        var excerpts = new List<string> { text[..Math.Min(300, limit)] };
        foreach (var label in labels)
        {
            var position = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (position < 0) continue;
            var start = Math.Max(0, position - 100);
            var excerpt = text.Substring(start, Math.Min(label.Length + 200, text.Length - start));
            if (excerpts.Sum(e => e.Length + 5) + excerpt.Length <= limit) excerpts.Add(excerpt);
        }
        return string.Join("\n[…]\n", excerpts);
    }
}

public sealed class JobSkillClassificationOptions
{
    public double AcceptanceProbability { get; set; } = .8;
    public double MinimumMargin { get; set; } = .2;
    public int MaxStateCharacters { get; set; } = 4000;
}

internal sealed record JobSkillClassification(List<JdFieldCandidate> Candidates,
    IReadOnlyDictionary<string, string> Classes, List<JobSkillDecision> Decisions, List<string> Warnings);
