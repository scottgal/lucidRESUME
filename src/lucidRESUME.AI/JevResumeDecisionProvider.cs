using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace lucidRESUME.AI;

/// <summary>
/// TypeSafe Jev adapter for closed-set ingestion decisions. It cannot generate a value
/// outside the candidates supplied by the deterministic pipeline.
/// </summary>
public sealed partial class JevResumeDecisionProvider : IResumeDecisionProvider
{
    private readonly HttpClient _http;
    private readonly JevOptions _options;

    public string ProviderName => "typesafe-jev";

    public JevResumeDecisionProvider(HttpClient http, IOptions<JevOptions> options)
    {
        _http = http;
        _options = options.Value;

        if (!_options.Enabled)
            throw new InvalidOperationException("Jev is disabled. Set Jev:Enabled only after reviewing its data policy.");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Jev is enabled but Jev:ApiKey is empty.");
        if (_options.MaxStateCharacters is < 256 or > 32_000)
            throw new InvalidOperationException("Jev:MaxStateCharacters must be between 256 and 32000.");

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("lucidRESUME/2.x");
    }

    public async Task<ResumeDecisionResult> DecideAsync(
        ResumeDecisionRequest request,
        CancellationToken ct = default)
    {
        if (request.Candidates.Count is < 2 or > 255)
            throw new ArgumentException("Jev choice decisions require between 2 and 255 candidates.", nameof(request));

        var state = request.State.Length > _options.MaxStateCharacters
            ? request.State[.._options.MaxStateCharacters]
            : request.State;
        if (_options.RedactContactDetails)
            state = Redact(state);

        var payload = new JevRequest(
            _options.Model,
            state,
            new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                ["decision"] = new("choice", request.Instructions, request.Candidates)
            });

        using var response = await _http.PostAsJsonAsync("v1/systemone", payload, JevJsonContext.Default.JevRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Jev returned HTTP {(int)response.StatusCode}. Request id: {ReadRequestId(response) ?? "unavailable"}.",
                null, response.StatusCode);

        var parsed = JsonSerializer.Deserialize(responseBody, JevJsonContext.Default.JevResponse)
                     ?? throw new InvalidDataException("Jev returned an empty response.");
        if (string.IsNullOrWhiteSpace(parsed.Model))
            throw new InvalidDataException("Jev response did not identify the model used.");
        if (!parsed.Answers.TryGetValue("decision", out var answer))
            throw new InvalidDataException("Jev response did not contain the requested decision.");
        if (!string.Equals(answer.Type, "choice", StringComparison.Ordinal))
            throw new InvalidDataException($"Jev returned answer type '{answer.Type}' instead of 'choice'.");
        if (string.IsNullOrWhiteSpace(answer.Choice) || !request.Candidates.ContainsKey(answer.Choice))
            throw new InvalidDataException("Jev returned a choice that was not offered.");

        var probabilities = request.Candidates.Keys.ToDictionary(key => key, _ => 0d, StringComparer.Ordinal);
        foreach (var (key, probability) in answer.Probabilities)
        {
            if (!probabilities.ContainsKey(key))
                throw new InvalidDataException($"Jev returned a probability for unknown choice '{key}'.");
            if (!double.IsFinite(probability) || probability is < 0 or > 1)
                throw new InvalidDataException($"Jev returned an invalid probability for '{key}'.");
            probabilities[key] = probability;
        }

        var selectedProbability = probabilities[answer.Choice];
        if (selectedProbability <= 0)
            throw new InvalidDataException("Jev did not return a positive probability for its selected choice.");
        if (!double.IsFinite(answer.Confidence) || answer.Confidence is < 0 or > 1)
            throw new InvalidDataException("Jev returned an invalid confidence value.");
        var probabilityTotal = probabilities.Values.Sum();
        if (probabilityTotal is < 0.98 or > 1.02)
            throw new InvalidDataException($"Jev probabilities sum to {probabilityTotal:F4}, not 1.");
        if (selectedProbability + 1e-9 < probabilities.Values.Max())
            throw new InvalidDataException("Jev selected a choice that was not the most probable option.");

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
        response.Headers.TryGetValues("x-typesafe-request-id", out var values) ? values.FirstOrDefault() : null;

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"https?://\S+|\b(?:www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d .()\-]{7,}\d)(?!\w)")]
    private static partial Regex PhonePattern();
}

internal sealed record JevRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("questions")] Dictionary<string, JevQuestion> Questions);

internal sealed record JevQuestion(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("criteria")] IReadOnlyDictionary<string, string> Criteria);

internal sealed class JevResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("answers")]
    public Dictionary<string, JevAnswer> Answers { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("usage")]
    public JevUsage Usage { get; set; } = new();
}

internal sealed class JevAnswer
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

internal sealed class JevUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

[JsonSerializable(typeof(JevRequest))]
[JsonSerializable(typeof(JevResponse))]
internal partial class JevJsonContext : JsonSerializerContext;
