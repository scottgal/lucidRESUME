using lucidRESUME.Matching;
using Mostlylucid.DocSummarizer;
using Mostlylucid.DocSummarizer.Models;

namespace lucidRESUME.GitHub;

/// <summary>
/// Extracts skills from GitHub README markdown using lucidRAG's DocSummarizer.
/// Uses BERT mode (no LLM) to extract segments, then cross-references against
/// the skill taxonomy for technology/skill detection.
/// </summary>
internal sealed class ReadmeSkillExtractor
{
    private readonly IDocumentSummarizer _summarizer;

    public ReadmeSkillExtractor(IDocumentSummarizer summarizer)
    {
        _summarizer = summarizer;
    }

    /// <summary>
    /// Summarize a README and extract skills from it.
    /// Returns canonical skill names with confidence scores, plus a project summary.
    /// </summary>
    public async Task<ReadmeExtractionResult> ExtractAsync(string markdown, string repoName, CancellationToken ct)
    {
        var skills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        string? summary = null;

        try
        {
            var result = await _summarizer.SummarizeMarkdownAsync(
                markdown,
                documentId: repoName,
                focusQuery: "technologies, frameworks, tools, programming languages used",
                mode: SummarizationMode.Bert,
                cancellationToken: ct);

            summary = result.ExecutiveSummary;

            // Extract skills from topic summaries — section headings often name technologies
            foreach (var topic in result.TopicSummaries)
            {
                TryAddSkill(skills, topic.Topic, 0.7);

                // Scan source chunks for skill mentions
                foreach (var chunk in topic.SourceChunks)
                    ExtractSkillsFromText(skills, chunk, 0.65);
            }

            // Extract from the executive summary
            if (!string.IsNullOrEmpty(result.ExecutiveSummary))
                ExtractSkillsFromText(skills, result.ExecutiveSummary, 0.6);

            // Extract from entities if available
            if (result.Entities is { HasAny: true })
            {
                foreach (var org in result.Entities.Organizations)
                    TryAddSkill(skills, org, 0.6);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Fall back to simple taxonomy scanning of the raw markdown
            ExtractSkillsFromText(skills, markdown, 0.5);
        }

        return new ReadmeExtractionResult(
            skills.Select(kv => (kv.Key, kv.Value)).ToList(),
            summary);
    }

    private static void ExtractSkillsFromText(Dictionary<string, double> skills, string text, double baseConfidence)
    {
        // Split text into words and multi-word phrases, check each against taxonomy
        var words = text.Split([' ', ',', ';', '(', ')', '[', ']', '\n', '\r', '\t', '/', '|'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var clean = word.Trim('.', ':', '*', '`', '"', '\'');
            if (clean.Length < 2) continue;
            TryAddSkill(skills, clean, baseConfidence);
        }

        // Also try consecutive pairs for multi-word skills ("machine learning", "asp.net core")
        for (var i = 0; i < words.Length - 1; i++)
        {
            var pair = words[i].Trim('.', ':', '*', '`') + " " + words[i + 1].Trim('.', ':', '*', '`');
            TryAddSkill(skills, pair, baseConfidence);
        }
    }

    private static void TryAddSkill(Dictionary<string, double> skills, string term, double confidence)
    {
        // Use the taxonomy as the single source of truth.
        // If a term canonicalises via SkillTaxonomy, it's a known skill.
        // The taxonomy files (Resources/taxonomies/*.txt) are the data — no hardcoded lists.
        var canonical = SkillTaxonomy.Canonicalize(term);
        if (canonical == null || skills.ContainsKey(canonical)) return;

        // Only accept terms that are in the "information-technology" taxonomy.
        // Other taxonomies (healthcare, finance, etc.) would produce false positives
        // for common English words found in READMEs.
        // The LoadedTaxonomies list tells us which files are loaded — if the term
        // has aliases, it was found in a taxonomy. We accept it.
        var aliases = SkillTaxonomy.GetAliases(canonical);
        if (aliases.Count > 1) // has aliases = found in a taxonomy
            skills[canonical] = confidence;
    }
}

internal record ReadmeExtractionResult(
    List<(string Skill, double Confidence)> Skills,
    string? Summary);
