using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DateTime;
using Microsoft.Recognizers.Text.Sequence;

namespace lucidRESUME.Extraction.Recognizers;

public sealed class ResumeRecognizerDetector : IEntityDetector
{
    public string DetectorId => "recognizer";
    public int Priority => 100;

    private const string Culture = "en-us";

    private static readonly Regex LinkedInPattern = new(
        @"linkedin\.com/in/[\w\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly Regex GitHubPattern = new(
        @"github\.com/[\w\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private readonly ILogger<ResumeRecognizerDetector> _logger;

    public ResumeRecognizerDetector(ILogger<ResumeRecognizerDetector> logger) => _logger = logger;

    public Task<IReadOnlyList<ExtractedEntity>> DetectAsync(DetectionContext context, CancellationToken ct = default)
    {
        var entities = new List<ExtractedEntity>();

        DetectEmails(context, entities);
        DetectPhones(context, entities);
        DetectDates(context, entities);
        DetectUrls(context, entities);
        DetectLinkedIn(context, entities);
        DetectGitHub(context, entities);

        _logger.LogDebug("Recognizer detected {Count} entities", entities.Count);
        return Task.FromResult<IReadOnlyList<ExtractedEntity>>(entities);
    }

    private static void DetectEmails(DetectionContext context, List<ExtractedEntity> entities)
    {
        var emails = SequenceRecognizer.RecognizeEmail(context.Text, Culture);
        foreach (var e in emails)
            entities.Add(ExtractedEntity.Create(e.Text, "Email", DetectionSource.Recognizer, 0.95, context.PageNumber));
    }

    private static void DetectPhones(DetectionContext context, List<ExtractedEntity> entities)
    {
        var phones = SequenceRecognizer.RecognizePhoneNumber(context.Text, Culture);
        foreach (var ph in phones)
        {
            if (ph.Text.Length < 10) continue;
            entities.Add(ExtractedEntity.Create(ph.Text, "PhoneNumber", DetectionSource.Recognizer, 0.88, context.PageNumber));
        }
    }

    // Season names that Recognizers.Text misidentifies as date references inside tech-stack text
    private static readonly HashSet<string> SeasonOnlyWords =
        new(StringComparer.OrdinalIgnoreCase) { "spring", "summer", "fall", "winter", "autumn" };

    private static void DetectDates(DetectionContext context, List<ExtractedEntity> entities)
    {
        var dates = DateTimeRecognizer.RecognizeDateTime(context.Text, Culture);
        foreach (var d in dates)
        {
            // Skip single-word season matches - "Spring Boot", "Spring MVC", etc.
            if (SeasonOnlyWords.Contains(d.Text.Trim())) continue;

            var resolutionValues = d.Resolution?.TryGetValue("values", out var v) == true
                && v is IList<Dictionary<string, string>> list
                ? list.FirstOrDefault()
                : null;

            string classification;
            if (resolutionValues?.TryGetValue("type", out var type) == true)
            {
                if (type is "duration" or "set") continue;
                classification = type is "daterange" or "datetimerange" ? "DateRange" : "Date";
            }
            else
            {
                classification = "Date";
            }

            entities.Add(ExtractedEntity.Create(d.Text, classification, DetectionSource.Recognizer, 0.88, context.PageNumber));
        }
    }

    private static void DetectUrls(DetectionContext context, List<ExtractedEntity> entities)
    {
        var urls = SequenceRecognizer.RecognizeURL(context.Text, Culture);
        foreach (var u in urls)
        {
            // LinkedIn/GitHub get their own classification - skip here
            if (LinkedInPattern.IsMatch(u.Text) || GitHubPattern.IsMatch(u.Text)) continue;
            entities.Add(ExtractedEntity.Create(u.Text, "Url", DetectionSource.Recognizer, 0.90, context.PageNumber));
        }
    }

    private static void DetectLinkedIn(DetectionContext context, List<ExtractedEntity> entities)
    {
        foreach (Match m in LinkedInPattern.Matches(context.Text))
            entities.Add(ExtractedEntity.Create(m.Value, "LinkedInUrl", DetectionSource.Recognizer, 0.95, context.PageNumber));
    }

    private static void DetectGitHub(DetectionContext context, List<ExtractedEntity> entities)
    {
        foreach (Match m in GitHubPattern.Matches(context.Text))
            entities.Add(ExtractedEntity.Create(m.Value, "GitHubUrl", DetectionSource.Recognizer, 0.95, context.PageNumber));
    }
}