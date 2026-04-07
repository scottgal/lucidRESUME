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

    // Allowlist: terms we know are tech skills from GitHub language names + common tech
    // This avoids false positives from cross-domain taxonomy matching
    private static readonly Lazy<HashSet<string>> ItSkills = new(() =>
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // All aliases from the information-technology taxonomy
        foreach (var alias in SkillTaxonomy.GetAliases("python")) { } // force lazy load
        // The taxonomy's LoadedTaxonomies tells us which files exist
        // We accept any term that Canonicalize returns AND whose canonical form
        // is in the IT sub-category keywords list
        foreach (var (keywords, _) in new[]
        {
            (new[] { "python", "javascript", "typescript", "java", "c#", "c++", "go", "rust", "ruby", "php", "swift", "kotlin", "r", "scala", "sql", "html", "css" }, ""),
            (new[] { "aws", "azure", "gcp", "docker", "kubernetes", "terraform", "ansible", "jenkins", "github actions", "gitlab ci", "git", "github", "devops", "ci/cd" }, ""),
            (new[] { "mongodb", "redis", "elasticsearch", "cassandra", "neo4j", "sqlite", "oracle", "postgresql", "mysql", "dynamodb", "duckdb", "qdrant" }, ""),
            (new[] { "react", "angular", "vue", "django", "flask", "spring", "express", "fastapi", ".net", "asp.net", "avalonia", "blazor", "htmx", "alpine.js", "tailwind", "bootstrap", "wpf", "maui", "node.js", "next.js" }, ""),
            (new[] { "linux", "nginx", "apache", "grafana", "prometheus", "jira", "confluence", "vscode", "vim", "jetbrains", "rider" }, ""),
            (new[] { "oauth", "jwt", "ssl", "tls", "penetration testing", "soc2", "gdpr", "owasp" }, ""),
            (new[] { "machine learning", "deep learning", "nlp", "computer vision", "tensorflow", "pytorch", "scikit-learn", "onnx", "bert", "llm", "rag", "langchain", "huggingface", "ollama", "openai", "anthropic" }, ""),
            (new[] { "agile", "scrum", "devops", "microservices", "rest", "graphql", "grpc", "event-driven", "cqrs", "ddd" }, ""),
            (new[] { "markdown", "yaml", "json", "xml", "csv", "protobuf", "toml" }, ""),
            (new[] { "nuget", "npm", "pip", "cargo", "maven", "gradle", "cmake", "make", "msbuild" }, ""),
            (new[] { "playwright", "selenium", "xunit", "nunit", "jest", "pytest", "cypress" }, ""),
            (new[] { "rabbitmq", "kafka", "nats", "signalr", "websocket", "grpc", "mqtt" }, ""),
        })
        {
            foreach (var keyword in keywords)
            {
                set.Add(keyword);
                var canonical = SkillTaxonomy.Canonicalize(keyword);
                if (canonical != null) set.Add(canonical);
                foreach (var alias in SkillTaxonomy.GetAliases(keyword))
                    set.Add(alias);
            }
        }
        return set;
    });

    private static void TryAddSkill(Dictionary<string, double> skills, string term, double confidence)
    {
        var canonical = SkillTaxonomy.Canonicalize(term);
        if (canonical == null || skills.ContainsKey(canonical)) return;

        // Only accept terms we know are tech skills
        if (ItSkills.Value.Contains(canonical) || ItSkills.Value.Contains(term))
            skills[canonical] = confidence;
    }
}

internal record ReadmeExtractionResult(
    List<(string Skill, double Confidence)> Skills,
    string? Summary);
