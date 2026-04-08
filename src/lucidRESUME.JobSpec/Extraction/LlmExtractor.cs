using System.Text.Json;
using lucidRESUME.Core.Interfaces;

namespace lucidRESUME.JobSpec.Extraction;

/// <summary>
/// LLM-based extractor — the backstop that should NEVER fail.
/// Runs in parallel with structural and NER, but when other signals are weak,
/// this is the decider. Gets the full text, produces structured JSON.
/// </summary>
public static class LlmExtractor
{
    public static async Task<List<JdFieldCandidate>> ExtractAsync(
        string text, ILlmExtractionService? llm, CancellationToken ct = default)
    {
        var candidates = new List<JdFieldCandidate>();
        if (llm is null) return candidates;

        try
        {
            var input = text.Length > 6000 ? text[..6000] : text;
            var prompt = """
                Extract structured data from this job description. Reply with ONLY valid JSON, no explanation.
                Extract technical skills even when embedded in prose paragraphs, not just from bulleted lists.
                For skills, list individual technologies/tools/languages — not sentences.

                {
                  "title": "job title only",
                  "company": "company/employer name",
                  "location": "city, country or region",
                  "isRemote": true/false,
                  "isHybrid": true/false,
                  "requiredSkills": ["skill1", "skill2"],
                  "preferredSkills": ["skill1", "skill2"],
                  "yearsExperience": N,
                  "education": "degree requirement or null",
                  "seniorityLevel": "Junior|Mid|Senior|Lead|Principal",
                  "industry": "sector/industry"
                }

                Job description:
                """ + "\n" + input;

            var result = await llm.ExtractJsonAsync(prompt, ct);
            if (result is null) return candidates;

            // Strip markdown fences
            result = result.Trim();
            if (result.StartsWith("```")) result = result[result.IndexOf('\n')..].TrimStart();
            if (result.EndsWith("```")) result = result[..result.LastIndexOf("```")].TrimEnd();

            // Find JSON object boundaries (handle LLM preamble/postamble)
            var jsonStart = result.IndexOf('{');
            var jsonEnd = result.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart) return candidates;
            result = result[jsonStart..(jsonEnd + 1)];

            var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            AddStringField(candidates, root, "title", "title", 0.85);
            AddStringField(candidates, root, "company", "company", 0.85);
            AddStringField(candidates, root, "location", "location", 0.80);
            AddStringField(candidates, root, "education", "education", 0.80);
            AddStringField(candidates, root, "seniorityLevel", "seniority", 0.80);
            AddStringField(candidates, root, "industry", "industry", 0.80);

            if (root.TryGetProperty("isRemote", out var r) && r.ValueKind == JsonValueKind.True)
                candidates.Add(new("remote", "true", 0.85, "llm"));
            if (root.TryGetProperty("isHybrid", out var h) && h.ValueKind == JsonValueKind.True)
                candidates.Add(new("remote", "hybrid", 0.80, "llm"));
            if (root.TryGetProperty("yearsExperience", out var y) && y.ValueKind == JsonValueKind.Number)
                candidates.Add(new("yearsexp", y.GetInt32().ToString(), 0.85, "llm"));

            AddArrayField(candidates, root, "requiredSkills", "skill", 0.80);
            AddArrayField(candidates, root, "preferredSkills", "preferredskill", 0.80);
        }
        catch { /* LLM failure is non-fatal — other extractors still contribute */ }

        return candidates;
    }

    private static void AddStringField(List<JdFieldCandidate> candidates,
        JsonElement root, string jsonProp, string fieldType, double confidence)
    {
        if (root.TryGetProperty(jsonProp, out var val)
            && val.ValueKind == JsonValueKind.String
            && val.GetString() is { Length: > 1 } s
            && !s.Equals("null", StringComparison.OrdinalIgnoreCase)
            && !s.Equals("n/a", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(new(fieldType, s, confidence, "llm"));
        }
    }

    private static void AddArrayField(List<JdFieldCandidate> candidates,
        JsonElement root, string jsonProp, string fieldType, double confidence)
    {
        if (root.TryGetProperty(jsonProp, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 1 } s)
                    candidates.Add(new(fieldType, s, confidence, "llm"));
            }
        }
    }
}
