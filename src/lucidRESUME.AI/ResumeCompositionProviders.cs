using System.Net.Http.Json;
using System.Text.Json;
using lucidRESUME.Compiler;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

public sealed class OpenAiResumeCompositionProvider : IResumeCompositionProvider
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    public string ProviderId => "openai";
    public bool IsAvailable => _options.IsConfigured;

    public OpenAiResumeCompositionProvider(HttpClient http, IOptions<OpenAiOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (_options.IsConfigured)
            _http.DefaultRequestHeaders.Authorization = new("Bearer", _options.ApiKey);
    }

    public async Task<CompositionDraft> RunPassAsync(CompositionPassRequest request,
        CancellationToken cancellationToken = default)
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
                current_draft = request.CurrentBlocks,
                section_briefs = request.Manifest.Sections.Select(x => new
                {
                    x.SectionId,
                    x.Intent,
                    x.MaximumWords,
                    allowed_emphasis = x.RequirementIds.Select(id => request.Manifest.Requirements
                        .First(requirement => requirement.Id == id).Text)
                })
            }),
            text = new
            {
                format = new
                {
                    type = "json_schema", name = "bounded_resume_edit", strict = true,
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
                        sectionId = new { type = "string" }, text = new { type = "string" },
                        claimIds = new { type = "array", items = new { type = "string" } },
                        evidenceIds = new { type = "array", items = new { type = "string" } }
                    },
                    required = new[] { "sectionId", "text", "claimIds", "evidenceIds" },
                    additionalProperties = false
                }
            },
            warnings = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "blocks", "warnings" }, additionalProperties = false
    };

    private static string SystemInstructions(CompositionPass pass) => $"""
        You are performing the {pass} editing pass on a resume compiled from a verified career ledger.
        This is editing, never blank-page generation. Use only facts, numbers, names, dates, technologies,
        claim IDs and evidence IDs present in immutable_human_sources. allowed_emphasis controls emphasis only.
        Preserve the candidate's first-person/third-person stance, vocabulary and concrete voice. Do not add
        generic leadership language, hype, fabricated scale or missing requirements. Do not use em dashes.
        For Tighten, remove repetition and filler without erasing concrete detail. For HumanVoice, remove stock
        AI phrasing and keep the source's rhythm. Return every section once and preserve all identifier arrays.
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
        foreach (var current in request.CurrentBlocks)
        {
            var source = request.SourceBlocks.Single(x => x.SectionId == current.SectionId);
            var packet = request.Manifest.Sections.Single(x => x.SectionId == current.SectionId);
            var allowed = packet.RequirementIds.Select(id => request.Manifest.Requirements
                .First(requirement => requirement.Id == id).Text).ToList();
            var prompt = "Edit this single section and return JSON only.\n" + JsonSerializer.Serialize(new
            {
                pass = request.Pass.ToString(),
                immutable_human_source = source.Text,
                current_draft = current.Text,
                maximum_words = packet.MaximumWords,
                allowed_emphasis = allowed
            });
            var system = """
                You are a bounded resume editor. Edit, never invent. Every fact, number, name, date and
                technology must already occur in immutable_human_source. allowed_emphasis may change focus,
                not facts. Tighten means remove repetition and filler. HumanVoice means remove stock AI
                wording while preserving the source's stance and rhythm. Never use an em dash. Return exactly
                one JSON object shaped as {"text":"edited prose","warnings":[]}.
                """;
            var output = await runtime.GenerateAsync(prompt, system, 1400, cancellationToken);
            var result = ParseSection(output);
            blocks.Add(current with { Text = result.Text });
            warnings.AddRange(result.Warnings);
        }
        return new CompositionDraft(blocks, warnings);
    }

    private static SectionEdit ParseSection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The model returned no section JSON.");
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("The model returned invalid section JSON.");
        return JsonSerializer.Deserialize<SectionEdit>(json[start..(end + 1)],
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("The model returned an empty section edit.");
    }

    private sealed record SectionEdit(string Text, IReadOnlyList<string> Warnings);
}
