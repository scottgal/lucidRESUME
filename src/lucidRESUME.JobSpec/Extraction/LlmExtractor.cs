using System.Text.Json;
using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// LLM-based extractor. Just another signal — runs in parallel with structural and NER.
/// Only fires if an LLM is available.
/// </summary>
public static class LlmExtractor
{
    public static async Task<List<JdFieldCandidate>> ExtractAsync(
        string text, ILlmExtractionService? llm, CancellationToken ct = default)
    {
        var candidates = new List<JdFieldCandidate>();
        if (llm is null || !llm.IsAvailable) return candidates;

        try
        {
            var input = text.Length > 4000 ? text[..4000] : text;
            var prompt = "Extract from this job description. Reply with ONLY valid JSON:\n" +
                "{\"title\":\"...\",\"company\":\"...\",\"location\":\"...\",\"isRemote\":true/false," +
                "\"requiredSkills\":[\"...\"],\"preferredSkills\":[\"...\"],\"yearsExperience\":N}\n\n" + input;

            var result = await llm.ExtractSkillsAsync(prompt, ct);
            if (result is null) return candidates;

            // Strip markdown fences
            result = result.Trim();
            if (result.StartsWith("```")) result = result[result.IndexOf('\n')..].TrimStart();
            if (result.EndsWith("```")) result = result[..result.LastIndexOf("```")].TrimEnd();

            var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String && t.GetString()?.Length > 2)
                candidates.Add(new("title", t.GetString()!, 0.85, "llm"));
            if (root.TryGetProperty("company", out var c) && c.ValueKind == JsonValueKind.String && c.GetString()?.Length > 1)
                candidates.Add(new("company", c.GetString()!, 0.85, "llm"));
            if (root.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.String)
                candidates.Add(new("location", l.GetString()!, 0.8, "llm"));
            if (root.TryGetProperty("isRemote", out var r) && r.ValueKind == JsonValueKind.True)
                candidates.Add(new("remote", "true", 0.85, "llm"));
            if (root.TryGetProperty("yearsExperience", out var y) && y.ValueKind == JsonValueKind.Number)
                candidates.Add(new("yearsexp", y.GetInt32().ToString(), 0.85, "llm"));

            foreach (var prop in new[] { ("requiredSkills", "skill"), ("preferredSkills", "preferredskill") })
            {
                if (root.TryGetProperty(prop.Item1, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString()?.Length > 1)
                            candidates.Add(new(prop.Item2, item.GetString()!, 0.8, "llm"));
                    }
                }
            }
        }
        catch { /* LLM failure is non-fatal — other extractors still contribute */ }

        return candidates;
    }
}
