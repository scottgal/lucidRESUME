using System.Text.Json;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

/// <summary>Materialises the full portable career projection once per career revision.</summary>
public sealed class CompleteCareerJobMlService(IAppStore store, CareerRecordJobMlBuilder builder)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<ResumeDocument?> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await store.LoadAsync(ct);
            var career = state.GetCompleteTranscript();
            if (career is null) return null;
            if (!string.IsNullOrWhiteSpace(career.JobMlSource))
            {
                if (state.CompleteCareer!.SemanticIndex is null)
                {
                    var existing = new JobMlParser().Parse(career.JobMlSource);
                    var index = CaptureSemanticIndex(existing);
                    if (index is not null)
                        await store.MutateAsync(latest =>
                        {
                            if (latest.CompleteCareer?.Transcript.JobMlSource == career.JobMlSource)
                                latest.CompleteCareer.SemanticIndex = index;
                        }, ct);
                }
                return career;
            }
            var original = JsonSerializer.Serialize(career);
            var file = await Task.Run(() => builder.BuildAsync(career, ct, state.CompleteCareer?.SemanticIndex), ct);
            var source = new JobMlParser().Serialize(file);
            await store.MutateAsync(latest =>
            {
                var current = latest.GetCompleteTranscript();
                if (current is null || JsonSerializer.Serialize(current) != original)
                    throw new InvalidOperationException("The career changed while building JobML. Reopen the complete transcript.");
                current.JobMlSource = source;
                current.CanonicalMarkdown = file.Markdown;
                current.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(source);
                latest.CompleteCareer!.SemanticIndex = CaptureSemanticIndex(file);
            }, ct);
            career.JobMlSource = source;
            career.CanonicalMarkdown = file.Markdown;
            career.JobMlRevision = MarkdownEvidenceIndex.Fingerprint(source);
            return career;
        }
        finally { _gate.Release(); }
    }
    private static CareerSemanticIndex? CaptureSemanticIndex(JobMlFile file)
    {
        var space = file.Data.SemanticSpaces.SingleOrDefault();
        return space is null ? null : new CareerSemanticIndex
        {
            Model = space.Model,
            ModelDigest = space.ModelDigest,
            Normalization = space.Normalization,
            Concepts = file.Data.Concepts.Where(concept => concept.Embedding?.Space == space.Id)
                .ToDictionary(concept => concept.Name, concept => concept.Embedding!.Vector.ToArray(), StringComparer.OrdinalIgnoreCase)
        };
    }

}
