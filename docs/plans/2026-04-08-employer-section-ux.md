# Employer Section UX Plan

## Goal

Add an employer-facing section for drafting, analysing, and improving job descriptions using lucidRESUME's requirement extraction, skill ledger matching, and quality feedback.

The UX should make fuzzy requirements explicit without forcing employers into rigid ATS-style forms too early.

---

## Core Workflow

```
Employer opens "Employer" section
    ↓
Paste or draft job description
    ↓
Parser extracts role, responsibilities, skills, constraints
    ↓
UI turns extracted requirements into editable chips/sliders
    ↓
Employer adjusts priority and strictness
    ↓
Feedback updates in real time
    ↓
Optional: compare against one or more candidate skill ledgers
    ↓
Export improved JD or API payload
```

---

## Page Structure

### 1. Job Description Input

Primary input modes:

- Paste full job description
- Start from structured fields
- Import from URL where existing JD fetcher can handle it
- Load a saved draft

Fields:

- Job title
- Company
- Location / remote model
- Employment type
- Salary range
- Seniority
- Raw JD text

The raw text remains first-class. Structured fields should not become the only source of truth.

### 2. Requirement Builder

Extracted requirements appear as editable cards or chips.

Each requirement has:

- Text
- Normalised skill or concept
- Type: skill, responsibility, domain, tooling, credential, work model, culture signal
- Priority: must have, should have, nice to have, context
- Strictness: fuzzy to exact
- Evidence expectation: named skill, role evidence, recent evidence, multi-role evidence
- Confidence from parser

Suggested controls:

- Priority segmented control: `Must`, `Should`, `Nice`, `Context`
- Strictness slider: `Broad match` -> `Related skill` -> `Exact named skill`
- Evidence slider: `Mentioned` -> `Used in role` -> `Recent role` -> `Repeated evidence`
- Toggle: `Day-one requirement`
- Toggle: `Can be learned on job`

### 3. Real-Time Feedback Panel

Feedback should update as the user edits requirements.

Groups:

- Clarity: ambiguous requirements, missing context, duplicated concepts
- Bar calibration: too many must-haves, seniority mismatch, contradictory signals
- Candidate impact: likely false negatives, over-specific tooling, excessive credential filters
- Inclusivity and accessibility: unnecessary degree requirements, vague culture phrasing
- Market fit: salary/title/requirements mismatch if enough data exists later

Example findings:

- “You have 14 must-have requirements. Consider moving Terraform and Kubernetes to should-have unless they are required on day one.”
- “Cloud experience is broad. Pick AWS, Azure, GCP, or describe the deployment outcome.”
- “This says senior but asks for 2 years of experience. Clarify seniority.”
- “Degree is marked must-have, but no responsibilities require a credential. Consider making it context or removing it.”

### 4. Candidate Skill Ledger Comparison

When a candidate ledger is available, show:

- Overall fit
- Required coverage
- Preferred coverage
- Evidence strength
- Presentation gaps
- True gaps
- Near misses
- Matching evidence snippets

Important UX distinction:

- True gap: no evidence for the requirement
- Presentation gap: related evidence exists but the JD wording is too exact
- Weak evidence: mentioned but little role-backed proof
- Adjacent skill: nearby skill likely transferable

Do not present this as a rejection score. Present it as “what the JD is asking for versus what the candidate has evidenced.”

---

## Interaction Model

### Fuzzy Requirement Editing

Requirement cards should support free-form text plus structured controls.

Example:

```text
Requirement: Kubernetes
Priority: Nice to have
Strictness: Related skill acceptable
Evidence: Mentioned or adjacent container evidence
```

Changing strictness changes matching semantics:

- Broad match: accept adjacent domain evidence, e.g. “container platforms”
- Related skill: taxonomy/embedding match, e.g. “AKS”, “EKS”, “Docker orchestration”
- Exact: require named skill “Kubernetes”

### Requirement Grouping

Group requirements automatically:

- Core delivery skills
- Platform and infrastructure
- Domain knowledge
- Collaboration and leadership
- Credentials
- Work model constraints
- Nice-to-haves

The user can drag requirements between groups.

### Sliders Versus Chips

Use chips for priority and type because they are discrete.

Use sliders for strictness and evidence expectation because they are gradients.

Use warnings when slider combinations are hostile or incoherent:

- Must-have + exact + repeated evidence for many niche tools
- Nice-to-have + exact + day-one requirement
- Broad match + regulated credential

---

## Data Model Additions

Potential Core model:

```csharp
public sealed class EmployerJobDraft
{
    public Guid DraftId { get; set; }
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string RawText { get; set; } = "";
    public List<EmployerRequirement> Requirements { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
}

public sealed class EmployerRequirement
{
    public string Text { get; set; } = "";
    public string? NormalisedText { get; set; }
    public RequirementPriority Priority { get; set; }
    public RequirementStrictness Strictness { get; set; }
    public EvidenceExpectation EvidenceExpectation { get; set; }
    public RequirementType Type { get; set; }
    public double Confidence { get; set; }
}
```

Potential enums:

```text
RequirementStrictness: Broad, Related, Exact
EvidenceExpectation: Mentioned, RoleBacked, Recent, Repeated
RequirementType: Skill, Responsibility, Domain, Tool, Credential, WorkModel, Culture, Unknown
```

---

## First Implementation Slice

1. Add an Employer page shell with paste box and extracted requirement list.
2. Reuse existing `JobSpecParser` to populate initial structured fields.
3. Add local-only `EmployerJobDraft` model and view model.
4. Implement requirement priority and strictness controls.
5. Run `JobQualityAnalyser` live on edits.
6. If a candidate ledger is selected, run `SkillLedgerMatcher` and show gaps.
7. Add export to JSON request payload for the future SaaS API.

---

## Open UX Questions

- Should employers compare one candidate at a time, or compare anonymised ledger cohorts?
- Should the UI allow “must-have budget” warnings, e.g. max 5 must-have skills?
- How strongly should the UI push back on impossible JDs?
- Should the employer see candidate evidence snippets by default, or only high-level match explanations?
- Should filters be editable as natural language first, then structured after extraction?
