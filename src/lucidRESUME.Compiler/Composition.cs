using System.Text.RegularExpressions;

namespace lucidRESUME.Compiler;

public sealed class CompositionValidator
{
    public const string PolicyVersion = "selected-facts-purpose-v4";
    private static readonly Regex Number = new(@"(?<![\w-])\d+(?:[.,]\d+)*(?:%\+?|x|\+)?", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"[\p{L}\p{N}][\p{L}\p{N}+#.-]{2,}", RegexOptions.Compiled);
    private static readonly Regex Purpose = new(
        @"\b(?:so that |so (?=[^.;!?\r\n]{0,120}\b(?:could|can|would|may|might)\b)|in order to )([^.;!?\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AmbiguousMeasurementSubject = new(
        @"\b(?:in|within) (?:a|an|the) (?:\w+ ){0,3}environment,\s*which (?:did not|didn't|does not|doesn't) measure\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingPurpose = new(@"^\s*To ([^,;.!?]+)[,;]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PersonalPronoun = new(@"(?<![\p{L}\p{N}/])(?:I|my)(?![\p{L}\p{N}/])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CollectivePronoun = new(@"\b(?:we|our)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly (string Action, Regex Personal, Regex Team)[] PersonalActions =
        new[] { ("reviewed", "review"), ("designed", "design"), ("built", "build"), ("implemented", "implement"),
                ("developed", "develop"), ("tested", "test"), ("operated", "operate") }
            .Select(action => (action.Item1,
                new Regex(@"\b(?:led|managed|headed|directed)\b[^.;!?]{0,80}\bteam\b[^.;!?]{0,40}\band " + action.Item1 + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bteam\b[^.;!?]{0,24}\bto " + action.Item2 + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)))
            .ToArray();
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "the", "with", "from", "that", "this", "for", "into", "through", "role", "have",
            "must", "will", "your", "their", "our", "using", "across", "including", "alongside", "while",
            "within", "between", "building", "build", "system", "platform", "tool", "was", "were", "been", "being",
            "its", "but", "though", "which", "before", "after", "prior", "during"
        };

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
            if (AmbiguousMeasurementSubject.IsMatch(block.Text))
                errors.Add($"Section '{block.SectionId}' gives a measurement limitation an ambiguous subject. Name the drills, tests or other source activity explicitly instead of 'environment, which did not measure'.");
            if (!PersonalPronoun.IsMatch(original.Text) && PersonalPronoun.IsMatch(block.Text))
                errors.Add($"Section '{block.SectionId}' introduced I/my into prose with an implied subject. Preserve the source's grammatical person.");
            if (!CollectivePronoun.IsMatch(original.Text) && CollectivePronoun.IsMatch(block.Text))
                errors.Add($"Section '{block.SectionId}' introduced we/our and changed the contribution to a collective actor. Preserve the source's grammatical person.");
            var leadingPurpose = LeadingPurpose.Match(block.Text);
            if (leadingPurpose.Success)
            {
                var purposeWords = Words(leadingPurpose.Groups[1].Value);
                if (purposeWords.Count >= 2 && purposeWords.Intersect(Words(original.Text)).Count() < purposeWords.Count * .75)
                    errors.Add($"Section '{block.SectionId}' introduced an unsupported purpose opening. Use a purpose stated in the source, or start with the candidate's action.");
            }
            if (Regex.IsMatch(block.Text, @"\b(led|lead|owned|directed)\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(original.Text, @"\b(led|lead|owned|directed|headed|managed)\b", RegexOptions.IgnoreCase))
                errors.Add($"Section '{block.SectionId}' introduced leadership or ownership not present in the source prose.");
            foreach (var action in PersonalActions)
                if (action.Personal.IsMatch(original.Text) && action.Team.IsMatch(block.Text))
                    errors.Add($"Section '{block.SectionId}' reassigned the candidate's personal action '{action.Action}' to the team. Retain that the candidate led the team and personally {action.Action} the work.");
            // Conservative editorial gates. Ambiguous rewrites retain the accepted human source.
            const string tentative = @"\b(aimed to|designed to|intended to|planned to|would|may|might|hoped to)\b";
            if (Regex.IsMatch(original.Text, tentative, RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(block.Text, tentative, RegexOptions.IgnoreCase))
                errors.Add($"Section '{block.SectionId}' removed intended or uncertain outcome status.");
            const string assistance = @"\b(helped|assisted|supported|contributed|contribution)\b";
            if (Regex.IsMatch(original.Text, assistance, RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(block.Text, assistance, RegexOptions.IgnoreCase))
                errors.Add($"Section '{block.SectionId}' removed assisted ownership from the source.");
            foreach (var skill in packet.Claims.SelectMany(claim => claim.Claim.Concepts.All).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(skill) + @"(?![\p{L}\p{N}])";
                if (Regex.IsMatch(original.Text, pattern, RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(block.Text, pattern, RegexOptions.IgnoreCase))
                    errors.Add($"Section '{block.SectionId}' removed named skill '{skill}'.");
            }
            foreach (var qualifier in new[] { "not", "never", "planned", "proposed", "intended", "estimated", "approximately", "up to", "no longer", "pilot", "prototype", "past year", "last year", "past month", "last month" })
                if (Regex.IsMatch(original.Text, @"\b" + qualifier + @"\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(block.Text, @"\b" + qualifier + @"\b", RegexOptions.IgnoreCase) &&
                    !(qualifier == "not" && Regex.Matches(original.Text, @"\bnot\b", RegexOptions.IgnoreCase).Count == 1 &&
                      Regex.IsMatch(original.Text, @"\bnot (?:yet )?measure(?:d)?\b", RegexOptions.IgnoreCase) &&
                      Regex.IsMatch(block.Text, @"\b(?:unmeasured|without measuring)\b", RegexOptions.IgnoreCase)))
                    errors.Add($"Section '{block.SectionId}' removed source qualifier '{qualifier}'.");
            if (WordCount(original.Text) >= 8 && WordCount(block.Text) < (packet.Kind == "summary" ? 4 : 6))
                errors.Add($"Section '{block.SectionId}' lost the substantive account and became a fragment.");
            var originalContent = Words(original.Text);
            if (packet.Kind != "summary" && originalContent.Count >= 8 &&
                Words(block.Text).Intersect(originalContent).Count() < originalContent.Count * .25)
                errors.Add($"Section '{block.SectionId}' dropped too much of the selected contribution.");
            if (packet.Kind is "experience" or "project")
            {
                var editedWords = Words(block.Text);
                foreach (var claim in packet.Claims.DistinctBy(item => item.Claim.Id))
                {
                    var fact = Words(claim.Claim.Statement);
                    // Only guard claims represented in this passage, not broader ledger accounts.
                    if (fact.Count >= 4 && fact.Intersect(originalContent).Count() >= fact.Count * .8 &&
                        fact.Intersect(editedWords).Count() < fact.Count * .5)
                        errors.Add($"Section '{block.SectionId}' dropped the substance of selected claim '{claim.Claim.Id}'.");
                }
                foreach (var purpose in PurposeDetails(original.Text))
                {
                    var detail = Words(purpose);
                    if (detail.Count >= 3 && detail.Intersect(editedWords).Count() < detail.Count * .8)
                        errors.Add($"Section '{block.SectionId}' dropped the supported purpose detail '{purpose}'.");
                }
            }
            var allowedNumbers = Number.Matches(original.Text).Select(x => CanonicalNumber(x.Value))
                .ToHashSet(StringComparer.Ordinal);
            var introduced = Number.Matches(block.Text).Select(x => x.Value)
                .Where(x => !allowedNumbers.Contains(CanonicalNumber(x))).Distinct();
            foreach (var value in introduced) errors.Add($"Section '{block.SectionId}' introduced numeric fact '{value}'.");
            if (packet.Kind != "summary")
                foreach (var value in allowedNumbers.Where(value =>
                    !Number.Matches(block.Text).Any(number => CanonicalNumber(number.Value) == value)))
                    errors.Add($"Section '{block.SectionId}' removed numeric fact '{value}'.");
            var selectedClaims = packet.Claims.Where(x => original.ClaimIds.Contains(x.Claim.Id, StringComparer.OrdinalIgnoreCase));
            var allowedFacts = Words(original.Text + " " + string.Join(' ', selectedClaims.SelectMany(x =>
                x.Claim.Concepts.All.Append(x.Claim.Statement))));
            var targetTerms = Words(string.Join(' ', manifest.Requirements.Select(x => x.Text)));
            var leaked = Words(block.Text).Where(x => targetTerms.Contains(x) && !allowedFacts.Contains(x)).ToList();
            foreach (var term in leaked)
                errors.Add($"Section '{block.SectionId}' introduced target-role term '{term}' without selected evidence.");
            // A lexical overlap floor alone accepts fluent, invented extra clauses.
            // This conservative novelty budget is advisory grounding, not entailment proof.
            var editorialWords = new HashSet<string>("added created developed implemented delivered used helped assisted contributed supported reviewed worked enabled enabling enables backed made approach focused involved provided established applied remains unmeasured inspection".Split(' '));
            var novel = Words(block.Text).Except(allowedFacts).Except(editorialWords).ToList();
            if (novel.Count > Math.Max(2, allowedFacts.Count * .15))
                errors.Add($"Section '{block.SectionId}' introduced unsupported descriptive content: {string.Join(", ", novel)}.");
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

    // Explicit purpose clauses only: a conservative editorial check, not semantic entailment.
    public static IReadOnlyList<string> PurposeDetails(string text) => Purpose.Matches(text)
        .Select(match => match.Groups[1].Value.Trim()).ToArray();
    private static string CanonicalNumber(string value) => value.Replace(",", string.Empty, StringComparison.Ordinal);
    private static HashSet<string> Words(string value) => Word.Matches(value)
        .Select(x => CanonicalWord(x.Value)).Where(x => !StopWords.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string CanonicalWord(string value) => value.TrimEnd('.').ToLowerInvariant() switch
    {
        "led" => "lead",
        "teams" => "team",
        "engineers" => "engineer",
        "systems" => "system",
        "services" => "service",
        "applications" => "application",
        "products" => "product",
        "models" => "model",
        "tools" => "tool",
        "workflows" => "workflow",
        "releases" => "release",
        "standards" => "standard",
        "delivers" or "delivered" or "delivery" => "deliver",
        "implemented" or "implementing" => "implement",
        "measured" or "measuring" or "unmeasured" => "measure",
        "impact" => "effect",
        "attempts" => "attempt",
        "improvements" => "improvement",
        _ => value.TrimEnd('.').ToLowerInvariant()
    };
}

public sealed class ResumeCompositionOrchestrator(
    IEnumerable<IResumeCompositionProvider> providers,
    CompositionValidator validator)
{
    public async Task<(IReadOnlyList<CompositionBlock> Blocks, bool Used, string? Provider, IReadOnlyList<string> Warnings)>
        ComposeAsync(ProjectionManifest manifest, string jobDescription, CompilationOptions options,
            CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        // The initial state is selected human prose, never model-authored text.
        var source = manifest.Sections.Select(packet => new CompositionBlock(
            packet.SectionId,
            string.Join("\n\n", packet.Claims.Select(x => x.Prose.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()
                .Select(text => packet.Kind is not ("experience" or "summary" or "project") ||
                                ".!?".Contains(text[^1]) ? text : text + ".")),
            packet.Claims.Select(x => x.Claim.Id).Distinct().ToList(),
            packet.Claims.SelectMany(x => x.EvidenceIds).Distinct().ToList())).ToList();
        if (!options.ComposeProse) return (source, false, null, []);

        var provider = providers.FirstOrDefault(x => x.IsAvailable &&
            (string.IsNullOrWhiteSpace(options.CompositionProvider) ||
             x.ProviderId.Equals(options.CompositionProvider, StringComparison.OrdinalIgnoreCase)));
        if (provider is null) return (source, false, null, ["No requested composition provider was available; retained selected human prose."]);

        IReadOnlyList<CompositionBlock> current = source;
        var editableSectionIds = manifest.Sections
            .Where(section => !section.Kind.Equals("publication", StringComparison.OrdinalIgnoreCase) &&
                              !section.Kind.Equals("additional_experience", StringComparison.OrdinalIgnoreCase))
            .Select(section => section.SectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (editableSectionIds.Count == 0) return (source, false, null, []);
        var editableSource = source.Where(block => editableSectionIds.Contains(block.SectionId)).ToList();
        var editableManifest = manifest with
        {
            Sections = manifest.Sections.Where(section => editableSectionIds.Contains(section.SectionId)).ToList()
        };
        var warnings = new List<string>();
        var acceptedPass = false;
        var repairNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pass in new[] { CompositionPass.Tighten, CompositionPass.HumanVoice })
        {
            try
            {
                var passManifest = editableManifest with
                {
                    Sections = editableManifest.Sections.Select(section => repairNotes.TryGetValue(section.SectionId, out var note)
                        ? section with { Intent = section.Intent + " Previous edit was rejected. Correct these specific errors while editing the immutable source: " + note }
                        : section).ToList()
                };
                var draft = await provider.RunPassAsync(
                    new CompositionPassRequest(pass, jobDescription, passManifest, editableSource,
                        current.Where(block => editableSectionIds.Contains(block.SectionId)).ToList())
                    { Progress = progress }, cancellationToken);
                var returned = draft.Blocks
                    .GroupBy(block => block.SectionId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
                var next = new List<CompositionBlock>(current.Count);
                var acceptedSections = 0;
                foreach (var sourceBlock in source)
                {
                    var currentBlock = current.Single(block =>
                        block.SectionId.Equals(sourceBlock.SectionId, StringComparison.OrdinalIgnoreCase));
                    if (!editableSectionIds.Contains(sourceBlock.SectionId))
                    {
                        next.Add(currentBlock);
                        continue;
                    }
                    if (draft.RejectedSections.TryGetValue(sourceBlock.SectionId, out var rejection))
                    {
                        repairNotes[sourceBlock.SectionId] = rejection;
                        next.Add(currentBlock);
                        continue;
                    }
                    if (!returned.TryGetValue(sourceBlock.SectionId, out var candidates) || candidates.Count != 1)
                    {
                        next.Add(currentBlock);
                        warnings.Add($"Discarded {pass} edit for '{sourceBlock.SectionId}': section was missing or duplicated.");
                        continue;
                    }
                    var packet = editableManifest.Sections.Single(section =>
                        section.SectionId.Equals(sourceBlock.SectionId, StringComparison.OrdinalIgnoreCase));
                    var sectionManifest = manifest with { Sections = [packet] };
                    var errors = validator.Validate(
                        new CompositionDraft([candidates[0]], draft.Warnings), [sourceBlock], sectionManifest);
                    if (errors.Count > 0)
                    {
                        next.Add(currentBlock);
                        repairNotes[sourceBlock.SectionId] = string.Join(" ", errors);
                        warnings.Add($"Discarded {pass} edit for '{sourceBlock.SectionId}': {string.Join(" ", errors)}");
                        continue;
                    }
                    next.Add(candidates[0]);
                    acceptedSections++;
                }
                foreach (var unexpected in returned.Keys.Where(id =>
                             source.All(block => !block.SectionId.Equals(id, StringComparison.OrdinalIgnoreCase))))
                    warnings.Add($"Discarded {pass} edit for unknown section '{unexpected}'.");
                current = next;
                acceptedPass |= acceptedSections > 0;
                warnings.AddRange(draft.Warnings);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                warnings.Add($"Discarded {pass} pass after provider failure: {ex.Message}");
            }
        }
        return (current, acceptedPass, acceptedPass ? provider.ProviderId : null, warnings);
    }
}
