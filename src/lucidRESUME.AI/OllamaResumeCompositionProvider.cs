using System.Net.Http.Json;
using System.Text.Json;
using lucidRESUME.Compiler;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>Local structured prose editing over already-selected evidence packets.</summary>
public sealed class OllamaResumeCompositionProvider(HttpClient http, IOptions<OllamaOptions> configured)
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

        var edited = new List<CompositionBlock>();
        var warnings = new List<string>();
        var summaryIds = request.Manifest.Sections
            .Where(section => section.Kind.Equals("summary", StringComparison.OrdinalIgnoreCase))
            .Select(section => section.SectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var batches = request.CurrentBlocks.Where(block => summaryIds.Contains(block.SectionId))
            .Select(block => new[] { block })
            .Concat(request.CurrentBlocks.Where(block => !summaryIds.Contains(block.SectionId)).Chunk(3));
        foreach (var batch in batches)
        {
            var sections = batch.Select(current =>
            {
                var source = request.SourceBlocks.Single(block => block.SectionId == current.SectionId);
                var packet = request.Manifest.Sections.Single(section => section.SectionId == current.SectionId);
                return new
                {
                    sectionId = current.SectionId,
                    immutable_human_source = source.Text,
                    current_draft = current.Text,
                    section_brief = packet.Intent,
                    maximum_words = summaryIds.Contains(current.SectionId)
                        ? Math.Min(packet.MaximumWords, 65) : packet.MaximumWords
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
                    ? string.Empty : request.JobDescription,
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
            var content = document.RootElement.GetProperty("message").GetProperty("content").GetString();
            var result = LlamaSharpResumeCompositionProvider.ParseBatch(content);
            var sourceById = batch.ToDictionary(block => block.SectionId, StringComparer.OrdinalIgnoreCase);
            edited.AddRange(result.Sections.Select(section => sourceById.TryGetValue(section.SectionId, out var original)
                ? original with { Text = section.Text }
                : new CompositionBlock(section.SectionId, section.Text, [], [])));
            warnings.AddRange(result.Warnings ?? []);
        }
        return new CompositionDraft(edited, warnings);
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
        preserve its supplied sectionId. Tighten removes repetition and irrelevant setup; HumanVoice
        makes the tightened text sound like a specific engineer in natural UK English. For the
        professional summary, remove generic praise and preserve the source's grammatical person.
        Keep complete
        sentences and stay within maximum_words. The target role guides emphasis but supplies no
        candidate facts. Return exactly one JSON object with a sections array containing one
        {"sectionId":"...","text":"..."} for each supplied section.
        """;
}
