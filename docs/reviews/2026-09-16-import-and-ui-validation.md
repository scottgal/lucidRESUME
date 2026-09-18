# Import and UI validation — 2026-09-16

## Outcome

The release validation path passes on .NET 10 and Avalonia 12.1.2. It imports
two real DOCX résumé variants, renders the first document with Morph, presents
the second source as a reviewable merge, applies every reviewed item, reloads
the merged document, and renders the resulting preview without an error banner.

## Automated evidence

- Release build: 0 warnings, 0 errors.
- Test suite: 304 passed, 0 failed.
- Modern Avalonia UI script: 12 actions passed in 7.13 seconds.
- Dependency vulnerability audit: no known vulnerable direct or transitive
  packages in the solution at the time of the run.
- Visual artefacts: `ux-test-results/avalonia12-multi-resume/` (local, ignored).

The UI run uses `LUCIDRESUME_DATA_DIR` with a newly-created temporary directory,
so it cannot read or modify the user's normal application database.

## Import/parser coverage added

- Multiple roles, company/title/location headings, month-year ranges, current
  roles, achievement bullets, and skills.
- Direct Open XML parsing against `Scott_Galloway_CTO.docx` from the repository
  fixture corpus.
- Multiple separately stored résumé variants and deduplicated aggregate data.
- Merge behaviour when the local embedding model is absent or corrupt. Exact
  deterministic matching remains available; semantic matching becomes an
  optional enhancement rather than an import dependency.
- Explicit résumé summaries no longer absorb the contact header above them.

## UI harness corrections

- The modern `Mostlylucid.Avalonia.UITesting` runner now supports an
  `ImportFile` action.
- Missing fixtures and missing host hooks fail loudly.
- A failed script now exits non-zero instead of always shutting down with code
  zero.
- Import controls and the review/apply flow have stable locators.
- First-import selection state and post-merge preview source paths are retained.

## Dependency decisions

- Avalonia and its desktop, test, mobile, icon, chart, and Skia dependencies
  were moved together to the Avalonia 12-compatible line and visually tested.
- Runtime libraries were updated to current compatible releases, including
  Open XML 3.5.1, ONNX Runtime 1.30.0, Playwright 1.62.0, YamlDotNet 18.1.0,
  ReverseMarkdown 6.2.1, QuestPDF 2026.9.0, and Microsoft.Extensions patches.
- `Morph.Skia` 1.13.3 was evaluated but requires a truthful Papyrine
  SponsorCheck declaration. The repo does not claim a sponsorship or licence
  exemption it does not have. `Morph.OpenXml.Skia` 0.10.0 remains temporarily;
  see `docs/papyrine-evaluation.md`.
- Parchment was evaluated but not added: it overlaps the existing DOCX exporter
  and is not required for reversible Markdown/JobML editing.

## Remaining release decisions

- Resolve Papyrine sponsorship (or replace Morph) before treating the legacy
  preview dependency as a long-term choice.
- The Microsoft Recognizers packages and xUnit v2 are marked legacy upstream.
  Their replacement is a separate behavioural/test-runner migration, not a safe
  package-number bump.
- PDF OCR and scanned-document validation still requires the external Docling
  service and is outside this isolated fast-import run.
