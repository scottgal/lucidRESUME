# Evidence-preserving resume prose: structure, tone and related work

**Date:** 12 October 2026  
**Status:** Research spike, source audit and article-discovery pilot  
**Scope:** Contribution prose, optional interviews, tone, blog citations and related GitHub projects

## Abstract

A useful resume communicates what a person contributed, how they approached it and
what happened. Keyword overlap alone cannot supply that account. This spike proposes
a structured fact record beneath the prose, separate controls for length, detail and
expression, and small sets of relevant articles and repositories linked to their
supporting passages. The implementation direction is to reuse the complete career
transcript and refresh external sources incrementally. Neither a matching article
nor a repository technology mention establishes personal production experience.

This is a design paper with a small retrieval experiment. It does not report a hiring
outcome study, a validated NLP classifier or a completed end-to-end product feature.

## 1. The communication problem

There are three distinct failures to avoid: generic duties with no identifiable
contribution; energetic wording that exaggerates ownership or results; and a resume
whose content barely changes between different jobs. Requiring every missing field
before generation adds a fourth failure: the person cannot use the material they
already have. Generation should proceed, with an optional route to enrich the record.

The UK National Careers Service recommends describing situation, task, action and
result when explaining experience. We adapt that interview structure to short resume
passages rather than requiring four explicit headings in every bullet. This is a
design inference, not evidence that one sentence template improves hiring outcomes.
[National Careers Service: STAR](https://nationalcareers.service.gov.uk/careers-advice/interview-advice/the-star-method).

Our proposed default is **action + object + relevant decision/context + supported
result or current status**. A role header can carry context. A result can be
qualitative. An ongoing prototype should remain an ongoing prototype. If a source
does not contain an outcome, omit it and optionally ask about it.

For example, the synthetic source “I helped the team add a retry queue to the billing
service in C#; we had not measured the effect” can become “Helped add a C# retry queue
to the billing service; its effect had not yet been measured.” It cannot become
“Led a billing transformation that eliminated payment failures.”

## 2. Store the account before rewriting it

The complete transcript should retain reviewed prose and structured fields together:

| Field | Purpose |
|---|---|
| Actor and ownership | Individual, team, assisted, led; preserve the actual boundary. |
| Action and object | What changed, was built, investigated or operated. |
| Context and decision | Constraints, alternatives and the candidate's reasoning. |
| Outcome and status | Observed result, intended result, prototype, deployed, ongoing. |
| Skills and technologies | Accepted identifiers plus their actual source mentions. |
| Qualifiers | Uncertainty, negation, dates, scope and measurement limitations. |
| Provenance | Role/project ID, claim IDs, evidence IDs, passage/revision and review state. |

These fields need not all be present. Interviews should ask about the highest-value
missing field for the target role: “What did you personally implement?”, “What made
that choice difficult?” or “How did you know it worked?” Answers augment the same
role record after review. They should not create a parallel, disconnected biography.

Cache a neutral account by the source revision, selected claims, accepted skills,
model identity and writing-policy version. Cache its job-specific rendition with the
job identity and length/detail/tone preferences as well. Changing tone should not
trigger another scan of imported resumes, another GitHub import or new embeddings.

The native writer audit found that selected claims existed in the composition
manifest but its Ollama request exposed only flattened source prose. Passing the
section's claim statements, skills and evidence IDs makes that structure available
without sending unrelated roles. The accompanying prototype adds these fields and
removes a separate hard-coded 65-word summary ceiling. It retains validation and
source fallback; richer prompts do not establish factual correctness by themselves.

## 3. Where NLP helps

Semantic role labelling offers a representation of predicates and their arguments,
as illustrated by PropBank. Dependency parsing and semantic roles are plausible
ways to identify an action and its participants, then flag a passage which contains
only duties. They cannot establish whether the candidate actually performed the
action. Pronoun resolution and implied ownership remain difficult cases.
[Palmer, Gildea and Kingsbury, 2005](https://aclanthology.org/J05-1004/).

Use this as an **advisory diagnostic** over accepted transcript prose. Retain the
original passage and record extraction confidence. Missing structure can guide a
question; it should not block a resume or silently become a new claim.

For rewritten prose, check named entities, skills, numbers, dates, negation, modality,
ownership and deployment status. “Designed to reduce latency” and “reduced latency”
share most words but assert different facts. Embedding similarity is consequently
unsuitable as the sole acceptance test or as evidence of a skill.

Sentence-level natural-language inference is worth testing as a secondary warning.
SummaC demonstrates that granularity matters when detecting inconsistencies in
summaries. Its benchmark is not resume prose, so its results do not justify a
certification threshold here. Contradiction scores should supplement provenance and
explicit checks, with human review and source fallback for unresolved cases.
[Laban et al., 2022](https://aclanthology.org/2022.tacl-1.10/).

Do not add a parser or NLI model to every generation request yet. First compare its
diagnostic value and latency on a fixed set of career passages, including difficult
ownership and hypothetical-result examples.

## 4. Tone is expression, not claim strength

Style-transfer research treats preservation of meaning as a separate requirement
from changing style, and shows how an apparent style change can alter semantics.
This directly motivates bounded expression controls rather than an “impressiveness”
slider. [Krishna, Wieting and Iyyer, 2020](https://aclanthology.org/2020.emnlp-main.55/).

| Control | Changes | Must preserve |
|---|---|---|
| Length | Number of selected roles, examples and supporting links | Career chronology and selected evidence identities. |
| Detail | Depth of supported explanation per selected role | Actual contribution and known limitations. |
| Tone | Sentence rhythm, emphasis and verbal economy | Facts, ownership, uncertainty and outcome status. |

The prototype offers **Direct**, **Understated** and **Expressive**. Direct uses plain,
active phrasing. Understated reduces rhetorical emphasis. Expressive allows more
variation in rhythm and puts the supported contribution first. It must not add
superlatives, invented impact, personal enthusiasm or upgraded responsibility.

Illustrative, manually written variants of one synthetic fact:

- Understated: “Contributed a retry queue to the C# billing service; the effect had
  not yet been measured.”
- Direct: “Helped add a C# retry queue to the billing service; its effect had not
  yet been measured.”
- Expressive: “For the billing service, helped add a C# retry queue; its effect
  had not yet been measured.”

The differences are deliberately modest. Tone cannot rescue missing evidence.
Evaluation should score style intensity, content preservation and naturalness
separately, following the separation proposed by Mir and colleagues. These examples
are not model evaluation results. [Mir et al., 2019](https://aclanthology.org/N19-1049/).

## 5. Blog discovery: API and local corpus pilot

The blog source exposes these routes, checked against live responses on this date:

| Route | Observed behavior | Integration implication |
|---|---|---|
| `/search/semantic?query=…&limit=5` | JSON objects containing `slug` and `score` | Candidate retrieval only; no title or supporting passage. |
| `/blog/categories?language=en` | JSON `name` / `postCount` entries; 281 returned | Discovery facets, not a controlled skill taxonomy. |
| `/search?query=…&category=…&language=en` | HTML search; source uses hybrid search | Human fallback; not a documented JSON hybrid endpoint. |
| `/search/related/{slug}/en?limit=5` | Source enriches semantic neighbors with title/date/categories | Useful expansion, but still needs claim-relevant passages. |

The related route was inspected in source, not exercised in this pilot. Semantic
search can legitimately return an empty list when disabled; an integration must not
interpret that as evidence that no relevant articles exist. A Python urllib request
received HTTP 403 while curl succeeded, so client/header compatibility needs testing.
No authentication or server configuration was changed.

Reproduce local discovery without network access:

```sh
python3 scripts/research-publication-matching.py \
  --markdown-root /path/to/Mostlylucid/Markdown \
  --as-of 2026-10-12 \
  --query 'agent orchestration context retrieval memory tool execution state management safety evaluation observability model routing production reliability' \
  --output /tmp/article-candidates.json
```

The standalone script [research-publication-matching.py](../../scripts/research-publication-matching.py)
reads the supplied local Markdown directory. It recognizes the actual title,
`datetime` and category-comment format, excludes future-dated and translated files,
and produces deterministic candidate URLs with content hashes and two matching
passages. It does not contact the site or modify the career database. Local presence
is not proof of publication; output explicitly marks URLs unverified.

One Morena-inspired query used these terms:

```text
agent orchestration context retrieval memory tool execution state management
safety evaluation observability model routing production reliability
```

Observed pilot: 807 eligible local articles were indexed and ranked in approximately
294 ms in one run. That includes file reading and is not a production latency
benchmark. The algorithm weights query terms by corpus rarity, title matches ×3,
category matches ×2 and the two strongest prose passages ×1. There is no stemming,
synonym expansion, model call or embedding computation.

| Rank | Local lexical candidate | Live semantic candidate |
|---:|---|---|
| 1 | `why-i-dont-use-langchain` | `typescript-sdk` |
| 2 | `dotnet-geo-blocking-routing-library` | `stylobot-fingerprint` |
| 3 | `constrained-mom-mixture-of-models` | `sidecar-architecture` |
| 4 | `fire-and-dont-quite-forget-ephemeral-execution` | `stylobot-release-reliability` |
| 5 | `doomsummarizer-deep-research` | `botdetection-part2-signature-pipeline-and-stylobot-architecture` |

The lists have zero top-five overlap. This is a useful failure signal, not a ranking
winner: there are no relevance labels. “Routing” retrieves geographic routing in the
lexical baseline; the semantic list emphasizes bot infrastructure. A long blended
query obscures the requirement being supported. The next iteration should retrieve
per requirement, combine the candidate lists, then assess the actual passages.

Three local candidates have accessible live article pages: the
[agent architecture discussion](https://www.mostlylucid.net/blog/why-i-dont-use-langchain),
[model coordination discussion](https://www.mostlylucid.net/blog/constrained-mom-mixture-of-models)
and [DoomSummarizer article](https://www.mostlylucid.net/blog/doomsummarizer-deep-research).
Their existence establishes publication availability, not the validity of every
technical statement or a production deployment claim.

For the product, import and cache article metadata/passages once, keyed by canonical
URL and content hash. Categories help discovery; accepted skill aliases come from our
skill catalogue. Use semantic search to expand candidates where useful, then show
why each article matches a specific requirement. Persist approved links as publication
evidence in the complete transcript. Never make publishing an article automatically
declare every technology it mentions as a personal skill.

## 6. GitHub: concise related projects

Each resume can include a small **Related projects** section, tailored independently
of its full project narratives. Proposed presentation: linked name, one short
evidence-backed relevance phrase and optional attribution. Select two to four useful
links within the length budget; prefer fewer when evidence is weak. Avoid repeating
a project already explained at length in the same document.

Existing GitHub import models already retain README skills, observed revision,
creation/last-push dates, forks, contribution observations and engineering signals.
The assessment explicitly ranks evidence sources rather than personal proficiency.
Reuse those records. Resume generation should not crawl every repository again.

Rank by supported requirement coverage and README passage specificity first. Use
observed contributions, recency and engineering signals as tie-breakers; stars and
repository age cannot establish competence. Creation-to-last-push span is not years
of continuous personal work. A non-fork under an account is not sufficient proof
that the candidate wrote it. Unknown contributor coverage stays unknown.

Store each related link with repository ID/URL, revision, supporting passage,
requirement IDs, contribution observation and review state. The visible resume can
remain brief while JobML and the archive expose that evidence. Do not attach a
personal repository to an employer role unless the transcript establishes the
relationship. This section describes the proposed feature; this spike does not
claim it is already wired through the client and exporters.

## 7. Next experiment and acceptance criteria

Use six reviewed synthetic fact packets spanning assisted work, leadership,
prototype status, uncertain impact, absent metrics and measured results. Generate
three tones at two detail levels: 36 outputs per provider. Compare matched outputs
blind for clarity, specificity, naturalness and desired tone. Repeat selection for
two substantially different jobs without changing the facts. Record latency, model
identity, validation failures, source fallbacks and cache hits separately.

For retrieval, manually label article/repository passages against individual Morena
requirements and a contrasting job. Compare lexical, semantic and combined retrieval
at the same candidate budget. Measure precision, coverage, duplicate evidence and
unsupported personal-attribution rate. Do not promote the current unlabelled score
to an app “fit” percentage.

Provisional acceptance: no known ownership/status/number corruption in fixtures;
every emitted link resolves to its retained evidence record; relevant skills survive
rewrites; article mentions never create employment claims; generation remains
available when optional questions are unanswered. These are regression criteria,
not a guarantee about arbitrary generated text.

The immediate recommendation is to improve structured inputs and reuse first,
keep tone bounded, and run a labelled passage-level pilot before introducing another
runtime NLP model. A resume should communicate the candidate's actual work more
clearly while letting the reader follow a small number of relevant links.
