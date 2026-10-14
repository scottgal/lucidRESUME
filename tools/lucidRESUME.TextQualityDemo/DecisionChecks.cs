using System.Security.Cryptography;
using System.Text;
using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using Microsoft.Extensions.Options;

/// <summary>Small demo facade over the application's validated Nimble adapter.</summary>
public sealed class DecisionChecks : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly NimbleResumeDecisionProvider _provider;

    public DecisionChecks(Uri server, string model = "nimble") =>
        _provider = new(_http, Options.Create(new NimbleOptions
        { Enabled = true, BaseUrl = server.ToString(), Model = model, RedactContactDetails = false }));

    public Task<ResumeDecisionResult> ClassifyAsync(string text, string instructions,
        IReadOnlyDictionary<string, string> criteria, CancellationToken ct = default)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return _provider.DecideAsync(new("demo", "synthetic", fingerprint, text, instructions, criteria), ct);
    }

    public void Dispose() => _http.Dispose();
}
