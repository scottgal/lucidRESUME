using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.AI;

/// <summary>
/// Scores how "AI-generated" a resume's text appears using three signals:
///   1. Embedding variance - AI text clusters tighter in embedding space
///   2. Stylometric signals - sentence uniformity, buzzword density, passive voice
///   3. Lexical diversity - AI tends toward lower type-token ratio
///
/// Returns a score 0-100 where 100 = "almost certainly AI-generated".
/// </summary>
public sealed class AiDetectionScorer
{
    private readonly IEmbeddingService _embedder;
    private readonly ILlmExtractionService? _llm;
    private readonly OnnxAiTextDetector? _onnxDetector;
    private readonly ILogger<AiDetectionScorer> _logger;

    public AiDetectionScorer(IEmbeddingService embedder, ILogger<AiDetectionScorer> logger,
        ILlmExtractionService? llm = null, OnnxAiTextDetector? onnxDetector = null)
    {
        _embedder = embedder;
        _llm = llm;
        _onnxDetector = onnxDetector;
        _logger = logger;
    }

    public async Task<AiDetectionReport> ScoreAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var bullets = ExtractBullets(resume);
        if (bullets.Count < 3)
            return new AiDetectionReport(0, [], "Too few bullets to score");

        // Signal 1: Embedding variance (0-100, high = more AI-like)
        var embeddingScore = await ScoreEmbeddingVarianceAsync(bullets, ct);

        // Signal 2: Stylometric signals (0-100)
        var styleScore = ScoreStylometrics(bullets);

        // Signal 3: Lexical diversity (0-100)
        var lexicalScore = ScoreLexicalDiversity(bullets);

        // Signal 4: Local LLM judgement (0-100, optional)
        var llmScore = await ScoreLlmJudgementAsync(bullets, ct);

        // Signal 5: ONNX RoBERTa detector (0-100, optional)
        var onnxScore = ScoreOnnxDetector(bullets);

        // Weighted combination - optional signals get weight only if available
        var signals = new List<(double score, double weight)>
        {
            (embeddingScore.Score, 0.30),
            (styleScore.Score, 0.25),
            (lexicalScore.Score, 0.15),
        };
        if (llmScore.Score >= 0) signals.Add((llmScore.Score, 0.15));
        if (onnxScore.Score >= 0) signals.Add((onnxScore.Score, 0.15));

        // Normalize weights to sum to 1.0
        var totalWeight = signals.Sum(s => s.weight);
        double overall = signals.Sum(s => s.score * s.weight / totalWeight);

        var findings = new List<AiDetectionFinding>();
        findings.AddRange(embeddingScore.Findings);
        findings.AddRange(styleScore.Findings);
        findings.AddRange(lexicalScore.Findings);
        findings.AddRange(llmScore.Findings);
        findings.AddRange(onnxScore.Findings);

        _logger.LogInformation(
            "AI detection: overall={Overall} (emb={Emb}, style={Style}, lex={Lex}, llm={Llm}, onnx={Onnx}), {Bullets} bullets",
            (int)Math.Round(overall), embeddingScore.Score, styleScore.Score, lexicalScore.Score,
            llmScore.Score, onnxScore.Score, bullets.Count);

        return new AiDetectionReport((int)Math.Round(overall), findings);
    }

    private async Task<(int Score, List<AiDetectionFinding> Findings)> ScoreEmbeddingVarianceAsync(
        List<string> bullets, CancellationToken ct)
    {
        var findings = new List<AiDetectionFinding>();
        try
        {
            // Embed all bullets
            var embeddings = new List<float[]>();
            foreach (var bullet in bullets)
            {
                var emb = await _embedder.EmbedAsync(bullet, ct);
                embeddings.Add(emb);
            }

            // Calculate pairwise cosine similarities
            var similarities = new List<float>();
            for (int i = 0; i < embeddings.Count; i++)
            for (int j = i + 1; j < embeddings.Count; j++)
                similarities.Add(_embedder.CosineSimilarity(embeddings[i], embeddings[j]));

            if (similarities.Count == 0)
                return (0, findings);

            var avgSim = similarities.Average();
            var variance = similarities.Select(s => (s - avgSim) * (s - avgSim)).Average();
            var stdDev = (float)Math.Sqrt(variance);

            // Human writing: avg similarity ~0.3-0.5 with high variance (stdDev > 0.15)
            // AI writing: avg similarity ~0.6-0.8 with low variance (stdDev < 0.10)
            // Map: avgSim 0.3→0, 0.8→100; low stdDev amplifies the score

            var simScore = Math.Clamp((avgSim - 0.30f) / 0.50f, 0f, 1f) * 100;
            var varianceBonus = stdDev < 0.08f ? 20 : stdDev < 0.12f ? 10 : 0;
            var score = (int)Math.Min(100, simScore + varianceBonus);

            if (avgSim > 0.6f)
                findings.Add(new("Embedding", $"Bullet points are suspiciously similar (avg cosine={avgSim:F2}, stddev={stdDev:F2})", score));
            else if (avgSim > 0.45f)
                findings.Add(new("Embedding", $"Moderate bullet similarity (avg cosine={avgSim:F2})", score));

            return (score, findings);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embedding variance scoring failed");
            return (0, findings);
        }
    }

    private (int Score, List<AiDetectionFinding> Findings) ScoreOnnxDetector(List<string> bullets)
    {
        var findings = new List<AiDetectionFinding>();
        if (_onnxDetector is null || !_onnxDetector.IsAvailable)
            return (-1, findings);

        try
        {
            var allText = string.Join("\n", bullets);
            var prob = _onnxDetector.Detect(allText);
            if (prob < 0) return (-1, findings);

            var score = (int)(prob * 100);
            var label = score > 70 ? "likely AI-generated" : score > 40 ? "mixed signals" : "likely human";
            findings.Add(new("ONNX Detector", $"RoBERTa classifier: {prob:P0} AI probability ({label})", score));
            return (score, findings);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ONNX AI detection failed");
            return (-1, findings);
        }
    }

    private async Task<(int Score, List<AiDetectionFinding> Findings)> ScoreLlmJudgementAsync(
        List<string> bullets, CancellationToken ct)
    {
        var findings = new List<AiDetectionFinding>();
        if (_llm is null || !_llm.IsAvailable)
            return (-1, findings); // -1 = not available

        try
        {
            // Send a sample of bullets (max 10) to the LLM for judgement
            var sample = bullets.Take(10).ToList();
            var text = string.Join("\n- ", sample);
            var prompt = $"""
                Rate how likely this resume text is AI-generated on a scale of 0-100.
                0 = clearly human-written, 100 = clearly AI-generated.

                Consider: generic buzzwords, formulaic structure, lack of specific details,
                unnaturally uniform sentence patterns, and absence of personal voice.

                Reply with ONLY a number 0-100 and one sentence explanation. Nothing else.

                Resume bullets:
                - {text}
                """;

            var result = await _llm.ExtractSkillsAsync(prompt, ct); // reuse the extraction endpoint
            if (result is null) return (-1, findings);

            // Parse the score from the LLM response
            var match = Regex.Match(result, @"\b(\d{1,3})\b");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var score) && score is >= 0 and <= 100)
            {
                var explanation = result.Length > match.Index + match.Length
                    ? result[(match.Index + match.Length)..].Trim().TrimStart('.', '-', ':', ' ')
                    : "";
                if (explanation.Length > 100) explanation = explanation[..97] + "...";

                findings.Add(new("LLM Judge", $"LLM scored {score}/100" +
                    (explanation.Length > 0 ? $": {explanation}" : ""), score));
                return (score, findings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM AI detection judgement failed");
        }
        return (-1, findings);
    }

    private static (int Score, List<AiDetectionFinding> Findings) ScoreStylometrics(List<string> bullets)
    {
        var findings = new List<AiDetectionFinding>();
        var scores = new List<float>();

        // Signal: Buzzword density
        ReadOnlySpan<string> buzzwords =
        [
            "leverage", "leveraged", "leveraging",
            "spearhead", "spearheaded",
            "orchestrat", // orchestrate/orchestrated/orchestrating
            "synerg",     // synergy/synergistic
            "streamlin",  // streamline/streamlined
            "facilitat",  // facilitate/facilitated
            "implementat", // implementation
            "utiliz",     // utilize/utilized (vs "use")
            "impactful",
            "cutting-edge",
            "best-in-class",
            "robust",
            "comprehensive",
            "innovative",
            "transformative",
            "strategic",
            "scalable",
            "seamless",
        ];

        var allText = string.Join(" ", bullets).ToLowerInvariant();
        var wordCount = allText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var buzzHits = 0;
        foreach (var bw in buzzwords)
            buzzHits += CountOccurrences(allText, bw);

        var buzzDensity = wordCount > 0 ? (float)buzzHits / wordCount * 100 : 0;
        // > 3% buzzword density = suspicious
        var buzzScore = Math.Clamp(buzzDensity / 5f * 100, 0, 100);
        if (buzzDensity > 2)
            findings.Add(new("Buzzwords", $"{buzzHits} AI-typical buzzwords ({buzzDensity:F1}% density)", (int)buzzScore));
        scores.Add(buzzScore);

        // Signal: Sentence length uniformity
        var sentenceLengths = bullets.Select(b => b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToList();
        if (sentenceLengths.Count >= 3)
        {
            var avgLen = sentenceLengths.Average();
            var lenVariance = sentenceLengths.Select(l => (l - avgLen) * (l - avgLen)).Average();
            var lenStdDev = Math.Sqrt(lenVariance);
            var cv = avgLen > 0 ? lenStdDev / avgLen : 0; // coefficient of variation

            // Human: CV > 0.4 (varied lengths). AI: CV < 0.25 (uniform)
            var uniformScore = Math.Clamp((1 - cv / 0.5) * 100, 0, 100);
            if (cv < 0.25)
                findings.Add(new("Uniformity", $"Suspiciously uniform bullet lengths (CV={cv:F2})", (int)uniformScore));
            scores.Add((float)uniformScore);
        }

        // Signal: Starts with action verb pattern (AI loves this)
        string[] actionStarters =
        [
            "led", "managed", "developed", "designed", "implemented", "built",
            "created", "established", "drove", "delivered", "architected",
            "spearheaded", "orchestrated", "transformed", "optimized",
        ];
        var startsWithAction = bullets.Count(b =>
        {
            var firstWord = b.TrimStart('-', '*', '•', ' ').Split(' ')[0].ToLowerInvariant().TrimEnd(',');
            return actionStarters.Contains(firstWord);
        });
        var actionPct = (float)startsWithAction / bullets.Count * 100;
        // > 80% starting with action verbs = AI pattern
        var actionScore = actionPct > 60 ? Math.Clamp((actionPct - 60) / 40 * 100, 0, 100) : 0;
        if (actionPct > 70)
            findings.Add(new("Action Verbs", $"{actionPct:F0}% bullets start with action verbs", (int)actionScore));
        scores.Add(actionScore);

        var avg = scores.Count > 0 ? (int)scores.Average() : 0;
        return (avg, findings);
    }

    private static (int Score, List<AiDetectionFinding> Findings) ScoreLexicalDiversity(List<string> bullets)
    {
        var findings = new List<AiDetectionFinding>();
        var allText = string.Join(" ", bullets).ToLowerInvariant();
        var words = allText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', ';', ':', '(', ')', '"', '\''))
            .Where(w => w.Length > 2)
            .ToList();

        if (words.Count < 20)
            return (0, findings);

        var uniqueWords = words.Distinct().Count();
        var ttr = (float)uniqueWords / words.Count; // Type-Token Ratio

        // Human writing: TTR > 0.55 (varied vocabulary)
        // AI writing: TTR < 0.45 (repetitive, formulaic)
        var score = (int)Math.Clamp((1 - ttr / 0.6f) * 100, 0, 100);
        if (ttr < 0.45f)
            findings.Add(new("Vocabulary", $"Low lexical diversity (TTR={ttr:F2}, {uniqueWords}/{words.Count} unique words)", score));

        return (score, findings);
    }

    private static List<string> ExtractBullets(ResumeDocument resume)
    {
        var bullets = new List<string>();

        // Achievement bullets from experience
        foreach (var exp in resume.Experience)
            bullets.AddRange(exp.Achievements.Where(a => a.Length > 20));

        // If not enough from structured data, fall back to markdown lines
        if (bullets.Count < 3 && !string.IsNullOrEmpty(resume.RawMarkdown))
        {
            bullets = resume.RawMarkdown
                .Split('\n')
                .Select(l => l.Trim().TrimStart('-', '*', '•', ' '))
                .Where(l => l.Length > 30 && !l.StartsWith('#'))
                .ToList();
        }

        return bullets;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}

public sealed record AiDetectionReport(
    int Score,
    IReadOnlyList<AiDetectionFinding> Findings,
    string? Note = null);

public sealed record AiDetectionFinding(
    string Signal,
    string Message,
    int SignalScore);