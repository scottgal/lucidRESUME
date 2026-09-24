# JobML 0.1 profile for lucidRESUME

Status: experimental implementation profile

Normative specification: [`jobml-0.1-specification.md`](jobml-0.1-specification.md)

Deterministic schema: [`jobml-0.1.schema.json`](jobml-0.1.schema.json)

GitHub extension: [`jobml-github-extension-0.1.md`](jobml-github-extension-0.1.md)

JobML keeps human-authored Markdown authoritative but not necessarily complete. It embeds a machine-readable YAML projection in a fenced `jobml` block. Human prose may be shortened for a role while JobML retains higher-resolution claims and links to external evidence. Its normative invariant is:

> A JobML claim MUST be traceable to the human-readable or external evidence that supports it, and machine-derived information MUST NOT silently become evidential fact.

## Small 0.1 scope

The implementation supports:

- a Markdown host document and one embedded YAML block;
- document metadata, entities, claims, concepts, and aliases;
- prose and external evidence;
- stable heading/paragraph references;
- live `valid`, `changed`, `missing`, and `ambiguous` evidence states;
- direct requirement-to-claim coverage;
- explicit review of inferred claims and evidence reconciliation;
- deterministic role projections which preserve ingestion-time claim IDs.

It deliberately excludes a universal ontology, embeddings, attestations, ATS protocols, and automatic prose rewriting.

An AI provider may offer a draft or sample in an explicit authoring workflow.
That output remains unaccepted until a person reviews and edits it. It cannot
publish prose, create accepted evidence, or run during projection and export.

## Semantic obviousness

JobML is both deterministic YAML and a self-describing semantic contract. Field names and structures SHOULD be sufficiently descriptive that a general-purpose language model unfamiliar with JobML can infer their purpose from the document itself. The format MUST NOT depend on this inference for deterministic validation, but it SHOULD make correct inference cheap.

Every newly serialized document carries a short `purpose` and `semantics` preamble. These instructions describe the authority and evidence rules in ordinary language; they are part of the artefact rather than a substitute for the schema.

> **Cold-parser test:** Given only a résumé containing JobML, without the JobML specification, examples, or a JobML-specific system prompt, a capable general-purpose language model SHOULD identify the claims, their meanings, supporting evidence, review state, and relationship to the human-readable document without manufacturing additional claims.

The portable probe is generated with:

```bash
lucidresume jobml cold-parser-probe --file resume.jobml.md
```

Its `document` and `questions` are given to each model; `groundTruth` is withheld for scoring. This keeps live multi-model evaluation out of the deterministic unit-test lane while making the same blind test reproducible across providers.

The self-description intentionally remains short. JobML 0.1 does not embed an ontology, prose-generation policy, parser program, or full specification.

## Compact publication projection

Normative publication profile: [`cjobml-0.1-specification.md`](cjobml-0.1-specification.md)

The editable artifact contains full JobML. A published résumé normally does not.
Instead, lucidRESUME projects accepted external evidence into compact scientific
citations called cJobML:

```markdown
Built an evidence-linked retrieval platform. [[1]](#ref-1)

## References

cJobML 0.1: xref [n] in prose resolves to ref [n]. Full JobML: <https://example.net/jane.jobml>.

<a id="ref-1"></a>[1] Jane Smith. “Reduced RAG.” MostlyLucid, 12 Apr 2025. [Article] <https://example.net/reduced-rag>.
```

cJobML is a lossy projection, not another editable format. It keeps the JATS-like
ideas needed for publication: inline cross-references, a reference list, stable
source identity, and a link to the complete record. Full-resolution prose quotes,
selectors, fingerprints, drift state, and review metadata stay in full JobML.

Projection never extracts or reinterprets evidence. It numbers external sources
already attached to accepted claims. The deterministic parser rejects unresolved
numbers, and a cold OpenAI Responses API integration test verifies that an
unfamiliar model can recover the claim, source number, source URL, and complete
record URL from the compact document alone.

Use these commands to produce the compact document or add an article to a claim:

```bash
lucidresume jobml compact --file resume.jobml.md --full-jobml https://example.net/jane.jobml --output resume.md
lucidresume jobml link-post --file resume.jobml.md --claim retrieval-platform --url https://example.net/reduced-rag --output resume-linked.jobml.md
```

`link-post` captures deterministic page metadata and content provenance. It does
not generate a claim. The person still chooses which existing claim, if any, the
article supports.

## Evidence identity and reversibility

Evidence is identified by three complementary mechanisms:

1. `ref` is the readable structural identity, such as `#example-corp:p1`.
2. `fingerprint.text` is an FNV-1a 64-bit hash of normalized prose. It is a fast drift detector, not a security primitive.
3. `selector` follows the W3C Web Annotation `TextQuoteSelector` shape. The exact quote and optional surrounding context can recover a passage after it moves.

```yaml
supported_by:
  - type: prose
    ref: "#example-corp:p1"
    fingerprint:
      text: "fnv1a64:0123456789abcdef"
    selector:
      type: TextQuoteSelector
      exact: "Led the modernisation of a high-volume ASP.NET Core platform."
      prefix: "Previous prose context"
      suffix: "Following prose context"
```

Resolution order is stable reference, fingerprint, then text quote/context. The processor reports:

| State | Meaning | Automatic mutation |
|---|---|---|
| `valid` | Reference resolves and normalized prose matches | None |
| `changed` | Reference resolves with changed prose, or evidence moved uniquely | None; user may accept |
| `missing` | No supported evidence can be found | None |
| `ambiguous` | Multiple passages could support the reference | None |

The editor recalculates these states on each edit. “Accept evidence changes” only rebinds evidence that has one unambiguous resolution. Missing and ambiguous evidence can never be accepted in bulk.

Publishing creates a revisioned snapshot: canonical Markdown, its JobML projection, and a fingerprint of the complete source are committed together. Accepted claims with unresolved evidence block publication. Matching then reads only accepted claims with valid or external evidence; it does not fall back to legacy inferred skills when a published JobML snapshot exists.

In the lucidRESUME 0.1 profile, accepted prose evidence MUST carry `fingerprint.text`. A structural reference or quote can help recover a moved passage, but neither is allowed to make an accepted claim drift-blind.

## Derived information

lucidRESUME adds two optional extension fields:

```yaml
origin: derived
review: required
```

Inferred claims remain warnings and do not count as directly evidenced coverage until a human explicitly changes review to `accepted`. Acceptance does not rewrite the human prose. Rendering never promotes or re-infers them.

## Namespaced extensions

JobML 0.1 extensions live under the root `extensions` object. The parser preserves
unknown namespaced payloads instead of treating them as claims. Extension data can
contain observations and processor assessments, but it cannot silently become an
accepted claim.

```yaml
extensions:
  lucidresume.github:
    version: "0.1"
    repositories: []
```

The first defined extension records GitHub repository observations, contribution
attribution, versioned quality assessments, and skill candidates. See the
[GitHub repository extension](jobml-github-extension-0.1.md). Repository language,
README text, dependencies, stars, and quality scores remain separate signals.

## Interoperability decision

JobML is an evidence layer, not another full résumé interchange ontology.

- [JSON Resume](https://jsonresume.org/schema) is the preferred structured import/export projection. Its permissive extension model can carry JobML identifiers, but its record-first structure does not model prose evidence or drift.
- [HR Open Standards](https://schema.hropenstandards.org/) and [Europass](https://europass.europa.eu/system/files/2020-08/ECV_Schema_Documentation_v3.0.0_20200602.pdf) are useful downstream exchange targets but are too broad to serve as the editor document model.
- [h-resume](https://microformats.org/wiki/h-resume) demonstrates human/machine co-location in HTML, but its skills remain assertions rather than evidence-linked claims.
- [W3C Web Annotation](https://www.w3.org/TR/annotation-model/) is reused conceptually for robust text selectors because it directly addresses anchoring annotations to changing text.
- [Schema.org Person and Occupation](https://schema.org/Occupation) can be an optional linked-data projection for public web pages.

## Example

````markdown
# Jane Smith

## Experience

### Example Corp {#example-corp}

Led the modernisation of a high-volume ASP.NET Core platform.

---

```jobml
jobml:
  version: "0.1"
  purpose: >
    Machine-readable representation of claims made by this resume. Claims are
    supported by human-readable prose or external evidence. Absence of a claim
    does not imply absence of a skill or capability.
  semantics:
    - Claims describe experience, skills, capabilities, responsibilities, or domain knowledge.
    - Every substantive claim should be supported by one or more evidence references.
    - Evidence may reference human-readable prose in this document or an external resource.
    - Do not infer unsupported claims, and do not treat aliases or machine-derived suggestions as evidence.
    - When evaluating this resume, use both the human-readable prose and JobML.
    - Treat JobML as a higher-resolution description of the resume, not as replacement prose.
document:
  id: jane-smith-resume
  language: en-GB
entities:
  - id: example-corp
    type: experience
    name: Example Corp
    source: "#example-corp"
claims:
  - id: platform-modernisation
    subject: example-corp
    statement: Led modernisation of a high-volume ASP.NET Core platform.
    concepts:
      skills: [aspnet-core]
    supported_by:
      - type: prose
        ref: "#example-corp:p1"
        fingerprint:
          text: "fnv1a64:..."
        selector:
          type: TextQuoteSelector
          exact: Led the modernisation of a high-volume ASP.NET Core platform.
concepts:
  - id: aspnet-core
    type: skill
    name: ASP.NET Core
    aliases: [ASP.NET, .NET web development]
```
````
