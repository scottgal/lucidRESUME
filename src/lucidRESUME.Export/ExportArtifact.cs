using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

internal static partial class ExportArtifact
{
    public static ResumeTemplate Template(ResumeDocument resume) =>
        ResumeTemplateCatalog.Get(resume.OutputTemplateId);

    public static string? JobMlYaml(ResumeDocument resume)
    {
        if (string.IsNullOrWhiteSpace(resume.JobMlSource)) return null;
        var match = JobMlFence().Match(resume.JobMlSource);
        return match.Success ? match.Groups["yaml"].Value.Trim() : null;
    }

    public static CJobMlProjection? CompactJobMl(ResumeDocument resume)
    {
        if (string.IsNullOrWhiteSpace(resume.JobMlSource)) return null;
        var parser = new JobMlParser();
        return parser.TryParse(resume.JobMlSource, out var file, out _)
            ? CJobMlProjector.Project(file!)
            : null;
    }

    public static IReadOnlyList<int> CitationNumbers(string text, CJobMlProjection? projection)
    {
        if (projection is null || string.IsNullOrWhiteSpace(text)) return [];
        var normalized = MarkdownEvidenceIndex.NormalizeText(text);
        return projection.Anchors
            .Where(anchor => string.Equals(anchor.ProseText, normalized, StringComparison.Ordinal))
            .SelectMany(anchor => anchor.ReferenceNumbers)
            .Distinct()
            .Order()
            .ToList();
    }

    public const string MachineArticleUrl = JobMlArtifactComposer.ArticleUrl;

    [GeneratedRegex(@"(?ms)^\s*```jobml\s*\n(?<yaml>.*?)^\s*```\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex JobMlFence();
}
