using System.Net.Http.Json;
using System.Text.Json;
using lucidRESUME.Compiler;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class OpenAiResumeCompositionProvider : IResumeCompositionProvider
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ReusableProseStore? _proseStore;
    public string ProviderId => "openai";
    public bool IsAvailable => _options.IsConfigured;

    public OpenAiResumeCompositionProvider(HttpClient http, IOptions<OpenAiOptions> options, ReusableProseStore? proseStore = null)
    {
        _proseStore = proseStore;
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (_options.IsConfigured)
            _http.DefaultRequestHeaders.Authorization = new("Bearer", _options.ApiKey);
    }

    public Task<CompositionDraft> RunPassAsync(CompositionPassRequest request,
        CancellationToken cancellationToken = default) => _proseStore is null
        ? RunUncachedAsync(request, cancellationToken)
        : ReusableProseEditor.RunAsync(request, _proseStore, ProviderId,
            $"{_options.BaseUrl}|{_options.Model}|{_options.MaxTokens}", SystemInstructions(request.Pass),
            pending => RunUncachedAsync(pending, cancellationToken), cancellationToken);

    private async Task<CompositionDraft> RunUncachedAsync(CompositionPassRequest request,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            model = _options.Model,
            store = false,
            max_output_tokens = Math.Min(_options.MaxTokens, 5000),
            instructions = SystemInstructions(request.Pass),
            input = JsonSerializer.Serialize(new
            {
                pass = request.Pass.ToString(),
                immutable_human_sources = request.SourceBlocks,
                current_draft = request.CurrentBlocks.Where(block => request.SourceBlocks.Single(source => source.SectionId == block.SectionId).Text != block.Text),
                target_role = request.Pass == CompositionPass.Tighten ? "" : request.JobDescription,
                section_briefs = request.Manifest.Sections.Select(x => new
                {
                    x.SectionId,
                    x.Intent,
                    x.MaximumWords,
                    experience_sentences_not_to_repeat = x.DistinctFrom,
                    contribution_accounts = x.Accounts,
                    writing_plan = x.WritingPlan?.ForPass(request.Pass),
                    selected_claims = x.Claims.Select(claim => new
                    {
                        claim.Claim.Id,
                        claim.Claim.Subject,
                        claim.Claim.Statement,
                        skills = claim.Claim.Concepts.All,
                        claim.EvidenceIds
                    })
                })
            }),
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "bounded_resume_edit",
                    strict = true,
                    schema = Schema()
                }
            }
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var response = await _http.PostAsJsonAsync("responses", body, cts.Token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        return Parse(OpenAiTailoringService.ExtractOutputText(json));
    }

    private static object Schema() => new
    {
        type = "object",
        properties = new
        {
            blocks = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        sectionId = new { type = "string" },
                        text = new { type = "string" },
                        claimIds = new { type = "array", items = new { type = "string" } },
                        evidenceIds = new { type = "array", items = new { type = "string" } }
                    },
                    required = new[] { "sectionId", "text", "claimIds", "evidenceIds" },
                    additionalProperties = false
                }
            },
            warnings = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "blocks", "warnings" },
        additionalProperties = false
    };

    private static string SystemInstructions(CompositionPass pass) => $"""
        You are performing the {pass} editing pass on a resume compiled from a verified career ledger.
        This is editing, never blank-page generation. Use only facts, numbers, names, dates, technologies,
        claim IDs and evidence IDs present in immutable_human_sources. The job brief controls emphasis only.
        writing_plan focus entries are retrieval hints, not evidence that the candidate satisfies a requirement.
        Answer them with the selected source facts; never fill unmatched terms or missing_details.
        Keep limitations beside the facts they qualify and retain each detail_claim_id in experience prose.
        Follow each section brief. Preserve the candidate's first-person/third-person stance, vocabulary and
        concrete voice. For a summary, synthesize relevant focus from selected_claims without copying a complete
        experience_sentences_not_to_repeat sentence. Keep detailed outcomes in Experience and preserve any
        limitations on facts included in the summary. A role section is a selective account for this vacancy, not a compressed list of every
        duty in the source. Combine overlapping evidence and foreground the most relevant outcome, but retain
        the factual substance represented by every supplied claim ID. Do not add generic leadership language,
        hype, fabricated scale or missing requirements. Do not use em dashes.
        Preserve who did the work: helping or collaborating must not become leading or owning.
        Preserve qualifiers, negation and causality. An intended benefit must not become an achieved result.
        Prefer an action, its concrete object and a supported outcome. Qualitative outcomes are useful;
        never force a number into a passage. Avoid repeating the same outcome in multiple sections.
        For Tighten, remove repetition, repeated setup and filler while keeping names, outcomes and useful
        technical specifics. For HumanVoice, respect the section word budget and requested detail, remove stock phrases such as "proven
        track record", "passionate", "results-driven", "leveraged" and "spearheaded", and favour direct,
        natural UK English. Return every section once and preserve all identifier arrays.
        """;

    internal static CompositionDraft Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The model returned no composition JSON.");
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("The model returned invalid composition JSON.");
        return JsonSerializer.Deserialize<CompositionDraft>(json[start..(end + 1)],
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("The model returned an empty composition result.");
    }
}

public sealed class LlamaSharpResumeCompositionProvider(LlamaSharpRuntime runtime) : IResumeCompositionProvider
{
    public string ProviderId => "llamasharp";
    public bool IsAvailable => runtime.IsAvailable;

    public async Task<CompositionDraft> RunPassAsync(CompositionPassRequest request,
        CancellationToken cancellationToken = default)
    {
        var blocks = new List<CompositionBlock>();
        var warnings = new List<string>();
        foreach (var batch in request.CurrentBlocks.Chunk(3))
        {
            var prompt = "Edit this ordered batch of resume sections. Return JSON only.\n" +
                         JsonSerializer.Serialize(new
                         {
                             pass = request.Pass.ToString(),
                             document_context = new
                             {
                                 target_role = request.JobDescription,
                                 preceding_section_ids = blocks.Select(block => block.SectionId)
                             },
                             sections = batch.Select(current =>
                             {
                                 var source = request.SourceBlocks.Single(x => x.SectionId == current.SectionId);
                                 var packet = request.Manifest.Sections.Single(x => x.SectionId == current.SectionId);
                                 return new
                                 {
                                     sectionId = current.SectionId,
                                     immutable_human_source = source.Text,
                                     current_draft = current.Text,
                                     section_brief = packet.Intent,
                                     experience_sentences_not_to_repeat = packet.DistinctFrom,
                                     contribution_accounts = packet.Accounts,
                                     writing_plan = packet.WritingPlan?.ForPass(request.Pass),
                                     selected_claims = packet.Claims.Select(claim => new
                                     {
                                         claim.Claim.Id,
                                         claim.Claim.Subject,
                                         claim.Claim.Statement,
                                         skills = claim.Claim.Concepts.All,
                                         claim.EvidenceIds
                                     }),
                                     maximum_words = packet.MaximumWords,
                                     allowed_emphasis = packet.RequirementIds.Select(id => request.Manifest.Requirements
                                         .First(requirement => requirement.Id == id).Text)
                                 };
                             })
                         });
            var maximumWords = batch.Sum(current => request.Manifest.Sections
                .Single(section => section.SectionId == current.SectionId).MaximumWords);
            var maximumTokens = Math.Clamp(maximumWords * 3 + 350, 700, 1800);
            var output = await runtime.GenerateAsync(prompt, SystemInstructions, maximumTokens,
                cancellationToken, 0f);
            var result = ParseBatch(output);
            var sourceById = batch.ToDictionary(block => block.SectionId, StringComparer.OrdinalIgnoreCase);
            blocks.AddRange(result.Sections.Select(edit =>
            {
                if (!sourceById.TryGetValue(edit.SectionId, out var current))
                    return new CompositionBlock(edit.SectionId, edit.Text, [], []);
                return current with { Text = edit.Text };
            }));
            warnings.AddRange(result.Warnings);
        }

        return new CompositionDraft(blocks, warnings);
    }

    private const string SystemInstructions = """
            You are a bounded resume editor. Edit, never invent. Every fact, number, name, date and
            technology must already occur in that section's immutable_human_source. Follow each
            section_brief. allowed_emphasis may change focus, not facts.
            For a summary, synthesize relevant focus from selected_claims without copying any complete
            experience_sentences_not_to_repeat sentence. Keep detailed outcomes in Experience and retain
            limitations on facts included in the summary.
            writing_plan focus is a retrieval hint, not proof of a qualification. Use only selected source facts;
            do not fill unmatched terms or missing_details. Keep limitations beside their facts and retain detail_claim_ids.
            Make the sections work as one coherent resume without moving facts between sections. Write selective, compact role accounts
            rather than lists of every duty. Tighten means combine overlap and remove repetition, setup and
            filler. HumanVoice means respect the section budget and requested detail and remove stock AI wording while preserving
            the source's stance and rhythm. Prefer direct, natural UK English. Never use an em dash.
            Preserve personal versus team contribution, qualifiers, negation and causality. Helping must
            not become leading; intended benefits must not become achieved results. Qualitative outcomes
            are useful; never force a metric or generic claims of leadership into the prose.
            Return exactly one JSON object shaped as
            {"sections":[{"sectionId":"the supplied id","text":"edited prose"}],"warnings":[]}.
            Return every supplied section exactly once and no additional sections.
            """;

    internal static SectionBatchEdit ParseBatch(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The model returned no section JSON.");
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("The model returned invalid section JSON.");
        try
        {
            // Some local models flatten a one-item batch despite the requested schema.
            // Recover the explicit identity, never infer a section from unbound prose.
            using var document = JsonDocument.Parse(json[start..(end + 1)]);
            if (!document.RootElement.TryGetProperty("sections", out _) &&
                document.RootElement.TryGetProperty("sectionId", out var id) && id.ValueKind == JsonValueKind.String &&
                document.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()) && !string.IsNullOrWhiteSpace(text.GetString()))
                return new SectionBatchEdit([new SectionEdit(id.GetString()!, text.GetString()!)], []);
            var result = JsonSerializer.Deserialize<SectionBatchEdit>(json[start..(end + 1)], JsonOptions);
            if (result?.Sections is null || result.Sections.Any(section => section is null || string.IsNullOrWhiteSpace(section.SectionId) || string.IsNullOrWhiteSpace(section.Text)))
                throw new InvalidDataException("The model returned missing or empty section fields.");
            return result;
        }
        catch (JsonException)
        {
            var recovered = RecoverCompleteSections(json);
            if (recovered.Count == 0)
                throw new InvalidDataException("The model returned truncated JSON without a complete section edit.");
            return new SectionBatchEdit(recovered,
                [$"The local model response was truncated; recovered {recovered.Count} complete section edit(s)."]);
        }
    }

    private static IReadOnlyList<SectionEdit> RecoverCompleteSections(string json)
    {
        var property = json.IndexOf("\"sections\"", StringComparison.OrdinalIgnoreCase);
        var arrayStart = property < 0 ? -1 : json.IndexOf('[', property);
        if (arrayStart < 0) return [];

        var edits = new List<SectionEdit>();
        var objectStart = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = arrayStart + 1; index < json.Length; index++)
        {
            var character = json[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }
            if (character == '"')
            {
                inString = true;
                continue;
            }
            if (character == '{')
            {
                if (depth++ == 0) objectStart = index;
                continue;
            }
            if (character != '}' || depth == 0) continue;
            depth--;
            if (depth != 0 || objectStart < 0) continue;
            try
            {
                var edit = JsonSerializer.Deserialize<SectionEdit>(json[objectStart..(index + 1)], JsonOptions);
                if (edit is not null && !string.IsNullOrWhiteSpace(edit.SectionId) && !string.IsNullOrWhiteSpace(edit.Text))
                    edits.Add(edit);
            }
            catch (JsonException)
            {
                // Only complete, independently valid objects are recoverable.
            }
            objectStart = -1;
        }
        return edits;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal sealed record SectionBatchEdit(IReadOnlyList<SectionEdit> Sections, IReadOnlyList<string> Warnings);
    internal sealed record SectionEdit(string SectionId, string Text);
}
