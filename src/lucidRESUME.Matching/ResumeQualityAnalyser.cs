using System.Text.RegularExpressions;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Quality;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Matching;

public sealed partial class ResumeQualityAnalyser : IResumeQualityAnalyser
{
    private readonly IEmbeddingService? _embedder;

    public ResumeQualityAnalyser(IEmbeddingService? embeddingService = null)
    {
        _embedder = embeddingService;
    }

    // ── Scoring weights (must sum to 100) ─────────────────────────────────
    private const int WeightBulletQuality  = 30;
    private const int WeightCompleteness   = 22;
    private const int WeightAlignment      = 18;
    private const int WeightSpelling       = 12;
    private const int WeightFormat         = 10;
    private const int WeightPresentation   = 8;

    // ── Word lists loaded from Resources/*.txt ────────────────────────────
    private static readonly string[] StrongVerbFallback =
    [
        "accelerated","achieved","administered","advanced","architected","automated",
        "built","championed","coached","collaborated","conceived","configured",
        "consolidated","containerised","containerized","created","cut","decreased",
        "defined","delivered","deployed","designed","developed","devised","directed",
        "doubled","drove","eliminated","enabled","engineered","established","executed",
        "expanded","facilitated","generated","grew","guided","halved","implemented",
        "improved","increased","initiated","integrated","introduced","launched","led",
        "mentored","migrated","modernised","modernized","monitored","negotiated",
        "optimised","optimized","orchestrated","overhauled","owned","partnered",
        "pioneered","planned","produced","proposed","published","rebuilt","redesigned",
        "reduced","refactored","released","replaced","resolved","scaled","secured",
        "shipped","simplified","spearheaded","standardised","standardized","streamlined",
        "trained","transformed","tripled","unified","upgraded","wrote"
    ];

    private static readonly string[] WeakVerbFallback =
    [
        "helped","assisted","worked","was","handled","did","made","got","used",
        "supported","involved","participated","contributed","responsible","tasked",
        "tried","attempted","managed"
    ];

    private static readonly string[] BuzzwordFallback =
    [
        "synergy","synergies","dynamic","results-driven","results-oriented",
        "go-getter","hardworking","hard-working","passionate","guru","ninja","rockstar",
        "wizard","thought leader","disruptive","innovative","proactive","self-starter",
        "team player","detail-oriented","fast-paced","leverage","leveraging"
    ];

    private static readonly string[] FillerFallback =
    [
        "just","very","really","quite","rather","somewhat","basically","literally"
    ];

    private static readonly Lazy<HashSet<string>> StrongVerbs = LoadWordList("strong-verbs.txt", StrongVerbFallback);
    private static readonly Lazy<HashSet<string>> WeakVerbs   = LoadWordList("weak-verbs.txt", WeakVerbFallback);
    private static readonly Lazy<HashSet<string>> Buzzwords   = LoadWordList("buzzwords.txt", BuzzwordFallback);
    private static readonly Lazy<HashSet<string>> Fillers     = LoadWordList("fillers.txt", FillerFallback);

    internal static Lazy<HashSet<string>> LoadWordList(string filename, IEnumerable<string> fallbackWords) => new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", filename);
        if (!File.Exists(path))
            path = Path.Combine(Path.GetDirectoryName(typeof(ResumeQualityAnalyser).Assembly.Location)!, "Resources", filename);
        if (File.Exists(path))
        {
            return new HashSet<string>(
                File.ReadAllLines(path)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                    .Select(l => l.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }
        return new HashSet<string>(fallbackWords, StringComparer.OrdinalIgnoreCase);
    });

    // ── Personal pronouns ─────────────────────────────────────────────────
    private static readonly Regex PronounRx = MyPronounRx();
    [GeneratedRegex(@"\b(I|we|our|my|me)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MyPronounRx();

    // ── Quantification ────────────────────────────────────────────────────
    private static readonly Regex QuantRx = MyQuantRx();
    [GeneratedRegex(
        @"(\$[\d,]+|\d+\s*%|\d+\s*x\b|\d+\s*(user|customer|engineer|system|service|request|" +
        @"transaction|hour|day|week|month|year|team|client|project|server|deploy|release|" +
        @"repo|repository|pipeline|endpoint|ticket|record|row|table|query|component)s?\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MyQuantRx();

    // ── Email / phone / LinkedIn ──────────────────────────────────────────
    private static readonly Regex EmailRx    = MyEmailRx();
    private static readonly Regex PhoneRx    = MyPhoneRx();
    private static readonly Regex LinkedInRx = MyLinkedInRx();
    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}")]
    private static partial Regex MyEmailRx();
    [GeneratedRegex(@"(\+?[\d\s\-().]{7,20})")]
    private static partial Regex MyPhoneRx();
    [GeneratedRegex(@"linkedin\.com/in/", RegexOptions.IgnoreCase)]
    private static partial Regex MyLinkedInRx();

    // ─────────────────────────────────────────────────────────────────────

    public QualityReport Analyse(ResumeDocument resume) =>
        BuildReport(resume, null);

    public QualityReport Analyse(ResumeDocument resume, JobDescription job) =>
        BuildReport(resume, job);

    // ── Main pipeline ─────────────────────────────────────────────────────

    private static QualityReport BuildReport(ResumeDocument resume, JobDescription? job)
    {
        var bulletFindings       = CheckBulletQuality(resume);
        var completenessFindings = CheckCompleteness(resume);
        var formatFindings       = CheckFormat(resume);
        var presentationFindings = CheckPresentation(resume);
        var spellingFindings     = SpellChecker.Check(resume);
        var alignmentFindings    = job is null ? (IReadOnlyList<QualityFinding>)[] : CheckAlignment(resume, job);

        int bulletScore       = ScoreFromFindings(bulletFindings, resume.Experience.Sum(e => Math.Max(e.Achievements.Count, 1)));
        int completenessScore = ScoreFromFindings(completenessFindings, 8);
        int formatScore       = ScoreFromFindings(formatFindings, 4);
        int presentationScore = ScoreFromFindings(presentationFindings, 5);
        int spellingScore     = ScoreFromFindings(spellingFindings, Math.Max(resume.Experience.Sum(e => e.Achievements.Count), 1));
        int alignmentScore    = job is null ? 100 : ScoreFromFindings(alignmentFindings, Math.Max(job.RequiredSkills.Count, 5));

        var categories = new List<QualityCategory>
        {
            new("Bullet Quality",   bulletScore,       WeightBulletQuality,  bulletFindings),
            new("Completeness",     completenessScore, WeightCompleteness,   completenessFindings),
            new("Spelling",         spellingScore,     WeightSpelling,       spellingFindings),
            new("Format",           formatScore,       WeightFormat,         formatFindings),
            new("Presentation",     presentationScore, WeightPresentation,   presentationFindings),
            new("JD Alignment",     alignmentScore,    job is null ? 0 : WeightAlignment, alignmentFindings),
        };

        int overall = WeightedScore(categories);

        return new QualityReport(overall, categories, DateTimeOffset.UtcNow);
    }

    private static int WeightedScore(IList<QualityCategory> cats)
    {
        int totalWeight = 0;
        int weightedSum = 0;
        for (int i = 0; i < cats.Count; i++)
        {
            var w = cats[i].Weight;
            if (w == 0) continue;
            totalWeight += w;
            weightedSum += cats[i].Score * w;
        }
        return totalWeight == 0 ? 0 : weightedSum / totalWeight;
    }

    // ── Bullet Quality ────────────────────────────────────────────────────

    private static IReadOnlyList<QualityFinding> CheckBulletQuality(ResumeDocument resume)
    {
        var findings = new List<QualityFinding>();

        for (int j = 0; j < resume.Experience.Count; j++)
        {
            var exp = resume.Experience[j];
            string jobLabel = $"Experience[{j}] ({exp.Company ?? "?"})";

            if (exp.Achievements.Count == 0)
            {
                findings.Add(new($"Experience[{j}]", FindingSeverity.Error,
                    "NO_BULLETS", $"{jobLabel}: no achievement bullets found"));
                continue;
            }

            if (exp.Achievements.Count < 3)
                findings.Add(new($"Experience[{j}]", FindingSeverity.Warning,
                    "FEW_BULLETS", $"{jobLabel}: only {exp.Achievements.Count} bullet(s) - aim for 3-6"));

            if (exp.Achievements.Count > 8)
                findings.Add(new($"Experience[{j}]", FindingSeverity.Info,
                    "MANY_BULLETS", $"{jobLabel}: {exp.Achievements.Count} bullets - consider trimming to 6"));

            for (int k = 0; k < exp.Achievements.Count; k++)
            {
                string bullet  = exp.Achievements[k].Trim();
                string section = $"Experience[{j}].Achievements[{k}]";

                // Verb check
                string firstWord = bullet.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                firstWord = firstWord.TrimEnd('.', ',', ';', ':');

                if (WeakVerbs.Value.Contains(firstWord))
                    findings.Add(new(section, FindingSeverity.Warning,
                        "WEAK_VERB", $"Starts with weak verb \"{firstWord}\" - try a stronger action verb"));
                else if (!StrongVerbs.Value.Contains(firstWord))
                    findings.Add(new(section, FindingSeverity.Info,
                        "UNRECOGNISED_VERB", $"\"{firstWord}\" not in strong-verb list - verify it's an active verb"));

                // Quantification
                if (!QuantRx.IsMatch(bullet))
                    findings.Add(new(section, FindingSeverity.Warning,
                        "MISSING_QUANTITY", "No measurable result or number detected - add a metric if possible"));

                // Pronouns
                if (PronounRx.IsMatch(bullet))
                    findings.Add(new(section, FindingSeverity.Error,
                        "PRONOUN", "Contains personal pronoun - remove \"I\", \"we\", \"my\" etc."));

                // Buzzwords
                foreach (var bw in Buzzwords.Value)
                    if (bullet.Contains(bw, StringComparison.OrdinalIgnoreCase))
                        findings.Add(new(section, FindingSeverity.Warning,
                            "BUZZWORD", $"Buzzword detected: \"{bw}\""));

                // Filler words
                foreach (var f in Fillers.Value)
                    if (Regex.IsMatch(bullet, $@"\b{Regex.Escape(f)}\b", RegexOptions.IgnoreCase))
                        findings.Add(new(section, FindingSeverity.Info,
                            "FILLER_WORD", $"Filler word \"{f}\" weakens the bullet"));

                // Length
                int wordCount = bullet.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount < 8)
                    findings.Add(new(section, FindingSeverity.Warning,
                        "BULLET_TOO_SHORT", $"Only {wordCount} words - too vague"));
                else if (wordCount > 35)
                    findings.Add(new(section, FindingSeverity.Info,
                        "BULLET_TOO_LONG", $"{wordCount} words - try splitting or trimming"));
            }
        }

        return findings;
    }

    // ── Completeness ──────────────────────────────────────────────────────

    private static IReadOnlyList<QualityFinding> CheckCompleteness(ResumeDocument resume)
    {
        var findings = new List<QualityFinding>();
        var p = resume.Personal;

        if (string.IsNullOrWhiteSpace(p.FullName))
            findings.Add(new("Personal", FindingSeverity.Error, "NO_NAME", "Full name not found"));

        if (string.IsNullOrWhiteSpace(p.Email))
            findings.Add(new("Personal", FindingSeverity.Error, "NO_EMAIL", "Email address not found"));

        if (string.IsNullOrWhiteSpace(p.Phone))
            findings.Add(new("Personal", FindingSeverity.Warning, "NO_PHONE", "Phone number not found"));

        if (string.IsNullOrWhiteSpace(p.Summary))
            findings.Add(new("Personal", FindingSeverity.Warning, "NO_SUMMARY",
                "No summary or objective section - add 2-3 sentences at the top"));

        if (resume.Experience.Count == 0)
            findings.Add(new("Experience", FindingSeverity.Error, "NO_EXPERIENCE",
                "No work experience section found"));

        if (resume.Education.Count == 0)
            findings.Add(new("Education", FindingSeverity.Warning, "NO_EDUCATION",
                "No education section found"));

        if (resume.Skills.Count == 0)
            findings.Add(new("Skills", FindingSeverity.Warning, "NO_SKILLS",
                "No skills section found - this hurts ATS keyword matching"));

        // Check each work entry has dates
        for (int i = 0; i < resume.Experience.Count; i++)
        {
            var exp = resume.Experience[i];
            if (exp.StartDate is null)
                findings.Add(new($"Experience[{i}]", FindingSeverity.Warning, "MISSING_START_DATE",
                    $"{exp.Company ?? "?"}: start date not found"));
        }

        return findings;
    }

    // ── Format checks ─────────────────────────────────────────────────────

    private static IReadOnlyList<QualityFinding> CheckFormat(ResumeDocument resume)
    {
        var findings = new List<QualityFinding>();

        // File type check
        if (!string.IsNullOrEmpty(resume.ContentType) &&
            !resume.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) &&
            !resume.ContentType.Contains("docx", StringComparison.OrdinalIgnoreCase) &&
            !resume.ContentType.Contains("openxml", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new("File", FindingSeverity.Warning, "NON_STANDARD_FORMAT",
                "Use PDF or DOCX for best ATS compatibility"));
        }

        // Word count check (from plain text if available)
        if (!string.IsNullOrWhiteSpace(resume.PlainText))
        {
            int wordCount = resume.PlainText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            int yearsExp  = EstimateYearsExperience(resume);

            if (yearsExp < 10 && wordCount > 900)
                findings.Add(new("Length", FindingSeverity.Warning, "TOO_LONG",
                    $"~{wordCount} words for <10 years experience - aim for 1 page (~600 words)"));
            else if (wordCount < 300)
                findings.Add(new("Length", FindingSeverity.Warning, "TOO_SHORT",
                    $"Only ~{wordCount} words - the resume may be too sparse"));
        }

        // Page count check
        if (resume.PageCount > 3)
            findings.Add(new("Length", FindingSeverity.Warning, "TOO_MANY_PAGES",
                $"{resume.PageCount} pages - keep to 1-2 pages for most roles"));

        return findings;
    }

    // ── Presentation ──────────────────────────────────────────────────────

    private static IReadOnlyList<QualityFinding> CheckPresentation(ResumeDocument resume)
    {
        var findings = new List<QualityFinding>();

        // Reverse-chronological order check
        var dated = resume.Experience
            .Where(e => e.StartDate.HasValue)
            .ToList();

        for (int i = 1; i < dated.Count; i++)
        {
            if (dated[i].StartDate > dated[i - 1].StartDate)
            {
                findings.Add(new("Experience", FindingSeverity.Warning, "NOT_REVERSE_CHRON",
                    "Experience entries may not be in reverse-chronological order"));
                break;
            }
        }

        // Missing location on jobs
        int missingLocation = resume.Experience.Count(e => string.IsNullOrWhiteSpace(e.Location));
        if (missingLocation > 0 && resume.Experience.Count > 0)
            findings.Add(new("Experience", FindingSeverity.Info, "MISSING_LOCATIONS",
                $"{missingLocation} experience entry/entries missing location"));

        // Education: missing graduation year
        for (int i = 0; i < resume.Education.Count; i++)
        {
            var ed = resume.Education[i];
            if (ed.GraduationYear is null)
                findings.Add(new($"Education[{i}]", FindingSeverity.Info, "MISSING_GRAD_DATE",
                    $"{ed.Institution ?? "?"}: graduation year not found"));
        }

        return findings;
    }

    // ── JD Alignment (keyword overlap) ────────────────────────────────────
    // NOTE: This is Phase 1 - keyword overlap only.
    // Phase 2 replaces with embedding-based semantic similarity via IEmbeddingService.

    private static IReadOnlyList<QualityFinding> CheckAlignment(ResumeDocument resume, JobDescription job)
    {
        var findings = new List<QualityFinding>();

        var resumeSkillNames = resume.Skills
            .Select(s => s.Name.ToLowerInvariant())
            .ToHashSet();

        // Also extract from plain text for better coverage
        string resumeText = (resume.PlainText ?? resume.RawMarkdown ?? "").ToLowerInvariant();

        var missing = job.RequiredSkills
            .Where(req =>
            {
                string r = req.ToLowerInvariant();
                return !resumeSkillNames.Contains(r) && !resumeText.Contains(r);
            })
            .ToList();

        foreach (var skill in missing)
            findings.Add(new("Alignment", FindingSeverity.Warning,
                "MISSING_REQUIRED_SKILL", $"Required skill not found in resume: \"{skill}\""));

        var preferredMissing = job.PreferredSkills
            .Where(pref =>
            {
                string p = pref.ToLowerInvariant();
                return !resumeSkillNames.Contains(p) && !resumeText.Contains(p);
            })
            .Take(5)
            .ToList();

        foreach (var skill in preferredMissing)
            findings.Add(new("Alignment", FindingSeverity.Info,
                "MISSING_PREFERRED_SKILL", $"Preferred skill not found in resume: \"{skill}\""));

        // Title alignment
        if (!string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(resume.Personal.Summary))
        {
            string summaryLower = resume.Personal.Summary!.ToLowerInvariant();
            string titleLower   = job.Title.ToLowerInvariant();
            // Check if any word from the job title appears in summary
            bool titleMentioned = titleLower
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(w => w.Length > 3 && summaryLower.Contains(w));

            if (!titleMentioned)
                findings.Add(new("Alignment", FindingSeverity.Info,
                    "TITLE_NOT_IN_SUMMARY",
                    $"Job title \"{job.Title}\" not reflected in resume summary"));
        }

        return findings;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int EstimateYearsExperience(ResumeDocument resume)
    {
        var earliest = resume.Experience
            .Where(e => e.StartDate.HasValue)
            .Select(e => e.StartDate!.Value)
            .OrderBy(d => d)
            .FirstOrDefault();

        if (earliest == default) return 5; // assume mid-level if unknown
        return (int)((DateOnly.FromDateTime(DateTime.Today).DayNumber - earliest.DayNumber) / 365.25);
    }

    /// <summary>
    /// Scores 0-100 based on ratio of error+warning findings to total opportunities.
    /// </summary>
    private static int ScoreFromFindings(IReadOnlyList<QualityFinding> findings, int opportunities)
    {
        if (opportunities <= 0) return 100;
        int errorCount   = findings.Count(f => f.Severity == FindingSeverity.Error);
        int warningCount = findings.Count(f => f.Severity == FindingSeverity.Warning);
        // Errors cost 2x; infos don't affect score
        double penalty = (errorCount * 2.0 + warningCount) / (opportunities * 2.0);
        return Math.Clamp((int)((1.0 - penalty) * 100), 0, 100);
    }

    // ── Async interface methods ───────────────────────────────────────────

    public Task<QualityReport> AnalyseAsync(ResumeDocument resume, CancellationToken ct = default) =>
        Task.FromResult(Analyse(resume));

    public async Task<QualityReport> AnalyseAsync(ResumeDocument resume, JobDescription job,
        CancellationToken ct = default)
    {
        // Run all deterministic checks synchronously
        var bulletFindings       = CheckBulletQuality(resume);
        var completenessFindings = CheckCompleteness(resume);
        var formatFindings       = CheckFormat(resume);
        var presentationFindings = CheckPresentation(resume);

        // Alignment: semantic if embedder available, keyword fallback otherwise
        IReadOnlyList<QualityFinding> alignmentFindings;
        if (_embedder is not null)
        {
            alignmentFindings = await CheckAlignmentSemanticAsync(resume, job, _embedder, ct);
        }
        else
        {
            alignmentFindings = CheckAlignment(resume, job);
        }

        int bulletScore       = ScoreFromFindings(bulletFindings, resume.Experience.Sum(e => Math.Max(e.Achievements.Count, 1)));
        int completenessScore = ScoreFromFindings(completenessFindings, 8);
        int formatScore       = ScoreFromFindings(formatFindings, 4);
        int presentationScore = ScoreFromFindings(presentationFindings, 5);
        int alignmentScore    = ScoreFromFindings(alignmentFindings, Math.Max(job.RequiredSkills.Count, 5));

        var categories = new List<QualityCategory>
        {
            new("Bullet Quality",   bulletScore,       WeightBulletQuality,  bulletFindings),
            new("Completeness",     completenessScore, WeightCompleteness,   completenessFindings),
            new("Format",           formatScore,       WeightFormat,         formatFindings),
            new("Presentation",     presentationScore, WeightPresentation,   presentationFindings),
            new("JD Alignment",     alignmentScore,    WeightAlignment,      alignmentFindings),
        };

        return new QualityReport(WeightedScore(categories), categories, DateTimeOffset.UtcNow);
    }

    private static async Task<IReadOnlyList<QualityFinding>> CheckAlignmentSemanticAsync(
        ResumeDocument resume, JobDescription job, IEmbeddingService embedder, CancellationToken ct)
    {
        var findings = new List<QualityFinding>();

        // Build the full resume vocabulary: skill names + extracted tokens from plain text
        var resumeTerms = resume.Skills
            .Select(s => s.Name)
            .Concat(resume.Experience.SelectMany(e => e.Technologies))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Supplement with plain-text tokens when skill list is thin
        if (resumeTerms.Count < 5 && !string.IsNullOrWhiteSpace(resume.PlainText))
        {
            var textTokens = resume.PlainText
                .Split([' ', '\n', '\r', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2 && t.Length < 30)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100);
            resumeTerms.AddRange(textTokens);
        }

        if (resumeTerms.Count == 0)
        {
            // Nothing to compare - fall back to keyword check
            return CheckAlignment(resume, job);
        }

        // Embed all required skills and resume terms
        var requiredSkills = job.RequiredSkills.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (requiredSkills.Count == 0) return findings;

        float[][]? reqVectors;
        float[][]? resumeVectors;
        try
        {
            reqVectors    = await Task.WhenAll(requiredSkills.Select(s => embedder.EmbedAsync(s, ct)));
            resumeVectors = await Task.WhenAll(resumeTerms.Select(s => embedder.EmbedAsync(s, ct)));
        }
        catch (Exception)
        {
            // Embedding service unavailable - fall back to keyword check
            return CheckAlignment(resume, job);
        }

        const float SemanticThreshold = 0.82f;

        for (int i = 0; i < requiredSkills.Count; i++)
        {
            float bestSim = reqVectors[i].Length == 0 ? 0f :
                resumeVectors.Max(rv => embedder.CosineSimilarity(reqVectors[i], rv));

            if (bestSim < SemanticThreshold)
            {
                // Double-check with plain keyword search as safety net
                string reqLower    = requiredSkills[i].ToLowerInvariant();
                string resumeText  = (resume.PlainText ?? resume.RawMarkdown ?? "").ToLowerInvariant();
                bool keywordFound  = resume.Skills.Any(s => s.Name.Contains(reqLower, StringComparison.OrdinalIgnoreCase))
                                  || resumeText.Contains(reqLower);

                if (!keywordFound)
                    findings.Add(new("Alignment", FindingSeverity.Warning,
                        "MISSING_REQUIRED_SKILL",
                        $"Required skill not found in resume: \"{requiredSkills[i]}\" (best semantic match: {bestSim:P0})"));
            }
        }

        // Preferred skills - semantic check, info severity
        var preferredSkills = job.PreferredSkills.Where(s => !string.IsNullOrWhiteSpace(s)).Take(10).ToList();
        if (preferredSkills.Count > 0)
        {
            float[][]? prefVectors;
            try { prefVectors = await Task.WhenAll(preferredSkills.Select(s => embedder.EmbedAsync(s, ct))); }
            catch { prefVectors = null; }

            if (prefVectors is not null)
            {
                for (int i = 0; i < preferredSkills.Count; i++)
                {
                    float bestSim = prefVectors[i].Length == 0 ? 0f :
                        resumeVectors.Max(rv => embedder.CosineSimilarity(prefVectors[i], rv));
                    if (bestSim < SemanticThreshold)
                        findings.Add(new("Alignment", FindingSeverity.Info,
                            "MISSING_PREFERRED_SKILL",
                            $"Preferred skill not found in resume: \"{preferredSkills[i]}\""));
                }
            }
        }

        // Title alignment (semantic)
        if (!string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(resume.Personal.Summary))
        {
            try
            {
                var titleVec   = await embedder.EmbedAsync(job.Title, ct);
                var summaryVec = await embedder.EmbedAsync(resume.Personal.Summary!, ct);
                float sim = embedder.CosineSimilarity(titleVec, summaryVec);
                if (sim < 0.70f)
                    findings.Add(new("Alignment", FindingSeverity.Info,
                        "TITLE_NOT_IN_SUMMARY",
                        $"Job title \"{job.Title}\" not well-reflected in resume summary (semantic match: {sim:P0})"));
            }
            catch { /* ignore - title check is Info only */ }
        }

        return findings;
    }
}