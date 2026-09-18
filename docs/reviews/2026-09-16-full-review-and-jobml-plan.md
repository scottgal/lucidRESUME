# lucidRESUME full review and JobML 0.1 plan

Date: 2026-09-16

## Remediation status

The kickoff implementation now has a clean desktop baseline: the default solution restores and builds with analyzers enabled and zero warnings, all 289 tests pass, and NuGet reports no known vulnerable direct or transitive packages. Android and iOS source projects remain available but are excluded from the default desktop solution. CI now uses the same restore/build/test commands and audits dependencies without ignoring failures.

The reversible authoring loop is operational: My CV opens directly into the editor; the editor autosaves and recovers its workspace, selects source prose from evidence cards, performs live reconciliation, requires explicit acceptance for changed evidence and derived claims, and publishes one reviewed revision back to app state. Markdown export and matching consume that canonical snapshot. The same validation, reconciliation, and coverage operations are available through the CLI.

## Executive assessment

lucidRESUME has a strong differentiator—evidence-backed career data—and the existing skill ledger, extraction, matching, export, and local-first architecture are a natural foundation for JobML. The problem is not lack of capability. It is that the repository currently presents a release-sized surface while its build, dependency posture, and primary editing workflow are still research-preview quality.

The shortest route to a credible 0.1 is to make one workflow excellent:

```text
open Markdown → edit human prose → see live evidence state
→ explicitly reconcile claims → compare against requirements → save one reversible file
```

Job search aggregation, email tracking, mobile targets, career graphs, Collabora, and multi-provider AI should remain available experiments, but should not define the 0.1 release gate.

## What is already good

- The product principle (“every skill is backed by evidence”) is coherent across ingestion, matching, tailoring, and JobML.
- Domain code is mostly separated into inward-facing modules around `lucidRESUME.Core`.
- The Avalonia UI is local-first and cross-platform, and the existing native Markdown renderer is reusable in the editor.
- The repository has substantial automated coverage (roughly 275 test attributes were found) and its own UI automation tooling.
- AI providers are optional and the code consistently attempts to keep failed enhancements non-fatal.
- JSON Resume, Markdown, DOCX, and PDF exporters already give JobML sensible projection targets.

## Original release blockers and disposition

### P0 — establish one green, reproducible build — resolved

The solution does not build normally on this macOS environment:

- Android is included in the default solution build and fails without an Android SDK.
- analyzer warnings are treated as errors in `lucidRESUME.Core` and `Mostlylucid.Avalonia.UITesting` (19 desktop-path errors in the reviewed build);
- the initial restore was incomplete; a targeted restore repaired the missing Avalonia package and sqlite-vec compile issue;
- the desktop application builds only with `RunAnalyzers=false` at review time.

Action:

1. Introduce a desktop CI solution/filter that excludes mobile workloads.
2. Fix or baseline analyzer findings; do not require developers to disable analyzers.
3. Add clean restore, build, unit-test, and headless UI smoke jobs for macOS, Windows, and Linux.
4. Make the README build command identical to the CI command.

### P0 — remediate vulnerable and inconsistent dependencies — security audit resolved

Restore reports:

- critical: `NuGet.CommandLine 5.11.5`;
- high: `SQLitePCLRaw.lib.e_sqlite3 2.1.11` and `Tmds.DBus.Protocol 0.21.2`;
- moderate: AngleSharp versions, MailKit, and OpenTelemetry.Api.

Package versions are also inconsistent across projects (for example Markdig, DocumentFormat.OpenXml, Microsoft.Extensions 9/10, Newtonsoft stable/beta, and AngleSharp stable/beta).

Action: add central package management (`Directory.Packages.props`), remove unnecessary transitive sources, upgrade vulnerable packages, and enforce `dotnet list package --vulnerable` in CI. Keep the experimental sqlite-vec dependency behind a replaceable adapter.

### P0 — make Markdown/JobML the canonical authoring path — resolved for 0.1

Before this change the main résumé page could export Markdown but not import or edit it; drag-and-drop accepted PDF, DOCX, TXT, and ZIP only. That made the human-authoritative principle impossible in the primary UI.

The JobML editor now provides the complete narrow path: handoff from My CV, autosave/recovery, live evidence state, exact source selection, explicit claim/evidence acceptance, guarded publication, persisted revision identity, canonical Markdown export, and reviewed-claim-only matching. A richer unsaved-change dialog and per-item reject history remain UX improvements, not integrity blockers.

## Important design risks

### P1 — breadth obscures the product

The solution contains 20+ production/test projects, mobile shells, two UI-testing systems, Collabora/WOPI code, seven job sources, email tracking, GitHub ingestion, AI providers, and multiple parser stacks. Several projects still contain template `Class1` files, while `lucidRESUME.Processing` is effectively a placeholder.

Action: publish a supported/experimental matrix. Remove empty project/template artefacts and consolidate the two UI-testing paths after migration.

### P1 — documentation and actual maturity diverge

The README accurately labels the app a research preview, but it also describes a mature 1.x-sized product and hard counts/features that will drift quickly. Release notes already run through 1.5.0 while the requested product direction is 0.1.

Action: reset application versioning independently from the mature UI-testing package, replace hard test counts with CI badges, and lead with the one supported JobML workflow.

### P1 — silent failure reduces evidential trust

Many background paths intentionally swallow exceptions. That is appropriate for optional enhancement, but evidence/indexing failures should be observable because they can alter coverage results.

Action: classify failures as optional, degraded, or integrity-affecting. Log all three; surface the last category in the document UI and prevent a “valid” result when integrity-affecting work failed.

### P1 — duplicated representations can drift

`ResumeDocument` stores raw Markdown, plain text, parsed sections, extracted entities, and tailored variants. JobML adds another projection. Without a clear authority rule these can diverge.

Action: for JobML documents, Markdown is authoritative; JobML is its reviewed evidence projection; parsed `ResumeDocument` and skill ledgers are rebuildable caches with source revision identifiers. Exports should be generated from a single revision snapshot.

## JobML format decision

No existing résumé format should be extended as the canonical editor file:

| Format | Useful part | Why it is not the base |
|---|---|---|
| JSON Resume | Widely understood JSON projection; extensions permitted | Record-first; no prose anchors, evidence state, or reversibility |
| HR Open / Europass | Rich recruiting and public-employment interchange | Large exchange vocabularies; unsuitable as an authoring model |
| h-resume | Human and machine content co-located in HTML | Skills are assertions; no claim/evidence lifecycle |
| Schema.org | Public linked-data output | Describes people/occupations, not document reconciliation |
| W3C Web Annotation | Robust text quote and position selectors | Not a résumé schema, but ideal to reuse inside JobML evidence |

Decision: keep JobML as a small Markdown evidence profile, reuse W3C selector semantics, and provide loss-aware projections to JSON Resume/Europass/Schema.org later.

JobML also treats semantic obviousness as a conformance goal. Serialized documents carry a compact ordinary-language purpose and evidence contract, and the CLI can emit a blind cold-parser challenge plus separately scored deterministic ground truth. This is deliberately a small preamble, not an embedded replacement for the formal schema.

## Reversibility contract

For every prose evidence item store:

- a stable structural reference;
- a fast normalized-text fingerprint (`fnv1a64`);
- exact quote and optional prefix/suffix context;
- last reviewed state.

On every editor change:

1. Resolve the structural reference.
2. Compare the fingerprint.
3. If the reference moved, recover using quote/context.
4. Report `valid`, `changed`, `missing`, or `ambiguous`.
5. Never mutate the reference, fingerprint, claim, or prose without an explicit user action.

Generated claims carry `origin: derived` and `review: required`. They remain excluded from direct coverage until explicitly accepted.

## Implemented in this kickoff

- New dependency-light `lucidRESUME.JobML` project.
- YAML fenced-block parser and serializer.
- JobML 0.1 models for document, entities, claims, evidence, concepts, aliases, jobs, and requirements.
- Markdown passage indexing with stable heading/paragraph references.
- FNV-1a normalized-text drift fingerprints.
- W3C-style text quote/context selectors.
- Evidence reconciliation and JobML validation.
- Direct/ambiguous/missing requirement coverage with review gating.
- Explicitly derived draft generation with stable heading anchors.
- New three-pane Avalonia JobML editor with live evidence and diagnostics.
- Explicit acceptance actions for changed evidence and draft claims.
- JobML parser, drift, source-span, coverage, and strict matching tests.

## Proposed delivery sequence

### Milestone A — credible engineering baseline

- Green desktop restore/build/test in CI.
- Dependency vulnerability remediation.
- One supported desktop target per OS; mobile excluded from default gates.
- Remove or label incomplete commands/features.

### Milestone B — reversible authoring loop

- Persist JobML documents and recovery snapshots.
- Click evidence card to select/highlight exact source prose.
- Debounced background processing for large documents.
- Individual accept/reject controls and reconciliation history.
- Property-based edit/move/duplicate tests for anchors.

### Milestone C — connect existing intelligence

- Convert existing skill-ledger evidence into JobML candidate claims.
- Keep deterministic extraction separate from LLM suggestions.
- Feed reviewed JobML into existing matching/coverage services.
- Add JSON Resume import/export with a documented loss report.

### Milestone D — 0.1 release

- CLI commands: `jobml validate`, `jobml reconcile`, and `jobml coverage`.
- Golden corpus of real-world Markdown documents.
- Signed-off schema examples and conformance fixtures.
- Minimal user guide focused on the reversible authoring workflow.

## 0.1 acceptance criteria

- Editing referenced prose updates evidence state in under 100 ms for a typical CV.
- Moving a uniquely identified paragraph proposes, but does not apply, a new reference.
- Duplicate matches become ambiguous and cannot be bulk accepted.
- Missing evidence prevents a claim from satisfying direct coverage.
- Machine-generated claims never satisfy direct coverage before explicit review.
- Saving and reopening produces the same Markdown, JobML, evidence states, and coverage result.
- Desktop CI is green with analyzers enabled and has no known high/critical vulnerable runtime dependencies.
