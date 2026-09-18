# Papyrine ecosystem evaluation

Reviewed 2026-09-16 while updating the DOCX preview pipeline.

## Decision

lucidRESUME currently uses `Morph.OpenXml.Skia` 0.10.0 for optional, in-process
DOCX previews. The package is deprecated upstream, but remains the newest legacy
release that can build without asserting a Papyrine sponsorship state.

The supported successor, `Morph.Skia` 1.13.3, was restored and evaluated. Its
transitive `Morph` build targets deliberately fail with SponsorCheck `SC021`
until a consuming organisation supplies one of the publisher's recognised
sponsorship properties. This repository must not use
`Papyrine_SponsorshipLicenseIgnored`; doing so explicitly accepts a licence
breach.

Before adopting Morph 1.x, the maintainer should review the current Papyrine
OSMF terms, select the applicable sponsorship/exemption route, and configure the
real property outside source control where appropriate.

## Related projects

- **Morph / Morph.Skia:** desirable successor for DOCX rendering once licensing
  is resolved.
- **Parchment:** useful for generating Word documents from DOCX or Markdown
  templates, but it duplicates lucidRESUME's existing Open XML exporter and is
  outside JobML 0.1's import/editor scope. Do not add it without a concrete
  template-generation requirement and the same licensing review.
- **OpenXmlHtml:** potentially useful for richer editable preview/export later;
  not needed for the reversible Markdown + JobML source-of-truth workflow.

Upstream: <https://github.com/Papyrine>
