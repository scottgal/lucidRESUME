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

    public const string MachineArticleUrl = JobMlArtifactComposer.ArticleUrl;

    [GeneratedRegex(@"(?ms)^\s*```jobml\s*\n(?<yaml>.*?)^\s*```\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex JobMlFence();
}
