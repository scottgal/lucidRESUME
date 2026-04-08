# SaaS API Surface Plan

## Goal

Create a small employer-facing SaaS API that uses job descriptions and candidate skill ledgers to give real-time feedback on job-description quality, candidate fit, and requirement clarity.

This is not a recruitment database, ATS replacement, or scraping platform. The first version should expose the existing lucidRESUME matching primitives as a clean API:

- Job description in
- Candidate skill ledger in
- Structured feedback out
- No candidate identity required
- No resume document required
- No automated hiring decision

---

## Product Shape

Employers use the API while drafting or reviewing job descriptions.

The API answers:

- Is this JD internally coherent?
- Which requirements are hard filters versus preference signals?
- Which skills are likely over-specific, ambiguous, or inflated?
- How does this JD match a candidate skill ledger?
- What missing requirements are true gaps versus presentation gaps?
- How can the JD be made clearer without lowering the bar?

The important distinction: this is feedback for humans, not a black-box ranking engine.

---

## Minimal API Surface

### `POST /v1/job-descriptions/analyse`

Analyses one job description without a candidate.

Request:

```json
{
  "jobDescription": {
    "title": "Senior .NET Engineer",
    "company": "ExampleCo",
    "rawText": "Full pasted JD text...",
    "requirements": [
      { "text": "C#", "priority": "must_have" },
      { "text": "Azure", "priority": "should_have" },
      { "text": "Kubernetes", "priority": "nice_to_have" }
    ],
    "metadata": {
      "location": "Remote UK",
      "seniority": "senior",
      "employmentType": "full_time"
    }
  }
}
```

Response:

```json
{
  "jobDescriptionId": "jd_...",
  "quality": {
    "score": 82,
    "findings": [
      {
        "severity": "warning",
        "code": "requirement_too_broad",
        "message": "Cloud experience is broad; name the platforms or outcomes expected.",
        "section": "requirements"
      }
    ]
  },
  "requirements": [
    {
      "text": "C#",
      "priority": "must_have",
      "confidence": 0.96,
      "normalisedSkill": "C#"
    }
  ],
  "suggestedEdits": [
    {
      "type": "clarify_requirement",
      "from": "Cloud experience",
      "to": "Experience deploying services to Azure App Service or AKS"
    }
  ]
}
```

### `POST /v1/matches/analyse`

Analyses a JD against one candidate skill ledger.

Request:

```json
{
  "jobDescription": {
    "rawText": "Full pasted JD text..."
  },
  "candidateSkillLedger": {
    "entries": [
      {
        "skillName": "C#",
        "category": "Language",
        "calculatedYears": 10,
        "isCurrent": true,
        "evidence": [
          {
            "sourceText": "Built payment APIs in C#",
            "source": "achievement_bullet",
            "jobTitle": "Lead Developer",
            "company": "Example Ltd",
            "confidence": 0.9
          }
        ]
      }
    ]
  },
  "options": {
    "includeSuggestedEdits": true,
    "includeEvidence": true
  }
}
```

Response:

```json
{
  "fit": {
    "overall": 0.78,
    "requiredCoverage": 0.86,
    "preferredCoverage": 0.55,
    "averageEvidenceStrength": 0.74
  },
  "matches": [
    {
      "requiredSkill": "Azure",
      "matchedCandidateSkill": "Microsoft Azure",
      "matchType": "taxonomy",
      "similarity": 0.82,
      "evidenceCount": 4
    }
  ],
  "gaps": [
    {
      "requirement": "Kubernetes",
      "gapType": "presentation_gap",
      "reason": "Related container deployment evidence exists, but Kubernetes is not named directly."
    }
  ],
  "jdFeedback": [
    {
      "severity": "info",
      "message": "Kubernetes appears as a nice-to-have. Keep it as a preference unless it is required for day-one delivery."
    }
  ]
}
```

### `POST /v1/requirements/normalise`

Turns messy requirement text into structured requirement candidates.

Use this for interactive UI and ATS integrations.

```json
{
  "rawRequirements": [
    "Strong cloud / DevOps",
    "Must know .NET and ideally Azure",
    "Nice to have k8s"
  ]
}
```

---

## Data Contracts

### Job Requirement

Priority should not be just Boolean required/not-required:

```text
must_have      Hard requirement for credible fit
should_have    Strong signal, but not a hard rejection
nice_to_have   Bonus signal
context        Describes environment, domain, or tools
avoid          Skills/processes the role should not over-index on
unknown        Extracted but needs human confirmation
```

### Candidate Skill Ledger

The API should accept an evidence-backed ledger, not a resume:

```text
Skill name
Category
Calculated years
First seen / last seen
Current flag
Evidence snippets
Source type
Confidence
Role count
Optional embedding vector later
```

Do not require PII. A customer can pass an anonymous candidate id if needed.

---

## First Implementation Slice

1. Extract a DTO-only Web API project, likely `lucidRESUME.Api`.
2. Add request/response contracts that map to existing Core models without leaking UI types.
3. Reuse `JobSpecParser`, `JobQualityAnalyser`, `SkillLedgerMatcher`, `JdSkillLedgerBuilder`, and `ResumeCoverageAnalyser`.
4. Add in-memory operation only. No account system, no persistence, no billing.
5. Add API golden tests with fixed sample ledgers and JDs.
6. Only after API output is stable, add persistence and auth.

---

## Non-Goals For V1

- Candidate sourcing
- Resume ingestion from employer systems
- Automated reject/accept decisions
- Browser scraping
- Bulk candidate ranking
- Storing candidate PII
- Company-wide analytics
- Billing

---

## Open Questions

- Should evidence snippets be returned verbatim, redacted, or hash-addressed?
- Should employers upload skill ledgers generated locally by candidates, or should the SaaS generate ledgers from resumes later?
- Do we need explainability tiers: terse, normal, audit?
- How do we prevent the tool from becoming a proxy for discriminatory filtering?
- Should the API enforce “must-have” count limits to discourage impossible job descriptions?
