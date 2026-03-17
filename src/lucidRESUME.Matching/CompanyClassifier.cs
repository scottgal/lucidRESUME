using lucidRESUME.Core.Models.Jobs;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Matching;

/// <summary>
/// Classifies a job's employer type from JD text and title.
/// Rules and keywords are configured in <see cref="CompanyClassifierOptions"/>.
/// First matching rule wins — a company is one type.
/// </summary>
public sealed class CompanyClassifier
{
    private readonly CompanyClassifierOptions _options;

    public CompanyClassifier(IOptions<CompanyClassifierOptions> options)
    {
        _options = options.Value;
    }

    public CompanyType Classify(JobDescription job)
    {
        var text = ((job.Title ?? "") + " " + job.RawText).ToLowerInvariant();

        foreach (var rule in _options.Rules)
        {
            if (!Enum.TryParse<CompanyType>(rule.Type, ignoreCase: true, out var type))
                continue;

            if (rule.Keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return type;
        }

        return CompanyType.Unknown;
    }
}
