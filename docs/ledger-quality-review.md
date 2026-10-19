# Ledger source-quality review

Reviewed the saved career and 20 saved adverts on 19 October 2026 using read-only
SQLite access. Personal text, before/after JSON and native screenshots remain in
the ignored `artifacts/ledger-quality/` directory.

## What the saved records contained

The career held 17 roles, 88 declared skills, 623 claims and 2,137 evidence
records. Of those claims, 172 were accepted and 451 were unreviewed extraction
observations. Acceptance is a workflow decision, not independent verification.

Twenty-one accepted role accounts explicitly named tools without corresponding
concept links. Examples included Azure Functions, SignalR, WPF, Application
Insights, K6 and Code LLMs. The old linking step relied on global skills and NER
observations, so a real source mention could be overlooked while an unreviewed
label could nominate a concept into an accepted account.

The saved Kyckr advert had 124 required entries and no preferred entries in the
final audited snapshot. These included benefits, ordinary sentence fragments,
SOLID inferred from “Solid experience”, and SAFe inferred from “safe” deployments.
Inline Essential/Nice to Have headings lost their boundaries. Matching could
rerun extraction, bypass saved classifications, split competency commas, or
discard long requirements.

## Changes

- Versioned literal concept linking recognises a small explicit vocabulary and
  audited aliases directly in source prose, including Azure service names.
  Negated mentions are excluded. .NET alone does not imply C# experience.
- Unreviewed extraction observations cannot nominate new accepted career
  concepts. Refreshes preserve claim identity, source supports and review state.
  Existing portable JobML gains literal links for matching accepted statements
  while retaining prose, evidence links and author review decisions.
- Advert classification records its source excerpt and section. Plain and inline
  required/preferred headings remain distinct; benefits are excluded. Acronyms
  with ordinary-word collisions require their explicit casing.
- Structural extraction retains complete competency sentences and avoids
  splitting flattened paragraphs into comma fragments. Source-backed vocabulary
  extraction works without optional NER or embedding models.
- Audited aliases are deduplicated. Job matching consumes saved requirements
  and decisions, keeps full competencies, and retains source excerpts. Rejected
  and negated career accounts do not satisfy coverage checks. Bare skill
  declarations contribute zero employment roles.
- My career → Evidence prompts shows accepted claims, unreviewed observations
  and source-link findings. Jobs → Requirements shows priorities, source excerpts
  and excluded extraction. Editing the advert hides stale review results until
  extraction runs again.

## Results and limits

The refreshed career retained all 623 claims, all 172 accepted decisions and all
2,137 supports. The 21 missing literal-link findings fell to zero. The derived
skill ledger contains 108 source-backed concepts, reflecting additional explicit
role mentions rather than new author claims.

Kyckr now has 27 required entries and 14 preferred entries. Docker, Kubernetes,
React and Azure DevOps are preferred; Azure Functions, Service Bus, Blob Storage,
Key Vault and APIM remain required. Benefits and false SOLID/SAFe matches are
excluded. Sentence-level competency adverts retain their complete accounts.

This checks traceability and extraction consistency. Assessing actual proficiency,
resolving overlapping roles or date discrepancies, and confirming incomplete job
identity still require author judgement. Two saved adverts retain generic “About
the job” titles; the intake flow already requires title and company confirmation.
The live database was read only during this review; native tests used a backup.

## Reproduce

```sh
dotnet run --project src/lucidRESUME.Cli -- evidence \
  --career-store /path/to/data.db --audit --output /path/to/audit.json
```

The command compares stored career links before/after an in-memory refresh and
reparses saved adverts without writing the store. Use an offline configuration
(`Tailoring.ExtractionProvider=none`, `Nimble.Enabled=false`) to reproduce the
deterministic audit; otherwise configured extraction services may be used.

Regression coverage includes literal aliases, negation, unreviewed/rejected
claims, source-link refresh integrity, flattened adverts, benefits, required vs
preferred priority, acronym collisions, intact competencies, extraction-free
matching and stale UI results. `ux-scripts/ledger-quality.yaml` exercises the
native views and tab returns at 1200×800 and 900×600.
