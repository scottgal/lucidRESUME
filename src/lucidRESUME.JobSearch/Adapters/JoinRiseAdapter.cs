using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.JobSearch.Adapters;

/// <summary>JoinRise - free public job API, no API key required</summary>
public sealed class JoinRiseAdapter : IJobSearchAdapter
{
    private readonly HttpClient _http;
    public string AdapterName => "JoinRise";
    public bool IsConfigured => true;

    public JoinRiseAdapter(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var url = $"https://api.joinrise.io/api/v1/jobs/public?page=1&limit={query.MaxResults}&sort=desc&sortedBy=createdAt";
        var response = await _http.GetFromJsonAsync<JoinRiseResponse>(url, ct);
        if (response?.Result?.Jobs is null) return [];

        var keyword = query.Keywords.ToLowerInvariant();
        var results = response.Result.Jobs
            .Where(j => string.IsNullOrWhiteSpace(keyword) ||
                        (j.Title?.ToLowerInvariant().Contains(keyword) == true) ||
                        (j.SkillsSuggest?.Any(s => s.ToLowerInvariant().Contains(keyword)) == true))
            .Select(ToJobDescription)
            .ToList();

        return results;
    }

    private static JobDescription ToJobDescription(JoinRiseJob j)
    {
        var breakdown = j.DescriptionBreakdown;
        var summary = breakdown?.OneSentenceJobSummary ?? "";

        var job = JobDescription.Create(summary, new JobSource
        {
            Type = JobSourceType.JoinRise,
            Url = j.Url,
            ExternalId = j.Id
        });

        job.Title = j.Title;
        job.Company = j.Owner?.CompanyName;
        job.Location = j.LocationAddress;

        if (breakdown is not null)
        {
            job.IsRemote = string.Equals(breakdown.WorkModel, "remote", StringComparison.OrdinalIgnoreCase);

            if (breakdown.SalaryRangeMinYearly > 0 || breakdown.SalaryRangeMaxYearly > 0)
                job.Salary = new SalaryRange(breakdown.SalaryRangeMinYearly, breakdown.SalaryRangeMaxYearly);

            if (breakdown.SkillRequirements?.Count > 0)
                job.RequiredSkills = breakdown.SkillRequirements;
        }

        if (j.SkillsSuggest?.Count > 0 && job.RequiredSkills.Count == 0)
            job.RequiredSkills = j.SkillsSuggest;

        return job;
    }

    // Response shape

    private record JoinRiseResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("result")] JoinRiseResult? Result);

    private record JoinRiseResult(
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("jobs")] List<JoinRiseJob> Jobs);

    private record JoinRiseJob(
        [property: JsonPropertyName("_id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("locationAddress")] string? LocationAddress,
        [property: JsonPropertyName("owner")] JoinRiseOwner? Owner,
        [property: JsonPropertyName("skills_suggest")] List<string>? SkillsSuggest,
        [property: JsonPropertyName("descriptionBreakdown")] JoinRiseBreakdown? DescriptionBreakdown,
        [property: JsonPropertyName("createdAt")] string? CreatedAt);

    private record JoinRiseOwner(
        [property: JsonPropertyName("companyName")] string? CompanyName,
        [property: JsonPropertyName("locationAddress")] string? LocationAddress);

    private record JoinRiseBreakdown(
        [property: JsonPropertyName("oneSentenceJobSummary")] string? OneSentenceJobSummary,
        [property: JsonPropertyName("workModel")] string? WorkModel,
        [property: JsonPropertyName("employmentType")] string? EmploymentType,
        [property: JsonPropertyName("salaryRangeMinYearly")] decimal SalaryRangeMinYearly,
        [property: JsonPropertyName("salaryRangeMaxYearly")] decimal SalaryRangeMaxYearly,
        [property: JsonPropertyName("skillRequirements")] List<string>? SkillRequirements);
}