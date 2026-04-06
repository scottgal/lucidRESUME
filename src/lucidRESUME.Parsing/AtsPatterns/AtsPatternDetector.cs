using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace lucidRESUME.Parsing.AtsPatterns;

/// <summary>
/// Detects which ATS/template system generated a resume based on structural signals.
/// Loads patterns from ats-patterns.yaml. Returns the best-matching pattern with
/// confidence score and extracted section keywords + language.
/// </summary>
public sealed class AtsPatternDetector
{
    private readonly AtsPatternConfig _config;

    public AtsPatternDetector()
    {
        var yamlPath = Path.Combine(AppContext.BaseDirectory, "AtsPatterns", "ats-patterns.yaml");
        if (!File.Exists(yamlPath))
            yamlPath = Path.Combine(Path.GetDirectoryName(typeof(AtsPatternDetector).Assembly.Location)!,
                "AtsPatterns", "ats-patterns.yaml");

        if (File.Exists(yamlPath))
        {
            var yaml = File.ReadAllText(yamlPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            _config = deserializer.Deserialize<AtsPatternConfig>(yaml) ?? new();
        }
        else
        {
            _config = new();
        }
    }

    public AtsDetectionResult Detect(string text, string? pdfMetadata = null)
    {
        var lines = text.Split('\n');
        var headings = lines.Count(l => l.TrimStart().StartsWith('#'));
        var nonEmpty = lines.Where(l => l.Trim().Length > 0).ToList();
        var shortLines = nonEmpty.Count(l => l.Trim().Length < 30);
        var shortRatio = nonEmpty.Count > 0 ? (double)shortLines / nonEmpty.Count : 0;
        var lower = text.ToLowerInvariant();

        // Detect language first
        var language = DetectLanguage(lower);

        // Score each pattern
        AtsPattern? bestPattern = null;
        int bestScore = 0;

        foreach (var pattern in _config.Patterns ?? [])
        {
            int score = 0;
            foreach (var signal in pattern.Signals ?? [])
            {
                switch (signal.Type)
                {
                    case "metadata_contains":
                        if (pdfMetadata?.Contains(signal.Value ?? "", StringComparison.OrdinalIgnoreCase) == true)
                            score += 2;
                        break;
                    case "text_contains":
                        if (lower.Contains(signal.Value ?? ""))
                            score += 2;
                        break;
                    case "heading_count_min":
                        if (int.TryParse(signal.Value, out var minH) && headings >= minH)
                            score += 1;
                        break;
                    case "heading_count_max":
                        if (int.TryParse(signal.Value, out var maxH) && headings <= maxH)
                            score += 1;
                        break;
                    case "line_count_min":
                        if (int.TryParse(signal.Value, out var minL) && nonEmpty.Count >= minL)
                            score += 1;
                        break;
                    case "short_line_ratio_above":
                        if (double.TryParse(signal.Value, out var ratio) && shortRatio > ratio)
                            score += 1;
                        break;
                    case "has_section_pattern":
                        if (Regex.IsMatch(text, signal.Value ?? ""))
                            score += 1;
                        break;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestPattern = pattern;
            }
        }

        // Build section keyword map from detected pattern + language
        var sectionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Add language-specific mappings first
        if (language != "en" && _config.LanguageSignals?.TryGetValue(language, out var langConfig) == true)
        {
            foreach (var kv in langConfig.SectionMap ?? new())
                sectionMap.TryAdd(kv.Key, kv.Value);
        }

        // Add pattern-specific keywords
        if (bestPattern?.SectionKeywords != null)
        {
            foreach (var kv in bestPattern.SectionKeywords)
            {
                foreach (var keyword in kv.Value)
                    sectionMap.TryAdd(keyword, kv.Key switch
                    {
                        "experience" => "Experience",
                        "education" => "Education",
                        "skills" => "Skills",
                        _ => kv.Key
                    });
            }
        }

        return new AtsDetectionResult
        {
            PatternName = bestPattern?.Name ?? "unknown",
            Confidence = bestScore,
            Language = language,
            NameStrategy = bestPattern?.NameStrategy ?? "first_line",
            ExperienceStrategy = bestPattern?.ExperienceStrategy,
            SectionMap = sectionMap,
        };
    }

    private string DetectLanguage(string lowerText)
    {
        if (_config.LanguageSignals == null) return "en";

        var bestLang = "en";
        int bestHits = 0;

        foreach (var (lang, config) in _config.LanguageSignals)
        {
            var hits = (config.Keywords ?? []).Count(k => lowerText.Contains(k));
            if (hits > bestHits)
            {
                bestHits = hits;
                bestLang = lang;
            }
        }

        return bestHits >= 2 ? bestLang : "en"; // Need at least 2 keyword hits
    }
}

// YAML model classes
public class AtsPatternConfig
{
    public List<AtsPattern>? Patterns { get; set; }
    public Dictionary<string, LanguageConfig>? LanguageSignals { get; set; }
}

public class AtsPattern
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<AtsSignal>? Signals { get; set; }
    public Dictionary<string, List<string>>? SectionKeywords { get; set; }
    public string? NameStrategy { get; set; }
    public string? ExperienceStrategy { get; set; }
}

public class AtsSignal
{
    public string Type { get; set; } = "";
    public string? Value { get; set; }
}

public class LanguageConfig
{
    public List<string>? Keywords { get; set; }
    public Dictionary<string, string>? SectionMap { get; set; }
}

public class AtsDetectionResult
{
    public string PatternName { get; set; } = "unknown";
    public int Confidence { get; set; }
    public string Language { get; set; } = "en";
    public string NameStrategy { get; set; } = "first_line";
    public string? ExperienceStrategy { get; set; }
    public Dictionary<string, string> SectionMap { get; set; } = new();
}
