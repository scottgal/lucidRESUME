using System.Text.RegularExpressions;

namespace lucidRESUME.Compiler;

public sealed class CompositionValidator
{
    private static readonly Regex Number = new(@"(?<![\w-])\d+(?:[.,]\d+)*(?:%|x|\+)?", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"[\p{L}\p{N}][\p{L}\p{N}+#.-]{2,}", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "and", "the", "with", "from", "that", "this", "for", "into", "through", "role", "have", "must", "will", "your", "their", "our" };

    public IReadOnlyList<string> Validate(
        CompositionDraft draft,
        IReadOnlyList<CompositionBlock> source,
        ProjectionManifest manifest)
    {
        var errors = new List<string>();
        var sourceBySection = source.ToDictionary(x => x.SectionId, StringComparer.OrdinalIgnoreCase);
        var packetBySection = manifest.Sections.ToDictionary(x => x.SectionId, StringComparer.OrdinalIgnoreCase);
        foreach (var block in draft.Blocks)
        {
            if (!sourceBySection.TryGetValue(block.SectionId, out var original) ||
                !packetBySection.TryGetValue(block.SectionId, out var packet))
            {
                errors.Add($"Unknown section '{block.SectionId}'.");
                continue;
            }
            if (!block.ClaimIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(original.ClaimIds))
                errors.Add($"Section '{block.SectionId}' changed the selected claim identities.");
            if (!block.EvidenceIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(original.EvidenceIds))
                errors.Add($"Section '{block.SectionId}' changed the selected evidence identities.");
            if (string.IsNullOrWhiteSpace(block.Text))
                errors.Add($"Section '{block.SectionId}' returned empty prose.");
            var allowedNumbers = Number.Matches(original.Text).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
            var introduced = Number.Matches(block.Text).Select(x => x.Value).Where(x => !allowedNumbers.Contains(x)).Distinct();
            foreach (var value in introduced) errors.Add($"Section '{block.SectionId}' introduced numeric fact '{value}'.");
            var selectedClaims = packet.Claims.Where(x => original.ClaimIds.Contains(x.Claim.Id, StringComparer.OrdinalIgnoreCase));
            var allowedFacts = Words(original.Text + " " + string.Join(' ', selectedClaims.SelectMany(x =>
                x.Claim.Concepts.All.Append(x.Claim.Statement))));
            var targetTerms = Words(string.Join(' ', manifest.Requirements.Select(x => x.Text)));
            var leaked = Words(block.Text).Where(x => targetTerms.Contains(x) && !allowedFacts.Contains(x)).ToList();
            foreach (var term in leaked)
                errors.Add($"Section '{block.SectionId}' introduced target-role term '{term}' without selected evidence.");
            if (WordCount(block.Text) > packet.MaximumWords)
                errors.Add($"Section '{block.SectionId}' exceeds its {packet.MaximumWords}-word budget.");
        }
        if (draft.Blocks.Count != source.Count ||
            !draft.Blocks.Select(x => x.SectionId).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(source.Select(x => x.SectionId)))
            errors.Add("The composition response did not preserve every selected section exactly once.");
        return errors;
    }

    private static int WordCount(string value) =>
        Regex.Matches(value, @"\b[\p{L}\p{N}][\p{L}\p{N}'’-]*\b").Count;
    private static HashSet<string> Words(string value) => Word.Matches(value)
        .Select(x => x.Value.ToLowerInvariant()).Where(x => !StopWords.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed class ResumeCompositionOrchestrator(
    IEnumerable<IResumeCompositionProvider> providers,
    CompositionValidator validator)
{
    public async Task<(IReadOnlyList<CompositionBlock> Blocks, bool Used, string? Provider, IReadOnlyList<string> Warnings)>
        ComposeAsync(ProjectionManifest manifest, string jobDescription, CompilationOptions options,
            CancellationToken cancellationToken = default)
    {
        // The initial state is selected human prose, never model-authored text.
        var source = manifest.Sections.Select(packet => new CompositionBlock(
            packet.SectionId,
            string.Join("\n\n", packet.Claims.Select(x => x.Prose).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
            packet.Claims.Select(x => x.Claim.Id).Distinct().ToList(),
            packet.Claims.SelectMany(x => x.EvidenceIds).Distinct().ToList())).ToList();
        if (!options.ComposeProse) return (source, false, null, []);

        var provider = providers.FirstOrDefault(x => x.IsAvailable &&
            (string.IsNullOrWhiteSpace(options.CompositionProvider) ||
             x.ProviderId.Equals(options.CompositionProvider, StringComparison.OrdinalIgnoreCase)));
        if (provider is null) return (source, false, null, ["No requested composition provider was available; retained selected human prose."]);

        IReadOnlyList<CompositionBlock> current = source;
        var warnings = new List<string>();
        var acceptedPass = false;
        foreach (var pass in new[] { CompositionPass.Tighten, CompositionPass.HumanVoice })
        {
            try
            {
                var draft = await provider.RunPassAsync(
                    new CompositionPassRequest(pass, jobDescription, manifest, source, current), cancellationToken);
                var errors = validator.Validate(draft, source, manifest);
                if (errors.Count > 0)
                {
                    warnings.Add($"Discarded {pass} pass: {string.Join(" ", errors)}");
                    continue;
                }
                current = draft.Blocks;
                acceptedPass = true;
                warnings.AddRange(draft.Warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"Discarded {pass} pass after provider failure: {ex.Message}");
            }
        }
        return (current, acceptedPass, acceptedPass ? provider.ProviderId : null, warnings);
    }
}
