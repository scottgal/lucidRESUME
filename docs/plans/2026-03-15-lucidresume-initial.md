# lucidRESUME Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a personal job hunting AI assistant that ingests a resume, maintains a rich user profile, parses job descriptions, searches job boards, and uses local LLMs to tailor applications honestly.

**Architecture:** Portable class libraries (lucidRESUME.Core → domain out, everything else depends inward). Avalonia UI shell wires all libs via DI. Docling handles document ingestion; Microsoft Recognizers + ONNX NER handle extraction; Ollama/LLamaSharp handles AI tailoring.

**Tech Stack:** .NET 10, Avalonia 11, Docling (HTTP), Microsoft.Recognizers.Text, Microsoft.ML.OnnxRuntime, LLamaSharp, DocumentFormat.OpenXml, PdfPig, PDFsharp, SixLabors.ImageSharp, System.Text.Json, xUnit

**Reference code to copy/adapt:**
- Docling client: `D:\ma\internal-tools\src\MortgageAutomator.InternalTools.DocExtractor.Server\Infrastructure\Docling\DoclingClient.cs`
- Recognizer detector: `...\Infrastructure\Extraction\Recognizers\RecognizerDetector.cs`
- NER detector: `...\Infrastructure\Extraction\Ner\OnnxNerDetector.cs`
- Domain models: `...\Domain\Document.cs` and `...\Domain\ExtractedEntity.cs`

---

## Task 1: Solution & Project Scaffolding

**Files:**
- Modify: `lucidRESUME.sln`
- Create: `src/lucidRESUME.Core/lucidRESUME.Core.csproj`
- Create: `src/lucidRESUME.Ingestion/lucidRESUME.Ingestion.csproj`
- Create: `src/lucidRESUME.Extraction/lucidRESUME.Extraction.csproj`
- Create: `src/lucidRESUME.JobSpec/lucidRESUME.JobSpec.csproj`
- Create: `src/lucidRESUME.JobSearch/lucidRESUME.JobSearch.csproj`
- Create: `src/lucidRESUME.Matching/lucidRESUME.Matching.csproj`
- Create: `src/lucidRESUME.AI/lucidRESUME.AI.csproj`
- Create: `src/lucidRESUME.Export/lucidRESUME.Export.csproj`
- Create: `src/lucidRESUME/lucidRESUME.csproj` (Avalonia app)

**Step 1: Create solution folder structure**

```bash
cd E:/source/lucidRESUME/lucidRESUME
mkdir -p src/lucidRESUME.Core src/lucidRESUME.Ingestion src/lucidRESUME.Extraction
mkdir -p src/lucidRESUME.JobSpec src/lucidRESUME.JobSearch src/lucidRESUME.Matching
mkdir -p src/lucidRESUME.AI src/lucidRESUME.Export src/lucidRESUME
mkdir -p tests/lucidRESUME.Core.Tests tests/lucidRESUME.Extraction.Tests
mkdir -p tests/lucidRESUME.JobSpec.Tests tests/lucidRESUME.Matching.Tests
```

**Step 2: Create all class library projects**

```bash
cd E:/source/lucidRESUME/lucidRESUME
dotnet new classlib -n lucidRESUME.Core -o src/lucidRESUME.Core --framework net10.0
dotnet new classlib -n lucidRESUME.Ingestion -o src/lucidRESUME.Ingestion --framework net10.0
dotnet new classlib -n lucidRESUME.Extraction -o src/lucidRESUME.Extraction --framework net10.0
dotnet new classlib -n lucidRESUME.JobSpec -o src/lucidRESUME.JobSpec --framework net10.0
dotnet new classlib -n lucidRESUME.JobSearch -o src/lucidRESUME.JobSearch --framework net10.0
dotnet new classlib -n lucidRESUME.Matching -o src/lucidRESUME.Matching --framework net10.0
dotnet new classlib -n lucidRESUME.AI -o src/lucidRESUME.AI --framework net10.0
dotnet new classlib -n lucidRESUME.Export -o src/lucidRESUME.Export --framework net10.0
```

**Step 3: Create Avalonia app project**

```bash
dotnet new avalonia.app -n lucidRESUME -o src/lucidRESUME --framework net10.0
```

If Avalonia template not installed: `dotnet new install Avalonia.Templates`

**Step 4: Create test projects**

```bash
dotnet new xunit -n lucidRESUME.Core.Tests -o tests/lucidRESUME.Core.Tests --framework net10.0
dotnet new xunit -n lucidRESUME.Extraction.Tests -o tests/lucidRESUME.Extraction.Tests --framework net10.0
dotnet new xunit -n lucidRESUME.JobSpec.Tests -o tests/lucidRESUME.JobSpec.Tests --framework net10.0
dotnet new xunit -n lucidRESUME.Matching.Tests -o tests/lucidRESUME.Matching.Tests --framework net10.0
```

**Step 5: Add all projects to solution**

```bash
cd E:/source/lucidRESUME/lucidRESUME
dotnet sln add src/lucidRESUME.Core/lucidRESUME.Core.csproj
dotnet sln add src/lucidRESUME.Ingestion/lucidRESUME.Ingestion.csproj
dotnet sln add src/lucidRESUME.Extraction/lucidRESUME.Extraction.csproj
dotnet sln add src/lucidRESUME.JobSpec/lucidRESUME.JobSpec.csproj
dotnet sln add src/lucidRESUME.JobSearch/lucidRESUME.JobSearch.csproj
dotnet sln add src/lucidRESUME.Matching/lucidRESUME.Matching.csproj
dotnet sln add src/lucidRESUME.AI/lucidRESUME.AI.csproj
dotnet sln add src/lucidRESUME.Export/lucidRESUME.Export.csproj
dotnet sln add src/lucidRESUME/lucidRESUME.csproj
dotnet sln add tests/lucidRESUME.Core.Tests/lucidRESUME.Core.Tests.csproj
dotnet sln add tests/lucidRESUME.Extraction.Tests/lucidRESUME.Extraction.Tests.csproj
dotnet sln add tests/lucidRESUME.JobSpec.Tests/lucidRESUME.JobSpec.Tests.csproj
dotnet sln add tests/lucidRESUME.Matching.Tests/lucidRESUME.Matching.Tests.csproj
```

**Step 6: Wire project references (dependency rules: everything depends on Core only)**

```bash
# Ingestion depends on Core
dotnet add src/lucidRESUME.Ingestion reference src/lucidRESUME.Core
# Extraction depends on Core
dotnet add src/lucidRESUME.Extraction reference src/lucidRESUME.Core
# JobSpec depends on Core
dotnet add src/lucidRESUME.JobSpec reference src/lucidRESUME.Core
# JobSearch depends on Core + JobSpec
dotnet add src/lucidRESUME.JobSearch reference src/lucidRESUME.Core
dotnet add src/lucidRESUME.JobSearch reference src/lucidRESUME.JobSpec
# Matching depends on Core
dotnet add src/lucidRESUME.Matching reference src/lucidRESUME.Core
# AI depends on Core
dotnet add src/lucidRESUME.AI reference src/lucidRESUME.Core
# Export depends on Core
dotnet add src/lucidRESUME.Export reference src/lucidRESUME.Core
# App depends on everything
dotnet add src/lucidRESUME reference src/lucidRESUME.Core
dotnet add src/lucidRESUME reference src/lucidRESUME.Ingestion
dotnet add src/lucidRESUME reference src/lucidRESUME.Extraction
dotnet add src/lucidRESUME reference src/lucidRESUME.JobSpec
dotnet add src/lucidRESUME reference src/lucidRESUME.JobSearch
dotnet add src/lucidRESUME reference src/lucidRESUME.Matching
dotnet add src/lucidRESUME reference src/lucidRESUME.AI
dotnet add src/lucidRESUME reference src/lucidRESUME.Export
# Test references
dotnet add tests/lucidRESUME.Core.Tests reference src/lucidRESUME.Core
dotnet add tests/lucidRESUME.Extraction.Tests reference src/lucidRESUME.Core
dotnet add tests/lucidRESUME.Extraction.Tests reference src/lucidRESUME.Extraction
dotnet add tests/lucidRESUME.JobSpec.Tests reference src/lucidRESUME.Core
dotnet add tests/lucidRESUME.JobSpec.Tests reference src/lucidRESUME.JobSpec
dotnet add tests/lucidRESUME.Matching.Tests reference src/lucidRESUME.Core
dotnet add tests/lucidRESUME.Matching.Tests reference src/lucidRESUME.Matching
```

**Step 7: Verify solution builds**

```bash
dotnet build E:/source/lucidRESUME/lucidRESUME/lucidRESUME.sln
```
Expected: Build succeeded, 0 errors

**Step 8: Commit**

```bash
git init E:/source/lucidRESUME/lucidRESUME
cd E:/source/lucidRESUME/lucidRESUME
git add .
git commit -m "feat: scaffold solution with all projects and references"
```

---

## Task 2: Core Domain Models - Resume Schema

**Files:**
- Create: `src/lucidRESUME.Core/Models/Resume/ResumeDocument.cs`
- Create: `src/lucidRESUME.Core/Models/Resume/PersonalInfo.cs`
- Create: `src/lucidRESUME.Core/Models/Resume/WorkExperience.cs`
- Create: `src/lucidRESUME.Core/Models/Resume/Education.cs`
- Create: `src/lucidRESUME.Core/Models/Resume/Skill.cs`
- Create: `src/lucidRESUME.Core/Models/Resume/Certification.cs`
- Create: `src/lucidRESUME.Core/Models/Resume/Project.cs`
- Create: `src/lucidRESUME.Core/Models/Extraction/ExtractedEntity.cs`
- Create: `src/lucidRESUME.Core/Models/Extraction/DetectionSource.cs`
- Test: `tests/lucidRESUME.Core.Tests/Models/ResumeDocumentTests.cs`

**Step 1: Write failing test**

```csharp
// tests/lucidRESUME.Core.Tests/Models/ResumeDocumentTests.cs
public class ResumeDocumentTests
{
    [Fact]
    public void Create_SetsIdAndTimestamp()
    {
        var doc = ResumeDocument.Create("resume.pdf", "application/pdf", 12345);
        Assert.NotEqual(Guid.Empty, doc.ResumeId);
        Assert.Equal("resume.pdf", doc.FileName);
        Assert.True(doc.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AddEntity_AppendsToEntities()
    {
        var doc = ResumeDocument.Create("resume.pdf", "application/pdf", 12345);
        var entity = ExtractedEntity.Create("John Smith", "PersonName", DetectionSource.Ner, 0.95, 1);
        doc.AddEntity(entity);
        Assert.Single(doc.Entities);
        Assert.Equal("John Smith", doc.Entities[0].Value);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "ResumeDocumentTests"
```
Expected: FAIL - types not found

**Step 3: Implement models**

```csharp
// src/lucidRESUME.Core/Models/Extraction/DetectionSource.cs
namespace lucidRESUME.Core.Models.Extraction;

public enum DetectionSource { Pattern, Recognizer, Ner, Llm, Manual }
```

```csharp
// src/lucidRESUME.Core/Models/Extraction/ExtractedEntity.cs
namespace lucidRESUME.Core.Models.Extraction;

public sealed class ExtractedEntity
{
    public Guid EntityId { get; private set; }
    public string Value { get; private set; } = "";
    public string NormalizedValue { get; private set; } = "";
    public string Classification { get; private set; } = "";
    public double Confidence { get; private set; }
    public DetectionSource Source { get; private set; }
    public int PageNumber { get; private set; }
    public int? CharOffset { get; private set; }
    public int? CharLength { get; private set; }
    public string? Label { get; private set; }
    public string? Section { get; private set; }

    private ExtractedEntity() { }

    public static ExtractedEntity Create(string value, string classification,
        DetectionSource source, double confidence, int pageNumber) => new()
    {
        EntityId = Guid.NewGuid(),
        Value = value,
        NormalizedValue = value.Trim().ToLowerInvariant(),
        Classification = classification,
        Source = source,
        Confidence = confidence,
        PageNumber = pageNumber
    };

    public void SetLabel(string label, string? section = null)
    {
        Label = label;
        Section = section;
    }
}
```

```csharp
// src/lucidRESUME.Core/Models/Resume/PersonalInfo.cs
namespace lucidRESUME.Core.Models.Resume;

public sealed class PersonalInfo
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Location { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Summary { get; set; }
}
```

```csharp
// src/lucidRESUME.Core/Models/Resume/WorkExperience.cs
namespace lucidRESUME.Core.Models.Resume;

public sealed class WorkExperience
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Company { get; set; }
    public string? Title { get; set; }
    public string? Location { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public List<string> Achievements { get; set; } = [];
    public List<string> Technologies { get; set; } = [];
}
```

```csharp
// src/lucidRESUME.Core/Models/Resume/Education.cs
namespace lucidRESUME.Core.Models.Resume;

public sealed class Education
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Institution { get; set; }
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public double? Gpa { get; set; }
    public List<string> Highlights { get; set; } = [];
}
```

```csharp
// src/lucidRESUME.Core/Models/Resume/Skill.cs
namespace lucidRESUME.Core.Models.Resume;

public sealed class Skill
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }  // e.g. "Language", "Framework", "Tool"
    public int? YearsExperience { get; set; }
}
```

```csharp
// src/lucidRESUME.Core/Models/Resume/Certification.cs
namespace lucidRESUME.Core.Models.Resume;

public sealed class Certification
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Issuer { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? CredentialUrl { get; set; }
}
```

```csharp
// src/lucidRESUME.Core/Models/Resume/Project.cs
namespace lucidRESUME.Core.Models.Resume;

public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Technologies { get; set; } = [];
    public string? Url { get; set; }
    public DateOnly? Date { get; set; }
}
```

```csharp
// src/lucidRESUME.Core/Models/Resume/ResumeDocument.cs
namespace lucidRESUME.Core.Models.Resume;

public sealed class ResumeDocument
{
    public Guid ResumeId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long FileSizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }

    // Docling output
    public string? RawMarkdown { get; private set; }
    public string? RawJson { get; private set; }
    public string? PlainText { get; private set; }

    // Parsed sections
    public PersonalInfo Personal { get; private set; } = new();
    public List<WorkExperience> Experience { get; private set; } = [];
    public List<Education> Education { get; private set; } = [];
    public List<Skill> Skills { get; private set; } = [];
    public List<Certification> Certifications { get; private set; } = [];
    public List<Project> Projects { get; private set; } = [];

    // Extraction metadata
    private readonly List<ExtractedEntity> _entities = [];
    public IReadOnlyList<ExtractedEntity> Entities => _entities.AsReadOnly();

    // Tailoring metadata
    public Guid? TailoredForJobId { get; private set; }
    public bool IsTailored => TailoredForJobId.HasValue;

    private ResumeDocument() { }

    public static ResumeDocument Create(string fileName, string contentType, long fileSizeBytes) => new()
    {
        ResumeId = Guid.NewGuid(),
        FileName = fileName,
        ContentType = contentType,
        FileSizeBytes = fileSizeBytes,
        CreatedAt = DateTimeOffset.UtcNow
    };

    public void SetDoclingOutput(string markdown, string? json, string? plainText)
    {
        RawMarkdown = markdown;
        RawJson = json;
        PlainText = plainText;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public void AddEntity(ExtractedEntity entity) => _entities.Add(entity);

    public void MarkTailoredFor(Guid jobId) => TailoredForJobId = jobId;
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "ResumeDocumentTests"
```
Expected: PASS

**Step 5: Commit**

```bash
git add src/lucidRESUME.Core tests/lucidRESUME.Core.Tests
git commit -m "feat: add core resume domain models and extracted entity"
```

---

## Task 3: Core Domain Models - UserProfile & JobDescription

**Files:**
- Create: `src/lucidRESUME.Core/Models/Profile/UserProfile.cs`
- Create: `src/lucidRESUME.Core/Models/Profile/SkillPreference.cs`
- Create: `src/lucidRESUME.Core/Models/Profile/WorkPreferences.cs`
- Create: `src/lucidRESUME.Core/Models/Jobs/JobDescription.cs`
- Create: `src/lucidRESUME.Core/Models/Jobs/JobSource.cs`
- Create: `src/lucidRESUME.Core/Models/Jobs/SalaryRange.cs`
- Test: `tests/lucidRESUME.Core.Tests/Models/UserProfileTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/lucidRESUME.Core.Tests/Models/UserProfileTests.cs
public class UserProfileTests
{
    [Fact]
    public void BlockCompany_AddsToBlocklist()
    {
        var profile = new UserProfile();
        profile.BlockCompany("Amazon");
        Assert.Contains("Amazon", profile.BlockedCompanies);
    }

    [Fact]
    public void AddAvoidSkill_AppendsToList()
    {
        var profile = new UserProfile();
        profile.AvoidSkill("PHP", "Not interested in legacy web");
        Assert.Single(profile.SkillsToAvoid);
        Assert.Equal("PHP", profile.SkillsToAvoid[0].SkillName);
    }
}
```

**Step 2: Run to verify fail**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "UserProfileTests"
```
Expected: FAIL

**Step 3: Implement models**

```csharp
// src/lucidRESUME.Core/Models/Profile/SkillPreference.cs
namespace lucidRESUME.Core.Models.Profile;

public sealed class SkillPreference
{
    public string SkillName { get; set; } = "";
    public string? Reason { get; set; }
}
```

```csharp
// src/lucidRESUME.Core/Models/Profile/WorkPreferences.cs
namespace lucidRESUME.Core.Models.Profile;

public sealed class WorkPreferences
{
    public bool OpenToRemote { get; set; } = true;
    public bool OpenToHybrid { get; set; } = true;
    public bool OpenToOnsite { get; set; } = true;
    public List<string> PreferredLocations { get; set; } = [];
    public decimal? MinSalary { get; set; }
    public string? PreferredCurrency { get; set; } = "GBP";
    public List<string> TargetRoles { get; set; } = [];
    public List<string> TargetIndustries { get; set; } = [];
    public List<string> BlockedIndustries { get; set; } = [];
    public int? MaxCommuteMinutes { get; set; }
    public string? Notes { get; set; }
}
```

```csharp
// src/lucidRESUME.Core/Models/Profile/UserProfile.cs
namespace lucidRESUME.Core.Models.Profile;

public sealed class UserProfile
{
    public Guid ProfileId { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    // Who they are
    public string? DisplayName { get; set; }
    public string? CurrentTitle { get; set; }
    public int? YearsOfExperience { get; set; }

    // What they want
    public WorkPreferences Preferences { get; set; } = new();

    // Skills they actively want to use
    public List<SkillPreference> SkillsToEmphasise { get; private set; } = [];

    // Skills they have but want to avoid
    public List<SkillPreference> SkillsToAvoid { get; private set; } = [];

    // Companies/orgs they won't work for
    public List<string> BlockedCompanies { get; private set; } = [];

    // Free-form notes for the AI (context, career goals, anything)
    public string? CareerGoals { get; set; }
    public string? AdditionalContext { get; set; }

    public void BlockCompany(string company)
    {
        if (!BlockedCompanies.Contains(company, StringComparer.OrdinalIgnoreCase))
            BlockedCompanies.Add(company);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void EmphasiseSkill(string skill, string? reason = null)
    {
        SkillsToEmphasise.Add(new SkillPreference { SkillName = skill, Reason = reason });
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AvoidSkill(string skill, string? reason = null)
    {
        SkillsToAvoid.Add(new SkillPreference { SkillName = skill, Reason = reason });
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

```csharp
// src/lucidRESUME.Core/Models/Jobs/SalaryRange.cs
namespace lucidRESUME.Core.Models.Jobs;

public sealed record SalaryRange(decimal? Min, decimal? Max, string Currency = "GBP", string Period = "annual");
```

```csharp
// src/lucidRESUME.Core/Models/Jobs/JobSource.cs
namespace lucidRESUME.Core.Models.Jobs;

public enum JobSourceType { PastedText, Url, Adzuna, Remotive, Reed, Indeed, LinkedIn }

public sealed class JobSource
{
    public JobSourceType Type { get; init; }
    public string? Url { get; init; }
    public string? ApiName { get; init; }
    public string? ExternalId { get; init; }
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

```csharp
// src/lucidRESUME.Core/Models/Jobs/JobDescription.cs
namespace lucidRESUME.Core.Models.Jobs;

public sealed class JobDescription
{
    public Guid JobId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Source
    public JobSource Source { get; private set; } = new();

    // Core fields
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? Location { get; set; }
    public bool? IsRemote { get; set; }
    public bool? IsHybrid { get; set; }
    public SalaryRange? Salary { get; set; }

    // Extracted requirements
    public List<string> RequiredSkills { get; set; } = [];
    public List<string> PreferredSkills { get; set; } = [];
    public int? RequiredYearsExperience { get; set; }
    public string? RequiredEducation { get; set; }
    public List<string> Responsibilities { get; set; } = [];
    public List<string> Benefits { get; set; } = [];

    // Raw content always preserved
    public string RawText { get; private set; } = "";

    // Application tracking
    public double? MatchScore { get; private set; }
    public bool IsBlocked { get; private set; }
    public string? BlockReason { get; private set; }

    private JobDescription() { }

    public static JobDescription Create(string rawText, JobSource source) => new()
    {
        JobId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        RawText = rawText,
        Source = source
    };

    public void SetMatchScore(double score) => MatchScore = score;

    public void Block(string reason)
    {
        IsBlocked = true;
        BlockReason = reason;
    }
}
```

**Step 4: Run tests**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "UserProfileTests"
```
Expected: PASS

**Step 5: Commit**

```bash
git add src/lucidRESUME.Core tests/lucidRESUME.Core.Tests
git commit -m "feat: add UserProfile and JobDescription domain models"
```

---

## Task 4: Core Interfaces & Persistence Abstractions

**Files:**
- Create: `src/lucidRESUME.Core/Interfaces/IDoclingClient.cs`
- Create: `src/lucidRESUME.Core/Interfaces/IEntityDetector.cs`
- Create: `src/lucidRESUME.Core/Interfaces/IResumeParser.cs`
- Create: `src/lucidRESUME.Core/Interfaces/IJobSpecParser.cs`
- Create: `src/lucidRESUME.Core/Interfaces/IJobSearchAdapter.cs`
- Create: `src/lucidRESUME.Core/Interfaces/IResumeExporter.cs`
- Create: `src/lucidRESUME.Core/Interfaces/IMatchingService.cs`
- Create: `src/lucidRESUME.Core/Interfaces/IAiTailoringService.cs`
- Create: `src/lucidRESUME.Core/Persistence/IAppStore.cs`

**Step 1: No test for interfaces - implement directly**

```csharp
// src/lucidRESUME.Core/Interfaces/IDoclingClient.cs
namespace lucidRESUME.Core.Interfaces;

public record DoclingConversionResult(string Markdown, string? Json, string? PlainText);

public interface IDoclingClient
{
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
    Task<DoclingConversionResult> ConvertAsync(string filePath, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Interfaces/IEntityDetector.cs
namespace lucidRESUME.Core.Interfaces;

public record DetectionContext(string Text, string? Markdown = null, int PageNumber = 1);

public interface IEntityDetector
{
    string DetectorId { get; }
    int Priority { get; }
    Task<IReadOnlyList<ExtractedEntity>> DetectAsync(DetectionContext context, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Interfaces/IResumeParser.cs
namespace lucidRESUME.Core.Interfaces;

public interface IResumeParser
{
    Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Interfaces/IJobSpecParser.cs
namespace lucidRESUME.Core.Interfaces;

public interface IJobSpecParser
{
    Task<JobDescription> ParseFromTextAsync(string text, CancellationToken ct = default);
    Task<JobDescription> ParseFromUrlAsync(string url, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Interfaces/IJobSearchAdapter.cs
namespace lucidRESUME.Core.Interfaces;

public record JobSearchQuery(string Keywords, string? Location = null, bool? RemoteOnly = null, int MaxResults = 20);

public interface IJobSearchAdapter
{
    string AdapterName { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Interfaces/IResumeExporter.cs
namespace lucidRESUME.Core.Interfaces;

public enum ExportFormat { JsonResume, Pdf, Docx, Markdown }

public interface IResumeExporter
{
    ExportFormat Format { get; }
    Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Interfaces/IMatchingService.cs
namespace lucidRESUME.Core.Interfaces;

public record MatchResult(double Score, List<string> MatchedSkills, List<string> MissingSkills, string Summary);

public interface IMatchingService
{
    Task<MatchResult> MatchAsync(ResumeDocument resume, JobDescription job, UserProfile profile, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Interfaces/IAiTailoringService.cs
namespace lucidRESUME.Core.Interfaces;

public interface IAiTailoringService
{
    bool IsAvailable { get; }
    Task<ResumeDocument> TailorAsync(ResumeDocument resume, JobDescription job, UserProfile profile, CancellationToken ct = default);
}
```

```csharp
// src/lucidRESUME.Core/Persistence/IAppStore.cs
namespace lucidRESUME.Core.Persistence;

/// Single-user local store - all data lives in one JSON file.
public interface IAppStore
{
    Task<AppState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppState state, CancellationToken ct = default);
}

public sealed class AppState
{
    public ResumeDocument? Resume { get; set; }
    public UserProfile Profile { get; set; } = new();
    public List<JobDescription> Jobs { get; set; } = [];
    public DateTimeOffset LastSaved { get; set; }
}
```

**Step 2: Build to verify no errors**

```bash
dotnet build src/lucidRESUME.Core/lucidRESUME.Core.csproj
```
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/lucidRESUME.Core
git commit -m "feat: add core interfaces and app store abstraction"
```

---

## Task 5: JSON App Store (Local Persistence)

**Files:**
- Create: `src/lucidRESUME.Core/Persistence/JsonAppStore.cs`
- Test: `tests/lucidRESUME.Core.Tests/Persistence/JsonAppStoreTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/lucidRESUME.Core.Tests/Persistence/JsonAppStoreTests.cs
public class JsonAppStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"lucidresume_test_{Guid.NewGuid()}.json");

    [Fact]
    public async Task SaveAndLoad_RoundTripsAppState()
    {
        var store = new JsonAppStore(_tempPath);
        var state = new AppState
        {
            Profile = new UserProfile { DisplayName = "Test User" },
            Jobs = [JobDescription.Create("Senior Dev role", new JobSource { Type = JobSourceType.PastedText })]
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.Equal("Test User", loaded.Profile.DisplayName);
        Assert.Single(loaded.Jobs);
    }

    [Fact]
    public async Task Load_WhenNoFile_ReturnsEmptyState()
    {
        var store = new JsonAppStore(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json"));
        var state = await store.LoadAsync();
        Assert.NotNull(state);
        Assert.Empty(state.Jobs);
    }

    public void Dispose() => File.Delete(_tempPath);
}
```

**Step 2: Run to verify fail**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "JsonAppStoreTests"
```
Expected: FAIL

**Step 3: Add System.Text.Json to Core**

```bash
# Already included in .NET 10 BCL - no package needed
```

**Step 4: Implement JsonAppStore**

```csharp
// src/lucidRESUME.Core/Persistence/JsonAppStore.cs
namespace lucidRESUME.Core.Persistence;

public sealed class JsonAppStore : IAppStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonAppStore(string filePath) => _filePath = filePath;

    public async Task<AppState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return new AppState();

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AppState>(stream, Options, ct) ?? new AppState();
    }

    public async Task SaveAsync(AppState state, CancellationToken ct = default)
    {
        state.LastSaved = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, state, Options, ct);
    }
}
```

**Step 5: Run tests**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "JsonAppStoreTests"
```
Expected: PASS

**Step 6: Commit**

```bash
git add src/lucidRESUME.Core tests/lucidRESUME.Core.Tests
git commit -m "feat: add JSON-backed local app store for single-user persistence"
```

---

## Task 6: Ingestion - Docling Client

**Files:**
- Create: `src/lucidRESUME.Ingestion/Docling/DoclingOptions.cs`
- Create: `src/lucidRESUME.Ingestion/Docling/DoclingClient.cs`
- Create: `src/lucidRESUME.Ingestion/ServiceCollectionExtensions.cs`

**Step 1: Add NuGet packages to Ingestion project**

```bash
cd E:/source/lucidRESUME/lucidRESUME
dotnet add src/lucidRESUME.Ingestion package Microsoft.Extensions.Http.Resilience
dotnet add src/lucidRESUME.Ingestion package Microsoft.Extensions.Options
dotnet add src/lucidRESUME.Ingestion package Microsoft.Extensions.Logging.Abstractions
```

**Step 2: Copy and adapt from DocExtractor reference**

Port `DoclingClient.cs` from:
`D:\ma\internal-tools\src\MortgageAutomator.InternalTools.DocExtractor.Server\Infrastructure\Docling\DoclingClient.cs`

Key adaptations:
- Remove all mortgage domain references
- Replace `DoclingConversionResult` with Core's version
- Implement `IDoclingClient` from `lucidRESUME.Core`
- Keep the async polling pattern intact

```csharp
// src/lucidRESUME.Ingestion/Docling/DoclingOptions.cs
namespace lucidRESUME.Ingestion.Docling;

public sealed class DoclingOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5001";
    public int PollingIntervalMs { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 120;
}
```

```csharp
// src/lucidRESUME.Ingestion/ServiceCollectionExtensions.cs
namespace lucidRESUME.Ingestion;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngestion(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DoclingOptions>(config.GetSection("Docling"));
        services.AddHttpClient<IDoclingClient, DoclingClient>()
            .AddStandardResilienceHandler();
        return services;
    }
}
```

**Step 3: Build to verify no errors**

```bash
dotnet build src/lucidRESUME.Ingestion/lucidRESUME.Ingestion.csproj
```
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/lucidRESUME.Ingestion
git commit -m "feat: add Docling client (ported from DocExtractor)"
```

---

## Task 7: Extraction Pipeline - Recognizers & NER

**Files:**
- Create: `src/lucidRESUME.Extraction/Recognizers/ResumeRecognizerDetector.cs`
- Create: `src/lucidRESUME.Extraction/Ner/OnnxNerDetector.cs`
- Create: `src/lucidRESUME.Extraction/Pipeline/ExtractionPipeline.cs`
- Create: `src/lucidRESUME.Extraction/ServiceCollectionExtensions.cs`
- Test: `tests/lucidRESUME.Extraction.Tests/Recognizers/ResumeRecognizerDetectorTests.cs`

**Step 1: Add NuGet packages to Extraction project**

```bash
dotnet add src/lucidRESUME.Extraction package Microsoft.Recognizers.Text
dotnet add src/lucidRESUME.Extraction package Microsoft.Recognizers.Text.DateTime
dotnet add src/lucidRESUME.Extraction package Microsoft.Recognizers.Text.Number
dotnet add src/lucidRESUME.Extraction package Microsoft.Recognizers.Text.Sequence
dotnet add src/lucidRESUME.Extraction package Microsoft.ML.OnnxRuntime
dotnet add src/lucidRESUME.Extraction package Microsoft.Extensions.Logging.Abstractions
```

**Step 2: Write failing test for recognizer**

```csharp
// tests/lucidRESUME.Extraction.Tests/Recognizers/ResumeRecognizerDetectorTests.cs
public class ResumeRecognizerDetectorTests
{
    private readonly ResumeRecognizerDetector _detector = new(NullLogger<ResumeRecognizerDetector>.Instance);

    [Fact]
    public async Task DetectAsync_FindsEmailInText()
    {
        var context = new DetectionContext("Contact me at john.smith@example.com for more info.");
        var entities = await _detector.DetectAsync(context);
        Assert.Contains(entities, e => e.Classification == "Email");
    }

    [Fact]
    public async Task DetectAsync_FindsPhoneInText()
    {
        var context = new DetectionContext("Call me on +44 7700 900123");
        var entities = await _detector.DetectAsync(context);
        Assert.Contains(entities, e => e.Classification == "PhoneNumber");
    }

    [Fact]
    public async Task DetectAsync_FindsDateRangeInExperience()
    {
        var context = new DetectionContext("Senior Developer, Acme Corp, January 2020 - March 2024");
        var entities = await _detector.DetectAsync(context);
        Assert.Contains(entities, e => e.Classification == "DateRange" || e.Classification == "Date");
    }
}
```

**Step 3: Run to verify fail**

```bash
dotnet test tests/lucidRESUME.Extraction.Tests --filter "ResumeRecognizerDetectorTests"
```

**Step 4: Implement ResumeRecognizerDetector**

Port from:
`D:\ma\internal-tools\src\MortgageAutomator.InternalTools.DocExtractor.Server\Infrastructure\Extraction\Recognizers\RecognizerDetector.cs`

Key adaptations:
- Remove all mortgage-specific patterns (SSN, EIN, routing numbers, FICO scores, loan numbers)
- Keep: Email, Phone, URL, Date, DateRange, Number, Currency, Percentage, Age
- Add resume-specific: LinkedIn URL pattern, GitHub URL pattern
- Implement `IEntityDetector` from `lucidRESUME.Core`

**Step 5: Implement OnnxNerDetector**

Port from:
`D:\ma\internal-tools\src\MortgageAutomator.InternalTools.DocExtractor.Server\Infrastructure\Extraction\Ner\OnnxNerDetector.cs`

Keep all four NER labels: PER→PersonName, ORG→Organization, LOC→Address, MISC→Miscellaneous

**Step 6: Implement ExtractionPipeline**

```csharp
// src/lucidRESUME.Extraction/Pipeline/ExtractionPipeline.cs
namespace lucidRESUME.Extraction.Pipeline;

public sealed class ExtractionPipeline
{
    private readonly IEnumerable<IEntityDetector> _detectors;

    public ExtractionPipeline(IEnumerable<IEntityDetector> detectors)
        => _detectors = detectors.OrderBy(d => d.Priority);

    public async Task<IReadOnlyList<ExtractedEntity>> RunAsync(DetectionContext context, CancellationToken ct = default)
    {
        var all = new List<ExtractedEntity>();
        foreach (var detector in _detectors)
        {
            var found = await detector.DetectAsync(context, ct);
            all.AddRange(found);
        }
        return all.AsReadOnly();
    }
}
```

**Step 7: Run tests**

```bash
dotnet test tests/lucidRESUME.Extraction.Tests --filter "ResumeRecognizerDetectorTests"
```
Expected: PASS

**Step 8: Commit**

```bash
git add src/lucidRESUME.Extraction tests/lucidRESUME.Extraction.Tests
git commit -m "feat: add extraction pipeline with recognizer and ONNX NER detectors"
```

---

## Task 8: Resume Parser (Docling → Schema Mapping)

**Files:**
- Create: `src/lucidRESUME.Ingestion/Parsing/ResumeParser.cs`
- Create: `src/lucidRESUME.Ingestion/Parsing/SectionClassifier.cs`
- Test: `tests/lucidRESUME.Core.Tests/Parsing/SectionClassifierTests.cs`

**Step 1: Write failing test**

```csharp
public class SectionClassifierTests
{
    [Theory]
    [InlineData("## Work Experience", "Experience")]
    [InlineData("## Employment History", "Experience")]
    [InlineData("## Education", "Education")]
    [InlineData("## Skills", "Skills")]
    [InlineData("## Technical Skills", "Skills")]
    [InlineData("## Certifications", "Certifications")]
    [InlineData("## Projects", "Projects")]
    public void ClassifyHeading_ReturnsSectionName(string heading, string expected)
    {
        var result = SectionClassifier.ClassifyHeading(heading);
        Assert.Equal(expected, result);
    }
}
```

**Step 2: Run to verify fail**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "SectionClassifierTests"
```

**Step 3: Implement SectionClassifier**

```csharp
// src/lucidRESUME.Ingestion/Parsing/SectionClassifier.cs
namespace lucidRESUME.Ingestion.Parsing;

public static class SectionClassifier
{
    private static readonly Dictionary<string[], string> SectionMap = new(new StringArrayComparer())
    {
        { ["experience", "employment", "work history", "career history", "professional experience"], "Experience" },
        { ["education", "academic", "qualifications"], "Education" },
        { ["skills", "technical skills", "core competencies", "technologies", "expertise"], "Skills" },
        { ["certifications", "certificates", "accreditations", "credentials"], "Certifications" },
        { ["projects", "personal projects", "side projects", "open source"], "Projects" },
        { ["summary", "profile", "objective", "about me", "professional summary"], "Summary" },
    };

    public static string? ClassifyHeading(string heading)
    {
        var clean = heading.TrimStart('#').Trim().ToLowerInvariant();
        foreach (var (keys, section) in SectionMap)
            if (keys.Any(k => clean.Contains(k)))
                return section;
        return null;
    }
}
```

**Step 4: Implement ResumeParser**

```csharp
// src/lucidRESUME.Ingestion/Parsing/ResumeParser.cs
namespace lucidRESUME.Ingestion.Parsing;

public sealed class ResumeParser : IResumeParser
{
    private readonly IDoclingClient _docling;
    private readonly ExtractionPipeline _extraction;
    private readonly ILogger<ResumeParser> _logger;

    public ResumeParser(IDoclingClient docling, ExtractionPipeline extraction, ILogger<ResumeParser> logger)
    {
        _docling = docling;
        _extraction = extraction;
        _logger = logger;
    }

    public async Task<ResumeDocument> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var resume = ResumeDocument.Create(fileInfo.Name, GetContentType(fileInfo.Extension), fileInfo.Length);

        _logger.LogInformation("Converting {File} via Docling", fileInfo.Name);
        var docling = await _docling.ConvertAsync(filePath, ct);
        resume.SetDoclingOutput(docling.Markdown, docling.Json, docling.PlainText);

        var context = new DetectionContext(docling.PlainText ?? docling.Markdown, docling.Markdown);
        var entities = await _extraction.RunAsync(context, ct);
        foreach (var entity in entities)
            resume.AddEntity(entity);

        MapEntitiesToSchema(resume, entities);
        return resume;
    }

    private static void MapEntitiesToSchema(ResumeDocument resume, IReadOnlyList<ExtractedEntity> entities)
    {
        // Map high-confidence entities to personal info
        resume.Personal.Email = entities.FirstOrDefault(e => e.Classification == "Email")?.Value;
        resume.Personal.Phone = entities.FirstOrDefault(e => e.Classification == "PhoneNumber")?.Value;
        resume.Personal.FullName = entities.FirstOrDefault(e => e.Classification == "PersonName" && e.Confidence > 0.85)?.Value;
        resume.Personal.LinkedInUrl = entities.FirstOrDefault(e => e.Classification == "LinkedInUrl")?.Value;
        resume.Personal.GitHubUrl = entities.FirstOrDefault(e => e.Classification == "GitHubUrl")?.Value;
    }

    private static string GetContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };
}
```

**Step 5: Run tests**

```bash
dotnet test tests/lucidRESUME.Core.Tests --filter "SectionClassifierTests"
```
Expected: PASS

**Step 6: Commit**

```bash
git add src/lucidRESUME.Ingestion tests/lucidRESUME.Core.Tests
git commit -m "feat: add resume parser with section classifier and entity mapping"
```

---

## Task 9: Job Spec Parser

**Files:**
- Create: `src/lucidRESUME.JobSpec/JobSpecParser.cs`
- Create: `src/lucidRESUME.JobSpec/JobSpecRecognizers.cs`
- Create: `src/lucidRESUME.JobSpec/ServiceCollectionExtensions.cs`
- Test: `tests/lucidRESUME.JobSpec.Tests/JobSpecParserTests.cs`

**Step 1: Add NuGet packages**

```bash
dotnet add src/lucidRESUME.JobSpec package Microsoft.Recognizers.Text
dotnet add src/lucidRESUME.JobSpec package Microsoft.Recognizers.Text.Number
dotnet add src/lucidRESUME.JobSpec package Microsoft.Recognizers.Text.Sequence
dotnet add src/lucidRESUME.JobSpec package AngleSharp
dotnet add src/lucidRESUME.JobSpec package Microsoft.Extensions.Http.Resilience
```

**Step 2: Write failing tests**

```csharp
// tests/lucidRESUME.JobSpec.Tests/JobSpecParserTests.cs
public class JobSpecParserTests
{
    private readonly JobSpecParser _parser = new(NullLogger<JobSpecParser>.Instance);

    [Fact]
    public async Task ParseFromText_ExtractsTitleAndCompany()
    {
        var text = """
            Senior Software Engineer at Acme Corp
            We are looking for an experienced developer...
            Required: 5+ years of C# experience
            Skills: .NET, Azure, SQL Server
            Salary: £60,000 - £80,000 per year
            Remote: Yes
            """;

        var job = await _parser.ParseFromTextAsync(text);

        Assert.Equal("Senior Software Engineer", job.Title);
        Assert.Equal("Acme Corp", job.Company);
        Assert.Contains(".NET", job.RequiredSkills);
        Assert.True(job.IsRemote);
    }

    [Fact]
    public async Task ParseFromText_ExtractsSalary()
    {
        var text = "Salary: £60,000 - £80,000 per annum. Location: London.";
        var job = await _parser.ParseFromTextAsync(text);
        Assert.NotNull(job.Salary);
        Assert.Equal(60000, job.Salary!.Min);
        Assert.Equal(80000, job.Salary.Max);
    }
}
```

**Step 3: Run to verify fail**

```bash
dotnet test tests/lucidRESUME.JobSpec.Tests
```

**Step 4: Implement JobSpecParser**

Parse job descriptions using:
- Microsoft Recognizers for dates, numbers, currencies
- Regex patterns for skills lists (comma-separated, bullet lists)
- Keyword matching for remote/hybrid indicators
- Simple heuristic: first line usually contains title + company ("X at Y" or "X - Y")

```csharp
// src/lucidRESUME.JobSpec/JobSpecParser.cs
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

        // Skills extraction - lines starting with bullet or "required:"
        job.RequiredSkills = ExtractSkillsList(text, "required");
        job.PreferredSkills = ExtractSkillsList(text, "preferred|nice to have|desirable");

        // Salary via Microsoft Recognizers
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

    private static string StripHtml(string html)
    {
        // Simple tag stripping - use AngleSharp for production accuracy
        return Regex.Replace(html, "<[^>]+>", " ").Trim();
    }
}
```

**Step 5: Run tests**

```bash
dotnet test tests/lucidRESUME.JobSpec.Tests
```
Expected: PASS

**Step 6: Commit**

```bash
git add src/lucidRESUME.JobSpec tests/lucidRESUME.JobSpec.Tests
git commit -m "feat: add job spec parser with salary, skills, and remote extraction"
```

---

## Task 10: Job Search Adapters

**Files:**
- Create: `src/lucidRESUME.JobSearch/Adapters/AdzunaAdapter.cs`
- Create: `src/lucidRESUME.JobSearch/Adapters/RemotiveAdapter.cs`
- Create: `src/lucidRESUME.JobSearch/JobSearchService.cs`
- Create: `src/lucidRESUME.JobSearch/ServiceCollectionExtensions.cs`

**Step 1: Add NuGet packages**

```bash
dotnet add src/lucidRESUME.JobSearch package Microsoft.Extensions.Http.Resilience
dotnet add src/lucidRESUME.JobSearch package Microsoft.Extensions.Options
```

**Step 2: Implement Remotive adapter (no API key required)**

```csharp
// src/lucidRESUME.JobSearch/Adapters/RemotiveAdapter.cs
namespace lucidRESUME.JobSearch.Adapters;

/// Remotive.com - free, no API key required
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
```

**Step 3: Implement Adzuna adapter (free tier, requires API key)**

```csharp
// src/lucidRESUME.JobSearch/Adapters/AdzunaAdapter.cs
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
            job.Salary = new SalaryRange(r.SalaryMin, r.SalaryMax);
        return job;
    }

    private record AdzunaResponse(List<AdzunaResult> Results);
    private record AdzunaResult(string Id, string Title, AdzunaCompany? Company,
        AdzunaLocation? Location, string? Description, string RedirectUrl,
        decimal? SalaryMin, decimal? SalaryMax);
    private record AdzunaCompany(string DisplayName);
    private record AdzunaLocation(string DisplayName);
}
```

**Step 4: Implement JobSearchService**

```csharp
// src/lucidRESUME.JobSearch/JobSearchService.cs
namespace lucidRESUME.JobSearch;

public sealed class JobSearchService
{
    private readonly IEnumerable<IJobSearchAdapter> _adapters;
    private readonly IJobSpecParser _parser;

    public JobSearchService(IEnumerable<IJobSearchAdapter> adapters, IJobSpecParser parser)
    {
        _adapters = adapters;
        _parser = parser;
    }

    public async Task<IReadOnlyList<JobDescription>> SearchAllAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var tasks = _adapters
            .Where(a => a.IsConfigured)
            .Select(a => a.SearchAsync(query, ct));

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }
}
```

**Step 5: Build to verify**

```bash
dotnet build src/lucidRESUME.JobSearch/lucidRESUME.JobSearch.csproj
```

**Step 6: Commit**

```bash
git add src/lucidRESUME.JobSearch
git commit -m "feat: add Adzuna and Remotive job search adapters"
```

---

## Task 11: Matching Service

**Files:**
- Create: `src/lucidRESUME.Matching/SkillMatchingService.cs`
- Test: `tests/lucidRESUME.Matching.Tests/SkillMatchingServiceTests.cs`

**Step 1: Write failing tests**

```csharp
public class SkillMatchingServiceTests
{
    private readonly SkillMatchingService _service = new();

    [Fact]
    public async Task Match_HighOverlap_ReturnsHighScore()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        resume.Skills.Add(new Skill { Name = ".NET" });
        resume.Skills.Add(new Skill { Name = "Azure" });
        resume.Skills.Add(new Skill { Name = "SQL Server" });

        var job = JobDescription.Create("Dev role", new JobSource { Type = JobSourceType.PastedText });
        job.RequiredSkills = [".NET", "Azure", "SQL Server"];

        var profile = new UserProfile();
        var result = await _service.MatchAsync(resume, job, profile);

        Assert.True(result.Score >= 0.8);
        Assert.Equal(3, result.MatchedSkills.Count);
        Assert.Empty(result.MissingSkills);
    }

    [Fact]
    public async Task Match_BlockedCompany_ReturnsZero()
    {
        var resume = ResumeDocument.Create("cv.pdf", "application/pdf", 0);
        var job = JobDescription.Create("Role at Amazon", new JobSource { Type = JobSourceType.PastedText });
        job.Company = "Amazon";

        var profile = new UserProfile();
        profile.BlockCompany("Amazon");

        var result = await _service.MatchAsync(resume, job, profile);
        Assert.Equal(0, result.Score);
        Assert.Contains("blocked", result.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
```

**Step 2: Run to verify fail**

```bash
dotnet test tests/lucidRESUME.Matching.Tests
```

**Step 3: Implement SkillMatchingService**

```csharp
// src/lucidRESUME.Matching/SkillMatchingService.cs
namespace lucidRESUME.Matching;

public sealed class SkillMatchingService : IMatchingService
{
    public Task<MatchResult> MatchAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        // Check blocked companies first
        if (job.Company != null && profile.BlockedCompanies
            .Any(b => job.Company.Contains(b, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(new MatchResult(0, [], [], $"Company '{job.Company}' is on your blocklist."));
        }

        // Check blocked industries (via job title/description keywords)
        var resumeSkillNames = resume.Skills.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var avoidSkillNames = profile.SkillsToAvoid.Select(s => s.SkillName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var required = job.RequiredSkills;
        var matched = required.Where(s => resumeSkillNames.Contains(s)).ToList();
        var missing = required.Where(s => !resumeSkillNames.Contains(s)).ToList();
        var avoidHits = required.Where(s => avoidSkillNames.Contains(s)).ToList();

        // Score: matched/required ratio, penalise for avoid-skill hits
        double score = required.Count == 0 ? 0.5 : (double)matched.Count / required.Count;
        score -= avoidHits.Count * 0.1;
        score = Math.Clamp(score, 0, 1);

        var summary = $"{matched.Count}/{required.Count} required skills matched. Score: {score:P0}.";
        if (avoidHits.Count > 0)
            summary += $" Note: {avoidHits.Count} skill(s) you prefer to avoid are required.";

        return Task.FromResult(new MatchResult(score, matched, missing, summary));
    }
}
```

**Step 4: Run tests**

```bash
dotnet test tests/lucidRESUME.Matching.Tests
```
Expected: PASS

**Step 5: Commit**

```bash
git add src/lucidRESUME.Matching tests/lucidRESUME.Matching.Tests
git commit -m "feat: add skill matching service with blocklist and avoid-skill awareness"
```

---

## Task 12: Export Pipeline

**Files:**
- Create: `src/lucidRESUME.Export/JsonResumeExporter.cs`
- Create: `src/lucidRESUME.Export/MarkdownExporter.cs`
- Create: `src/lucidRESUME.Export/DocxExporter.cs`
- Create: `src/lucidRESUME.Export/PdfExporter.cs`
- Create: `src/lucidRESUME.Export/JsonResume/JsonResumeSchema.cs`
- Create: `src/lucidRESUME.Export/ServiceCollectionExtensions.cs`

**Step 1: Add NuGet packages**

```bash
dotnet add src/lucidRESUME.Export package DocumentFormat.OpenXml
dotnet add src/lucidRESUME.Export package PDFsharp
dotnet add src/lucidRESUME.Export package Markdig
```

**Step 2: Implement JSON Resume schema mapping**

JSON Resume spec: https://jsonresume.org/schema/

```csharp
// src/lucidRESUME.Export/JsonResume/JsonResumeSchema.cs
namespace lucidRESUME.Export.JsonResume;

// Follows https://jsonresume.org/schema/
public sealed class JsonResumeRoot
{
    [JsonPropertyName("basics")] public JsonResumeBasics? Basics { get; set; }
    [JsonPropertyName("work")] public List<JsonResumeWork> Work { get; set; } = [];
    [JsonPropertyName("education")] public List<JsonResumeEducation> Education { get; set; } = [];
    [JsonPropertyName("skills")] public List<JsonResumeSkill> Skills { get; set; } = [];
    [JsonPropertyName("certificates")] public List<JsonResumeCertificate> Certificates { get; set; } = [];
    [JsonPropertyName("projects")] public List<JsonResumeProject> Projects { get; set; } = [];
}

public sealed class JsonResumeBasics
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("location")] public JsonResumeLocation? Location { get; set; }
    [JsonPropertyName("profiles")] public List<JsonResumeProfile> Profiles { get; set; } = [];
}

public sealed record JsonResumeLocation(string? City, string? CountryCode);
public sealed record JsonResumeProfile(string Network, string Url, string? Username);
public sealed record JsonResumeWork(string? Name, string? Position, string? StartDate, string? EndDate, string? Summary, List<string> Highlights);
public sealed record JsonResumeEducation(string? Institution, string? Area, string? StudyType, string? StartDate, string? EndDate, string? Score);
public sealed record JsonResumeSkill(string? Name, string? Level, List<string> Keywords);
public sealed record JsonResumeCertificate(string? Name, string? Date, string? Issuer, string? Url);
public sealed record JsonResumeProject(string? Name, string? Description, List<string> Keywords, string? Url);
```

**Step 3: Implement JsonResumeExporter**

```csharp
// src/lucidRESUME.Export/JsonResumeExporter.cs
namespace lucidRESUME.Export;

public sealed class JsonResumeExporter : IResumeExporter
{
    public ExportFormat Format => ExportFormat.JsonResume;

    public Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var root = MapToJsonResume(resume);
        var json = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult(json);
    }

    private static JsonResumeRoot MapToJsonResume(ResumeDocument r) => new()
    {
        Basics = new JsonResumeBasics
        {
            Name = r.Personal.FullName,
            Email = r.Personal.Email,
            Phone = r.Personal.Phone,
            Summary = r.Personal.Summary,
            Url = r.Personal.WebsiteUrl,
            Location = r.Personal.Location != null ? new JsonResumeLocation(r.Personal.Location, null) : null,
            Profiles = BuildProfiles(r.Personal)
        },
        Work = r.Experience.Select(e => new JsonResumeWork(
            e.Company, e.Title,
            e.StartDate?.ToString("yyyy-MM-dd"), e.EndDate?.ToString("yyyy-MM-dd"),
            null, e.Achievements)).ToList(),
        Education = r.Education.Select(e => new JsonResumeEducation(
            e.Institution, e.FieldOfStudy, e.Degree,
            e.StartDate?.ToString("yyyy-MM-dd"), e.EndDate?.ToString("yyyy-MM-dd"),
            e.Gpa?.ToString())).ToList(),
        Skills = r.Skills.GroupBy(s => s.Category ?? "General")
            .Select(g => new JsonResumeSkill(g.Key, null, g.Select(s => s.Name).ToList())).ToList(),
        Certificates = r.Certifications.Select(c => new JsonResumeCertificate(
            c.Name, c.IssuedDate?.ToString("yyyy-MM-dd"), c.Issuer, c.CredentialUrl)).ToList(),
        Projects = r.Projects.Select(p => new JsonResumeProject(
            p.Name, p.Description, p.Technologies, p.Url)).ToList()
    };

    private static List<JsonResumeProfile> BuildProfiles(PersonalInfo p)
    {
        var profiles = new List<JsonResumeProfile>();
        if (p.LinkedInUrl != null) profiles.Add(new JsonResumeProfile("LinkedIn", p.LinkedInUrl, null));
        if (p.GitHubUrl != null) profiles.Add(new JsonResumeProfile("GitHub", p.GitHubUrl, null));
        return profiles;
    }
}
```

**Step 4: Implement MarkdownExporter**

```csharp
// src/lucidRESUME.Export/MarkdownExporter.cs
namespace lucidRESUME.Export;

public sealed class MarkdownExporter : IResumeExporter
{
    public ExportFormat Format => ExportFormat.Markdown;

    public Task<byte[]> ExportAsync(ResumeDocument resume, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var p = resume.Personal;

        sb.AppendLine($"# {p.FullName ?? "Resume"}");
        if (p.Email != null) sb.AppendLine($"**Email:** {p.Email}");
        if (p.Phone != null) sb.AppendLine($"**Phone:** {p.Phone}");
        if (p.Location != null) sb.AppendLine($"**Location:** {p.Location}");
        if (p.LinkedInUrl != null) sb.AppendLine($"**LinkedIn:** {p.LinkedInUrl}");
        if (p.GitHubUrl != null) sb.AppendLine($"**GitHub:** {p.GitHubUrl}");
        if (p.Summary != null) { sb.AppendLine(); sb.AppendLine($"## Summary"); sb.AppendLine(p.Summary); }

        if (resume.Experience.Count > 0)
        {
            sb.AppendLine("\n## Experience");
            foreach (var e in resume.Experience)
            {
                sb.AppendLine($"\n### {e.Title} - {e.Company}");
                var dates = $"{e.StartDate?.ToString("MMM yyyy")} – {(e.IsCurrent ? "Present" : e.EndDate?.ToString("MMM yyyy"))}";
                sb.AppendLine($"*{dates}*");
                foreach (var a in e.Achievements) sb.AppendLine($"- {a}");
            }
        }

        if (resume.Education.Count > 0)
        {
            sb.AppendLine("\n## Education");
            foreach (var e in resume.Education)
                sb.AppendLine($"\n**{e.Degree} in {e.FieldOfStudy}** - {e.Institution} ({e.EndDate?.Year})");
        }

        if (resume.Skills.Count > 0)
        {
            sb.AppendLine("\n## Skills");
            var grouped = resume.Skills.GroupBy(s => s.Category ?? "General");
            foreach (var g in grouped)
                sb.AppendLine($"**{g.Key}:** {string.Join(", ", g.Select(s => s.Name))}");
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
```

**Step 5: Build to verify**

```bash
dotnet build src/lucidRESUME.Export/lucidRESUME.Export.csproj
```
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/lucidRESUME.Export
git commit -m "feat: add JSON Resume and Markdown exporters"
```

---

## Task 13: AI Tailoring Service (Ollama Stub + Implementation)

**Files:**
- Create: `src/lucidRESUME.AI/OllamaTailoringService.cs`
- Create: `src/lucidRESUME.AI/TailoringPromptBuilder.cs`
- Create: `src/lucidRESUME.AI/OllamaOptions.cs`
- Create: `src/lucidRESUME.AI/ServiceCollectionExtensions.cs`

**Step 1: Add NuGet packages**

```bash
dotnet add src/lucidRESUME.AI package LLamaSharp
dotnet add src/lucidRESUME.AI package Microsoft.Extensions.Options
dotnet add src/lucidRESUME.AI package Microsoft.Extensions.Http.Resilience
```

**Step 2: Implement prompt builder**

```csharp
// src/lucidRESUME.AI/TailoringPromptBuilder.cs
namespace lucidRESUME.AI;

public static class TailoringPromptBuilder
{
    public static string Build(ResumeDocument resume, JobDescription job, UserProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a professional CV editor. Your task is to tailor the candidate's resume for a specific job.");
        sb.AppendLine("CRITICAL RULES:");
        sb.AppendLine("- NEVER invent, fabricate, or exaggerate any facts, skills, or experiences.");
        sb.AppendLine("- Only reorder, rephrase, or emphasise information that already exists in the resume.");
        sb.AppendLine("- Do not add skills the candidate does not have.");
        sb.AppendLine();
        sb.AppendLine($"## Target Role: {job.Title} at {job.Company}");
        sb.AppendLine($"## Required Skills: {string.Join(", ", job.RequiredSkills)}");
        sb.AppendLine($"## Preferred Skills: {string.Join(", ", job.PreferredSkills)}");
        sb.AppendLine();

        if (profile.SkillsToEmphasise.Count > 0)
            sb.AppendLine($"## Candidate wants to emphasise: {string.Join(", ", profile.SkillsToEmphasise.Select(s => s.SkillName))}");
        if (profile.SkillsToAvoid.Count > 0)
            sb.AppendLine($"## Candidate prefers NOT to emphasise: {string.Join(", ", profile.SkillsToAvoid.Select(s => s.SkillName))}");
        if (profile.CareerGoals != null)
            sb.AppendLine($"## Candidate career goals: {profile.CareerGoals}");

        sb.AppendLine();
        sb.AppendLine("## Candidate's Current Resume (Markdown):");
        sb.AppendLine(resume.RawMarkdown ?? "No markdown available.");
        sb.AppendLine();
        sb.AppendLine("Output the tailored resume as clean Markdown only. No explanations or preamble.");

        return sb.ToString();
    }
}
```

```csharp
// src/lucidRESUME.AI/OllamaOptions.cs
namespace lucidRESUME.AI;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";
    public int TimeoutSeconds { get; set; } = 120;
}
```

```csharp
// src/lucidRESUME.AI/OllamaTailoringService.cs
namespace lucidRESUME.AI;

public sealed class OllamaTailoringService : IAiTailoringService
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaTailoringService> _logger;

    public bool IsAvailable { get; private set; }

    public OllamaTailoringService(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaTailoringService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResumeDocument> TailorAsync(ResumeDocument resume, JobDescription job,
        UserProfile profile, CancellationToken ct = default)
    {
        var prompt = TailoringPromptBuilder.Build(resume, job, profile);

        var request = new { model = _options.Model, prompt, stream = false };
        var response = await _http.PostAsJsonAsync($"{_options.BaseUrl}/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);
        var tailoredMarkdown = result?.Response ?? resume.RawMarkdown ?? "";

        // Create a new ResumeDocument stamped as tailored - original is preserved
        var tailored = ResumeDocument.Create(resume.FileName, resume.ContentType, resume.FileSizeBytes);
        tailored.SetDoclingOutput(tailoredMarkdown, null, null);
        tailored.MarkTailoredFor(job.JobId);

        // Copy structured data - the LLM output is used for the markdown/export only
        // Structured fields stay from the original extraction
        foreach (var entity in resume.Entities)
            tailored.AddEntity(entity);

        return tailored;
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_options.BaseUrl}/api/tags", ct);
            IsAvailable = response.IsSuccessStatusCode;
            return IsAvailable;
        }
        catch
        {
            IsAvailable = false;
            return false;
        }
    }

    private record OllamaGenerateResponse(string Response);
}
```

**Step 3: Build to verify**

```bash
dotnet build src/lucidRESUME.AI/lucidRESUME.AI.csproj
```

**Step 4: Commit**

```bash
git add src/lucidRESUME.AI
git commit -m "feat: add Ollama AI tailoring service with honest-only prompt constraints"
```

---

## Task 14: Avalonia UI - Shell & Navigation

**Files:**
- Modify: `src/lucidRESUME/App.axaml`
- Modify: `src/lucidRESUME/App.axaml.cs`
- Create: `src/lucidRESUME/Views/MainWindow.axaml`
- Create: `src/lucidRESUME/Views/MainWindow.axaml.cs`
- Create: `src/lucidRESUME/ViewModels/MainWindowViewModel.cs`
- Create: `src/lucidRESUME/ViewModels/ViewModelBase.cs`
- Create: `src/lucidRESUME/Views/Pages/ResumePage.axaml`
- Create: `src/lucidRESUME/Views/Pages/JobsPage.axaml`
- Create: `src/lucidRESUME/Views/Pages/SearchPage.axaml`
- Create: `src/lucidRESUME/Views/Pages/ApplyPage.axaml`
- Create: `src/lucidRESUME/Views/Pages/ProfilePage.axaml`

**Step 1: Add Avalonia NuGet packages to app project**

```bash
dotnet add src/lucidRESUME package Avalonia
dotnet add src/lucidRESUME package Avalonia.Desktop
dotnet add src/lucidRESUME package Avalonia.Themes.Fluent
dotnet add src/lucidRESUME package CommunityToolkit.Mvvm
dotnet add src/lucidRESUME package Microsoft.Extensions.DependencyInjection
```

**Step 2: Implement ViewModelBase with CommunityToolkit.Mvvm**

```csharp
// src/lucidRESUME/ViewModels/ViewModelBase.cs
namespace lucidRESUME.ViewModels;

public abstract class ViewModelBase : ObservableObject { }
```

**Step 3: Implement MainWindowViewModel with navigation**

```csharp
// src/lucidRESUME/ViewModels/MainWindowViewModel.cs
namespace lucidRESUME.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string _selectedNav = "Resume";

    public MainWindowViewModel(
        ResumePageViewModel resumePage,
        JobsPageViewModel jobsPage,
        SearchPageViewModel searchPage,
        ApplyPageViewModel applyPage,
        ProfilePageViewModel profilePage)
    {
        _pages = new Dictionary<string, ViewModelBase>
        {
            ["Resume"] = resumePage,
            ["Jobs"] = jobsPage,
            ["Search"] = searchPage,
            ["Apply"] = applyPage,
            ["Profile"] = profilePage
        };
        _currentPage = resumePage;
    }

    private readonly Dictionary<string, ViewModelBase> _pages;

    [RelayCommand]
    private void Navigate(string page)
    {
        SelectedNav = page;
        CurrentPage = _pages[page];
    }
}
```

**Step 4: Create MainWindow AXAML with sidebar layout**

```xml
<!-- src/lucidRESUME/Views/MainWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:lucidRESUME.ViewModels"
        x:DataType="vm:MainWindowViewModel"
        Title="lucidRESUME" Width="1200" Height="800"
        MinWidth="900" MinHeight="600">

  <Grid ColumnDefinitions="200,*">

    <!-- Sidebar -->
    <Border Grid.Column="0" Background="#1E1E2E" Padding="8">
      <StackPanel Spacing="4">
        <TextBlock Text="lucidRESUME" FontSize="18" FontWeight="Bold"
                   Foreground="#CDD6F4" Margin="8,16,8,24"/>
        <Button Content="My CV" Command="{Binding NavigateCommand}" CommandParameter="Resume"
                HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"/>
        <Button Content="Jobs" Command="{Binding NavigateCommand}" CommandParameter="Jobs"
                HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"/>
        <Button Content="Search" Command="{Binding NavigateCommand}" CommandParameter="Search"
                HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"/>
        <Button Content="Apply" Command="{Binding NavigateCommand}" CommandParameter="Apply"
                HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"/>
        <Button Content="Profile" Command="{Binding NavigateCommand}" CommandParameter="Profile"
                HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"/>
      </StackPanel>
    </Border>

    <!-- Main content area - swaps based on CurrentPage -->
    <ContentControl Grid.Column="1" Content="{Binding CurrentPage}"/>

  </Grid>
</Window>
```

**Step 5: Create stub page ViewModels (one per nav item)**

```csharp
// src/lucidRESUME/ViewModels/Pages/ResumePageViewModel.cs
public sealed partial class ResumePageViewModel : ViewModelBase
{
    private readonly IResumeParser _parser;
    private readonly IAppStore _store;

    [ObservableProperty] private ResumeDocument? _resume;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;

    public ResumePageViewModel(IResumeParser parser, IAppStore store)
    {
        _parser = parser;
        _store = store;
    }

    [RelayCommand]
    private async Task ImportResumeAsync()
    {
        // File picker → parse → save
        IsLoading = true;
        StatusMessage = "Parsing resume...";
        try
        {
            // File dialog handled in code-behind, path passed here
            // See MainWindow.axaml.cs for file picker wiring
        }
        finally { IsLoading = false; }
    }
}
```

Create similar stubs for: `JobsPageViewModel`, `SearchPageViewModel`, `ApplyPageViewModel`, `ProfilePageViewModel`

**Step 6: Wire DI in App.axaml.cs**

```csharp
// src/lucidRESUME/App.axaml.cs
public override void OnFrameworkInitializationCompleted()
{
    var services = new ServiceCollection();
    ConfigureServices(services);
    var provider = services.BuildServiceProvider();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = provider.GetRequiredService<MainWindow>();

    base.OnFrameworkInitializationCompleted();
}

private static void ConfigureServices(IServiceCollection services)
{
    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .Build();

    services.AddIngestion(config);
    services.AddExtraction(config);
    services.AddJobSpec(config);
    services.AddJobSearch(config);
    services.AddMatching();
    services.AddAiTailoring(config);
    services.AddExport();

    var appDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "lucidRESUME", "data.json");
    services.AddSingleton<IAppStore>(_ => new JsonAppStore(appDataPath));

    // ViewModels
    services.AddTransient<MainWindowViewModel>();
    services.AddTransient<ResumePageViewModel>();
    services.AddTransient<JobsPageViewModel>();
    services.AddTransient<SearchPageViewModel>();
    services.AddTransient<ApplyPageViewModel>();
    services.AddTransient<ProfilePageViewModel>();

    // Window
    services.AddTransient<MainWindow>();
}
```

**Step 7: Build and run**

```bash
dotnet build src/lucidRESUME/lucidRESUME.csproj
dotnet run --project src/lucidRESUME/lucidRESUME.csproj
```
Expected: App opens with sidebar navigation

**Step 8: Commit**

```bash
git add src/lucidRESUME
git commit -m "feat: add Avalonia shell with sidebar navigation and page ViewModels"
```

---

## Task 15: Full Integration - Resume Import Flow (End to End)

**Files:**
- Modify: `src/lucidRESUME/Views/Pages/ResumePage.axaml`
- Modify: `src/lucidRESUME/ViewModels/Pages/ResumePageViewModel.cs`

**Step 1: Complete ResumePage AXAML**

```xml
<!-- src/lucidRESUME/Views/Pages/ResumePage.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:lucidRESUME.ViewModels"
             x:DataType="vm:ResumePageViewModel">
  <Grid RowDefinitions="Auto,*,Auto">
    <!-- Toolbar -->
    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8" Margin="16,8">
      <Button Content="Import Resume..." Command="{Binding ImportResumeCommand}"/>
      <Button Content="Export..." Command="{Binding ExportCommand}" IsEnabled="{Binding Resume, Converter={x:Static ObjectConverters.IsNotNull}}"/>
      <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center" Foreground="#A6ADC8"/>
    </StackPanel>

    <!-- Resume display: personal info + sections -->
    <ScrollViewer Grid.Row="1" IsVisible="{Binding Resume, Converter={x:Static ObjectConverters.IsNotNull}}">
      <StackPanel Margin="16" Spacing="16">
        <!-- Personal Info card -->
        <Border Background="#313244" CornerRadius="8" Padding="16">
          <StackPanel>
            <TextBlock Text="{Binding Resume.Personal.FullName}" FontSize="22" FontWeight="Bold"/>
            <TextBlock Text="{Binding Resume.Personal.Email}"/>
            <TextBlock Text="{Binding Resume.Personal.Phone}"/>
            <TextBlock Text="{Binding Resume.Personal.Location}"/>
          </StackPanel>
        </Border>
        <!-- Experience list -->
        <ItemsControl ItemsSource="{Binding Resume.Experience}">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Border Background="#313244" CornerRadius="8" Padding="16" Margin="0,4">
                <StackPanel>
                  <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
                  <TextBlock Text="{Binding Company}" Foreground="#A6ADC8"/>
                </StackPanel>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </StackPanel>
    </ScrollViewer>

    <!-- Empty state -->
    <StackPanel Grid.Row="1" IsVisible="{Binding Resume, Converter={x:Static ObjectConverters.IsNull}}"
                HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
      <TextBlock Text="No resume imported yet." FontSize="16" HorizontalAlignment="Center"/>
      <Button Content="Import Resume..." Command="{Binding ImportResumeCommand}" HorizontalAlignment="Center"/>
    </StackPanel>

    <!-- Loading overlay -->
    <Border Grid.Row="1" IsVisible="{Binding IsLoading}" Background="#80000000">
      <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
        <ProgressBar IsIndeterminate="True" Width="200"/>
        <TextBlock Text="Parsing resume..." HorizontalAlignment="Center" Foreground="White" Margin="0,8"/>
      </StackPanel>
    </Border>
  </Grid>
</UserControl>
```

**Step 2: Complete ResumePageViewModel with file picker**

Wire file picker using Avalonia's `StorageProvider` API (no WinForms dependency):

```csharp
[RelayCommand]
private async Task ImportResumeAsync()
{
    var topLevel = TopLevel.GetTopLevel(/* passed via constructor or service */);
    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
        Title = "Select your resume",
        AllowMultiple = false,
        FileTypeFilter = [
            new FilePickerFileType("Resume files") { Patterns = ["*.pdf", "*.docx"] },
            new FilePickerFileType("All files") { Patterns = ["*.*"] }
        ]
    });

    if (files.Count == 0) return;

    IsLoading = true;
    StatusMessage = "Parsing resume via Docling...";
    try
    {
        var path = files[0].Path.LocalPath;
        Resume = await _parser.ParseAsync(path);

        var state = await _store.LoadAsync();
        state.Resume = Resume;
        await _store.SaveAsync(state);

        StatusMessage = $"Imported successfully - {Resume.Entities.Count} entities extracted.";
    }
    catch (Exception ex)
    {
        StatusMessage = $"Error: {ex.Message}";
    }
    finally { IsLoading = false; }
}
```

**Step 3: Build and manually test with a sample PDF**

```bash
dotnet run --project src/lucidRESUME/lucidRESUME.csproj
```

Ensure Docling is running locally at `http://localhost:5001` before testing.

**Step 4: Commit**

```bash
git add src/lucidRESUME
git commit -m "feat: complete resume import flow with file picker and Docling parsing"
```

---

## Future Phases (Not In This Plan)

These are tracked here for awareness but are out of scope for the initial implementation:

1. **Phase 2 - Job Description UI**: Full CRUD for job descriptions, paste/URL/search ingestion
2. **Phase 3 - Match Dashboard**: Visual match scores, skill gap analysis per job
3. **Phase 4 - AI Tailoring UI**: Generate tailored resume per job with diff view (original vs tailored)
4. **Phase 5 - LinkedIn Import**: Parse LinkedIn JSON export as an alternative to PDF resume
5. **Phase 6 - Profile Editor UI**: Full UserProfile editing (preferences, blocklists, career goals)
6. **Phase 7 - DOCX/PDF Export**: Complete DocxExporter and PdfExporter implementations
7. **Phase 8 - Reed/Indeed adapters**: Additional job search API adapters

---

## Running the Full Stack Locally

### Prerequisites
- .NET 10 SDK
- Docling running: `docker run -p 5001:5000 ds4sd/docling-serve`
- Ollama running (for AI tailoring): `ollama serve` with `ollama pull llama3.2`

### Configuration (`src/lucidRESUME/appsettings.json`)

```json
{
  "Docling": {
    "BaseUrl": "http://localhost:5001",
    "TimeoutSeconds": 120
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2"
  },
  "Adzuna": {
    "AppId": "",
    "AppKey": "",
    "Country": "gb"
  }
}
```