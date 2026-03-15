using System.Net.Http.Json;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using Microsoft.Extensions.Options;

namespace lucidRESUME.JobSearch.Adapters;

public sealed class AdzunaOptions
{
    public string AppId { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string Country { get; set; } = "gb";
}

public sealed class AdzunaAdapter : IJobSearchAdapter
{
    private readonly HttpClient _http;
    private readonly AdzunaOptions _options;
    public string AdapterName => "Adzuna";
    public bool IsConfigured => !string.IsNullOrEmpty(_options.AppId) && !string.IsNullOrEmpty(_options.AppKey);

    public AdzunaAdapter(HttpClient http, IOptions<AdzunaOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        if (!IsConfigured) return [];

        var url = $"https://api.adzuna.com/v1/api/jobs/{_options.Country}/search/1" +
                  $"?app_id={_options.AppId}&app_key={_options.AppKey}" +
                  $"&what={Uri.EscapeDataString(query.Keywords)}" +
                  $"&results_per_page={query.MaxResults}";

        if (!string.IsNullOrEmpty(query.Location))
            url += $"&where={Uri.EscapeDataString(query.Location)}";

        var response = await _http.GetFromJsonAsync<AdzunaResponse>(url, ct);
        return response?.Results.Select(ToJobDescription).ToList() ?? [];
    }

    private static JobDescription ToJobDescription(AdzunaResult r)
    {
        var job = JobDescription.Create(r.Description ?? "", new JobSource
        {
            Type = JobSourceType.Adzuna,
            Url = r.RedirectUrl,
            ExternalId = r.Id
        });
        job.Title = r.Title;
        job.Company = r.Company?.DisplayName;
        job.Location = r.Location?.DisplayName;
        if (r.SalaryMin.HasValue || r.SalaryMax.HasValue)
            job.Salary = new SalaryRange(r.SalaryMin ?? 0, r.SalaryMax ?? 0);
        return job;
    }

    private record AdzunaResponse([property: System.Text.Json.Serialization.JsonPropertyName("results")] List<AdzunaResult> Results);
    private record AdzunaResult(string Id, string Title, AdzunaCompany? Company,
        AdzunaLocation? Location, string? Description,
        [property: System.Text.Json.Serialization.JsonPropertyName("redirect_url")] string RedirectUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("salary_min")] decimal? SalaryMin,
        [property: System.Text.Json.Serialization.JsonPropertyName("salary_max")] decimal? SalaryMax);
    private record AdzunaCompany([property: System.Text.Json.Serialization.JsonPropertyName("display_name")] string DisplayName);
    private record AdzunaLocation([property: System.Text.Json.Serialization.JsonPropertyName("display_name")] string DisplayName);
}
