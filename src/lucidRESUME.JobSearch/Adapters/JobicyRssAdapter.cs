using System.ServiceModel.Syndication;
using System.Xml;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.JobSearch.Adapters;

/// <summary>Jobicy - free RSS feed, no API key required</summary>
public sealed class JobicyRssAdapter : IJobSearchAdapter
{
    private readonly HttpClient _http;
    public string AdapterName => "Jobicy";
    public bool IsConfigured => true;

    public JobicyRssAdapter(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var url = "https://jobicy.com/jobs-rss-feed?count=50";
        var xmlStream = await _http.GetStreamAsync(url, ct);

        using var xmlReader = XmlReader.Create(xmlStream, new XmlReaderSettings { Async = true });
        var feed = SyndicationFeed.Load(xmlReader);
        if (feed?.Items is null) return [];

        var keyword = query.Keywords.ToLowerInvariant();
        var results = feed.Items
            .Where(item => string.IsNullOrWhiteSpace(keyword) ||
                           (item.Title?.Text?.ToLowerInvariant().Contains(keyword) == true) ||
                           (item.Summary?.Text?.ToLowerInvariant().Contains(keyword) == true))
            .Take(query.MaxResults)
            .Select(ToJobDescription)
            .ToList();

        return results;
    }

    private static JobDescription ToJobDescription(SyndicationItem item)
    {
        var url = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "";
        var rawText = item.Summary?.Text ?? item.Title?.Text ?? "";

        var job = JobDescription.Create(rawText, new JobSource
        {
            Type = JobSourceType.Jobicy,
            Url = url,
            ExternalId = item.Id
        });

        job.Title = item.Title?.Text;

        // Author maps to company in RSS job feeds
        var author = item.Authors.FirstOrDefault();
        if (author is not null)
            job.Company = string.IsNullOrWhiteSpace(author.Name) ? author.Email : author.Name;

        // pubDate
        if (item.PublishDate != default)
            job.CreatedAt = item.PublishDate;

        return job;
    }
}