# Source comparison and job-response writing

## Method

Reviewed source text, extracted fields and generated passages as the comparator,
then converted concrete findings into annotated probes and regression checks.
The reviewing coding agent authored the expectations; these are diagnostic cases,
not independent human ratings or a held-out accuracy benchmark.

Runs use the application's actual parser, recovery, selection, composition and
validation paths. Synthetic exchanges, raw replies, output resumes and comparison
reports are retained under ignored `artifacts/quality-loop-2026-10-19/`. The live
career database is accessed read only. Private source passages stay in local
artifacts; provider request capture is used only for synthetic inputs.

## Ingestion findings and changes

| Source issue | Change |
| --- | --- |
| Short evidence such as “Built C# APIs.” disappeared | Retain explicit short bullets |
| Wrapped “not yet deployed” was detached or lost | Join indented continuations to their source account |
| “About the job” became the job title | Recover explicit hiring-role statements before considering generic page headings |
| A flattened “at Remote. We need …” header became an employer paragraph | Separate the first header sentence and recognise the remote arrangement |
| “Preferably 10+ years” became mandatory tenure | Use section and local qualifiers for both skill priority and tenure extraction |
| A negated domain sentence became a complete required skill | Exclude negative requirement statements before classification |
| Docker and Docker. became separate items | Normalise trailing punctuation before audited alias deduplication |
| Writer retrieval rediscovered benefits and optional demands | Share source section boundaries across ingestion, writing plans, retrieval terms, writing briefs and automatic budgets |
| Extractor added Python, C# or vacancy-only tools | Share literal extraction prompts across providers, use deterministic local decoding, validate proposed terms against positive candidate assertions and recover explicitly named vocabulary |
| Per-document name confidence lived on a shared parser instance | Pass confidence with the document and its asynchronous enhancement task |

Recovery distinguishes ASP.NET from ASP.NET Core/MVC and never infers C# from
.NET. Recognised source mentions missed by the model can be recovered literally.
Quoted target-job requirements and planned training are excluded from that
recovery context. Suggestions retain the LLM extraction review boundary; source
validation does not silently grant proficiency or author acceptance.

The source comparator now passes 40 checks across eight cases. Its first recorded
run, after the short-bullet and wrapped-prose fixes, still exposed two failures:
generic role identity and a negated domain leaking through a structural skill.
Both are fixed.

Three local extraction probes used qwen3.5:0.8b. The initial raw replies had seven
unexpected terms and one omission. Prompt tuning reduced those errors but did
not eliminate them: the final raw replies still had one omission and one
invention. The validated recovery results had zero missing or unexpected terms
against the six expected entries across those three probes. Raw and recovered
results are recorded separately. This small set establishes the specific fixes,
not general extraction accuracy.

## Writing findings and changes

The initial qwen3.5:4b comparison covered six evidence shapes at two tone/detail
settings. Review found a factual failure that existing validation accepted:

> Asked to stabilise an internal support agent after its pilot.

became:

> Stabilised an internal support agent post-pilot …

The assigned objective was presented as achieved. Composition now preserves the
assignment distinction, and the comparator records this failure explicitly. A
second check keeps a test limitation beside its experiment when reordering would
make “the test” refer ambiguously to a separate pilot.

Shared writing instructions now ask the writer to identify the supported
contribution answering the vacancy, lead with that contribution, connect related
details, retain clear source sentences and avoid copying the advert's claims.
They explicitly distinguish integrations from KYB/AML experience and mentoring
from formal performance management. The ledger is described as accepted and
source-linked, rather than independently verified. Expressive tone no longer
forces a purpose opening.

Selection also matters: role accounts now introduce directly relevant technical
work before supporting process detail. The AI leadership example opens with the
C# agent runtime before its evaluation cases. The database example selects query
performance and recovery evidence, and its summary now says:

> Hands-on software engineer with experience designing and optimising PostgreSQL queries.

This replaces “designing PostgreSQL reporting queries and optimising queries”.
The shared-object reduction is limited to compatible source phrases; distinct
internal and customer-facing objects remain separate.

The actual advert comparison also showed a broad authored introduction displacing
the primary job evidence. Summaries now lead with supported role-specific
strengths when that introduction misses the main technical focus, then retain a
relevant authored secondary account within the word budget. Both sources remain
bound to the summary. Repeated identity sentences are omitted while qualifying
negative statements remain intact. The Kyckr output now opens with .NET, SQL
Server, Azure and Service Bus before the authored agentic-product account.

The actual Kyckr run exposed a spelling-related false rejection: “distributed”
was flagged as an invented vacancy term because the source said “distrubted”.
A small reviewed ordinary-word map now normalises those comparisons and corrects
rendered spelling even when a substantive rewrite is rejected. Original source
text, company names, product identifiers, metrics and claim identities remain
unchanged. The final run removes all five reviewed misspellings that occurred in
the generated draft.

The 12-output comparison went from one annotated factual failure to zero.
Seven baseline outputs retained source wording; eight after-tuning outputs did
so. These counts are recorded as editorial outcomes, not successful rewrites.
Two additional rough-platform passages corrected “distrubted” and “applicaiton”
and linked telemetry work to its purpose while retaining “Tasked to reduce running
costs”, rather than inventing a measured reduction. A targeted two-output rerun
kept experiment caveats adjacent to the correct test.

Both full-career projections retained source roles and evidence bindings,
selected different evidence for the contrasting jobs, and passed the existing
career comparison checks. The fixture uses lexical fallback and native full-text
search; it does not establish vector-model quality.

## Validation and limits

The solution passed 790 tests. Native intake and ledger review passed at
1200×800 and 900×600. The source-comparison command passes all 40 checks.

The local Kyckr comparison is saved as
`artifacts/quality-loop-2026-10-19/kyckr-final.md`. It retains the
Microsoft platform contribution, including ClientID and CSS features, and makes
several engineering accounts more direct. Its summary now puts the primary
engineering evidence first. Lengthy source passages can still remain wordy when
a rewrite loses important facts. The
diagnostic output is not a replacement for the earlier manually reviewed
application resume. This observation is kept alongside the successful probes
rather than treating a clean validator result as a fluent final application.

These rules provide conservative local checks, not general semantic entailment.
Negation and quoted-job boundaries are heuristic, extraction prompts remain
bounded, and long source passages can still require review. Hosted writing
providers receive the shared prompt changes but were not called in this local
comparison. Small-model latency and source fallback remain visible in the run
reports. No hiring outcome or universal preference for the generated prose is
claimed.

Reproduce the loop with the commands in
[the evaluation tool](../tools/lucidRESUME.ProseEvaluation/README.md). Keep the
source annotations, raw model results and editorial judgments separate; fix a
shared application path, then rerun the affected cases and contrasting full
careers before broadening the claim.
