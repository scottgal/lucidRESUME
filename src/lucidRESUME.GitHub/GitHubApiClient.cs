using System.Net;
using System.Net.Http.Json;
using lucidRESUME.GitHub.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucidRESUME.GitHub;

/// <summary>
/// Typed HttpClient for the GitHub REST API.
/// Handles pagination, rate limit tracking, and optional auth.
/// </summary>
public sealed class GitHubApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubApiClient> _logger;
    private readonly GitHubImportOptions _options;
    private int _remainingRequests = -1;
    private DateTimeOffset _rateLimitReset;

    public GitHubApiClient(HttpClient http, IOptions<GitHubImportOptions> options, ILogger<GitHubApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrEmpty(_options.PersonalAccessToken))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);
    }

    public int RemainingRequests => _remainingRequests;
    public DateTimeOffset RateLimitReset => _rateLimitReset;

    /// <summary>
    /// Throws GitHubRateLimitException if fewer than <paramref name="needed"/> requests remain.
    /// Call before a batch of API calls to fail early instead of mid-operation.
    /// </summary>
    public void EnsureBudget(int needed = 1)
    {
        if (_remainingRequests >= 0 && _remainingRequests < needed)
            throw new GitHubRateLimitException(_rateLimitReset);
    }

    public async Task<GitHubProfile> GetUserProfileAsync(string username, CancellationToken ct)
    {
        var response = await GetAsync($"users/{username}", ct);
        return (await response.Content.ReadFromJsonAsync<GitHubProfile>(ct))!;
    }

    public async Task<List<GitHubRepo>> GetUserReposAsync(string username, CancellationToken ct)
    {
        var allRepos = new List<GitHubRepo>();
        var page = 1;

        while (true)
        {
            var response = await GetAsync($"users/{username}/repos?per_page=100&sort=pushed&page={page}", ct);
            var repos = await response.Content.ReadFromJsonAsync<List<GitHubRepo>>(ct);
            if (repos is null || repos.Count == 0) break;
            allRepos.AddRange(repos);
            if (repos.Count < 100) break;
            page++;
        }

        return allRepos;
    }

    public async Task<string?> GetRepoReadmeAsync(string owner, string repo, CancellationToken ct)
    {
        try
        {
            var response = await GetAsync($"repos/{owner}/{repo}/readme", ct);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("content", out var content))
            {
                var base64 = content.GetString()?.Replace("\n", "");
                if (base64 != null)
                    return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
        }
        catch (Exception) when (true) { /* no README, bad base64, etc. */ }
        return null;
    }

    public async Task<Dictionary<string, long>> GetRepoLanguagesAsync(string owner, string repo, CancellationToken ct)
    {
        var response = await GetAsync($"repos/{owner}/{repo}/languages", ct);
        return (await response.Content.ReadFromJsonAsync<Dictionary<string, long>>(ct))
               ?? new Dictionary<string, long>();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
    {
        if (_remainingRequests == 0)
            throw new GitHubRateLimitException(_rateLimitReset);

        var response = await _http.GetAsync(path, ct);

        // Track rate limit headers
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining))
        {
            if (int.TryParse(remaining.FirstOrDefault(), out var rem))
                _remainingRequests = rem;
        }
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset))
        {
            if (long.TryParse(reset.FirstOrDefault(), out var epoch))
                _rateLimitReset = DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("GitHub rate limit hit. Remaining: {Remaining}, resets at {Reset}",
                _remainingRequests, _rateLimitReset);
            throw new GitHubRateLimitException(_rateLimitReset);
        }

        response.EnsureSuccessStatusCode();
        return response;
    }
}
