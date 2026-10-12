using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using lucidRESUME.Compiler;
using lucidRESUME.Core.Models.Resume;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>Local structured prose editing over already-selected evidence packets.</summary>
public sealed class OllamaResumeCompositionProvider(HttpClient http, IOptions<OllamaOptions> configured,
    ILogger<OllamaResumeCompositionProvider>? logger = null, ReusableProseStore? proseStore = null)
    : IResumeCompositionProvider
{
    private readonly OllamaOptions _options = configured.Value;

    public string ProviderId => "ollama";
    public bool IsAvailable => _options.CompositionEnabled &&
                               !string.IsNullOrWhiteSpace(_options.Model) && LocalBaseUri() is not null;

    public async Task<CompositionDraft> RunPassAsync(CompositionPassRequest request,
        CancellationToken cancellationToken = default)
    {
        var baseUri = LocalBaseUri() ?? throw new InvalidOperationException(
            "Ollama prose editing requires a local HTTP loopback address.");
        if (!IsAvailable) throw new InvalidOperationException("Ollama prose editing is not enabled.");

        var factsBySection = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var factCounter = 0;
        foreach (var block in request.SourceBlocks)
        {
            var facts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match number in Regex.Matches(block.Text, @"(?<![\w-])\d+(?:[.,]\d+)*(?:%\+?|x|\+)?"))
                if (!facts.ContainsValue(number.Value)) facts[$"[VALUE_{++factCounter}]"] = number.Value;
            factsBySection[block.SectionId] = facts;
        }
        string Protect(string sectionId, string text) => Regex.Replace(text, @"(?<![\w-])\d+(?:[.,]\d+)*(?:%\+?|x|\+)?",
            match => factsBySection[sectionId].FirstOrDefault(fact => fact.Value == match.Value).Key ?? match.Value);
        string Restore(string sectionId, string text)
        {
            foreach (var fact in factsBySection[sectionId]) text = text.Replace(fact.Key, fact.Value, StringComparison.Ordinal);
            if (Regex.IsMatch(text, @"\[VALUE[_ ]?\d+\]", RegexOptions.IgnoreCase)) throw new InvalidDataException("Edit mixed numeric facts between sections.");
            return text;
        }
        var edited = new List<CompositionBlock>();
        var warnings = new List<string>();
        // Tighten creates a reusable, job-neutral account; HumanVoice adapts its emphasis.
        var jobBrief = request.Pass == CompositionPass.Tighten ? "" : request.JobDescription;
        var modelIdentity = proseStore is null ? null : await ResolveModelIdentityAsync(baseUri, cancellationToken);
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<CompositionBlock>();
        var validator = new CompositionValidator();
        foreach (var current in request.CurrentBlocks)
        {
            var packet = request.Manifest.Sections.Single(section => section.SectionId == current.SectionId);
            var source = request.SourceBlocks.Single(block => block.SectionId == current.SectionId);
            var key = ReusableProseStore.Fingerprint(JsonSerializer.Serialize(new
            {
                version = SystemInstructions + " neutral-tighten-v1",
                modelIdentity,
                _options.Model,
                _options.NumCtx,
                endpoint = baseUri.ToString(),
                pass = request.Pass.ToString(),
                jobBrief,
                source.Text,
                source.ClaimIds,
                source.EvidenceIds,
                currentDraft = current.Text,
                packet.Intent,
                packet.MaximumWords,
                packet.Kind,
                claims = packet.Claims.Select(claim => new
                {
                    claim.Claim.Id,
                    claim.Claim.Subject,
                    claim.Claim.Statement,
                    skills = claim.Claim.Concepts.All.Order(StringComparer.Ordinal).ToArray()
                })
            }));
            keys[current.SectionId] = key;
            var account = modelIdentity is null ? null : await proseStore!.FindAsync(key, cancellationToken);
            if (account is not null && (account.SourceText != source.Text || account.ModelIdentity != modelIdentity ||
                !account.ClaimIds.SequenceEqual(source.ClaimIds) || !account.EvidenceIds.SequenceEqual(source.EvidenceIds) ||
                !account.Skills.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(packet.Claims.SelectMany(claim => claim.Claim.Concepts.All))))
                account = null;
            var candidate = account is null ? null : current with { Text = account.Text };
            if (candidate is not null && (account!.RetainedSource ? account.Text == current.Text
                    : validator.Validate(new CompositionDraft([candidate], []), [source],
                        request.Manifest with { Sections = [packet] }).Count == 0))
            {
                edited.Add(candidate);
                warnings.AddRange(account.ReviewNotes);
            }
            else pending.Add(current);
        }
        if (edited.Count > 0)
            request.Progress?.Report($"Reusing {edited.Count} validated prose account(s) · {request.Pass}");
        var summaryIds = request.Manifest.Sections
            .Where(section => section.Kind.Equals("summary", StringComparison.OrdinalIgnoreCase))
            .Select(section => section.SectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var batches = pending.Where(block => summaryIds.Contains(block.SectionId))
            .Select(block => new[] { block })
            .Concat(pending.Where(block => !summaryIds.Contains(block.SectionId)).Chunk(3)).ToArray();
        var batchNumber = 0;
        var passName = request.Pass == CompositionPass.Tighten ? "Tightening prose (pass 1/2)" : "Polishing prose (pass 2/2)";
        foreach (var batch in batches)
        {
            batchNumber++;
            request.Progress?.Report($"{passName} · batch {batchNumber}/{batches.Length} · local model {_options.Model}…");
            var timer = Stopwatch.StartNew();
            var sections = batch.Select(current =>
            {
                var source = request.SourceBlocks.Single(block => block.SectionId == current.SectionId);
                var packet = request.Manifest.Sections.Single(section => section.SectionId == current.SectionId);
                return new
                {
                    sectionId = current.SectionId,
                    immutable_human_source = Protect(current.SectionId, source.Text),
                    current_draft = current.Text == source.Text ? null : Protect(current.SectionId, current.Text),
                    section_brief = packet.Intent,
                    selected_claims = packet.Claims.Select(claim => new
                    {
                        id = claim.Claim.Id,
                        subject = claim.Claim.Subject,
                        statement = Protect(current.SectionId, claim.Claim.Statement),
                        skills = claim.Claim.Concepts.All,
                        evidence_ids = claim.EvidenceIds
                    }),
                    maximum_words = packet.MaximumWords
                };
            }).ToList();
            var sectionIds = batch.Select(block => block.SectionId).ToArray();
            var schema = new
            {
                type = "object",
                properties = new
                {
                    sections = new
                    {
                        type = "array",
                        minItems = batch.Length,
                        maxItems = batch.Length,
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                sectionId = new { type = "string", @enum = sectionIds },
                                text = new { type = "string" }
                            },
                            required = new[] { "sectionId", "text" },
                            additionalProperties = false
                        }
                    }
                },
                required = new[] { "sections" },
                additionalProperties = false
            };
            var prompt = JsonSerializer.Serialize(new
            {
                pass = request.Pass.ToString(),
                target_role = batch.All(block => summaryIds.Contains(block.SectionId))
                    ? string.Empty : jobBrief,
                sections
            });
            var maximumWords = request.Manifest.Sections.Where(packet => sectionIds.Contains(packet.SectionId))
                .Sum(packet => packet.MaximumWords);
            var payload = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = SystemInstructions },
                    new { role = "user", content = prompt }
                },
                stream = false,
                think = false,
                format = schema,
                options = new
                {
                    temperature = 0,
                    num_ctx = _options.NumCtx,
                    num_predict = Math.Clamp(maximumWords * 4 + 250, 600, 1600)
                }
            };
            using var response = await http.PostAsJsonAsync(new Uri(baseUri, "api/chat"), payload,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            logger?.LogInformation("Prose {Pass} batch {Batch}/{Batches}: {Seconds:F1}s; prompt tokens {PromptTokens}, output tokens {OutputTokens}, model {Model}",
                request.Pass, batchNumber, batches.Length, timer.Elapsed.TotalSeconds,
                document.RootElement.TryGetProperty("prompt_eval_count", out var promptCount) ? promptCount.GetInt64() : 0,
                document.RootElement.TryGetProperty("eval_count", out var outputCount) ? outputCount.GetInt64() : 0, _options.Model);
            request.Progress?.Report($"{passName} · completed batch {batchNumber}/{batches.Length} in {timer.Elapsed.TotalSeconds:F0}s");
            var content = document.RootElement.GetProperty("message").GetProperty("content").GetString();
            var result = LlamaSharpResumeCompositionProvider.ParseBatch(content);
            var sourceById = batch.ToDictionary(block => block.SectionId, StringComparer.OrdinalIgnoreCase);
            edited.AddRange(result.Sections.Select(section => sourceById.TryGetValue(section.SectionId, out var original)
                ? original with { Text = Restore(section.SectionId, section.Text) }
                : new CompositionBlock(section.SectionId, section.Text, [], [])));
            warnings.AddRange(result.Warnings ?? []);
            if (modelIdentity is not null)
            {
                foreach (var original in batch)
                {
                    var candidates = edited.Where(block => block.SectionId == original.SectionId).ToList();
                    if (candidates.Count != 1) continue;
                    var packet = request.Manifest.Sections.Single(section => section.SectionId == original.SectionId);
                    var source = request.SourceBlocks.Single(block => block.SectionId == original.SectionId);
                    var errors = validator.Validate(new CompositionDraft(candidates, []), [source],
                        request.Manifest with { Sections = [packet] });
                    if (errors.Count > 0)
                    {
                        edited[edited.FindIndex(block => block.SectionId == original.SectionId)] = original;
                        warnings.AddRange(errors.Select(error => $"Retained prior wording after rejected {request.Pass} edit: {error}"));
                    }
                    await proseStore!.SaveAsync(new ReusableProseAccount
                    {
                        Key = keys[original.SectionId],
                        SourceFingerprint = ReusableProseStore.Fingerprint(JsonSerializer.Serialize(new { source.Text, source.ClaimIds, source.EvidenceIds, skills = packet.Claims.SelectMany(claim => claim.Claim.Concepts.All).Order(StringComparer.Ordinal).ToArray() })),
                        SourceText = source.Text,
                        Text = errors.Count == 0 ? candidates[0].Text : original.Text,
                        RetainedSource = errors.Count > 0,
                        ReviewNotes = errors.Select(error => $"Retained prior wording after rejected {request.Pass} edit: {error}").ToList(),
                        Skills = packet.Claims.SelectMany(claim => claim.Claim.Concepts.All).Distinct().ToList(),
                        ClaimIds = source.ClaimIds.ToList(),
                        EvidenceIds = source.EvidenceIds.ToList(),
                        SubjectIds = packet.Claims.Select(claim => claim.Claim.Subject).Distinct().ToList(),
                        Provider = ProviderId,
                        ModelIdentity = modelIdentity,
                        Pass = request.Pass.ToString(),
                        JobBrief = jobBrief,
                        MaximumWords = packet.MaximumWords
                    }, cancellationToken);
                }
            }
        }
        return new CompositionDraft(edited, warnings);
    }

    private async Task<string?> ResolveModelIdentityAsync(Uri baseUri, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await http.GetAsync(new Uri(baseUri, "api/tags"), timeout.Token);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var name = _options.Model.Contains(':') ? _options.Model : _options.Model + ":latest";
            foreach (var model in json.RootElement.GetProperty("models").EnumerateArray())
                if (model.GetProperty("name").GetString() == name)
                    return model.GetProperty("digest").GetString();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
        { logger?.LogDebug("Model identity unavailable; skipping prose reuse: {Reason}", ex.Message); }
        return null;
    }

    private Uri? LocalBaseUri()
    {
        if (!Uri.TryCreate(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)
            return null;
        return uri;
    }

    private const string SystemInstructions = """
        Edit resume prose using only the immutable human source for each section. Never add a fact,
        number, employer, technology, outcome, or responsibility. Keep every section separate and
        preserve its supplied sectionId. [VALUE_n] markers represent immutable numeric facts.
        Copy those markers exactly where the corresponding fact belongs; never interpret, rename,
        split or replace them with numbers. The client restores their original values. Preserve
        every source marker in role and project passages; the summary may omit facts to stay concise.
        Tighten removes repetition and irrelevant setup; HumanVoice
        makes the tightened text sound like a specific engineer in natural UK English. For the
        professional summary, remove generic praise and preserve the source's grammatical person.
        Do not add "I" or "my" to passages written with an implied subject. Retain the source's grammatical person.
        Preserve named technical details that distinguish the work. Keep metrics and numeric notation exactly as supplied, including 10+, 80% and up to. Rewrite conversational asides as concise professional statements, retaining cancellations, planned work and uncertainty. Fix obvious spelling and capitalization errors. Remove stock praise such as
        "proven track record", "competitive advantages", "rapid ROI" and "technical excellence".
        Preserve personal versus team contribution, qualifiers, negation and causality. Helping must
        not become leading; an intended benefit must not become an achieved result. Use concrete
        actions and supported outcomes, including qualitative outcomes. Never force metrics.
        Keep complete
        sentences and stay within maximum_words. The target role guides emphasis but supplies no
        candidate facts. Return exactly one JSON object with a sections array containing one
        {"sectionId":"...","text":"..."} for each supplied section.
        """;
}
