# Job Search API Research & Integration Plan

## Goal
Use the user's extracted resume skills + profile preferences to auto-suggest roles and search multiple free job APIs in parallel, score results against their profile, and surface the best matches.

---

## API Inventory

### Tier 1: No Auth Required (zero friction)

| API | Endpoint | Coverage | Notes |
|-----|----------|----------|-------|
| **Arbeitnow** | `https://www.arbeitnow.com/api/job-board-api` | EU + Remote | Multi-ATS (Greenhouse, SmartRecruiters, Join.com etc). Includes `remote`, `visa_sponsorship` fields. Paginated. |
| **Remotive** | `https://remotive.com/api/remote-jobs?search=X&category=Y` | Remote only | Category filter. Rate limit: 2 req/min. Clean JSON. |
| **Jobicy** | `https://jobicy.com/jobs-rss-feed?count=50&tag=X` | Remote only | RSS feed, parse with SyndicationFeed. |
| **JoinRise** | `https://api.joinrise.io/api/v1/jobs/public?page=1&limit=20` | US/global | CORS enabled. 1793ms avg response. |
| **DevITjobs UK** | `https://devitjobs.uk/job_feed.xml` (GraphQL also) | UK Tech | XML/GraphQL, tech roles only. |

### Tier 2: Free API Key (register once)

| API | Endpoint | Coverage | Free Limits | Notes |
|-----|----------|----------|-------------|-------|
| **Adzuna** | `https://api.adzuna.com/v1/api/jobs/{country}/search/1` | UK + 16 countries | Generous free tier | Has salary data, location, categories. Best all-rounder. |
| **Reed** | `https://www.reed.co.uk/api/1.0/search` | UK | 1000 req/day | UK-focused, quality listings, basic auth with API key |
| **The Muse** | `https://www.themuse.com/api/public/jobs` | US | No key for basic | Job + company profile data |
| **Findwork** | `https://findwork.dev/api/jobs/` | Global tech | Free tier | Tech-focused, has `employment_type`, `remote` |
| **USAJOBS** | `https://data.usajobs.gov/api/search` | US Gov only | Free | US government positions only |

### Tier 3: Paid / Skip for now
- TheirStack (LinkedIn/Glassdoor scraper) - paid
- Techmap/JobDataFeeds - paid
- Jooble - API key + approval process
- WhatJobs - affiliate model

---

## Integration Architecture

### Role Suggestion Engine

Before searching, generate search queries from the user's data:

```
UserProfile.TargetRoles          → direct keyword queries
UserProfile.Preferences.TargetIndustries → industry filter
ResumeDocument.Skills (top 5 by category) → tech stack queries
ResumeDocument.Experience (most recent title) → title-based query
```

**Example:** User has `.NET`, `Azure`, `SQL Server`, last title "Senior Developer"
→ Auto-generate queries:
- `"Senior .NET Developer"`
- `"C# Azure Developer"`
- `"Software Engineer .NET"`
- `.NET remote` (if remote preferred)

The AI layer (Ollama) can refine these: given the full skill set, suggest 3-5 targeted role titles.

### Search Pipeline

```
RoleSuggestionService
    ↓ generates JobSearchQuery[]
JobSearchOrchestrator
    ↓ fans out to all configured adapters in parallel
    ↓ Task.WhenAll(adapters.Select(a => a.SearchAsync(query)))
JobDeduplicator
    ↓ deduplicates by (company + title) hash
MatchingService
    ↓ scores each against ResumeDocument + UserProfile
    ↓ filters blocked companies/industries
JobSearchResult[]  (sorted by MatchScore desc)
```

### New adapter implementations needed

```
src/lucidRESUME.JobSearch/
├── Adapters/
│   ├── AdzunaAdapter.cs          ✅ in current plan
│   ├── RemotiveAdapter.cs        ✅ in current plan
│   ├── ArbeitnowAdapter.cs       🆕 no auth, EU+remote
│   ├── JoinRiseAdapter.cs        🆕 no auth, US/global
│   ├── JobicyRssAdapter.cs       🆕 no auth, RSS parse
│   ├── ReedAdapter.cs            🆕 UK, API key
│   ├── FindworkAdapter.cs        🆕 tech-focused
│   └── TheMuse Adapter.cs        🆕 no key needed
├── RoleSuggestionService.cs      🆕 generates queries from resume+profile
├── JobDeduplicator.cs            🆕 deduplication by hash
├── JobSearchOrchestrator.cs      🆕 fan-out + merge + dedupe
└── JobSearchService.cs           ✅ exists, extend it
```

---

## New Models Needed in Core

```csharp
// Extension to JobSearchQuery
public record JobSearchQuery(
    string Keywords,
    string? Location = null,
    bool? RemoteOnly = null,
    int MaxResults = 20,
    string? Country = "gb",          // NEW: country code for Adzuna etc
    List<string>? Categories = null  // NEW: category filters
);

// New: orchestrated search result with provenance
public sealed class JobSearchResult
{
    public JobDescription Job { get; init; }
    public MatchResult? Match { get; init; }
    public string AdapterName { get; init; }  // which API found it
}
```

---

## Role Suggestion Service

```csharp
public sealed class RoleSuggestionService
{
    // Generates search queries from resume + profile without LLM
    public IReadOnlyList<JobSearchQuery> GenerateQueries(ResumeDocument resume, UserProfile profile)
    {
        var queries = new List<JobSearchQuery>();

        // 1. Direct target roles from profile
        foreach (var role in profile.Preferences.TargetRoles)
            queries.Add(new JobSearchQuery(role, RemoteOnly: profile.Preferences.OpenToRemote));

        // 2. Most recent job title
        var lastTitle = resume.Experience.FirstOrDefault()?.Title;
        if (lastTitle != null)
            queries.Add(new JobSearchQuery(lastTitle));

        // 3. Top skills combined with "developer" / "engineer"
        var topSkills = resume.Skills
            .Where(s => s.Category is "Language" or "Framework")
            .Take(3)
            .Select(s => s.Name);
        foreach (var skill in topSkills)
            queries.Add(new JobSearchQuery($"{skill} developer"));

        return queries.DistinctBy(q => q.Keywords.ToLowerInvariant()).ToList();
    }

    // LLM-enhanced version (when Ollama available)
    public async Task<IReadOnlyList<string>> SuggestRoleTitlesAsync(
        ResumeDocument resume, UserProfile profile, IAiTailoringService ai)
    {
        // Prompt Ollama: "Given these skills and experience, suggest 5 job titles to search for"
        // Returns structured list of role suggestions
    }
}
```

---

## Deduplication Strategy

Jobs from multiple APIs will overlap (especially remote roles). Deduplicate by:

1. **Exact match**: `(company.ToLower(), title.ToLower())` hash
2. **Fuzzy match**: Levenshtein distance on title + same company → merge, keep highest-confidence source

```csharp
public sealed class JobDeduplicator
{
    public IReadOnlyList<JobDescription> Deduplicate(IEnumerable<JobDescription> jobs)
    {
        return jobs
            .GroupBy(j => $"{j.Company?.ToLowerInvariant()}|{NormaliseTitle(j.Title)}")
            .Select(g => g.OrderByDescending(j => j.Source.FetchedAt).First())
            .ToList();
    }

    private static string NormaliseTitle(string? title) =>
        Regex.Replace(title?.ToLowerInvariant() ?? "", @"\b(senior|junior|lead|principal|staff)\b", "").Trim();
}
```

---

## Adapter Implementation Notes

### Arbeitnow (no auth)
```
GET https://www.arbeitnow.com/api/job-board-api?page=1
Response: { data: [{ slug, title, company_name, location, remote, tags[], description, url, created_at }] }
```
Tags array = skills/keywords → map to `RequiredSkills`.

### Remotive (no auth, 2/min rate limit)
```
GET https://remotive.com/api/remote-jobs?search=dotnet&limit=20
Response: { jobs: [{ id, url, title, company_name, category, tags[], description, salary, job_type, candidate_required_location }] }
```
Rate limit handling: use Polly with 30s retry on 429.

### JoinRise (no auth)
```
GET https://api.joinrise.io/api/v1/jobs/public?page=1&limit=20&sort=desc&sortedBy=createdAt&jobLoc=Remote
Response: paginated job list
```

### Reed (API key, basic auth)
```
GET https://www.reed.co.uk/api/1.0/search?keywords=dotnet&location=london&distancefromlocation=15
Authorization: Basic base64(apiKey:)  ← note empty password
Response: { results: [{ jobId, employerName, jobTitle, locationName, minimumSalary, maximumSalary, expirationDate, date, jobDescription, jobUrl }] }
```

### Findwork (API key, token auth)
```
GET https://findwork.dev/api/jobs/?search=dotnet&employment_type=full+time&remote=true
Authorization: Token {apiKey}
Response: { results: [{ id, role, company_name, url, text, date_posted, employment_type, remote, tags[] }] }
```

---

## Configuration

Add to `appsettings.json`:

```json
{
  "JobSearch": {
    "EnabledAdapters": ["Remotive", "Arbeitnow", "JoinRise", "Adzuna", "Reed"],
    "DefaultMaxResults": 20,
    "DefaultCountry": "gb"
  },
  "Adzuna": { "AppId": "", "AppKey": "", "Country": "gb" },
  "Reed": { "ApiKey": "" },
  "Findwork": { "ApiKey": "" }
}
```

Adapters with empty keys are auto-disabled via `IsConfigured` check - no errors thrown.

---

## Implementation Tasks (additions to main plan)

### Task 10b: No-Auth Adapters (Arbeitnow, JoinRise, Jobicy RSS)

These require zero user setup and should be enabled by default.

**Jobicy** uses RSS - add `System.ServiceModel.Syndication` or parse with `XDocument`.

### Task 10c: Key-Auth Adapters (Reed, Findwork)

Keys stored in `appsettings.json`, adapters self-disable when key is empty.

### Task 10d: RoleSuggestionService + JobDeduplicator

Generates queries from resume/profile, deduplicates cross-API results.

### Task 10e: JobSearchOrchestrator

Fan-out across all enabled adapters, merge + deduplicate, run matching, sort by score.

---

## URL-Based Job Description Ingestion

User pastes a job URL (LinkedIn, Indeed, any job board) → app extracts the JD automatically where possible, gracefully falls back where not.

### The Reality of Scraping Job Boards

| Site | Bot Protection | Auth Required | Strategy |
|------|---------------|---------------|----------|
| LinkedIn | Very strong | Yes | JSON-LD extraction or manual paste fallback |
| Indeed | Moderate | No | Playwright works in most cases |
| Reed | Low | No | HttpClient + AngleSharp |
| Adzuna | Low | No | HttpClient + AngleSharp |
| Most smaller boards | None | No | HttpClient + AngleSharp |
| Company career pages | Varies | Usually no | Try HTTP → Playwright fallback |

### Layered Strategy (try each in order, stop on success)

```
Layer 1: Accept: text/markdown header (zero cost, zero config, always try first)
    → Plain HTTP GET with "Accept: text/markdown" header
    → Cloudflare's "Markdown for Agents" feature (Feb 2026) auto-converts HTML→markdown
      at the CDN edge for any site on Cloudflare with the feature enabled
    → No API key, no account, no cost - just a header
    → Returns Content-Type: text/markdown + x-markdown-tokens header
    → Works for: many job boards, company career pages, Reed, Adzuna, and any
      Cloudflare Pro/Business/Enterprise hosted site
    → Does NOT work for: LinkedIn, Indeed, Glassdoor (not on Cloudflare CDN)
    → If response Content-Type is text/markdown and body >200 chars → done

Layer 2: Plain HTTP GET → local HTML→markdown (always available fallback)
    → Same HTTP GET without the markdown header
    → Convert HTML → markdown locally via ReverseMarkdown or AngleSharp
    → While we have the HTML, also extract JSON-LD JobPosting schema (free structured data):
        <script type="application/ld+json"> → schema.org/JobPosting
        → LinkedIn, Greenhouse, Lever, Workday embed this for SEO
        → Gets title, company, location, salary, description fully structured
    → Also extract OpenGraph tags (og:title, og:description) as supplemental fields
    → Works for static/JS-light boards

Layer 3: Cloudflare Browser Rendering /markdown endpoint (if CF account configured)
    → POST https://api.cloudflare.com/client/v4/accounts/{accountId}/browser-rendering/markdown
    → Fully renders JS, runs on Cloudflare's IP network (better bot resistance)
    → Free: 10 hrs/month on Workers Paid plan
    → For JS-heavy boards where layers 1-2 return thin content

Layer 4: Local Playwright (heavy fallback, no CF account needed)
    → Lazy-init headless Chromium, only launched on demand
    → For JS-rendered boards when CF Browser Rendering not configured
    → Skipped if Layer 3 is configured

Layer 5: Manual paste fallback
    → LinkedIn (own CDN, login wall), Glassdoor (bot detection), anything that fails
    → App detects domain, shows: "Open this job in your browser and paste the text below."
    → Same flow as JobSpecParser.ParseFromTextAsync
```

### Why LinkedIn won't work with Accept: text/markdown

LinkedIn runs on its own CDN infrastructure (not Cloudflare). The Markdown for Agents feature requires:
1. The site is proxied through Cloudflare's CDN
2. The site owner has enabled it (Pro plan or higher)

LinkedIn, Indeed, and Glassdoor all run their own infrastructure, so layers 1-3 won't return markdown for them. Layer 2 (plain HTTP + JSON-LD) may still extract structured job data from LinkedIn's initial HTML before the login wall kicks in.

### Why Cloudflare beats local Playwright

| | Cloudflare BR | Local Playwright |
|---|---|---|
| Browser install | None | `playwright install chromium` (~300MB) |
| Bot detection | Cloudflare IPs, better reputation | Localhost IP, easily flagged |
| JS rendering | Yes | Yes |
| Cost | Free tier covers normal use | Free but heavy resource usage |
| Config needed | API token + account ID | Nothing (but heavier) |
| Works on Indeed | Yes | Usually |
| Works on LinkedIn | No (login wall) | No (login wall) |

### Cloudflare setup

Add to `appsettings.json`:

```json
{
  "CloudflareBrowserRendering": {
    "AccountId": "",
    "ApiToken": "",
    "WaitUntil": "networkidle0"
  }
}
```

When `AccountId` and `ApiToken` are empty, Layer 1 is skipped - falls straight to Layer 2. Zero friction for users who don't want to configure it.

### CloudflareBrowserRenderingClient

```csharp
// src/lucidRESUME.JobSpec/Scrapers/CloudflareMarkdownClient.cs
public sealed class CloudflareMarkdownClient
{
    private readonly HttpClient _http;
    private readonly CloudflareBrOptions _options;

    public bool IsConfigured => !string.IsNullOrEmpty(_options.AccountId)
                             && !string.IsNullOrEmpty(_options.ApiToken);

    public async Task<string?> GetMarkdownAsync(string url, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var endpoint = $"https://api.cloudflare.com/client/v4/accounts/{_options.AccountId}/browser-rendering/markdown";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        request.Content = JsonContent.Create(new
        {
            url,
            gotoOptions = new { waitUntil = _options.WaitUntil ?? "networkidle0" }
        });

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<CfBrResponse>(cancellationToken: ct);
        return result?.Success == true ? result.Result : null;
    }

    private record CfBrResponse(bool Success, string? Result);
}
```

### JSON-LD is the secret weapon

LinkedIn, Indeed, Greenhouse, Lever, Workday and most modern ATS systems embed `schema.org/JobPosting` in their HTML. This is parseable with a plain HTTP GET:

```csharp
// src/lucidRESUME.JobSpec/Scrapers/StructuredDataExtractor.cs
public sealed class StructuredDataExtractor
{
    // Finds <script type="application/ld+json"> blocks and extracts JobPosting schema
    public JobPostingSchema? ExtractJobPosting(string html)
    {
        // Parse with AngleSharp, find all ld+json scripts
        // Deserialize as JsonDocument, find @type == "JobPosting"
        // Map to our JobDescription model
    }
}

// Schema.org JobPosting fields we care about:
// title, hiringOrganization.name, jobLocation, baseSalary,
// description, employmentType, datePosted, validThrough
// jobLocationType (TELECOMMUTE = remote)
```

This gets us structured data from LinkedIn job pages **without needing auth or Playwright**, because the JSON-LD is in the initial HTML response. LinkedIn includes it for SEO/indexability.

### Implementation

```
src/lucidRESUME.JobSpec/
├── Scrapers/
│   ├── IJobPageScraper.cs            interface + strategy selector
│   ├── HttpMarkdownScraper.cs        Layer 1: HTTP GET → markdown (always first)
│   ├── StructuredDataExtractor.cs    Layer 2: JSON-LD + OG tags (from same response)
│   ├── PlaywrightJobScraper.cs       Layer 3: headless browser fallback
│   └── ScrapeStrategySelector.cs    picks strategy by domain
├── JobSpecParser.cs                  updated: ParseFromUrlAsync uses scrapers
```

```csharp
// ScrapeStrategySelector maps domain → strategy
private static readonly Dictionary<string, ScrapeStrategy> DomainStrategies = new()
{
    ["linkedin.com"]   = ScrapeStrategy.StructuredDataOnly,  // no Playwright, may fail gracefully
    ["glassdoor.com"]  = ScrapeStrategy.ManualFallback,      // bot detection too strong
    ["indeed.com"]     = ScrapeStrategy.Playwright,
    ["greenhouse.io"]  = ScrapeStrategy.StructuredData,
    ["lever.co"]       = ScrapeStrategy.StructuredData,
    ["workday.com"]    = ScrapeStrategy.StructuredData,
    ["reed.co.uk"]     = ScrapeStrategy.Http,
    ["adzuna.com"]     = ScrapeStrategy.Http,
};
// Default for unknown domains: try StructuredData → Http → Playwright
```

### Playwright setup (desktop app)

Since this is an Avalonia desktop app, Playwright runs in-process:

```bash
dotnet add src/lucidRESUME.JobSpec package Microsoft.Playwright
# On first run or install:
playwright install chromium
```

Playwright is only installed/launched if a URL requires it - lazy init, shown as "Loading job description..." in the UI.

For Glassdoor / totally blocked sites, the app shows:
```
"We can't automatically extract this job description.
 Open it in your browser, select all the text, and paste it here:"
[text area]  [Parse]
```

### NuGet packages needed

```xml
<PackageReference Include="AngleSharp" Version="1.4.1-beta" />
<PackageReference Include="Microsoft.Playwright" Version="1.49.0" />
```

AngleSharp is already in LucidRAG - copy version from there.

---

## Future: AI-Powered Role Discovery

When Ollama is available, add a second pass:

1. Pass top 5 skills + current title to local LLM
2. Ask: "What are 5 job titles this person should search for, given they want to avoid X and target Y?"
3. Use those titles as additional search queries
4. Optionally: summarise each job description to a 2-line "why this matches you" using the same LLM

This closes the loop between profile preferences (avoid boring work, avoid certain company types) and search results.