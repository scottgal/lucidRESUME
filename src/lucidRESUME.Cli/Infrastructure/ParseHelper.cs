using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Cli.Infrastructure;

/// <summary>
/// Wraps resume parsing with proper LLM enhancement awaiting.
/// The parser fires LLM skill/experience recovery as a background task.
/// CLI commands must await it before using the parsed data.
/// </summary>
public static class ParseHelper
{
    public static async Task<ResumeDocument> ParseAndAwaitAsync(
        IResumeParser parser, string filePath, CancellationToken ct = default)
    {
        var resume = await parser.ParseAsync(filePath, ct);

        // Wait for LLM enhancement if it was triggered
        if (resume.LlmEnhancementTask is not null)
        {
            try
            {
                await resume.LlmEnhancementTask;
            }
            catch
            {
                // LLM enhancement is best-effort — don't fail the parse
            }
        }

        return resume;
    }
}
