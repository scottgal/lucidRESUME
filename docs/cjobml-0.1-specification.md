# cJobML 0.1 Publication Projection

Status: Experimental Draft

Version: 0.1

Last updated: 2026-09-19

## Abstract

cJobML is the compact publication projection of a reviewed
[JobML 0.1](jobml-0.1-specification.md) document. It gives a résumé the citation
shape of a scientific paper: numbered cross-references beside human prose, a
short reference list, and an optional link to the complete evidence ledger.

cJobML is not an authoring format and not a second evidence ledger. A conforming
publisher derives it from full JobML without extraction, semantic inference, or
generation. Information needed for editing and drift reconciliation stays in the
full document.

## 1. Design goals

cJobML is designed to be:

1. compact enough to place at the end of an ordinary résumé;
2. readable as a conventional reference list;
3. deterministically parseable in one pass;
4. understandable by an unfamiliar general-purpose language model;
5. traceable to full JobML when a complete ledger is published;
6. harmless to conventional résumé parsers which do not understand cJobML.

The controlling invariant is inherited from JobML:

> A published citation MUST be a projection of an accepted evidence relationship.
> Rendering MUST NOT infer a new claim or evidence relationship.

## 2. Relationship to scientific publishing and JATS

cJobML borrows concepts from the Journal Article Tag Suite (JATS), but does not
embed JATS XML:

| JATS concept | cJobML representation |
|---|---|
| `xref` with `ref-type="bibr"` | An inline Markdown link such as `[[1]](#ref-1)` |
| `ref-list` | The final `## References` section |
| `ref` | One numbered evidence source with a stable anchor |
| `self-uri` or supplementary material | The optional `Full JobML` URI |

The analogy is semantic. cJobML remains ordinary Markdown and plain text so its
human reading order survives basic document converters and résumé parsers.

## 3. Source and authority

The inputs to a projection are:

- authoritative human Markdown;
- a valid full JobML document;
- accepted claims;
- prose evidence which resolves in the Markdown;
- external or qualification evidence already linked to those claims.

The human prose remains authoritative about what the published résumé says. Full
JobML remains authoritative about claim identity, evidence identity, review state,
drift, provenance, and concepts. cJobML is disposable and reproducible.

## 4. Document form

A cJobML publication consists of ordinary résumé Markdown with zero or more
inline cross-references, followed by one References section:

```markdown
Built an evidence-linked retrieval platform. [[1]](#ref-1)

## References

cJobML 0.1: xref [n] in prose resolves to ref [n]. Full JobML: <https://example.net/jane.jobml>.

<a id="ref-1"></a>[1] Jane Smith. “Reduced RAG.” Example Engineering Blog, 12 Apr 2025. [Article] <https://example.net/reduced-rag>.
```

The exact semantic sentence is REQUIRED:

```text
cJobML 0.1: xref [n] in prose resolves to ref [n].
```

The optional complete-ledger sentence is:

```text
Full JobML: <absolute-URI>.
```

It MAY occur on the same line as the semantic sentence.

## 5. Cross-references

The Markdown form of an inline cross-reference is:

```text
[[positive-integer]](#ref-positive-integer)
```

Rendered Word and PDF documents MAY show only `[n]`, provided the link target or
reading order still connects it to reference `n`.

Reference numbers identify evidence sources, not claims. Multiple claims which
cite the same evidence identity MUST reuse the same number. A claim which cites
several sources MAY carry several numbers.

Numbers MUST be positive, consecutive integers assigned in order of first
appearance in the prose.

## 6. References

Each reference has this Markdown shape:

```text
<a id="ref-n"></a>[n] bibliographic text [Evidence Type] <absolute-URI>.
```

The stable full-JobML evidence ID is the preferred deduplication key. When it is
absent, publishers SHOULD use the normalised absolute URI. Qualification evidence
without a URI MAY use the issuer and qualification identity.

The bibliographic text SHOULD include the available author, title, publisher, and
publication date. It MUST NOT invent missing metadata. Evidence type labels are
human-readable forms such as `Article`, `Repository`, `Project`, or
`Qualification`.

Linked posts are evidence sources. A post can support authorship, demonstrated
knowledge, or the reasoning it contains. Its presence MUST NOT silently establish
production usage, employment, proficiency, or responsibility.

## 7. Complete ledger endpoint

Full JobML MAY declare:

```yaml
document:
  complete_ledger: https://example.net/jane.jobml
```

The URI MUST be absolute. A publisher copies it into the compact preamble without
fetching or interpreting it. The endpoint SHOULD return the full JobML artifact
or a content-negotiated equivalent which preserves all claim and evidence IDs.

The endpoint is optional. A cJobML document remains parseable without network
access or a complete ledger.

## 8. Deliberate omissions

cJobML MUST NOT reproduce these full-resolution editing fields:

- quoted source passages;
- structural prose selectors;
- fingerprints or checksums;
- drift and reconciliation state;
- inference method or processor provenance;
- review history;
- concept graphs, aliases, and semantic vectors.

Their omission is the compression mechanism. Consumers needing those details
follow the complete-ledger link when one is available.

## 9. Projection algorithm

A conforming publisher performs these deterministic steps:

1. read accepted claims from the full JobML document;
2. resolve each claim's in-document prose evidence;
3. select only external or qualification evidence already attached to that claim;
4. deduplicate sources by stable evidence identity, then normalised URI;
5. assign numbers in first-prose-appearance order;
6. append linked markers to the resolved prose spans;
7. render the semantic preamble, optional complete-ledger URI, and references.

A publisher MUST NOT run NER, an LLM, embedding search, concept matching, or claim
extraction during these steps. If an accepted prose reference cannot be resolved,
the publication workflow MUST report it rather than guessing a new target.

## 10. Parser conformance

A conforming cJobML parser MUST:

- find the final References section;
- verify the exact semantic sentence;
- parse numbered cross-references and numbered references;
- reject duplicate reference numbers;
- reject a cross-reference without a matching reference;
- expose each reference's number, citation text, evidence type, and URI when present;
- expose the optional complete-ledger URI.

A parser SHOULD complete those operations in one forward pass after locating the
References section. It does not need the full JobML schema.

## 11. Semantic obviousness

Given only the published résumé, without this specification or a JobML-specific
system prompt, a capable general-purpose language model SHOULD recover:

- the prose claim carrying a citation;
- the reference number which supports it;
- the evidence source and URI;
- the complete-ledger URI when present.

This cold-parser test supplements deterministic parsing. It does not replace it.

## 12. Compatibility evidence

The lucidRESUME test fixture is exported to Markdown, DOCX, and PDF, then checked
for identical prose, markers, references, and ledger URI. The PDF has also been
submitted through the open-source [OpenResume](https://github.com/xitanggg/open-resume)
browser parser. OpenResume recovered the normal résumé fields, inline marker,
reference content, and URLs. Its fixed schema has no References category, so it
classified the unfamiliar section as project content.

That result demonstrates content survival through one inspectable ATS-style
parser. It is not a claim of universal compatibility with proprietary ATS
products.

## 13. Privacy and security

Compact references are public résumé content. Publishers MUST NOT expose private
repository URLs, credentials, private ledger endpoints, or evidence not selected
for publication. A checksum in full JobML is a drift detector unless explicitly
defined otherwise; cJobML contains no integrity or authenticity claim.

## Appendix A. Minimal valid publication

```markdown
# Jane Smith

## Experience

Built an evidence-linked retrieval platform. [[1]](#ref-1)

## References

cJobML 0.1: xref [n] in prose resolves to ref [n].

<a id="ref-1"></a>[1] Jane Smith. “Reduced RAG.” [Article] <https://example.net/reduced-rag>.
```
