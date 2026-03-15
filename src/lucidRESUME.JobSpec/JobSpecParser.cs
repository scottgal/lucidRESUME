using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.JobSpec;

public sealed class JobSpecParser : IJobSpecParser
{
    private readonly ILogger<JobSpecParser> _logger;
    private readonly HttpClient? _http;

    public JobSpecParser(ILogger<JobSpecParser> logger, HttpClient? http = null)
    {
        _logger = logger;
        _http = http;
    }

    public Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default)
    {
        var job = JobDescription.Create(text, new JobSource { Type = JobSourceType.PastedText });
        ExtractFields(job, text);
        return Task.FromResult(job);
    }

    public async Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default)
    {
        if (_http == null) throw new InvalidOperationException("HttpClient not configured for URL parsing");
        var html = await _http.GetStringAsync(url, ct);
        var text = StripHtml(html);
        var job = JobDescription.Create(text, new JobSource { Type = JobSourceType.Url, Url = url });
        ExtractFields(job, text);
        return job;
    }

    private static void ExtractFields(JobDescription job, string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Title + Company from first meaningful line: "Title at Company" or "Title - Company"
        var header = lines.FirstOrDefault(l => l.Length > 5);
        if (header != null)
        {
            var atSplit = header.Split(" at ", 2, StringSplitOptions.TrimEntries);
            var dashSplit = header.Split(" - ", 2, StringSplitOptions.TrimEntries);
            if (atSplit.Length == 2) { job.Title = atSplit[0]; job.Company = atSplit[1]; }
            else if (dashSplit.Length == 2) { job.Title = dashSplit[0]; job.Company = dashSplit[1]; }
        }

        // Remote detection
        var lower = text.ToLowerInvariant();
        job.IsRemote = lower.Contains("fully remote") || lower.Contains("remote: yes") || lower.Contains("100% remote");
        job.IsHybrid = lower.Contains("hybrid");

        // Skills extraction
        job.RequiredSkills = ExtractSkillsList(text, @"required|skills");
        job.PreferredSkills = ExtractSkillsList(text, @"preferred|nice to have|desirable");

        // Salary via regex: £60,000 - £80,000 or $60,000 - $80,000
        var salaryMatch = Regex.Match(text, @"[£$€](\d[\d,]+)\s*[-–]\s*[£$€]?(\d[\d,]+)", RegexOptions.IgnoreCase);
        if (salaryMatch.Success)
        {
            var min = decimal.Parse(salaryMatch.Groups[1].Value.Replace(",", ""));
            var max = decimal.Parse(salaryMatch.Groups[2].Value.Replace(",", ""));
            job.Salary = new SalaryRange(min, max);
        }
    }

    private static List<string> ExtractSkillsList(string text, string sectionPattern)
    {
        var skills = new List<string>();
        var sectionRegex = new Regex($@"(?:{sectionPattern})[:\s]+([^\n]+)", RegexOptions.IgnoreCase);
        foreach (Match m in sectionRegex.Matches(text))
        {
            var items = m.Groups[1].Value.Split([',', '•', '·', ';'], StringSplitOptions.RemoveEmptyEntries);
            skills.AddRange(items.Select(s => s.Trim()).Where(s => s.Length > 1));
        }
        return skills;
    }

    private static string StripHtml(string html) =>
        Regex.Replace(html, "<[^>]+>", " ").Trim();
}
