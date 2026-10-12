using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

public sealed record PublicationImportResult(IReadOnlyList<Project> Articles, int Reused, IReadOnlyList<string> Warnings);

/// <summary>Explicit refresh only. Resume generation uses the persisted catalog, never files or HTTP.</summary>
public static class PublicationCatalog
{
    public static async Task<PublicationImportResult> ImportAsync(string directory, IEnumerable<Project> previous,
        DateOnly? asOf = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose the blog's Markdown directory.");
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var known = previous.Where(article => article.Url is not null).GroupBy(article => article.Url!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<Project>();
        var warnings = new List<string>();
        var reused = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Contains('.')) continue; // Blog translation suffixes.
            string markdown;
            try { markdown = await File.ReadAllTextAsync(file, ct); }
            catch (IOException) { warnings.Add($"Could not read {Path.GetFileName(file)}."); continue; }
            var date = Regex.Match(markdown, @"<datetime\b[^>]*>(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase);
            var title = Regex.Match(markdown, @"^#\s+(.+)$", RegexOptions.Multiline);
            if (!title.Success || !DateOnly.TryParse(date.Groups[1].Value, out var published) || published > today) continue;
            var slug = stem.Replace('_', '-').Replace(' ', '-').ToLowerInvariant();
            var url = "https://www.mostlylucid.net/blog/" + Uri.EscapeDataString(slug);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))).ToLowerInvariant();
            if (known.TryGetValue(url, out var existing) && existing.EvidenceMetadata.GetValueOrDefault("content_hash") == hash)
            { result.Add(existing); reused++; continue; }
            var categories = Regex.Match(markdown, @"<!--\s*category\s*--\s*(.*?)-->", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value.Trim();
            var prose = Regex.Replace(markdown, @"```.*?```|<!--.*?-->|<[^>]+>|!\[[^\]]*\]\([^)]*\)", " ", RegexOptions.Singleline);
            prose = Regex.Replace(prose, @"\[([^\]]+)\]\([^)]*\)", "$1");
            var passages = Regex.Split(prose, @"\r?\n\s*\r?\n").Select(text => Regex.Replace(text, @"\s+", " ").Trim(' ', '#', '>', '*', '-'))
                .Where(text => text.Length >= 60 && text.Length <= 16000 && !text.StartsWith("[TOC]", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal).Take(80).ToList();
            result.Add(new Project
            {
                Id = EvidenceLedgerBuilder.StableGuid("publication:" + url),
                Name = title.Groups[1].Value.Trim(),
                Url = url,
                Date = published,
                Description = passages.FirstOrDefault() ?? title.Groups[1].Value.Trim(),
                ImportSources = ["Blog Markdown catalog"],
                EvidencePassages = passages.Select(text => new ProjectEvidencePassage { Text = text, SourceUri = url, Revision = hash }).ToList(),
                EvidenceMetadata = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["provider"] = "blog",
                    ["content_hash"] = hash,
                    ["categories"] = categories,
                    ["review"] = "required",
                    ["live_url_verified"] = "false",
                    ["observed_at"] = DateTimeOffset.UtcNow.ToString("O")
                }
            });
        }
        return new(result.DistinctBy(article => article.Url, StringComparer.OrdinalIgnoreCase).ToList(), reused, warnings);
    }
}

/// <summary>Optional explicit discovery/verification; only public topics go to the fixed blog host.</summary>
public sealed class PublicationDiscoveryService(HttpClient http)
{
    public async Task<IReadOnlyList<string>> SuggestAsync(IEnumerable<string> requirements, CancellationToken ct = default)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in requirements.Where(text => !string.IsNullOrWhiteSpace(text)).Distinct().Take(6))
        {
            var query = requirement.Length > 160 ? requirement[..160] : requirement;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://www.mostlylucid.net/search/semantic?limit=5&query=" + Uri.EscapeDataString(query));
                request.Headers.UserAgent.ParseAdd("lucidRESUME/2.6");
                using var response = await http.SendAsync(request, timeout.Token);
                response.EnsureSuccessStatusCode();
                var results = await response.Content.ReadFromJsonAsync<List<SearchHit>>(timeout.Token) ?? [];
                foreach (var hit in results.Where(hit => Regex.IsMatch(hit.Slug, @"^[a-z0-9_-]+$"))) slugs.Add(hit.Slug);
            }
            catch (HttpRequestException) { /* Offline catalog remains available. */ }
            catch (System.Text.Json.JsonException) { /* Unexpected endpoint response; retain local discovery. */ }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        return slugs.Order(StringComparer.Ordinal).ToList();
    }

    public async Task<bool> VerifyAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "www.mostlylucid.net" || !uri.AbsolutePath.StartsWith("/blog/", StringComparison.Ordinal)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("lucidRESUME/2.6");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode && response.RequestMessage?.RequestUri?.Host == uri.Host;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    private sealed record SearchHit(string Slug, double Score);
}
