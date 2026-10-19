using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI;

/// <summary>The same writing instructions are used by the client and synthetic evaluations.</summary>
public static class ResumeWritingPolicy
{
    public const string JobResponseInstructions = """

        JOB RESPONSE AND FLOW:
        First decide what this section actually demonstrates for this vacancy: hands-on building,
        integrations, delivery, or developing people. Open with that supported contribution and
        connect related details to it. Let the evidence answer the employer's question without
        saying 'this aligns with your requirements', naming the target employer, or copying its advert.
        A job requirement is a question, not an extra candidate fact. A related integration does not
        establish KYB/AML experience; mentoring does not establish formal performance management.
        Preserve the distinction between being asked to achieve something and having achieved it.
        'Asked to stabilise a service; added retries' supports the retries, not 'Stabilised the service'.
        Prefer a small edit that fixes spelling, repetition or an awkward join over a wholesale rewrite.
        Retain already clear sentences. Join facts only when their actor, object and status remain clear.
        Keep qualifiers close to their subject. Preserve 'not', 'never', 'pilot', and 'proposed' literally
        when they qualify included facts; avoid stylistic synonyms that blur the source distinction.
        A test's caveat belongs beside that test, before introducing a separate pilot or workload.
        Use an explicit test name when reordering would make 'the test' ambiguous.
        A summary introduces two or three relevant strengths supported by the selected claims;
        Experience supplies the concrete examples. Do not turn the summary into another duty list.
        Before returning, read each section aloud: remove repeated openings, vague praise, keyword
        piles and dangling clauses. Stop when it reads naturally; the word limit is not a target.
        """;

    public static string Describe(ResumeTone tone, ResumeDetail detail)
    {
        var voice = tone switch
        {
            ResumeTone.Understated => "Use short, separate, matter-of-fact sentences. Prefer simple verbs and a steady rhythm. Avoid rhetorical emphasis and elaborate connecting phrases.",
            ResumeTone.Expressive => "Vary sentence length and openings where it improves the flow. Foreground a supported technical decision, contribution or context. Use a purpose opening only when the source states that purpose and it reads naturally. Use concrete language without slogans, superlatives or self-praise.",
            _ => "Lead with the candidate's action. Use direct active phrasing, plain specific verbs and compact sentences."
        };
        var depth = detail switch
        {
            ResumeDetail.Thorough => "Retain the supported technical decisions, implementation details, context and outcomes of every selected claim. Use additional sentences when needed; the word limit is a ceiling, not a target.",
            ResumeDetail.Brief => "Combine related facts into a brief concrete account. Remove repeated setup, but retain each selected action, its object and purpose, and any outcome limitations.",
            _ => "Write a concise, concrete account retaining each selected contribution and the details that explain it."
        };
        return depth + " " + voice + " Structure the account around the candidate's action and its object, then a supported decision, context or result where available. Lead with the strongest relevant contribution, keeping limitations beside the facts they qualify. Use varied natural sentences without forced STAR labels, keyword piles or repeating the same opening. Answer the section's job focus using only selected evidence; unmatched job terms and missing detail are not facts to supply. Omit missing elements. Preserve the purpose and object of each selected action, even when combining sentences. Use explicit subjects for outcomes and limitations; avoid an ambiguous 'which' after a location or environment. Preserve personal versus team ownership, prototype versus production status, uncertainty and named skills. Never turn helped into led, a goal into an achieved result, or a publication into employment experience. In a summary, preserve explicit time periods and named publication sites from the source.";
    }
}
