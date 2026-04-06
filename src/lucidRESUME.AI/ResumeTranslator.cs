using System.Text;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.AI;

/// <summary>
/// Translates a resume to a target language using the configured LLM.
/// Uses a sliding context window to maintain term consistency:
/// - Builds a glossary of translated technical terms as it goes
/// - Each chunk sees the growing glossary so "Kubernetes" always translates consistently
/// - Section headings are translated first and included as context for body text
/// </summary>
public sealed class ResumeTranslator
{
    private readonly ILlmExtractionService _llm;
    private readonly ILogger<ResumeTranslator> _logger;

    public ResumeTranslator(ILlmExtractionService llm, ILogger<ResumeTranslator> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public bool IsAvailable => _llm.IsAvailable;

    public async Task<TranslationResult> TranslateAsync(
        ResumeDocument resume, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(resume.RawMarkdown))
            return new TranslationResult(null, "No markdown content to translate");

        _logger.LogInformation("Translating resume to {Language}", targetLanguage);

        // Split markdown into sections (heading + body pairs)
        var sections = SplitIntoSections(resume.RawMarkdown);
        var glossary = new Dictionary<string, string>();
        var translatedSections = new List<string>();
        var totalChunks = sections.Count;
        var translated = 0;

        foreach (var section in sections)
        {
            var chunk = await TranslateChunkAsync(section, targetLanguage, glossary, ct);
            if (chunk is not null)
            {
                translatedSections.Add(chunk);
                translated++;
            }
            else
            {
                translatedSections.Add(section); // keep original on failure
            }
        }

        var translatedMarkdown = string.Join("\n\n", translatedSections);

        // Create a new document with the translated content
        var doc = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        doc.SetDoclingOutput(translatedMarkdown, null, null);

        // Copy entities (they're language-agnostic metadata)
        foreach (var entity in resume.Entities)
            doc.AddEntity(entity);

        _logger.LogInformation("Translated {Done}/{Total} sections, glossary has {Terms} terms",
            translated, totalChunks, glossary.Count);

        return new TranslationResult(doc, null, glossary);
    }

    private async Task<string?> TranslateChunkAsync(
        string chunk, string targetLanguage, Dictionary<string, string> glossary, CancellationToken ct)
    {
        var glossaryContext = glossary.Count > 0
            ? "GLOSSARY (use these exact translations for consistency):\n" +
              string.Join("\n", glossary.Select(kv => $"  {kv.Key} → {kv.Value}"))
            : "";

        var prompt = $"""
            Translate this resume section to {targetLanguage}.

            Rules:
            - Keep technical terms, product names, company names, and programming languages UNCHANGED (e.g. "Kubernetes", "ASP.NET Core", "Docker" stay in English)
            - Translate job titles, descriptions, and soft skills naturally
            - Keep markdown formatting (headings, bullets)
            - Keep dates, numbers, and URLs unchanged
            - Use formal/professional register appropriate for a CV in {targetLanguage}
            {glossaryContext}

            After the translation, on a new line starting with "TERMS:", list any new technical term translations you made (format: English → {targetLanguage}, comma-separated). If none, write "TERMS: none".

            Text to translate:
            {chunk}
            """;

        var result = await _llm.ExtractSkillsAsync(prompt, ct);
        if (result is null) return null;

        // Extract glossary updates from the TERMS: line
        var termsIdx = result.LastIndexOf("TERMS:", StringComparison.OrdinalIgnoreCase);
        string translatedText;
        if (termsIdx >= 0)
        {
            translatedText = result[..termsIdx].Trim();
            var termsLine = result[(termsIdx + 6)..].Trim();
            if (!termsLine.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in termsLine.Split(',', StringSplitOptions.TrimEntries))
                {
                    var arrow = pair.IndexOf('→');
                    if (arrow < 0) arrow = pair.IndexOf("->");
                    if (arrow > 0)
                    {
                        var en = pair[..arrow].Trim();
                        var tl = pair[(arrow + (pair[arrow] == '→' ? 1 : 2))..].Trim();
                        if (en.Length > 1 && tl.Length > 1)
                            glossary.TryAdd(en, tl);
                    }
                }
            }
        }
        else
        {
            translatedText = result.Trim();
        }

        return translatedText;
    }

    private static List<string> SplitIntoSections(string markdown)
    {
        var sections = new List<string>();
        var current = new StringBuilder();

        foreach (var line in markdown.Split('\n'))
        {
            // New section at heading boundaries
            if (line.TrimStart().StartsWith('#') && current.Length > 0)
            {
                sections.Add(current.ToString().Trim());
                current.Clear();
            }
            current.AppendLine(line);
        }
        if (current.Length > 0)
            sections.Add(current.ToString().Trim());

        return sections;
    }
}

public sealed record TranslationResult(
    ResumeDocument? TranslatedDocument,
    string? Error,
    IReadOnlyDictionary<string, string>? Glossary = null);
