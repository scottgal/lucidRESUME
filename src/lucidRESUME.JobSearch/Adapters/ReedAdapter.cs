using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using Microsoft.Extensions.Options;

namespace lucidRESUME.JobSearch.Adapters;

public sealed class ReedOptions
{
    public string ApiKey { get; set; } = "";
}

public sealed class ReedAdapter : IJobSearchAdapter
{
    private readonly HttpClient _http;
    private readonly ReedOptions _options;

    public string AdapterName => "Reed";
    public bool IsConfigured => !string.IsNullOrEmpty(_options.ApiKey);

    public ReedAdapter(HttpClient http, IOptions<ReedOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        if (!IsConfigured) return [];

        // Reed uses Basic auth: API key as username, empty password
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:"));
        var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ReedResponse>(ct);
        return result?.Results.Select(ToJobDescription).ToList() ?? [];
    }

    private static string BuildUrl(JobSearchQuery query)
    {
        var url = $"https://www.reed.co.uk/api/1.0/search" +
                  $"?keywords={Uri.EscapeDataString(query.Keywords)}" +
                  $"&resultsToTake={Math.Min(query.MaxResults, 100)}";

        if (!string.IsNullOrEmpty(query.Location))
            url += $"&location={Uri.EscapeDataString(query.Location)}&distancefromlocation=15";

        return url;
    }

    private static JobDescription ToJobDescription(ReedResult r)
    {
        var job = JobDescription.Create(r.JobDescription ?? "", new JobSource
        {
            Type = JobSourceType.Reed,
            Url = r.JobUrl,
            ExternalId = r.JobId.ToString()
        });
        job.Title = r.JobTitle;
        job.Company = r.EmployerName;
        job.Location = r.LocationName;
        if (r.MinimumSalary.HasValue || r.MaximumSalary.HasValue)
            job.Salary = new SalaryRange(r.MinimumSalary, r.MaximumSalary);
        return job;
    }

    private record ReedResponse(
        [property: JsonPropertyName("results")] List<ReedResult> Results);

    private record ReedResult(
        [property: JsonPropertyName("jobId")] int JobId,
        [property: JsonPropertyName("employerName")] string? EmployerName,
        [property: JsonPropertyName("jobTitle")] string? JobTitle,
        [property: JsonPropertyName("locationName")] string? LocationName,
        [property: JsonPropertyName("minimumSalary")] decimal? MinimumSalary,
        [property: JsonPropertyName("maximumSalary")] decimal? MaximumSalary,
        [property: JsonPropertyName("expirationDate")] string? ExpirationDate,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("jobDescription")] string? JobDescription,
        [property: JsonPropertyName("jobUrl")] string? JobUrl);
}
