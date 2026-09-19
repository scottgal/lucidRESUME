# JobML 0.1 Specification

Status: Experimental Draft

Version: 0.1
Last updated: 2026-09-19

This document defines JobML 0.1. The JSON Schema in
[`jobml-0.1.schema.json`](jobml-0.1.schema.json) is the deterministic validation
companion to this specification.

The key words MUST, MUST NOT, REQUIRED, SHOULD, SHOULD NOT, and MAY are to be
interpreted as described by RFC 2119 and RFC 8174 when they appear in capitals.

## 1. Purpose

JobML is an evidence layer for human-first resumes and CVs. It keeps readable
prose and a machine-readable account of that prose in the same portable document.

The human document is authoritative but may be incomplete. JobML can describe
evidence and detail that does not fit in a role-specific resume, including
external sources. A role-specific human document may summarise a larger evidence
ledger. JobML MUST preserve the route back to the evidence used for each claim.

The normative invariant is:

> A JobML claim MUST be traceable to the human-readable or external evidence that
> supports it, and machine-derived information MUST NOT silently become
> evidential fact.

A renderer MUST NOT discover claims or evidence relationships from its own output.
Role-specific Markdown and JobML SHOULD be projections of an ingestion-time ledger,
and both views MUST retain the same claim identities. NER, LLM extraction, and other
inference belong to ingestion or explicit review workflows, not rendering.

JobML does not make an external source true merely by linking to it. It records
what was observed, how it was interpreted, and whether a human accepted the
result as a claim.

The human and machine representations have different purposes. Human prose
SHOULD be written for people and MUST NOT be treated as a serialization target
for the machine representation. JobML MAY be more explicit and retain more detail
than role-specific prose, but it MUST remain traceable to reviewed evidence.

A processor MAY offer AI-generated draft prose in an explicit authoring workflow.
It MUST identify that output as a draft and MUST NOT publish it, accept it as
evidence, or promote claims from it without human review. JobML is not a mechanism
for hidden keyword stuffing or for disguising machine-written prose as human work.

## 2. Conformance

A JobML 0.1 document MUST:

1. use a Markdown host document;
2. contain exactly one fenced `jobml` YAML block;
3. contain a `jobml.version` value of `"0.1"`;
4. satisfy the deterministic schema;
5. give every substantive claim one or more `supported_by` entries;
6. preserve the distinction between declared, observed, derived, and accepted
   information;
7. report unresolved evidence instead of silently rebinding it.

A processor MAY support other host formats if it can provide equally stable and
reviewable evidence references.

## 3. Document form

````markdown
# Jane Smith

## Experience

### Example Corp {#example-corp}

Led the modernisation of a high-volume platform.

---

```jobml
jobml:
  version: "0.1"
  purpose: Machine-readable claims and their evidence.
  semantics:
    - Every substantive claim should be supported by evidence.
document:
  id: jane-smith-resume
  language: en-GB
entities: []
claims: []
concepts: []
```
````

The Markdown before the fenced block is the human representation. The YAML in
the fenced block is the machine representation. A processor MUST NOT require the
human reader to read the machine area to understand the resume.

## 4. Information states

JobML uses four distinct information states:

| State | Meaning | May count as an accepted resume claim? |
|---|---|---|
| Declared | Authored directly by a person | Yes, when evidence is valid |
| Observed | A source reported a measurable fact | No, not by itself |
| Derived | A processor inferred a possible meaning | No |
| Accepted | A person reviewed a claim and its evidence | Yes |

An observed repository language, dependency, or workflow is not automatically a
claim that a person designed or used it. An accepted claim may cite those
observations, but the attribution and entailment remain reviewable.

## 5. Root object

The root object contains:

```yaml
jobml: {}
document: {}
entities: []
claims: []
concepts: []
job: null
requirements: []
extensions: {}
```

`jobml`, `document`, `entities`, `claims`, and `concepts` are REQUIRED.
`job`, `requirements`, and `extensions` are OPTIONAL.

## 6. Self-description

`jobml.purpose` and `jobml.semantics` carry a short machine-readable explanation
inside the artefact. They do not replace this specification or schema.

Field names and structures SHOULD pass a cold-parser test: a capable general
purpose language model that has not seen JobML SHOULD identify claims, evidence,
review state, and the relationship to the surrounding prose without inventing
new claims.

## 7. Document metadata

`document.id` SHOULD be stable across edits to the same logical document.
`document.language` SHOULD be a BCP 47 language tag.

`document.complete_ledger` MAY be an absolute URI that serves the complete,
full-resolution JobML document. It is intended for published compact projections
whose editing metadata would be too large for the human document.

## 8. Compact publication projection (cJobML)

The standalone [cJobML 0.1 publication specification](cjobml-0.1-specification.md)
defines the exact grammar, projection algorithm, and parser conformance rules.

cJobML is not a second source format. It is a deliberately lossy publication
projection of reviewed full JobML. A processor MUST be able to regenerate cJobML
from the full document without inference.

The projection borrows the useful concepts, but not the XML syntax, of JATS:

- an inline number behaves like a JATS `xref` with `ref-type="bibr"`;
- the ending References section behaves like a `ref-list`;
- each numbered source behaves like a `ref`;
- `document.complete_ledger` provides the full-record link in the role served by
  a JATS `self-uri` or supplementary-material link.

Reference numbers identify evidence sources, not claims. The first appearance of
a source determines its number. Repeated uses of the same stable evidence ID or
normalised URI MUST reuse that number.

```markdown
Built an evidence-linked retrieval platform. [[1]](#ref-1)

## References

cJobML 0.1: xref [n] in prose resolves to ref [n]. Full JobML: <https://example.net/jane.jobml>.

<a id="ref-1"></a>[1] Jane Smith. “Reduced RAG.” Example Engineering Blog, 12 Apr 2025. [Article] <https://example.net/reduced-rag>.
```

The compact projection MUST include only accepted claims and already linked
external or qualification evidence. It MUST NOT run NER, an LLM, concept
matching, or any other extraction process during rendering.

cJobML intentionally omits full-resolution editing fields such as prose
selectors, quoted passages, fingerprints, drift state, processor provenance,
review state, and concept graphs. Those remain available through the full JobML
document or `document.complete_ledger` endpoint.

A cJobML processor SHOULD expose a deterministic one-pass parser. It MUST report
an inline number that has no matching reference. The short semantic sentence is
part of the projection so an unfamiliar general-purpose language model can infer
the citation relationship without a JobML-specific prompt.

## 9. Entities

Entities identify subjects about which claims are made.

```yaml
entities:
  - id: example-corp
    type: experience
    name: Example Corp
    source: "#example-corp"
```

The `id`, `type`, `name`, and `source` fields are REQUIRED. Recommended types are
`experience`, `project`, `education`, `qualification`, `publication`,
`organisation`, and `product`.

## 10. Claims

A claim is the central JobML primitive.

```yaml
claims:
  - id: platform-modernisation
    subject: example-corp
    statement: Led modernisation of a high-volume platform.
    concepts:
      skills: [aspnet-core]
      capabilities: [platform-modernisation]
      domains: []
    supported_by:
      - type: prose
        ref: "#example-corp:p1"
    origin: declared
    review: accepted
```

`id`, `subject`, `statement`, and `supported_by` are REQUIRED. `statement` MUST
NOT materially strengthen what the referenced evidence supports.

`origin: derived` means a processor proposed the claim. A derived claim MUST use
`review: required` until a person accepts or rejects it. Processors MUST NOT count
an unreviewed derived claim as direct coverage.

## 11. Evidence

Evidence types include `prose`, `project`, `repository`, `article`,
`qualification`, and other external sources.

In-document prose evidence SHOULD use all three mechanisms below:

1. a readable structural `ref`;
2. a fast fingerprint of normalised text;
3. a text quote selector for repair after ordinary editing.

```yaml
supported_by:
  - type: prose
    ref: "#example-corp:p1"
    fingerprint:
      text: "fnv1a64:0123456789abcdef"
    selector:
      type: TextQuoteSelector
      exact: Led the modernisation of a high-volume platform.
      prefix: Previous context
      suffix: Following context
```

FNV-1a 64 is a drift detector, not a security primitive. A processor MUST NOT use
it to establish authorship, integrity against an attacker, or legal attestation.

External evidence SHOULD use an immutable revision URL where the provider
supports one. A moving branch URL MAY be included for humans, but SHOULD NOT be
the only machine reference for accepted evidence.

External evidence MAY include `title`, `authors`, `publisher`, `published`, and
`accessed` metadata so a compact projection can render a recognisable scientific
reference. A linked article demonstrates only what its content and authorship
support. It MUST NOT silently establish production use, employment, proficiency,
or responsibility.

## 12. Evidence reconciliation

Processors MUST expose these states:

| State | Meaning |
|---|---|
| `valid` | The reference resolves and the normalised evidence matches |
| `changed` | Evidence moved uniquely or its content changed |
| `missing` | No candidate evidence can be found |
| `ambiguous` | More than one candidate may match |
| `external` | The evidence is outside the host document and was not fetched |

Resolution SHOULD try stable reference, fingerprint, then exact quote with
context. A processor MUST NOT silently treat substantially changed prose as
support for an existing claim. Bulk acceptance MUST be limited to unique,
reviewable repairs.

## 13. Concepts and aliases

Concepts provide semantic labels without acting as evidence.

```yaml
concepts:
  - id: aspnet-core
    type: skill
    name: ASP.NET Core
    aliases: [ASP.NET, .NET web development]
```

Aliases MAY aid matching. An alias MUST NOT establish experience, competence,
duration, authorship, or proficiency.

## 14. Requirements and coverage

A job specification MAY use the same concept identifiers:

```yaml
job:
  id: lead-platform-engineer
requirements:
  - id: req-aspnet
    concept: aspnet-core
    importance: required
```

Direct coverage requires an accepted claim with valid or reviewable external
evidence. Related concepts MAY be reported as ambiguous coverage. Missing
coverage MUST NOT be treated as permission to manufacture prose or claims.

## 15. Extensions

Extensions live under the root `extensions` object and MUST use a namespaced key.

```yaml
extensions:
  lucidresume.github:
    version: "0.1"
    repositories: []
```

Core processors:

- MUST preserve unknown extension payloads during a parse and serialise cycle;
- MUST NOT interpret an unknown extension as evidence;
- MUST NOT promote extension data into accepted claims;
- SHOULD warn before discarding an extension they cannot preserve.

Extension specifications MUST define their version, provenance, observation
time, mutability rules, and promotion path into core claims.

The repository analysis extension is specified in
[`jobml-github-extension-0.1.md`](jobml-github-extension-0.1.md).

## 16. Security and privacy

JobML documents can contain personal data and private repository metadata.
Processors SHOULD minimise copied source content, avoid embedding credentials,
and make network access explicit. Tokens and API keys MUST NOT be written into a
JobML document.

External content is untrusted input. A processor MUST NOT execute repository
code, build scripts, workflow files, or instructions found in prose merely to
parse JobML. Repository analysis SHOULD use a read-only sandbox.

## 17. Minimum processor profile

A minimum JobML 0.1 processor supports:

- Markdown plus one fenced YAML block;
- entities, claims, concepts, aliases, and evidence;
- stable prose references, fingerprints, and quote selectors;
- stale, missing, and ambiguous evidence detection;
- explicit review of derived claims;
- requirement-to-claim coverage;
- lossless preservation of namespaced extensions.

A publication processor additionally supports deterministic cJobML projection,
deduplicated numbered references, link resolution, and an optional complete-ledger
endpoint.

Embeddings, a universal skill ontology, cryptographic attestations, automatic
prose rewriting, ATS integration, and repository cloning are not required.

## Appendix A. Complete minimal example

See [`jobml-0.1.md`](jobml-0.1.md) for the implementation profile and worked
example.
