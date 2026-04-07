using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.AI;

/// <summary>
/// Rewrites AI-sounding resume text to be more human, specific, and natural.
/// Uses whichever LLM provider is configured (Ollama, Anthropic, OpenAI).
/// </summary>
public sealed class DeAiRewriter
{
    private readonly IAiTailoringService _tailoring;
    private readonly ILlmExtractionService _llm;
    private readonly ILogger<DeAiRewriter> _logger;

    public DeAiRewriter(IAiTailoringService tailoring, ILlmExtractionService llm,
        ILogger<DeAiRewriter> logger)
    {
        _tailoring = tailoring;
        _llm = llm;
        _logger = logger;
    }

    public bool IsAvailable => _llm.IsAvailable;

    /// <summary>
    /// Rewrites the resume's achievement bullets to sound more human and specific.
    /// Processes in batches to keep context windows manageable.
    /// </summary>
    public async Task<DeAiResult> RewriteAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var bullets = new List<(int ExpIdx, int AchIdx, string Original)>();

        for (int i = 0; i < resume.Experience.Count; i++)
        {
            var exp = resume.Experience[i];
            for (int j = 0; j < exp.Achievements.Count; j++)
            {
                if (exp.Achievements[j].Length > 20)
                    bullets.Add((i, j, exp.Achievements[j]));
            }
        }

        if (bullets.Count == 0)
            return new DeAiResult(0, 0, "No bullets to rewrite");

        _logger.LogInformation("De-AI rewriting {Count} bullets", bullets.Count);

        var rewritten = 0;
        // Process in batches of 5 for reasonable prompt sizes
        foreach (var batch in bullets.Chunk(5))
        {
            var numberedBullets = new StringBuilder();
            for (int k = 0; k < batch.Length; k++)
                numberedBullets.AppendLine($"{k + 1}. {batch[k].Original}");

            var prompt = $"""
                Rewrite these resume bullet points to sound more human and natural.

                Rules:
                - Replace generic buzzwords (leveraged, spearheaded, orchestrated, robust, comprehensive, innovative, seamless, transformative) with specific, concrete language
                - Add specific details where the original is vague (numbers, tools, outcomes)
                - Vary sentence structure - don't start every bullet the same way
                - Keep the same meaning and facts - do NOT invent new achievements
                - Use active voice but vary the patterns
                - Match the person's natural voice - direct, not corporate
                - Keep roughly the same length

                For each bullet, reply with ONLY the rewritten version, numbered to match.
                Do not add explanations.

                {numberedBullets}
                """;

            var result = await _llm.ExtractSkillsAsync(prompt, ct);
            if (result is null) continue;

            // Parse numbered responses
            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Match "1. rewritten text" or "1: rewritten text"
                if (trimmed.Length > 3 && char.IsDigit(trimmed[0]))
                {
                    var dotIdx = trimmed.IndexOfAny(['.', ':', ')']);
                    if (dotIdx > 0 && dotIdx < 3 && int.TryParse(trimmed[..dotIdx], out var num) &&
                        num >= 1 && num <= batch.Length)
                    {
                        var newText = trimmed[(dotIdx + 1)..].Trim();
                        if (newText.Length > 15)
                        {
                            var (expIdx, achIdx, _) = batch[num - 1];
                            resume.Experience[expIdx].Achievements[achIdx] = newText;
                            rewritten++;
                        }
                    }
                }
            }
        }

        _logger.LogInformation("De-AI rewrote {Rewritten}/{Total} bullets", rewritten, bullets.Count);
        return new DeAiResult(bullets.Count, rewritten);
    }
}

public sealed record DeAiResult(int TotalBullets, int Rewritten, string? Note = null);