# Coverage Map & Company-Aware Tailoring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Treat the JD as a prioritised question set and the resume as an answer sheet — build a coverage map that links each requirement to resume evidence (or flags it as a gap), factor in company type, and feed both into a restructured tailoring prompt.

**Architecture:** `CompanyClassifier` (in Matching) extracts a typed `CompanyType` from a `JobDescription`. `ResumeCoverageAnalyser` (in Matching) maps each `JdRequirement` to the best-matching resume evidence using keyword overlap with optional semantic upgrade via `IEmbeddingService`. `TailoringPromptBuilder` receives the `CoverageReport` and uses it to order answers by priority and inject company-type tone guidance. The tailoring service calls coverage analysis before building the prompt.

**Tech Stack:** .NET 10 / C# 13, xUnit, lucidRESUME.Core (models), lucidRESUME.Matching (analysers), lucidRESUME.AI (prompt builder + service), Avalonia (UI)

---

## Task 1: CompanyType enum

**Files:**
- Create: `src/lucidRESUME.Core/Models/Jobs/CompanyType.cs`

**Step 1: Create the file**

```csharp
namespace lucidRESUME.Core.Models.Jobs;

public enum CompanyType
{
    Unknown,
    Startup,
    ScaleUp,
    Enterprise,
    Agency,
    Consultancy,
    Finance,
    Public,      // gov / NHS / charity / non-profit
    Academic
}
```

**Step 2: Commit**

```bash
git add src/lucidRESUME.Core/Models/Jobs/CompanyType.cs
git commit -m "feat: add CompanyType enum to Core"
```

---

## Task 2: CompanyClassifier

Moves the company-type detection already in `AspectExtractor` into a proper typed class so the rest of the system can depend on it.

**Files:**
- Create: `src/lucidRESUME.Matching/CompanyClassifier.cs`
- Modify: `src/lucidRESUME.Matching/ServiceCollectionExtensions.cs`

**Step 1: Write failing test**

File: `tests/lucidRESUME.Matching.Tests/CompanyClassifierTests.cs`

```csharp
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public class CompanyClassifierTests
{
    private readonly CompanyClassifier _sut = new();

    [Theory]
    [InlineData("We are a Series B startup disrupting the lending space", CompanyType.Startup)]
    [InlineData("Join our scale-up as we grow from 50 to 500", CompanyType.ScaleUp)]
    [InlineData("Fortune 500 enterprise software company", CompanyType.Enterprise)]
    [InlineData("We are a digital agency delivering for top brands", CompanyType.Agency)]
    [InlineData("Big 4 consultancy offering advisory services", CompanyType.Consultancy)]
    [InlineData("Global investment bank, regulated environment", CompanyType.Finance)]
    [InlineData("NHS Trust providing healthcare services", CompanyType.Public)]
    [InlineData("Research university seeks lecturer", CompanyType.Academic)]
    [InlineData("Some company doing some things", CompanyType.Unknown)]
    public void Classify_DetectsExpectedType(string rawText, CompanyType expected)
    {
        var job = JobDescription.Create(rawText, new JobSource { Type = JobSourceType.PastedText });
        Assert.Equal(expected, _sut.Classify(job));
    }

    [Fact]
    public void Classify_TitleSignals_Startup()
    {
        var job = JobDescription.Create("salary £80k", new JobSource { Type = JobSourceType.PastedText });
        job.Title = "Senior Engineer at a seed-stage startup";
        Assert.Equal(CompanyType.Startup, _sut.Classify(job));
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/lucidRESUME.Matching.Tests --filter CompanyClassifierTests -v minimal
```
Expected: compile error — `CompanyClassifier` does not exist.

**Step 3: Implement**

File: `src/lucidRESUME.Matching/CompanyClassifier.cs`

```csharp
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Matching;

/// <summary>
/// Classifies a job's employer type from JD text and title.
/// First definitive match wins — a company is one type.
/// </summary>
public sealed class CompanyClassifier
{
    public CompanyType Classify(JobDescription job)
    {
        var text = ((job.Title ?? "") + " " + job.RawText).ToLowerInvariant();

        if (ContainsAny(text, "university", "college", "academic", "faculty", "lecturer",
                              "professor", "research institute", "phd programme"))
            return CompanyType.Academic;

        if (ContainsAny(text, "nhs", "gov.uk", "local authority", "council", "charity",
                              "non-profit", "nonprofit", "public sector", "civil service"))
            return CompanyType.Public;

        if (ContainsAny(text, "investment bank", "hedge fund", "asset management", "fintech",
                              "financial services", "insurance", "regulated environment",
                              "big 4", "auditing", "accounting firm"))
            return CompanyType.Finance;

        if (ContainsAny(text, "consultancy", "consulting firm", "advisory", "big 4",
                              "systems integrator", "professional services"))
            return CompanyType.Consultancy;

        if (ContainsAny(text, " agency", "digital agency", "creative agency", "marketing agency",
                              "advertising agency", "media agency"))
            return CompanyType.Agency;

        if (ContainsAny(text, "enterprise", "corporate", "ftse", "fortune 500", "fortune500",
                              "global company", "multinational", "plc"))
            return CompanyType.Enterprise;

        if (ContainsAny(text, "scale-up", "scaleup", "growth stage", "series c", "series d",
                              "series e", "pre-ipo", "post-series b"))
            return CompanyType.ScaleUp;

        if (ContainsAny(text, "startup", "start-up", "seed", "series a", "series b",
                              "early stage", "pre-seed", "venture-backed", "founded in 20"))
            return CompanyType.Startup;

        return CompanyType.Unknown;
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
```

**Step 4: Register in DI**

In `src/lucidRESUME.Matching/ServiceCollectionExtensions.cs`, add:
```csharp
services.AddSingleton<CompanyClassifier>();
```

**Step 5: Run tests**

```bash
dotnet test tests/lucidRESUME.Matching.Tests --filter CompanyClassifierTests -v minimal
```
Expected: all 10 tests pass.

**Step 6: Commit**

```bash
git add src/lucidRESUME.Matching/CompanyClassifier.cs \
        src/lucidRESUME.Matching/ServiceCollectionExtensions.cs \
        tests/lucidRESUME.Matching.Tests/CompanyClassifierTests.cs
git commit -m "feat: add CompanyClassifier with typed CompanyType detection"
```

---

## Task 3: Coverage domain models

**Files:**
- Create: `src/lucidRESUME.Core/Models/Coverage/RequirementPriority.cs`
- Create: `src/lucidRESUME.Core/Models/Coverage/JdRequirement.cs`
- Create: `src/lucidRESUME.Core/Models/Coverage/CoverageEntry.cs`
- Create: `src/lucidRESUME.Core/Models/Coverage/CoverageReport.cs`

**Step 1: Create the files**

`RequirementPriority.cs`:
```csharp
namespace lucidRESUME.Core.Models.Coverage;

public enum RequirementPriority { Required, Preferred, Responsibility }
```

`JdRequirement.cs`:
```csharp
namespace lucidRESUME.Core.Models.Coverage;

/// <summary>A single "question" extracted from a job description.</summary>
public sealed record JdRequirement(
    string Text,
    RequirementPriority Priority);
```

`CoverageEntry.cs`:
```csharp
namespace lucidRESUME.Core.Models.Coverage;

/// <summary>
/// Maps one JD requirement to the best-matching evidence in the resume,
/// or null if no evidence was found (a gap).
/// </summary>
public sealed record CoverageEntry(
    JdRequirement Requirement,
    string? Evidence,          // null = gap
    string? EvidenceSection,   // e.g. "Experience[0].Achievements[2]"
    float Score                // 0–1 match confidence
)
{
    public bool IsCovered => Evidence is not null;
}
```

`CoverageReport.cs`:
```csharp
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.Core.Models.Coverage;

public sealed record CoverageReport(
    IReadOnlyList<CoverageEntry> Entries,
    CompanyType CompanyType,
    DateTimeOffset GeneratedAt)
{
    public IEnumerable<CoverageEntry> Gaps         => Entries.Where(e => !e.IsCovered);
    public IEnumerable<CoverageEntry> RequiredGaps => Gaps.Where(e => e.Requirement.Priority == RequirementPriority.Required);
    public IEnumerable<CoverageEntry> Covered      => Entries.Where(e => e.IsCovered);

    /// <summary>0–100 overall coverage percentage.</summary>
    public int CoveragePercent => Entries.Count == 0 ? 0
        : (int)(100.0 * Covered.Count() / Entries.Count);
}
```

**Step 2: Commit**

```bash
git add src/lucidRESUME.Core/Models/Coverage/
git commit -m "feat: add Coverage domain models (JdRequirement, CoverageEntry, CoverageReport)"
```

---

## Task 4: ICoverageAnalyser interface

**Files:**
- Create: `src/lucidRESUME.Core/Interfaces/ICoverageAnalyser.cs`

**Step 1: Create the file**

```csharp
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Interfaces;

public interface ICoverageAnalyser
{
    /// <summary>
    /// Maps each requirement in <paramref name="job"/> to the best-matching
    /// evidence in <paramref name="resume"/>. Gaps are entries with null Evidence.
    /// </summary>
    Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job,
        CancellationToken ct = default);
}
```

**Step 2: Commit**

```bash
git add src/lucidRESUME.Core/Interfaces/ICoverageAnalyser.cs
git commit -m "feat: add ICoverageAnalyser interface"
```

---

## Task 5: ResumeCoverageAnalyser

Matches JD requirements to resume evidence. Keyword overlap first; if `IEmbeddingService` is available, uses cosine similarity for ambiguous cases (same optional pattern as `SkillMatchingService`).

**Files:**
- Create: `src/lucidRESUME.Matching/ResumeCoverageAnalyser.cs`
- Modify: `src/lucidRESUME.Matching/ServiceCollectionExtensions.cs`

**Step 1: Write failing test**

File: `tests/lucidRESUME.Matching.Tests/ResumeCoverageAnalyserTests.cs`

```csharp
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Matching;

namespace lucidRESUME.Matching.Tests;

public class ResumeCoverageAnalyserTests
{
    private static ResumeCoverageAnalyser CreateSut() =>
        new(new CompanyClassifier());

    private static ResumeDocument ResumeWith(params string[] skills)
    {
        var r = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        foreach (var s in skills)
            r.Skills.Add(new Skill { Name = s });
        return r;
    }

    private static JobDescription JobWith(string[] required, string[] preferred)
    {
        var j = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        j.RequiredSkills.AddRange(required);
        j.PreferredSkills.AddRange(preferred);
        return j;
    }

    [Fact]
    public async Task RequiredSkill_PresentInResume_IsCovered()
    {
        var resume = ResumeWith("C#", "Azure");
        var job = JobWith(["C#"], []);

        var report = await CreateSut().AnalyseAsync(resume, job);

        var entry = Assert.Single(report.Entries, e => e.Requirement.Text == "C#");
        Assert.True(entry.IsCovered);
        Assert.Equal(RequirementPriority.Required, entry.Requirement.Priority);
    }

    [Fact]
    public async Task RequiredSkill_MissingFromResume_IsGap()
    {
        var resume = ResumeWith("Python");
        var job = JobWith(["Kubernetes"], []);

        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.Single(report.RequiredGaps);
        Assert.Equal(0, report.CoveragePercent);
    }

    [Fact]
    public async Task PreferredSkill_PresentInResume_IsCoveredWithPreferredPriority()
    {
        var resume = ResumeWith("Terraform");
        var job = JobWith([], ["Terraform"]);

        var report = await CreateSut().AnalyseAsync(resume, job);

        var entry = Assert.Single(report.Entries);
        Assert.True(entry.IsCovered);
        Assert.Equal(RequirementPriority.Preferred, entry.Requirement.Priority);
    }

    [Fact]
    public async Task Responsibility_MatchedByKeyword_IsCovered()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var exp = new WorkExperience { Company = "Acme" };
        exp.Achievements.Add("Designed and built a CI/CD pipeline reducing deploy time by 40%");
        resume.Experience.Add(exp);

        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        job.Responsibilities.Add("Build and maintain CI/CD pipelines");

        var report = await CreateSut().AnalyseAsync(resume, job);

        var entry = Assert.Single(report.Entries, e => e.Requirement.Priority == RequirementPriority.Responsibility);
        Assert.True(entry.IsCovered);
    }

    [Fact]
    public async Task CoveragePercent_PartialMatch_IsCorrect()
    {
        var resume = ResumeWith("C#");
        var job = JobWith(["C#", "Kubernetes"], []);

        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.Equal(50, report.CoveragePercent);
    }

    [Fact]
    public async Task CompanyType_StartupSignal_ClassifiedCorrectly()
    {
        var resume = ResumeWith("React");
        var job = JobDescription.Create("Series A startup building fintech", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills.Add("React");

        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.Equal(CompanyType.Startup, report.CompanyType);
    }

    [Fact]
    public async Task SkillInExperienceTechnologies_CountsAsEvidence()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var exp = new WorkExperience { Company = "TechCo" };
        exp.Technologies.Add("GraphQL");
        resume.Experience.Add(exp);

        var job = JobWith(["GraphQL"], []);
        var report = await CreateSut().AnalyseAsync(resume, job);

        Assert.True(report.Entries.Single().IsCovered);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/lucidRESUME.Matching.Tests --filter ResumeCoverageAnalyserTests -v minimal
```
Expected: compile error — `ResumeCoverageAnalyser` does not exist.

**Step 3: Implement**

File: `src/lucidRESUME.Matching/ResumeCoverageAnalyser.cs`

```csharp
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

public sealed class ResumeCoverageAnalyser : ICoverageAnalyser
{
    private readonly CompanyClassifier _classifier;
    private readonly IEmbeddingService? _embedder;

    public ResumeCoverageAnalyser(CompanyClassifier classifier,
        IEmbeddingService? embedder = null)
    {
        _classifier = classifier;
        _embedder = embedder;
    }

    public async Task<CoverageReport> AnalyseAsync(ResumeDocument resume, JobDescription job,
        CancellationToken ct = default)
    {
        var companyType = _classifier.Classify(job);

        // Build flat list of all resume evidence strings with section labels
        var skillEvidence = resume.Skills
            .Select((s, i) => (Text: s.Name, Section: $"Skills[{i}]"))
            .ToList();

        var techEvidence = resume.Experience
            .SelectMany((e, ei) => e.Technologies.Select((t, ti) =>
                (Text: t, Section: $"Experience[{ei}].Technologies[{ti}]")))
            .ToList();

        var achievementEvidence = resume.Experience
            .SelectMany((e, ei) => e.Achievements.Select((a, ai) =>
                (Text: a, Section: $"Experience[{ei}].Achievements[{ai}]")))
            .ToList();

        var allSkillEvidence = skillEvidence.Concat(techEvidence).ToList();

        var entries = new List<CoverageEntry>();

        // Required skills
        foreach (var req in job.RequiredSkills)
            entries.Add(await MatchSkillAsync(req, RequirementPriority.Required, allSkillEvidence, ct));

        // Preferred skills
        foreach (var pref in job.PreferredSkills)
            entries.Add(await MatchSkillAsync(pref, RequirementPriority.Preferred, allSkillEvidence, ct));

        // Responsibilities — keyword match against achievement bullets
        foreach (var resp in job.Responsibilities)
            entries.Add(await MatchResponsibilityAsync(resp, achievementEvidence, ct));

        return new CoverageReport(entries.AsReadOnly(), companyType, DateTimeOffset.UtcNow);
    }

    private async Task<CoverageEntry> MatchSkillAsync(
        string requirement,
        RequirementPriority priority,
        IReadOnlyList<(string Text, string Section)> evidence,
        CancellationToken ct)
    {
        var req = new JdRequirement(requirement, priority);

        // 1. Exact (case-insensitive) match
        var exact = evidence.FirstOrDefault(e =>
            e.Text.Contains(requirement, StringComparison.OrdinalIgnoreCase));
        if (exact != default)
            return new CoverageEntry(req, exact.Text, exact.Section, 1.0f);

        // 2. Semantic match if embedder available
        if (_embedder is not null)
        {
            try
            {
                var reqVec = await _embedder.EmbedAsync(requirement, ct);
                float bestScore = 0f;
                (string Text, string Section) bestMatch = default;

                foreach (var e in evidence)
                {
                    var eVec = await _embedder.EmbedAsync(e.Text, ct);
                    var score = _embedder.CosineSimilarity(reqVec, eVec);
                    if (score > bestScore) { bestScore = score; bestMatch = e; }
                }

                if (bestScore >= 0.82f)
                    return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, bestScore);
            }
            catch
            {
                // Fall through to gap
            }
        }

        return new CoverageEntry(req, null, null, 0f);
    }

    private async Task<CoverageEntry> MatchResponsibilityAsync(
        string responsibility,
        IReadOnlyList<(string Text, string Section)> achievements,
        CancellationToken ct)
    {
        var req = new JdRequirement(responsibility, RequirementPriority.Responsibility);

        // Extract keywords: words > 4 chars that aren't stop words
        var keywords = responsibility.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', ';', '(', ')').ToLowerInvariant())
            .Where(w => w.Length > 4 && !StopWords.Contains(w))
            .ToHashSet();

        // Keyword overlap score
        (string Text, string Section) bestMatch = default;
        int bestOverlap = 0;

        foreach (var ach in achievements)
        {
            var achLower = ach.Text.ToLowerInvariant();
            int overlap = keywords.Count(k => achLower.Contains(k));
            if (overlap > bestOverlap) { bestOverlap = overlap; bestMatch = ach; }
        }

        if (bestOverlap >= 2)
        {
            var score = Math.Min(1f, bestOverlap / (float)Math.Max(keywords.Count, 1));
            return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, score);
        }

        // Semantic fallback
        if (_embedder is not null && achievements.Count > 0)
        {
            try
            {
                var reqVec = await _embedder.EmbedAsync(responsibility, ct);
                float bestScore = 0f;

                foreach (var ach in achievements)
                {
                    var achVec = await _embedder.EmbedAsync(ach.Text, ct);
                    var score = _embedder.CosineSimilarity(reqVec, achVec);
                    if (score > bestScore) { bestScore = score; bestMatch = ach; }
                }

                if (bestScore >= 0.75f)
                    return new CoverageEntry(req, bestMatch.Text, bestMatch.Section, bestScore);
            }
            catch { /* fall through */ }
        }

        return new CoverageEntry(req, null, null, 0f);
    }

    private static readonly HashSet<string> StopWords =
    [
        "about", "above", "after", "also", "among", "being", "between",
        "their", "there", "these", "those", "through", "using", "where",
        "which", "while", "will", "with", "working", "within", "would",
        "experience", "ability", "knowledge", "skills", "strong", "across"
    ];
}
```

**Step 4: Register in DI**

In `src/lucidRESUME.Matching/ServiceCollectionExtensions.cs`:
```csharp
services.AddSingleton<ICoverageAnalyser>(sp =>
    new ResumeCoverageAnalyser(
        sp.GetRequiredService<CompanyClassifier>(),
        sp.GetService<IEmbeddingService>()));
```

**Step 5: Run tests**

```bash
dotnet test tests/lucidRESUME.Matching.Tests --filter ResumeCoverageAnalyserTests -v minimal
```
Expected: all 7 tests pass.

**Step 6: Run full test suite**

```bash
dotnet test --filter "FullyQualifiedName~lucidRESUME.Matching" -v minimal
```
Expected: all existing tests still pass.

**Step 7: Commit**

```bash
git add src/lucidRESUME.Matching/ResumeCoverageAnalyser.cs \
        src/lucidRESUME.Matching/ServiceCollectionExtensions.cs \
        tests/lucidRESUME.Matching.Tests/ResumeCoverageAnalyserTests.cs
git commit -m "feat: add ResumeCoverageAnalyser — maps JD requirements to resume evidence"
```

---

## Task 6: TailoringPromptBuilder — coverage-aware, company-type-aware

Replaces the current flat dump of resume + JD with a structured, prioritised answer format.

**Files:**
- Modify: `src/lucidRESUME.AI/TailoringPromptBuilder.cs`

**Step 1: Write failing test**

File: `tests/lucidRESUME.AI.Tests/TailoringPromptBuilderTests.cs`

First, create the test project if it doesn't exist:
```bash
dotnet new xunit -n lucidRESUME.AI.Tests -o tests/lucidRESUME.AI.Tests --framework net10.0
dotnet add tests/lucidRESUME.AI.Tests/lucidRESUME.AI.Tests.csproj reference src/lucidRESUME.AI/lucidRESUME.AI.csproj
dotnet add tests/lucidRESUME.AI.Tests/lucidRESUME.AI.Tests.csproj reference src/lucidRESUME.Core/lucidRESUME.Core.csproj
dotnet add tests/lucidRESUME.AI.Tests/lucidRESUME.AI.Tests.csproj reference src/lucidRESUME.Matching/lucidRESUME.Matching.csproj
dotnet sln lucidRESUME.sln add tests/lucidRESUME.AI.Tests/lucidRESUME.AI.Tests.csproj
```

```csharp
using lucidRESUME.AI;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Coverage;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public class TailoringPromptBuilderTests
{
    private static CoverageReport MakeCoverage(CompanyType type, params (string text, RequirementPriority pri, string? evidence)[] entries)
    {
        var list = entries.Select(e => new CoverageEntry(
            new JdRequirement(e.text, e.pri),
            e.evidence,
            e.evidence is null ? null : "Skills[0]",
            e.evidence is null ? 0f : 1f)).ToList().AsReadOnly();
        return new CoverageReport(list, type, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Build_RequiredGaps_AppearInPrompt()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane Smith\n## Skills\n- C#";

        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Unknown,
            ("C#",         RequirementPriority.Required,  "C#"),
            ("Kubernetes", RequirementPriority.Required,  null));

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        Assert.Contains("Kubernetes", prompt);
        Assert.Contains("not covered", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StartupTone_InjectsStartupGuidance()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane";
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Startup);

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        Assert.Contains("ownership", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_EnterpriseTone_InjectsEnterpriseGuidance()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane";
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Enterprise);

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        Assert.Contains("process", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_CoveredRequirements_ListedFirst()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.RawMarkdown = "# Jane";
        var job = JobDescription.Create("role", new JobSource { Type = JobSourceType.PastedText });
        var coverage = MakeCoverage(CompanyType.Unknown,
            ("C#",         RequirementPriority.Required, "C#"),
            ("Kubernetes", RequirementPriority.Required, null));

        var prompt = TailoringPromptBuilder.Build(resume, job, new UserProfile(), coverage: coverage);

        int coveredIdx = prompt.IndexOf("C#", StringComparison.Ordinal);
        int gapIdx     = prompt.IndexOf("Kubernetes", StringComparison.Ordinal);
        Assert.True(coveredIdx < gapIdx, "Covered requirements should appear before gaps");
    }
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test tests/lucidRESUME.AI.Tests --filter TailoringPromptBuilderTests -v minimal
```
Expected: compile error — `Build` overload with `coverage` parameter does not exist.

**Step 3: Update TailoringPromptBuilder**

In `src/lucidRESUME.AI/TailoringPromptBuilder.cs`, update the `Build` signature and body:

```csharp
public static string Build(ResumeDocument resume, JobDescription job, UserProfile profile,
    IReadOnlyList<TermMatch>? termMappings = null,
    CoverageReport? coverage = null)
{
    var sb = new StringBuilder();
    sb.AppendLine("You are a professional CV editor. Your task is to tailor the candidate's resume for a specific job.");
    sb.AppendLine("CRITICAL RULES:");
    sb.AppendLine("- NEVER invent, fabricate, or exaggerate any facts, skills, or experiences.");
    sb.AppendLine("- Only reorder, rephrase, or emphasise information that already exists in the resume.");
    sb.AppendLine("- Do not add skills the candidate does not have.");
    sb.AppendLine();

    // Company-type tone guidance
    if (coverage is not null)
    {
        var tone = CompanyTypeTone(coverage.CompanyType);
        if (tone is not null)
        {
            sb.AppendLine($"## Company Type: {coverage.CompanyType}");
            sb.AppendLine(tone);
            sb.AppendLine();
        }
    }

    sb.AppendLine($"## Target Role: {job.Title} at {job.Company}");
    sb.AppendLine();

    // Coverage: answered questions first, then gaps
    if (coverage is { Entries.Count: > 0 })
    {
        sb.AppendLine("## Requirement Coverage (structure your answer around this):");

        var covered = coverage.Covered
            .OrderBy(e => e.Requirement.Priority)
            .ToList();
        var gaps = coverage.RequiredGaps.ToList();

        if (covered.Count > 0)
        {
            sb.AppendLine("### Answered — lead with these, strongest first:");
            foreach (var e in covered)
                sb.AppendLine($"- [{e.Requirement.Priority}] {e.Requirement.Text} → \"{e.Evidence}\"");
        }

        if (gaps.Count > 0)
        {
            sb.AppendLine("### Not covered — do NOT fabricate; omit or note as developing:");
            foreach (var e in gaps)
                sb.AppendLine($"- {e.Requirement.Text} (not covered in resume)");
        }

        sb.AppendLine();
    }
    else
    {
        // Fallback: plain skill lists
        sb.AppendLine($"## Required Skills: {string.Join(", ", job.RequiredSkills)}");
        sb.AppendLine($"## Preferred Skills: {string.Join(", ", job.PreferredSkills)}");
        sb.AppendLine();
    }

    // Term normalization (existing)
    if (termMappings is { Count: > 0 })
    {
        var pairs = termMappings
            .Where(m => m.MatchedSourceTerm is not null &&
                        !string.Equals(m.MatchedSourceTerm, m.TargetTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pairs.Count > 0)
        {
            sb.AppendLine("## Term Normalization (IMPORTANT):");
            sb.AppendLine("When the resume uses any of the following equivalent terms, USE the job description's exact phrasing:");
            foreach (var m in pairs)
                sb.AppendLine($"- Resume says \"{m.MatchedSourceTerm}\" → use \"{m.TargetTerm}\"");
            sb.AppendLine();
        }
    }

    if (profile.SkillsToEmphasise.Count > 0)
        sb.AppendLine($"## Candidate wants to emphasise: {string.Join(", ", profile.SkillsToEmphasise.Select(s => s.SkillName))}");
    if (profile.SkillsToAvoid.Count > 0)
        sb.AppendLine($"## Candidate prefers NOT to emphasise: {string.Join(", ", profile.SkillsToAvoid.Select(s => s.SkillName))}");
    if (profile.CareerGoals != null)
        sb.AppendLine($"## Career goals: {profile.CareerGoals}");

    sb.AppendLine();
    sb.AppendLine("## Candidate's Current Resume (Markdown):");
    sb.AppendLine(resume.RawMarkdown ?? "No markdown available.");
    sb.AppendLine();
    sb.AppendLine("Output the tailored resume as clean Markdown only. No explanations or preamble.");

    return sb.ToString();
}

private static string? CompanyTypeTone(CompanyType type) => type switch
{
    CompanyType.Startup     => "Tone: emphasise ownership, breadth, speed of delivery, and shipped outcomes. " +
                               "De-emphasise process-heavy corporate language.",
    CompanyType.ScaleUp     => "Tone: emphasise building systems at scale, repeatability, and team/function growth. " +
                               "Show you can take things from scrappy to structured.",
    CompanyType.Enterprise  => "Tone: emphasise process adherence, risk management, stakeholder communication, " +
                               "and delivery within constraints. Use precise, professional language.",
    CompanyType.Agency      => "Tone: emphasise speed, client communication, multi-project delivery, " +
                               "and breadth of domain exposure.",
    CompanyType.Consultancy => "Tone: emphasise structured problem-solving, stakeholder management, " +
                               "frameworks, and on-time delivery across engagements.",
    CompanyType.Finance     => "Tone: emphasise accuracy, compliance awareness, quantified impact, " +
                               "and regulated-environment experience. Every bullet should have a number.",
    CompanyType.Public      => "Tone: emphasise service delivery, accessibility, stakeholder diversity, " +
                               "and policy/compliance alignment.",
    CompanyType.Academic    => "Tone: emphasise research rigour, publications, teaching, and methodological depth.",
    _                       => null
};
```

**Note:** Add `using lucidRESUME.Core.Models.Coverage;` and `using lucidRESUME.Core.Models.Jobs;` at the top if not already present.

**Step 4: Run tests**

```bash
dotnet test tests/lucidRESUME.AI.Tests --filter TailoringPromptBuilderTests -v minimal
```
Expected: all 4 pass.

**Step 5: Commit**

```bash
git add src/lucidRESUME.AI/TailoringPromptBuilder.cs \
        tests/lucidRESUME.AI.Tests/
git commit -m "feat: coverage-aware, company-type-aware prompt builder"
```

---

## Task 7: Wire coverage into OllamaTailoringService

**Files:**
- Modify: `src/lucidRESUME.AI/OllamaTailoringService.cs`
- Modify: `src/lucidRESUME.AI/ServiceCollectionExtensions.cs`

**Step 1: Update OllamaTailoringService**

Add `ICoverageAnalyser coverageAnalyser` to the constructor. Before building the prompt, call it:

```csharp
// Constructor addition:
private readonly ICoverageAnalyser _coverageAnalyser;

public OllamaTailoringService(HttpClient http, IOptions<OllamaOptions> options,
    ILogger<OllamaTailoringService> logger, ITermNormalizer termNormalizer,
    ICoverageAnalyser coverageAnalyser)
{
    // ... existing assignments ...
    _coverageAnalyser = coverageAnalyser;
}

// In TailorAsync, after termMappings resolution:
CoverageReport? coverage = null;
try
{
    coverage = await _coverageAnalyser.AnalyseAsync(resume, job, ct);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Coverage analysis failed; continuing without it");
}

var prompt = TailoringPromptBuilder.Build(resume, job, profile, termMappings, coverage);
```

**Step 2: Update AI ServiceCollectionExtensions**

In `src/lucidRESUME.AI/ServiceCollectionExtensions.cs`, update the `OllamaTailoringService` registration to inject `ICoverageAnalyser`:

```csharp
// The DI container will resolve ICoverageAnalyser from lucidRESUME.Matching's registration.
// No explicit factory needed — all constructor params are registered.
services.AddHttpClient<IAiTailoringService, OllamaTailoringService>()
    .AddStandardResilienceHandler();
```

If it's currently registered differently, switch to standard constructor injection (the container resolves `ICoverageAnalyser` automatically since it's registered in `AddMatching()`).

**Step 3: Build**

```bash
dotnet build src/lucidRESUME.AI/lucidRESUME.AI.csproj --no-restore -v quiet
```
Expected: 0 errors.

**Step 4: Run all tests**

```bash
dotnet test -v minimal
```
Expected: all pass.

**Step 5: Commit**

```bash
git add src/lucidRESUME.AI/OllamaTailoringService.cs \
        src/lucidRESUME.AI/ServiceCollectionExtensions.cs
git commit -m "feat: wire ICoverageAnalyser into tailoring pipeline"
```

---

## Task 8: Surface coverage in the Jobs page UI

Show a coverage breakdown below the JD quality banner — required requirements with a ✓/✗ indicator.

**Files:**
- Modify: `src/lucidRESUME/ViewModels/Pages/JobsPageViewModel.cs`
- Modify: `src/lucidRESUME/Views/Pages/JobsPage.axaml`

**Step 1: Add ViewModel pieces**

New record in `JobsPageViewModel.cs`:
```csharp
public sealed record CoverageItemViewModel(
    string Requirement,
    string Priority,
    bool IsCovered,
    string? Evidence,
    string StatusColor);  // #A6E3A1 covered, #F38BA8 gap
```

New observable properties:
```csharp
[ObservableProperty] private bool _hasCoverageReport;
[ObservableProperty] private int _coveragePercent;
[ObservableProperty] private IReadOnlyList<CoverageItemViewModel> _coverageItems = [];
```

Add `ICoverageAnalyser _coverageAnalyser` to the constructor (inject it).

In `OnSelectedJobChanged`, fire `_ = RunCoverageAsync(value.FullJob)` alongside the existing JD quality call.

```csharp
private async Task RunCoverageAsync(JobDescription job)
{
    var state = await _store.LoadAsync();
    if (state.Resume is null) return;

    try
    {
        var report = await _coverageAnalyser.AnalyseAsync(state.Resume, job);
        CoveragePercent = report.CoveragePercent;
        CoverageItems = report.Entries
            .Where(e => e.Requirement.Priority == RequirementPriority.Required)
            .OrderByDescending(e => e.IsCovered)
            .Select(e => new CoverageItemViewModel(
                e.Requirement.Text,
                e.Requirement.Priority.ToString(),
                e.IsCovered,
                e.Evidence,
                e.IsCovered ? "#A6E3A1" : "#F38BA8"))
            .ToList()
            .AsReadOnly();
        HasCoverageReport = true;
    }
    catch
    {
        // non-critical
    }
}
```

**Step 2: Add AXAML panel**

In `JobsPage.axaml`, add a Coverage panel between the JD Quality Banner (Grid.Row="1") and Description (Grid.Row="2"). This requires adding another row to the grid (`RowDefinitions="Auto,Auto,Auto,*,Auto,Auto"`):

```xml
<!-- Coverage Panel -->
<Border Grid.Row="2" Background="#1E1E2E" CornerRadius="6" Padding="12,8" Margin="0,0,0,10"
        IsVisible="{Binding HasCoverageReport}">
  <StackPanel Spacing="6">
    <Grid ColumnDefinitions="*,Auto">
      <TextBlock Text="Requirements Coverage" FontSize="12" FontWeight="SemiBold"
                 Foreground="#89B4FA" VerticalAlignment="Center"/>
      <TextBlock Grid.Column="1" FontSize="12" FontWeight="Bold" Foreground="#A6ADC8">
        <Run Text="{Binding CoveragePercent}"/>
        <Run Text="%"/>
      </TextBlock>
    </Grid>
    <ItemsControl ItemsSource="{Binding CoverageItems}">
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="vm:CoverageItemViewModel">
          <StackPanel Orientation="Horizontal" Spacing="6" Margin="0,2">
            <Border Width="8" Height="8" CornerRadius="4"
                    Background="{Binding StatusColor}" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding Requirement}" FontSize="11"
                       Foreground="#BAC2DE" TextWrapping="Wrap"/>
          </StackPanel>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </StackPanel>
</Border>
```

Update remaining Grid.Row values: Description → Row 3, Aspects → Row 4, Actions → Row 5.

**Step 3: Build**

```bash
dotnet build src/lucidRESUME/lucidRESUME.csproj --no-restore -v quiet 2>&1 | grep "error CS"
```
Expected: no compiler errors (file-lock warnings from running app are fine).

**Step 4: Commit**

```bash
git add src/lucidRESUME/ViewModels/Pages/JobsPageViewModel.cs \
        src/lucidRESUME/Views/Pages/JobsPage.axaml
git commit -m "feat: show requirement coverage panel on Jobs page"
```

---

## Task 9: Add CompanyType to JobDescription (optional enrichment)

Persist the classified `CompanyType` on the `JobDescription` so it doesn't need to be re-classified on every access (cheap but avoids repeated work on large job lists).

**Files:**
- Modify: `src/lucidRESUME.Core/Models/Jobs/JobDescription.cs`

**Step 1: Add property**

```csharp
public CompanyType CompanyType { get; set; } = CompanyType.Unknown;
```

**Step 2: Update CompanyClassifier** to write back to the job after classification (optional — only if the service has a reference to the job):

In `ResumeCoverageAnalyser.AnalyseAsync`, after classification:
```csharp
job.CompanyType = companyType;
```

**Step 3: Update AspectExtractor** to read `job.CompanyType` if already set (avoid re-classifying):

In `AspectExtractor.ExtractCore`, replace the inline company-type detection with:
```csharp
var companyType = job.CompanyType != CompanyType.Unknown
    ? job.CompanyType.ToString()
    : /* existing inline detection */;
if (companyType is not null)
    Add(AspectType.CompanyType, companyType, "CompanyType");
```

**Step 4: Build + test**

```bash
dotnet build src/lucidRESUME.Matching/ --no-restore -v quiet && dotnet test -v minimal
```

**Step 5: Commit**

```bash
git add src/lucidRESUME.Core/Models/Jobs/JobDescription.cs \
        src/lucidRESUME.Matching/ResumeCoverageAnalyser.cs \
        src/lucidRESUME.Matching/AspectExtractor.cs
git commit -m "feat: persist CompanyType on JobDescription, unify classification"
```

---

## Summary

| Task | What it builds | Tests |
|------|---------------|-------|
| 1 | `CompanyType` enum | — |
| 2 | `CompanyClassifier` | 10 theory tests |
| 3 | Coverage domain models | — |
| 4 | `ICoverageAnalyser` interface | — |
| 5 | `ResumeCoverageAnalyser` | 7 unit tests |
| 6 | Coverage + company-tone prompt builder | 4 unit tests |
| 7 | Wire into tailoring service | Build check |
| 8 | Coverage panel in Jobs page UI | Build check |
| 9 | Persist `CompanyType` on `JobDescription` | Build + full suite |
