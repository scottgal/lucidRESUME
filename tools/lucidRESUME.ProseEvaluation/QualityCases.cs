using System.Text.RegularExpressions;

// Authored synthetic regressions: do not derive these checks from CompositionValidator.
internal sealed record QualityCase(string Id, string Text, string[] Skills, string Brief,
    string[] RequiredPatterns, string[] ForbiddenPatterns, string[]? AwkwardPatterns = null)
{
    public IReadOnlyList<string> Review(string text) => RequiredPatterns
        .Where(pattern => !Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
        .Select(pattern => "Missing source distinction: " + pattern)
        .Concat(ForbiddenPatterns.Where(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            .Select(pattern => "Potential factual drift: " + pattern))
        .Concat((AwkwardPatterns ?? []).Where(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            .Select(pattern => "Editorial issue retained: " + pattern)).ToArray();

    public static readonly QualityCase[] All =
    [
        new("rough-platform-prose", "Supported a large, distrubted ASP.NET Core applicaiton on Azure with Cosmos DB, Service Bus and SQL Server. Asked to reduce running costs and improve reliability. Added Application Insights and custom telemetry to identify expensive operations. Mentored developers through code review.",
            ["ASP.NET Core", "Azure", "Cosmos DB", "Service Bus", "SQL Server", "Application Insights"],
            "Development Team Lead: hands-on .NET engineering, SQL Server, resilient integrations and mentoring a small team. Compliance experience and agentic coding are useful but not established by this source.",
            [@"ASP\.NET Core", @"Application Insights", @"\b(?:asked|tasked) to\b", @"expensive operations", @"\bmentored\b"],
            [@"\b(?:reduced|lowered) (?:running )?costs\b", @"\b(?:KYB|AML|Claude Code|Copilot)\b", @"\bline.managed\b"],
            [@"\bdistrubted\b", @"\bapplicaiton\b"]),
        new("internal-agent", "Asked to stabilise an internal support agent after its pilot. Using C#/.NET, split Azure OpenAI calls from tool execution, added per-tool allow-lists, bounded retries and runbook retrieval from Qdrant. Released to internal operations in production; it was not customer-facing. Pair-programmed with three engineers and personally reviewed tool-permission edge cases. Ticket reduction and answer accuracy have not been measured.",
            ["C#", ".NET", "Azure OpenAI", "Qdrant"],
            "Head of Applied AI Engineering: own production agent architecture, tool authorization, reliable execution, retrieval and evaluation. Hands-on technical leadership. Customer-facing experience is preferred; do not infer it.",
            [@"\binternal\b", @"\bproduction\b", @"\bnot customer-facing\b", @"\b(?:not (?:been )?measured|unmeasured)\b", @"\b(?:pair-programmed|pair programming)\b"],
            [@"\b(?:reduced|improved) (?:tickets|answer accuracy)\b", @"^Stabili[sz]ed\b"]),
        new("offline-evaluation", "In a 120-task offline test, manual review found 14 fewer tool-selection errors. The test did not measure production availability. A separate pilot showed no change in customer resolution time; the production effect remains unknown.",
            [], "Applied AI evaluation lead: curated evaluation datasets, deterministic checks, automated evaluators, production experiments and customer outcomes.",
            [@"\b120\b", @"\b14\b", @"\boffline\b", @"\bmanual\b", @"\bno change\b", @"\bunknown\b"],
            [@"\bautomated (?:review|evaluation)\b", @"\bimproved production (?:accuracy|availability)\b"]),
        new("personal-deployment", "Led three engineers through the release plan and personally deployed the C# service. The team wrote most adapters. Reviewed the rollback drill but did not operate it. Deployment duration and incident rates were not measured.",
            ["C#"], "Engineering lead: develop engineers, deploy production services, own incident response and operate recovery drills.",
            [@"\bpersonally deployed\b", @"\bteam (?:wrote|built) most adapters\b", @"\bdid not operate\b", @"\b(?:not measured|unmeasured)\b"],
            [@"\bled (?:three engineers|the team) to deploy\b", @"\b(?:owned|led) incident response\b"]),
        new("incident-log", "The retry log stored the request ID and tool name. Operations used it to see which calls failed, not why. This let responders find jobs to replay; it did not identify root cause.",
            [], "Platform reliability engineer: incident diagnosis, root cause analysis, tool-call tracing and safe recovery of failed jobs.",
            [@"\brequest IDs?\b", @"\btool names?\b", @"\breplay\b", @"\b(?:not (?:to )?identify|did not identify|without identifying) root cause\b"],
            [@"\b(?:prevented|eliminated) failures\b", @"\bdiagnosed root cause\b"]),
        new("metric-attribution", "Optimised PostgreSQL queries: measured test latency fell from 240 ms to 180 ms. For a separate batch workload, memory use rose from 180 MB to 240 MB. Neither test established production performance.",
            ["PostgreSQL"], "Database platform engineer: optimise PostgreSQL latency and memory use, validate performance trade-offs and operate production systems.",
            [@"latency[^.]*240\s*ms[^.]*180\s*ms", @"memory[^.]*180\s*MB[^.]*240\s*MB", @"\b(?:neither|not|unmeasured|unestablished)\b[^.]*production"],
            [@"\b(?:reduced|lowered) memory\b", @"\bproduction latency (?:fell|improved)\b"]),
        new("prototype-cancelled", "Built a Python/FastAPI document-classification prototype using PyTorch. It ran on synthetic documents only and was never deployed to production. Proposed a human review queue, but the project was cancelled before that queue was implemented.",
            ["Python", "FastAPI", "PyTorch"], "Applied AI product engineer: production document classification, Python APIs, PyTorch and human-in-the-loop review workflows.",
            [@"\bprototype\b", @"\bsynthetic\b", @"\bnever deploy(?:ed|ing)\b", @"\bcancelled\b", @"\b(?:proposed|before[^.]*implemented)\b"],
            [@"\b(?:built|implemented|shipped) (?:a )?human review queue\b"])
    ];
}
