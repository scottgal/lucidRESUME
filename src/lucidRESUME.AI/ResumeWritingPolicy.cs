using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>The same writing instructions are used by the client and synthetic evaluations.</summary>
public static class ResumeWritingPolicy
{
    public static string Describe(ResumeTone tone, ResumeDetail detail)
    {
        var voice = tone switch
        {
            ResumeTone.Understated => "Use short, separate, matter-of-fact sentences. Prefer simple verbs and a steady rhythm. Avoid rhetorical emphasis and elaborate connecting phrases.",
            ResumeTone.Expressive => "Vary sentence length and openings. When the source gives an explicit purpose, open with 'To' and that purpose, followed by the action that supports it. Otherwise foreground a supported technical decision or context before the action. Use concrete language without slogans, superlatives or self-praise.",
            _ => "Lead with the candidate's action. Use direct active phrasing, plain specific verbs and compact sentences."
        };
        var depth = detail switch
        {
            ResumeDetail.Thorough => "Retain the supported technical decisions, implementation details, context and outcomes of every selected claim. Use additional sentences when needed; the word limit is a ceiling, not a target.",
            ResumeDetail.Brief => "Combine related facts into a brief concrete account. Remove repeated setup, but retain each selected action, its object and purpose, and any outcome limitations.",
            _ => "Write a concise, concrete account retaining each selected contribution and the details that explain it."
        };
        return depth + " " + voice + " Structure the account around the candidate's action and its object, then a supported decision, context or result where available. Omit missing elements. Preserve the purpose and object of each selected action, even when combining sentences. Use explicit subjects for outcomes and limitations; avoid an ambiguous 'which' after a location or environment. Preserve personal versus team ownership, prototype versus production status, uncertainty and named skills. Never turn helped into led, a goal into an achieved result, or a publication into employment experience. In a summary, preserve explicit time periods and named publication sites from the source.";
    }
}
