using System.Text.Json;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>Shares saved accounts across stateless composition requests without promoting them to facts.</summary>
internal static class ReusableProseEditor
{
    public static async Task<CompositionDraft> RunAsync(CompositionPassRequest request, ReusableProseStore store,
        string provider, string modelIdentity, string instructions,
        Func<CompositionPassRequest, Task<CompositionDraft>> generate, CancellationToken ct)
    {
        var validator = new CompositionValidator();
        var result = new List<CompositionBlock>();
        var pending = new List<CompositionBlock>();
        var keys = new Dictionary<string, string>();
        var warnings = new List<string>();
        var rejections = new Dictionary<string, string>();
        var brief = request.Pass == CompositionPass.Tighten ? "" : request.JobDescription;
        foreach (var current in request.CurrentBlocks)
        {
            var source = request.SourceBlocks.Single(block => block.SectionId == current.SectionId);
            var packet = request.Manifest.Sections.Single(section => section.SectionId == current.SectionId);
            var key = ReusableProseStore.Fingerprint(JsonSerializer.Serialize(new
            {
                provider,
                modelIdentity,
                instructions,
                validatorPolicy = CompositionValidator.PolicyVersion,
                request.Pass,
                brief,
                source,
                current,
                packet.Intent,
                packet.MaximumWords,
                packet.Kind,
                packet.Accounts,
                writingPlan = packet.WritingPlan?.ForPass(request.Pass),
                claims = packet.Claims.Select(claim => new
                {
                    claim.Claim.Id,
                    claim.Claim.Subject,
                    claim.Claim.Statement,
                    skills = claim.Claim.Concepts.All.Order(StringComparer.Ordinal).ToArray()
                })
            }));
            keys[current.SectionId] = key;
            var account = await store.FindAsync(key, ct);
            var candidate = account is null ? null : current with { Text = account.Text };
            if (candidate is not null && account!.SourceText == source.Text &&
                account.ClaimIds.SequenceEqual(source.ClaimIds) && account.EvidenceIds.SequenceEqual(source.EvidenceIds) &&
                (account.RetainedSource ? account.Text == current.Text : validator.Validate(new([candidate], []), [source],
                    request.Manifest with { Sections = [packet] }).Count == 0))
            {
                result.Add(candidate);
                warnings.AddRange(account.ReviewNotes);
                if (account.RetainedSource) rejections[current.SectionId] = account.RejectionReason;
            }
            else pending.Add(current);
        }
        if (result.Count > 0) request.Progress?.Report($"Reusing {result.Count} prose account(s) · {request.Pass}");
        if (pending.Count == 0) return new(result, warnings) { RejectedSections = rejections };
        var ids = pending.Select(block => block.SectionId).ToHashSet();
        var draft = await generate(request with
        {
            JobDescription = brief,
            CurrentBlocks = pending,
            SourceBlocks = request.SourceBlocks.Where(block => ids.Contains(block.SectionId)).ToList(),
            Manifest = request.Manifest with { Sections = request.Manifest.Sections.Where(section => ids.Contains(section.SectionId)).ToList() }
        });
        warnings.AddRange(draft.Warnings);
        foreach (var current in pending)
        {
            var candidates = draft.Blocks.Where(block => block.SectionId == current.SectionId).ToList();
            if (candidates.Count != 1) continue;
            var source = request.SourceBlocks.Single(block => block.SectionId == current.SectionId);
            var packet = request.Manifest.Sections.Single(section => section.SectionId == current.SectionId);
            var errors = validator.Validate(new(candidates, []), [source], request.Manifest with { Sections = [packet] });
            var chosen = errors.Count == 0 ? candidates[0] : current;
            if (errors.Count > 0) rejections[current.SectionId] = string.Join(" ", errors);
            var notes = errors.Select(error => $"Retained prior wording after rejected {request.Pass} edit: {error}").ToList();
            result.Add(chosen);
            warnings.AddRange(notes);
            await store.SaveAsync(new ReusableProseAccount
            {
                Key = keys[current.SectionId],
                SourceFingerprint = ReusableProseStore.Fingerprint(JsonSerializer.Serialize(source)),
                SourceText = source.Text,
                Text = chosen.Text,
                Skills = packet.Claims.SelectMany(claim => claim.Claim.Concepts.All).Distinct().ToList(),
                ClaimIds = source.ClaimIds.ToList(),
                EvidenceIds = source.EvidenceIds.ToList(),
                SubjectIds = packet.Claims.Select(claim => claim.Claim.Subject).Distinct().ToList(),
                Provider = provider,
                ModelIdentity = modelIdentity,
                Pass = request.Pass.ToString(),
                JobBrief = brief,
                MaximumWords = packet.MaximumWords,
                RetainedSource = errors.Count > 0,
                RejectionReason = string.Join(" ", errors),
                ReviewNotes = notes
            }, ct);
        }
        return new(result, warnings) { RejectedSections = rejections };
    }
}
