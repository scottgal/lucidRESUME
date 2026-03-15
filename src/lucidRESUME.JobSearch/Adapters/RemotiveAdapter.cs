using System.Net.Http.Json;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.JobSearch.Adapters;

/// <summary>Remotive.com — free, no API key required</summary>
public sealed class RemotiveAdapter : IJobSearchAdapter
{
    private readonly HttpClient _http;
    public string AdapterName => "Remotive";
    public bool IsConfigured => true;

    public RemotiveAdapter(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var url = $"https://remotive.com/api/remote-jobs?search={Uri.EscapeDataString(query.Keywords)}&limit={query.MaxResults}";
        var response = await _http.GetFromJsonAsync<RemotiveResponse>(url, ct);
        return response?.Jobs.Select(ToJobDescription).ToList() ?? [];
    }

    private static JobDescription ToJobDescription(RemotiveJob j)
    {
        var job = JobDescription.Create(j.Description ?? "", new JobSource
        {
            Type = JobSourceType.Remotive,
            Url = j.Url,
            ExternalId = j.Id.ToString()
        });
        job.Title = j.Title;
        job.Company = j.CompanyName;
        job.IsRemote = true;
        return job;
    }

    private record RemotiveResponse(List<RemotiveJob> Jobs);
    private record RemotiveJob(int Id, string Title, string CompanyName, string? Description, string Url);
}
