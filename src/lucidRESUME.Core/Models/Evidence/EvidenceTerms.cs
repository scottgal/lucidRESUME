using System.Text.RegularExpressions;

namespace lucidRESUME.Core.Models.Evidence;

/// <summary>Literal vocabulary and assertion checks. A match is a source mention, never a proficiency claim.</summary>
public static class EvidenceTerms
{
    public const string Version = "explicit-concepts-v3";
    private static readonly IReadOnlyDictionary<string, string[]> Vocabulary = new Dictionary<string, string[]>
    {
        [".NET"] = [".NET"], ["C#"] = ["C#", "CSharp"],
        ["ASP.NET"] = ["ASP.NET"],
        ["ASP.NET Core"] = ["ASP.NET Core"], ["ASP.NET MVC"] = ["ASP.NET MVC"],
        ["SQL Server"] = ["SQL Server", "Microsoft SQL Server", "MS-SQL"],
        ["Azure"] = ["Azure", "Microsoft Azure", "Windows Azure"],
        ["Azure Functions"] = ["Azure Functions"], ["Azure Service Bus"] = ["Azure Service Bus", "Service Bus"],
        ["Azure DevOps"] = ["Azure DevOps"], ["Application Insights"] = ["Application Insights"],
        ["Azure App Services"] = ["Azure App Services", "App Services"],
        ["Azure Blob Storage"] = ["Azure Blob Storage", "Blob Storage"],
        ["Azure Key Vault"] = ["Azure Key Vault", "Key Vault"],
        ["Azure API Management"] = ["Azure API Management", "APIM"],
        ["Cosmos DB"] = ["Cosmos DB"], ["Azure OpenAI"] = ["Azure OpenAI"],
        ["Blazor"] = ["Blazor", "Blazor Server"], ["SignalR"] = ["SignalR"], ["WPF"] = ["WPF"],
        ["Entity Framework"] = ["Entity Framework"], ["Docker"] = ["Docker"],
        ["Docker Compose"] = ["Docker Compose"], ["Kubernetes"] = ["Kubernetes", "k8s"],
        ["OpenSearch"] = ["OpenSearch"], ["PostgreSQL"] = ["PostgreSQL", "Postgres"],
        ["MySQL"] = ["MySQL"], ["SQLite"] = ["SQLite"], ["Redis"] = ["Redis"],
        ["GraphQL"] = ["GraphQL"], ["REST"] = ["REST", "RESTful", "REST APIs", "RESTful APIs"],
        ["React"] = ["React", "React.js", "ReactJS"], ["JavaScript"] = ["JavaScript"],
        ["TypeScript"] = ["TypeScript"], ["Bicep"] = ["Bicep"], ["K6"] = ["K6"],
        ["CI/CD"] = ["CI/CD"], ["Code LLM"] = ["Code LLM", "Code LLMs"],
        ["Claude Code"] = ["Claude Code"], ["GitHub Copilot"] = ["GitHub Copilot"],
        ["GitHub Actions"] = ["GitHub Actions"], ["Agentic development"] = ["agentic development"],
        ["Microservices"] = ["microservices"], ["Event-driven architecture"] = ["event-driven architecture"],
        ["Mentoring"] = ["mentoring", "mentored", "mentoring my development team"],
        ["Engineering management"] = ["managed engineering team", "managed an engineering team"]
    };

    public static IReadOnlyList<string> FindExplicitConcepts(string text) => Vocabulary
        .Where(pair => pair.Value.Prepend(pair.Key).Any(alias => HasPositiveMention(text, alias)))
        .Select(pair => pair.Key).ToList();

    public static bool Mentions(string text, string concept) =>
        (Vocabulary.TryGetValue(Canonicalize(concept), out var aliases) ? aliases.Prepend(concept) : [concept]).Any(alias => HasPositiveMention(text, alias));

    public static string Canonicalize(string concept)
    {
        var clean = concept.Trim().TrimEnd('.', ';');
        return Vocabulary.FirstOrDefault(pair => pair.Key.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
            pair.Value.Contains(clean, StringComparer.OrdinalIgnoreCase)).Key ?? clean;
    }

    public static bool HasPositiveMention(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        var distinctProduct = phrase.Trim().Equals("ASP.NET", StringComparison.OrdinalIgnoreCase) ? @"(?!\s+(?:Core|MVC)\b)" : "";
        foreach (Match match in Regex.Matches(text, @"(?<![\p{L}\p{N}+#])" + Regex.Escape(phrase.Trim()) + @"(?![\p{L}\p{N}+#])" + distinctProduct, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var prefix = text[Math.Max(0, match.Index - 150)..match.Index];
            var boundary = Regex.Matches(prefix, @"[;!?\n]|\.\s|\b(?:but|however)\b", RegexOptions.IgnoreCase).LastOrDefault();
            if (boundary is not null) prefix = prefix[(boundary.Index + boundary.Length)..];
            prefix = Regex.Replace(prefix, @"\bnot\s+only\b", "", RegexOptions.IgnoreCase);
            var negative = Regex.IsMatch(prefix,
                @"\b(?:no\s+(?:(?:hands-on|production|professional|direct|practical|prior|previous|commercial)\s+)*(?:experience|knowledge|exposure|use)|never\s+(?:used|built|worked|operated)|(?:do|did|have|has|was|were)\s+not\s+(?:use|used|work|worked|responsible)|without\s+(?:using|experience|knowledge)|lack(?:ing)?\s+(?:experience|knowledge)|not\s+(?:in|with|using)?\s*)\b[^;!?]*$", RegexOptions.IgnoreCase);
            negative |= Regex.IsMatch(prefix, @"\bnot\s+(?:(?:in|with|using)\s+)?$", RegexOptions.IgnoreCase);
            negative |= Regex.IsMatch(prefix, @"\bno\s+$", RegexOptions.IgnoreCase);
            negative |= Regex.IsMatch(prefix, @"\b(?:don['’]t|do not)\s+need\b[^;!?]*$", RegexOptions.IgnoreCase);
            if (!negative) return true;
        }
        return false;
    }
}
