using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>
/// Local Ollama Nimble adapter for closed-set ingestion decisions. It cannot generate a value
/// outside the candidates supplied by the deterministic pipeline.
/// </summary>
public sealed partial class NimbleResumeDecisionProvider : IResumeDecisionProvider
{
    private readonly HttpClient _http;
    private readonly NimbleOptions _options;

    public string ProviderName => "ollama-nimble";

    public NimbleResumeDecisionProvider(HttpClient http, IOptions<NimbleOptions> options)
    {
        _http = http;
        _options = options.Value;

        if (!_options.Enabled)
            throw new InvalidOperationException("Nimble is disabled. Set Nimble:Enabled to use local decision assistance.");
        if (_options.MaxStateCharacters is < 256 or > 16_000)
            throw new InvalidOperationException("Nimble:MaxStateCharacters must be between 256 and 16000.");

        var baseUri = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (baseUri.Scheme != Uri.UriSchemeHttp || !baseUri.IsLoopback)
            throw new InvalidOperationException("Nimble:BaseUrl must be a local HTTP loopback address.");
        _http.BaseAddress = baseUri;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("lucidRESUME/2.x");
    }

    public async Task<ResumeDecisionResult> DecideAsync(
        ResumeDecisionRequest request,
        CancellationToken ct = default)
    {
        if (request.Candidates.Count is < 2 or > 26)
            throw new ArgumentException("Nimble choice decisions require between 2 and 26 candidates.", nameof(request));

        var state = request.State.Length > _options.MaxStateCharacters
            ? request.State[.._options.MaxStateCharacters]
            : request.State;
        if (_options.RedactContactDetails)
            state = Redact(state);

        var payload = new NimbleRequest(
            _options.Model,
            state,
            new Dictionary<string, NimbleQuestion>(StringComparer.Ordinal)
            {
                ["decision"] = new("choice", request.Instructions, request.Candidates)
            });

        using var response = await _http.PostAsJsonAsync("v1/systemone", payload, NimbleJsonContext.Default.NimbleRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Nimble returned HTTP {(int)response.StatusCode}. Request id: {ReadRequestId(response) ?? "unavailable"}.",
                null, response.StatusCode);

        var parsed = JsonSerializer.Deserialize(responseBody, NimbleJsonContext.Default.NimbleResponse)
                     ?? throw new InvalidDataException("Nimble returned an empty response.");
        if (string.IsNullOrWhiteSpace(parsed.Model))
            throw new InvalidDataException("Nimble response did not identify the model used.");
        if (!parsed.Answers.TryGetValue("decision", out var answer))
            throw new InvalidDataException("Nimble response did not contain the requested decision.");
        if (!string.Equals(answer.Type, "choice", StringComparison.Ordinal))
            throw new InvalidDataException($"Nimble returned answer type '{answer.Type}' instead of 'choice'.");
        if (string.IsNullOrWhiteSpace(answer.Choice) || !request.Candidates.ContainsKey(answer.Choice))
            throw new InvalidDataException("Nimble returned a choice that was not offered.");

        var probabilities = request.Candidates.Keys.ToDictionary(key => key, _ => 0d, StringComparer.Ordinal);
        foreach (var (key, probability) in answer.Probabilities)
        {
            if (!probabilities.ContainsKey(key))
                throw new InvalidDataException($"Nimble returned a probability for unknown choice '{key}'.");
            if (!double.IsFinite(probability) || probability is < 0 or > 1)
                throw new InvalidDataException($"Nimble returned an invalid probability for '{key}'.");
            probabilities[key] = probability;
        }

        var selectedProbability = probabilities[answer.Choice];
        if (selectedProbability <= 0)
            throw new InvalidDataException("Nimble did not return a positive probability for its selected choice.");
        if (!double.IsFinite(answer.Confidence) || answer.Confidence is < 0 or > 1)
            throw new InvalidDataException("Nimble returned an invalid confidence value.");
        var probabilityTotal = probabilities.Values.Sum();
        if (probabilityTotal is < 0.98 or > 1.02)
            throw new InvalidDataException($"Nimble probabilities sum to {probabilityTotal:F4}, not 1.");
        if (selectedProbability + 1e-9 < probabilities.Values.Max())
            throw new InvalidDataException("Nimble selected a choice that was not the most probable option.");

        return new ResumeDecisionResult(
            answer.Choice,
            probabilities,
            answer.Confidence,
            ProviderName,
            parsed.Model,
            parsed.Usage.InputTokens,
            parsed.Usage.OutputTokens,
            ReadRequestId(response));
    }

    internal static string Redact(string value)
    {
        value = EmailPattern().Replace(value, "[email]");
        value = UrlPattern().Replace(value, "[url]");
        return PhonePattern().Replace(value, "[phone]");
    }

    private static string? ReadRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"https?://\S+|\b(?:www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d .()\-]{7,}\d)(?!\w)")]
    private static partial Regex PhonePattern();
}

internal sealed record NimbleRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("questions")] Dictionary<string, NimbleQuestion> Questions);

internal sealed record NimbleQuestion(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("criteria")] IReadOnlyDictionary<string, string> Criteria);

internal sealed class NimbleResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("answers")]
    public Dictionary<string, NimbleAnswer> Answers { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("usage")]
    public NimbleUsage Usage { get; set; } = new();
}

internal sealed class NimbleAnswer
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("choice")]
    public string Choice { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("probabilities")]
    public Dictionary<string, double> Probabilities { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class NimbleUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

[JsonSerializable(typeof(NimbleRequest))]
[JsonSerializable(typeof(NimbleResponse))]
internal partial class NimbleJsonContext : JsonSerializerContext;
