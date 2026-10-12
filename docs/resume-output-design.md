# Resume output and template design

## End-to-end output

### October 2026 pagination and prose controls

Native projection now exposes saved-job selection, length, detail and bounded tone.
Automatic length scales its evidence budget with distinct requirements; Extended
permits more supported roles and examples. These preferences are archived with the
projection. Tone changes phrasing only when the optional prose editor runs.

Word exports use explicit A4 geometry and natural pagination. Role headers and tools
stay with the beginning of the account, while later bullets may flow onto another
page. PDF permits the same role flow. New native projections have no minimum-page
quota. Older PDF artifacts retain their legacy minimum-page preference.

Regression on the reported private DOCX preserved all 678 words in exactly the same
order. LibreOffice rendered the old version as three pages (296, 61 and 332 extracted
words), and the updated exporter as two pages (357 and 332 extracted words). Rendered
word counts include layout-dependent extraction differences; the Word XML text
comparison establishes content preservation. The original Desktop file was not
modified. Historical page counts later in this document describe earlier builds.

The [research spike](research/2026-10-12-evidence-preserving-prose.md) records the NLP,
tone, publication retrieval and concise related-project proposals separately from
implemented behavior.

### Projection workflow

The supported role-specific document path is:

1. Import one or more resume documents and LinkedIn data.
2. Import GitHub repositories and their public evidence.
3. Review the merge so uncertain facts do not enter the profile silently.
4. Add a job and choose Project Resume, select a saved job, or paste an advert into Project.
5. Choose length, detail, tone and an output template, then project the relevant accepted ledger records.
6. Review the human-owned prose and its JobML evidence links.
7. Export the same projection as Markdown, DOCX, or PDF.

Ingestion may use deterministic parsers, NER, or an optional LLM to propose ledger
records. Inferred records remain review-required. An optional AI authoring step may
also offer a clearly labelled prose draft or sample. It does not publish prose or
create accepted evidence. A person must review and edit it.

Selection builds a source-bound projection. Optional prose editing then runs two
bounded passes over selected passages; it cannot add ledger claims. Preview and
export use that reviewed projection without another model call or provenance
inference. Prose and cJobML references are emitted together from one
revisioned projection whose blocks already carry ledger claim IDs. Full JobML may
retain more detail than the role-specific prose, but every claim must still trace
to reviewed human or external evidence.

The detailed experience list is intentionally selective. Omitted accepted roles
lasting strictly more than three months appear as single-line entries under
`Additional consulting, contract and earlier experience`. The compiler derives
their titles, employers, dates, evidence links, and order directly from the
ledger. It neither summarizes nor sends these lines to an editing model. Contracts
of three months or less remain available through the full career-record endpoint
without consuming space in the role-specific resume.

This is not an automated application path and it is not detector evasion. The
goal is a clean separation: natural human writing for people, and explicit,
verifiable JobML for ATS and AI systems.

If the source ledger changes after projection, export fails until the role-specific
projection is rebuilt. This prevents a visually plausible document from carrying
stale evidence links.

The portable artifact uses inline reference markers and ends with:

```text
References
  cJobML one-line semantic contract
  optional complete JobML endpoint
  numbered external evidence
```

The DOCX and PDF exports render the same compact references as Markdown. The
full selectors, quoted evidence, fingerprints, reconciliation state, and review
metadata remain in the editable JobML source. cJobML is a publication projection,
not another source format.

## Template research

The templates are original layouts based on the following findings, not copies of
third-party designs:

- Arnulf, Tegner and Larssen found formal layouts were preferred to creative layouts
  for otherwise equivalent candidates: https://doi.org/10.1080/13594320902903613
- Wingate, Robie, Powell and Bourdage found clearer, more detailed, better-structured
  applications were associated with better job-search outcomes in their sample:
  https://doi.org/10.1111/ijsa.70022
- MIT Career Advising recommends concise, easy-to-scan, reverse-chronological resumes
  tailored to the role: https://capd.mit.edu/resources/resumes/

Those sources do not establish one perfect template. They support the conservative
choices shared by all three templates:

- one reading order and no sidebars;
- conventional section headings;
- restrained typography and colour;
- text contacts instead of icons;
- selectable density without changing content semantics;
- the same content model for PDF and Word.

## Included templates

| Template | Intended use |
|---|---|
| ATS Classic | Conservative default with generous spacing and minimal colour. |
| Modern Professional | Restrained blue accent and balanced density. |
| Compact Technical | Smaller margins and type for evidence-heavy technical careers. |

The templates are presentation choices. They never change claims, evidence, JobML,
or requirement coverage.

## ATS-safe document structure

Both office formats use the same conservative structure:

- candidate identity occurs once, in the page-one body;
- the advertised title is labelled `Target role` rather than presented as a role the
  candidate already holds;
- role, employer, location and dates have a stable delimited order;
- achievement bullets are semantic lists, not typed bullet glyphs;
- PDF output carries a PDF/UA-1 structure tree;
- DOCX output uses named heading styles and Word numbering;
- no tables, sidebars, text boxes, repeated headers or page counters carry résumé data.

The evidence-linked form remains the default. A cJobML reference section is visible
publication content, and compatible systems can follow its full JobML endpoint when
one is configured. Unknown ATS portals should still be treated empirically: DOCX is
the conservative first choice, and no open-source score is presented as a universal
pass probability.

The full research, parser limitations and reproduced results are recorded in
[the ATS compatibility review](research/2026-09-26-resume-cv-ats-review.md).

## OpenAI configuration

Set the provider and API key through Profile, or with environment variables:

```bash
export LUCIDRESUME_Tailoring__Provider=openai
export LUCIDRESUME_OpenAi__ApiKey=sk-...
export LUCIDRESUME_OpenAi__Model=gpt-5.6-luna
```

OpenAI is optional for ingestion-time extraction and authoring drafts. AI output
is displayed in the generated preview. It cannot publish a career record or create
accepted claims. Editing can run during generation; export only renders the existing
projection. The implementation follows the OpenAI Responses API
structured-output guidance:
https://developers.openai.com/api/docs/guides/structured-outputs

API keys are never written into an exported resume or JobML artifact.


## Optional role interviews

The desktop Project Resume flow runs requirement coverage before selecting the
resume. `RoleInterviewPlanner` prioritizes required gaps, then responsibilities,
then preferred requirements. It asks for a real example when a must-have has only
related evidence, a skill has no descriptive account, or a relevant role lacks
concrete action/outcome detail. Its English action/outcome cues are conservative
interview triggers, not a judgment that a claim is true, complete or well written.
Projection always proceeds with the current evidence. Gaps produce a warning, not
an interview gate. **Improve role evidence (optional)** opens the questions after
generation; **Back to projected resume** returns without requiring answers. The UI
presents one topic at a time and recalculates after a reviewed answer. The separate
complete-transcript builder remains available for deliberate career authoring.

The interview asks about the situation/scope, the person's contribution and
choices, and the outcome. This adapts the situation, action and result probes in
[OPM's Structured Interview Guide](https://www.opm.gov/policy-data-oversight/assessment-and-selection/structured-interviews/guide.pdf)
for candidate-owned evidence authoring, rather than applicant scoring. A metric is
optional: concrete qualitative outcomes are useful. Blank, unknown and skipped
answers create no claims. The draft button only assembles the person's words;
the person edits and reviews the prose before it is accepted.

Acceptance appends an achievement and an interview record to the selected
`WorkExperience`. The record retains the target requirement, raw context/action/
outcome answers, reviewed prose and acceptance time. The evidence ledger records
an `author-interview` passage with a separate interview source identity; portable
JobML labels it as a candidate declaration, never independent verification or text
extracted from the uploaded CV. Existing claims keep their review decisions and
original provenance. The transcript updater retains existing prose and links,
reuses stable or unambiguous existing role identities, and checks for concurrent
source edits before saving. No public career-record endpoint is republished by
this local save; publishing remains a separate action.

Related compiler matches remain usable for selection but do not close required
requirement gaps. Role affinity cannot create a requirement match on its own.
Matching reads linked human passages as well as the concise machine statement.
Bounded editing rejects newly introduced leadership/ownership wording and removal
of selected uncertainty/negation qualifiers, and otherwise falls back section by
section to accepted prose. These rules improve fidelity but are not a general
semantic entailment proof or a guarantee of perfect prose.

Validation includes synthetic VP Engineering, Platform Engineer and AI Director
projections, unrelated must-have counterexamples, editing guardrails, and a
save/reload/export test for role identity, custom transcript prose, interview
provenance and existing review decisions. The single-claim projection fixture
cannot establish whole-career narrative quality; richer candidate examples remain
necessary for that evaluation.


## Evidence-led skill matching

Skill identity comes from exact normalized names or the shipped, audited
`Resources/taxonomies/equivalences.txt` file. Legacy domain taxonomy associations
remain available for discovery, but do not establish equivalence. For example,
financial modeling does not establish Python, IaC does not establish Terraform,
and Helm does not establish Kubernetes. The equivalence asset rejects ambiguous
alias assignments rather than silently choosing the first mapping.

Skill-ledger matching reports `Equivalent`, `Related`, or `Missing`. Only an
explicit equivalent skill with ledger evidence counts toward required coverage.
A declaration in a skills section is still a declaration; it is not proof of
proficiency, and the interview asks for a concrete role account. Semantic retrieval
can populate `SuggestedResumeSkill` and `NearMisses`, but leaves the requirement
in `Gaps` and cannot inherit the suggested skill's years or evidence strength.
Retrieval failure does not prevent explicit matching or gap reporting.

Role evidence and calculated skill dates now use complete phrases or audited
synonyms, never embedding similarity. The raw achievement keyword fallback that
borrowed an unrelated ledger entry's metadata was removed. Skill coverage retains
related prose for interview context without counting it as covered. Responsibility
overlap is a related-account suggestion and does not establish coverage.

Career recommendations ask whether an adjacent skill has a genuine supporting
example; they do not advise renaming a related skill to satisfy a requirement.
EmbeddingGemma 2 remains an evaluation candidate for retrieval. No embedding model
switch or claim of improved matching accuracy is made by these changes.

The representation separates identity, relationships, and role evidence. This is
consistent with the concept distinctions in [ESCO skills and competences](https://esco.ec.europa.eu/en/classification/skill-main)
and the skills, knowledge, and work-activity dimensions of the [O*NET Content Model](https://www.onetcenter.org/content.html).
These external classifications are research references; they have not been imported
as authoritative product data.


## Morena regression and full transcript builder

The supplied Morena Head of Applied AI Engineering advert is a permanent parsing
fixture. Its sectioned requirements remain seven required competency groups,
seven responsibility groups and nine preferred examples, with the source text
retained for each group. Semantic retrieval suggests relevant role accounts; it
cannot establish that a broad competency is satisfied. The desktop labels these
as an evidence review rather than presenting keyword coverage as job eligibility.
The same parser runs for pasted and scraped adverts.

Build complete transcript is available from the resume page and the generation
page without a target job. For every role it covers scope, delivery, decisions and
tools, outcomes, leadership, and reliability/learning. Saved topic identities make
it resumable. Named skills are recorded as candidate declarations on that role;
they are not independent verification. In aggregate views, saving locates the
original source document that owns the selected role, preserving imported text.
Skipping creates no evidence and remains unanswered on a later session.

Generation uses Tailoring.ComposeProse (default true) and the configured writing
provider. Profile exposes the toggle; saving an Ollama writer enables its composition
adapter. Restart after changing provider settings. OpenAI, local Ollama and local
LlamaSharp composition adapters are supported; unavailable providers retain source
prose and report a warning. This release does not install a new model or change
live personal records. Editing preserves claim identities, role bindings, source
qualifiers and numeric results, and rejects unsupported ownership or target terms.
These are conservative checks, not a full semantic verification system; users
must inspect the generated preview.

Optional real-time interview assessment uses the Interview settings section.
Disabled (the default), it uses local rules. With Enabled=true and Provider=ollama,
BaseUrl must be local HTTP and Model can be an installed SystemOne decision model
such as nimble. The runtime must support /v1/systemone. Provider=cloudflare accepts
Model=clef or clef-flash, CloudflareAccountId and an ApiKey supplied through secrets
or environment. Cloud assessment sends bounded, contact-redacted answer context
to Cloudflare. Neither provider invents interview prose: it chooses from explicit
follow-up questions and assesses answer detail. Invalid, uncertain, timed-out or
unavailable model responses fall back to local rules; assessments do not verify
facts or score competence. No model/runtime migration is performed automatically.


Quality regression checks also cover numeric-marker restoration, retention of the
substantive contribution (a team-size fragment cannot replace a delivery account),
and a source claim reused in both summary and experience with one claim identity
and citations at both passages. Possible duplicate compact employment entries are
flagged for reconciliation; their underlying records are retained. Accounts that
mention both a delivered milestone and cancellation trigger a delivery-status
question. Status-sensitive claims stay out of the projection until clarified.
Import reconciliation maps original source evidence and review decisions into the persisted complete career. Matching reads accepted concepts directly from that career ledger; it does not read each imported document. Interview prose and raw answers are saved to the canonical role. Completed dimensions survive later imports.

Author-confirmed personal story updates can be stored as a separate source and a `PersonalInfoOverrides.Summary` overlay, preserving imported accounts. Publication records survive aggregation, receive source-bound external-link claims, and appear as up to three selected links in tailored Word/PDF output. Their article vocabulary supplies no skill concepts or dated role evidence. Publication titles and destinations are kept out of prose rewriting. Explicit relative time qualifiers such as “past year” are retained by the editorial validation gate.


Generated resumes are now saved separately from imported career evidence. The desktop archive retains the job, frozen generated document, full career evidence, original DOCX/PDF exports and rendered PDF pages. Word preview renders the saved DOCX. Reopening a version does not regenerate its prose. Numbered JobML citations select the saved claims and original source excerpts in the evidence panel; publication links remain clickable. Generated documents never become new career evidence automatically.

Native client validation used a private copy of all 21 imported sources. The legacy inputs contain 42 imported rows, of which 25 have malformed title/company fields; those remain in the source archives. The saved complete career contains the remaining 17 role identities. The Morena run selects six detailed roles and six compact chronology entries, preserves the author's agentic-products statement and three linked articles, and produces a two-page PDF. The current Word renderer produces four pages including references, so pagination differs between formats. Archive reopening, Word/PDF rendering, page navigation, zoom and citation-to-original-source navigation were exercised through the native UI test bridge.

Local prose editing currently makes sequential batches of up to three passages through two passes. With the 9.7B local Qwen model this takes several minutes. The client now displays pass and batch progress; provider logs capture per-batch wall time and prompt/output token counts without recording source text. Evidence validation still runs for every returned passage.


Prose reuse is now stored as role-linked accounts under the local `prose-library` directory. Each record contains original and edited prose, explicitly supported skills, subject/claim/evidence IDs, source fingerprint, model digest and editing purpose. It is derived wording and never imported into the skill evidence ledger. The Tighten pass is job neutral and can be reused across jobs; HumanVoice uses a short deterministic brief of parsed requirements and responsibilities. Exact variants are keyed by source content, evidence bindings, skills, instructions, word budget, pass, model identity and applicable job brief. Every cached edit is revalidated against the current source and job. Rejected edits retain the existing wording with review notes, rather than repeatedly generating the same rejected wording. Updating source facts, skills, instructions or the local model invalidates the affected variants.

Coverage memoizes each unique evidence string for the duration of an analysis, avoiding repeated embedding-service calls for each competency. ONNX inference and loading run off the UI thread; its bounded cache now holds 4096 vectors and partitions by model/vocabulary file signature so repaired or changed models do not reuse lexical fallback vectors. Career selection still runs for each job; cached prose never establishes skill coverage.


The full career JobML is materialised and saved per career revision. It contains the complete claims, source links, review states, concepts, embedding-space identity and vectors, including observations still awaiting review. The editor preserves this machine area, publishes reviewed achievement/summary edits back to structured career prose, and rejects stale publication after concurrent changes. Original import text remains an evidence archive. Numeric vectors use flow sequences to keep the machine editor manageable; full precision survives round trips. A separate derived semantic index reuses unchanged concept vectors only when model ID, digest and normalization match.

GitHub README passages and repository technology observations are persisted with source URLs, fingerprints, contributor counts/rank/share, project age and recency. They nominate skills for review, rather than automatically establishing personal experience. A candidate must have observed commits for personal-evidence eligibility; unknown or zero contribution stays observation-only. README skill claims require author review. Main-contributor status remains unknown when bounded contributor pagination is incomplete. Repository age and activity span are never converted to personal skill-years.

The final private native benchmark used this persisted career: initial new prose bindings 164.89 seconds; identical-job reuse 1.75 seconds including archive writes. The model stage starts within a second; first-time local prose editing remains the main cost.


## Complete career workflow and screenshots

See [complete career workflow](complete-career-workflow.md) for migration, full JobML editing, raw interview retention, GitHub review gates, generated archive previews and the measured reuse performance. The portable raw narrative and interview fields are documented in [transcript extension 0.1](jobml-transcript-extension-0.1.md).
