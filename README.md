# ***lucid*RESUME**

> NOTE: lucidRESUME is a **research project** and not intended as a product for general use. 

[![lucidRESUME release](https://img.shields.io/github/v/release/scottgal/lucidRESUME?logo=github&label=lucidRESUME)](https://github.com/scottgal/lucidRESUME/releases/latest)
[![lucidRESUME total downloads](https://img.shields.io/github/downloads/scottgal/lucidRESUME/total?logo=github&label=total%20downloads)](https://github.com/scottgal/lucidRESUME/releases)
[![Mostlylucid.Avalonia.UITesting on NuGet](https://img.shields.io/nuget/v/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=Mostlylucid.Avalonia.UITesting)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)
[![NuGet downloads](https://img.shields.io/nuget/dt/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)

**Human prose for people. Evidence-linked JobML for machines.**

***lucid*RESUME** is a free, open-source desktop editor for evidence-backed
resumes. You write and own the human prose. The app keeps a separate,
higher-resolution JobML representation for ATS and AI systems, with every
machine-facing claim linked to the evidence that supports it.

It builds a structured ledger from your resumes, LinkedIn data, repositories,
and other sources, then uses that ledger to:

- **Project role-specific resumes** from a persistent evidence ledger
- **Match you to relevant roles** with per-skill similarity scoring
- **Show what you're missing** (and what you're not)
- **Plan your next move** based on your actual skill graph

The ledger and deterministic extraction pipeline run locally on your machine. No
account is required. Data leaves your machine only when you explicitly configure
a cloud AI provider, import a remote source, or use an online job-search service.

OpenAI is the primary full-strength provider for assisted ingestion and optional
drafting. Anthropic and Ollama are also supported. grug 9B through LLamaSharp is
an experimental offline path. These providers can offer an explicitly labelled
prose draft or sample. A draft is not accepted
prose, evidence, or a published claim. The person remains the author and decides
what to edit, accept, and publish.

Projection itself is deterministic. It selects reviewed resume, LinkedIn, and
GitHub ledger records and starts with the accepted human prose. An optional,
bounded editing stage may tighten those selected passages with local grug 9B or
OpenAI. The deterministic planner first fits complete source sentences to a
compact per-section budget. Model edits are then accepted section by section;
any edit that changes claim or evidence identity, invents a number, borrows an
unsupported vacancy term, or exceeds its budget falls back to the valid human
selection. Detailed experience remains selective. Accepted roles omitted from the
detailed projection are preserved in a compact end-of-experience chronology when
they lasted more than three months; shorter engagements are left in the complete
career record. Chronology lines are deterministic ledger projections and never go
through a prose-editing model. The result exports as one
evidence-linked artifact in Markdown, Word, or PDF. Published documents use
inline numbered citations and a compact cJobML References section. A citation can
open the exact role or project in the published full career transcript. Full
JobML retains the passage selectors, drift hashes, review state, and semantic
metadata omitted from the compact document. Transcript links expose the fuller
candidate-maintained account; repositories, articles, releases, qualifications,
and similar artefacts remain distinguishable as stronger external evidence.
See [the design article](https://mostlylucid.net/blog/the-problem-with-resumes).
See [resume output and template design](docs/resume-output-design.md) for the full flow,
template rationale, and configuration.

> Built with .NET 10 + Avalonia. Runs on Windows, macOS, and Linux.

---

## Core Idea

**Every skill is backed by evidence.**

***lucid*RESUME** doesn't just list what you *say* you know - it builds a **skill ledger** where each skill is tied to:

- **Where** it appeared (job, project, repo)
- **When** you used it (date ranges, calculated years)
- **How often** it shows up across roles
- **How strong** the evidence is (recency, frequency, confidence)

No invented skills. No output-time guessing. Extraction is recorded once with its
method, confidence, source, and review state.

An optional local Ollama Nimble decision layer can resolve bounded ingestion ambiguities after local
rules and NER have proposed candidates. It can classify an unknown section or
prose-linked skill, or select an already-extracted person or employer span. It cannot generate a new
name, company, claim, or evidence value. For jobs it classifies extracted phrases as skills,
capabilities, logistics or noise in batches, replacing full LLM extraction when enabled.
Saved job decisions are reused during generation. Every decision is probability-gated,
source-hashed, and recorded for review. Nimble is disabled by default. See the
[local setup and decision guide](docs/nimble-parsing.md).
Cloud API keys entered in Profile are held by the operating system credential
store, never in the JSON settings file.

This is the foundation everything else builds on - matching, projection, gap analysis, and career direction.

The two representations have different jobs:

- Human prose is written for human readers. It should sound like its author.
- JobML is written for machines. It can be more explicit and more detailed, but
  it must remain traceable to reviewed prose or external evidence.
- AI may suggest draft prose. It cannot silently publish that prose, create
  accepted evidence, or turn an inference into fact.

lucidRESUME is not an automated job application service and it is not intended
to disguise machine-written text as human writing. Its purpose is to let human
writing remain human while giving ATS and AI systems a precise, verifiable view.

The experimental **JobML 0.1 editor** places authoritative human Markdown on the
left, editable JobML on the right, and live evidence links between them. Selecting
a link highlights both the supporting prose and machine reference. As prose
changes, claims are marked `valid`, `changed`, `missing`, or `ambiguous`;
inferred claims never become evidence without explicit acceptance. Markdown may
be shorter for a particular role while JobML retains higher-resolution external
evidence. The editor opens the complete career in the fast **Markdown** preview.
Choose **Document** to request the DOCX projection through the selected output
template, rendered by Morph beside those evidence links. It does not re-extract
or reinterpret the ledger. See the
[JobML specification](docs/jobml-0.1-specification.md),
[cJobML publication specification](docs/cjobml-0.1-specification.md),
[implementation profile](docs/jobml-0.1.md), and
[GitHub repository extension](docs/jobml-github-extension-0.1.md).

| | |
|---|---|
| ![DOCX Preview](docs/screenshots/docx-preview.png) | ![My Data Dashboard](docs/screenshots/my-data-page.png) |

---

## Why ***lucid*RESUME**?

Every major job site wants your email, your browsing history, and permission to sell your profile. AI resume tools send your CV to some SaaS vendor's cloud. Paid tools charge monthly for table-stakes features.

***lucid*RESUME** does things differently:

- **Evidence-first AI** - full-strength OpenAI is the primary assisted path; [grug 9B Q4_K_M](https://huggingface.co/ProCreations/grug-9b-gguf) through LLamaSharp remains an experimental offline option. Neither path runs during deterministic projection.
- **No account required** - data stored in a local SQLite database. You own it.
- **Evidence-led projection** - Project only renders claims already present in the ledger.
- **Career direction (based on your actual skill graph)** - not just "match this job" but "what to do next to reach your target cluster".
- **Free forever** - Unlicense. Public domain.

---

## What It Does

### Resume Import

![Resume import with DOCX preview](docs/screenshots/docx-preview.png)

- **Drag and drop** any file onto the app to import — resumes, LinkedIn exports, anything
- Import PDF or DOCX resumes
- **DOCX preview** powered by [Morph](https://github.com/SimonCropp/Morph) — cross-platform document-to-image rendering in pure C#, no LibreOffice needed
- **LinkedIn data export** — drop your LinkedIn ZIP archive and it auto-detects and imports your full profile: positions, skills (with endorsement counts), education, projects, contact info
- **GitHub evidence** - imports Linguist language totals, topics, README-derived
  candidates, project dates, and repository provenance. A bounded deep audit
  records original/fork status, repository age, activity span, tests, CI, release
  automation, package manifests, documentation, and browser-extension structure.
  Archived and small repositories remain visible as historical or experimental
  observations. Forks and repositories below the evidence threshold do not
  nominate personal skills by default. Observed technologies remain separate
  from reviewed personal claims.
- **NuGet package families** - audits a public publisher profile and official V3
  metadata, then groups package IDs by source product. Package count, public
  downloads, tags, and source repositories become searchable ledger evidence,
  not automatic résumé prose or claims of proficiency.
- All imports merge into a **single unified candidate document** using embedding cosine similarity — no duplicates, full source tracking
- Handles two-column, LaTeX, complex formatting
- Template learning: learns your resume's structure on first parse, deterministic on subsequent imports
- Multilingual: German, French, Spanish, Portuguese, Chinese, Dutch, Japanese, Korean

### Skill Ledger

![My Data — skills dashboard with pie chart, Gantt timeline, and skill communities](docs/screenshots/my-data-page.png)

- **Provenance chain**: skill -> which job -> which date range -> which bullet point
- **Calculated years**: sum of non-overlapping date ranges where the skill appears
- **Evidence strength**: combining years, role count, recency, confidence
- **Consistency checking**: flags skills listed but never demonstrated, claimed vs calculated years
- **Presentation gap vs true gap**: "you have adjacent skills" vs "you don't have this at all"

### Smart Matching

![Jobs page with Looking/Hiring toggle](docs/screenshots/jobs-page.png)

- Accepted skill evidence and audited equivalences determine requirement coverage
- Embedding similarity retrieves related evidence and adjacent skills; it does not establish a required skill
- Per-skill match detail with similarity scores, evidence strength, calculated years

### Career Direction
- **Skill graph** with co-occurrence edges and Leiden community detection
- **Career planner**: 4 gap types (PresentationGap, WeakEvidence, AdjacentSkill, TrueGap)
- **Effort/impact ranking**: Low (rewording), Medium (side project), High (new learning)
- **Search query generator**: suggests job searches from your strongest skill communities

### Guided role evidence

Project Resume checks the target requirements before generating. Missing evidence,
related matches for must-haves, bare skill labels, and thin relevant role accounts
open a focused interview. Choose the source role, describe the context, your personal
contribution and the outcome, then edit and review the prose before saving it.
Qualitative outcomes are valid; unknown details can stay blank. Skip a question or
project with the current evidence at any point.

Reviewed answers are saved on that role in the complete career record, with a
separate candidate-declared interview source. Existing source text, custom transcript
prose, evidence links and review decisions are retained. Coverage is checked again
so future role-specific resumes can reuse the new account. The current desktop
interview presents one outstanding question at a time, with local rules and optional
SystemOne decision-model assessment; it is authoring assistance,
not a measure of competence or eligibility.

The **Build complete transcript** action works without a job description. It guides
each role through scope, delivery, decisions/tools, outcomes, leadership and
reliability, saves reviewed prose and named skills to the source role, and resumes
unanswered topics later. Generation optionally tightens source-bound prose with
your configured writer; rejected edits retain source wording and show a warning.
See [generation and interview settings](docs/resume-output-design.md#morena-regression-and-full-transcript-builder).

The career now also stores analysed prose passages with accepted skills, evidence,
reviewed contribution accounts and reusable embeddings. Projection retrieves per job
requirement through a rebuildable SeekStorm index, deduplicates accounts within their
source role, and combines compatible facts for synthesis. Required career years
expand the dated chronology; relevant recent roles and older roles supplying distinct
job evidence receive more detail within the selected length budget. Concurrent roles
count once, and tenure shortfalls warn without blocking generation. Embeddings rank passages; they do not establish skills. The
Profile page can refresh this index and import/review cached blog passages. Relevant
articles and concise related repository links travel with the generated archive,
Word/PDF exports and JobML. See the [implementation and evaluation paper](docs/research/2026-10-12-evidence-preserving-prose.md).

### Complete career record and saved output

The complete career is a persisted authoritative record, separate from imported source files and generated resumes. Legacy imports are composed and deduplicated once; malformed employment headers stay in their original archives. Imports and reviewed interviews update the career at save time. Tailoring, coverage and career planning read its saved roles, claims, supported skill concepts and original evidence bindings; they do not reconstruct the career or rescan imported resumes for each job. Removing an input archive does not silently retract facts from the career. Invalid legacy JobML is retained in the archive with migration warnings and its claims require review.

The JobML editor opens this complete career, preserving the full claims/sources and semantic index instead of generating a new draft from one selected resume. Its transcript extension retains source narrative and complete role interview answers alongside reviewed prose and skills; retained narrative does not automatically become accepted evidence. Reviewed role prose and summary edits publish back to the career without rewriting imports. Structural role/profile changes use their dedicated editors. Vectors use compact YAML sequences and a persisted model/digest-specific semantic index; unchanged concepts survive career updates without embedding again. Word rendering is requested from the Document tab rather than on each editor load.

GitHub import fetches README content for repositories beyond the optional local-summary limit. It stores exact technology passages, actual README URLs, content/blob fingerprints, project age/activity and bounded contributor observations (candidate commit count/share/rank, completeness and main-contributor status). README and repository technology nominations require review before satisfying job requirements. Zero observed candidate contributions remain observation-only; repository age never becomes personal years of experience. Commit counts are an attribution signal, not a measure of contribution quality. See [GitHub contributor API semantics](https://docs.github.com/en/rest/repos/repos#list-repository-contributors).

Native desktop measurements on the private full-career test corpus: a fresh local two-pass prose edit took 164.89 seconds; repeating the same job using the saved career and validated prose accounts took 1.75 seconds, including generated-archive persistence. These are local measurements, not latency guarantees for new jobs or models.

See [the complete career workflow](docs/complete-career-workflow.md) for the editor, role interviews, archive, screenshots and migration details.

### Evidence Projection
- Treats the complete human career transcript and its canonical ledger as source data
- Exports that source as a portable JobML `career_record`, including sources and optional semantic artefacts
- Detects role requirements, then deterministically plans sections and selects evidence
- Semantic compression: 13 roles -> 6 relevant -> filtered to evidence-backed bullets
- Optionally runs bounded tightening and human-voice passes over selected source prose
- Renders Markdown and JobML together without output-time evidence re-inference
- Links compressed statements to the exact source role or project in the complete transcript
- Distinguishes candidate-maintained transcript provenance from external supporting artefacts
- Preserves human-owned prose while JobML carries explicit machine detail

### Embeddable web compiler

`lucidRESUME.Web` is an ASP.NET Core control for the narrow publish-and-compile
workflow. It does not ingest LinkedIn exports, repositories, or old CVs. That
happens upstream in the desktop application. The control accepts the already
exported Markdown + JobML `career_record`, publishes an immutable revision,
accepts a job description, and returns a shorter evidence-bounded projection with
Markdown, Word, and PDF downloads. Each published application receives an opaque
URL whose browser view shows the résumé, its selected claims, and links into the
complete transcript. The same publication exposes full JobML and cJobML to machine
clients. The application career ledger remains the
canonical source; the published JobML document is its portable projection.
OpenAI is the default interactive editor. The local LLamaSharp path batches the
projection in ordered groups of three and can recover complete section objects
from a truncated response without accepting an incomplete edit. The compiler
keeps the reviewed summary outside model editing, orders experience in reverse
chronology, and deterministically adds the detected target title, canonical
evidence-backed skills, role-relevant projects, and reviewed education.

![JobML web compiler rendering an evidence-linked projection](docs/screenshots/jobml-web-compiler.png)

```csharp
builder.Services.AddLucidResumeCompiler(builder.Configuration);

var app = builder.Build();
app.UseAntiforgery();
app.MapLucidResumeCompiler();
app.Run();
```

Run the included host with:

```bash
dotnet run --project samples/lucidRESUME.Web.Sample
```

The control is mounted at `/resume` by default. The current career-record
projection is served at `/resume/api/jobml`; immutable source revisions and
application-specific publication URLs carry ETags and cache headers. API keys
remain server-side. See the
[web compiler guide](docs/jobml-web-compiler.md).

For the adjacent Mostlylucid site checkout, run
`scripts/run-local-resume-integration.sh` to serve the compiler at
`http://127.0.0.1:8080/resume/`. Publish the reviewed Markdown and JobML career
record locally, paste a job description, then use the resulting evidence link to
inspect the full transcript, ordered source chunks, cJobML, Word, and PDF. The
sample's `App_Data` publications are ignored by Git; keep private career records
out of commits. Local Ollama setup and gateway configuration are in the
[web compiler guide](docs/jobml-web-compiler.md).

### Personal ATS (Pipeline)

![Pipeline tracking](docs/screenshots/pipeline-page.png)

- **Stage pipeline**: Saved -> Applied -> Screening -> Interview -> Offer -> Accepted/Rejected/Withdrawn/Ghosted
- Timeline per application, funnel visualization, stale detection
- **Email integration** (IMAP via MailKit): auto-detects confirmations, interviews, rejections, offers

### Job Search
Seven job board adapters searched in parallel (Adzuna, Reed, Findwork, Arbeitnow, JoinRise, Jobicy, Remotive). Near-duplicate detection via embedding similarity. Hoover role flagging.

### Export
JSON Resume (standard schema), Markdown, **DOCX** (Word via OpenXml), and tagged
**PDF/UA** ([QuestPDF](https://www.questpdf.com/)). DOCX uses real Word lists and
named headings; both office formats keep identity in the document body and avoid
tables, sidebars, text boxes and repeated résumé headers.

### Documentation
- [Release & Archive Guide](docs/release.md) - release workflow, platform archives, and single-page docs archive.
- [Technical Architecture](docs/architecture.md) - modules, data flow, persistence, and extraction pipeline.
- [Document Layout Detection](docs/layout-detection.md) - DocLayNet YOLO model, structural hashing, template communities.
- [Local Nimble Parsing](docs/nimble-parsing.md) - bounded section and NER decisions with source hashes and probabilities.
- [JobML 0.1 Specification](docs/jobml-0.1-specification.md) - normative document model, evidence reconciliation, review states, and extensions.
- [cJobML 0.1 Publication Projection](docs/cjobml-0.1-specification.md) - compact numbered citations, references, full-ledger endpoints, and one-pass parsing.
- [Resume and ATS Compatibility Review](docs/research/2026-09-26-resume-cv-ats-review.md) - sourced format guidance, reproduced parser results, limitations, and the validation plan.
- [JobML Web Compiler](docs/jobml-web-compiler.md) - complete master publication, deterministic role projection, bounded prose editing, and ASP.NET Core integration.
- [Chrome Evidence Filler](extensions/lucidresume-chrome/README.md) - local Prompt API field mapping from a published full JobML endpoint, with review and honest gaps.
- [Chrome Evidence Filler Design](docs/chrome-evidence-filler.md) - researched browser APIs, deterministic projection rules, privacy, threat model, and verification.
- [JobML GitHub Extension](docs/jobml-github-extension-0.1.md) - repository quality, attribution, and skill-observation model.
- [JobML Package Registry Extension](docs/jobml-package-extension-0.1.md) - package-family discovery and registry observations.
- [In-App User Manual](src/lucidRESUME/Resources/user-manual.md) - the same help content embedded in the desktop app.

### CLI

```bash
lucidresume parse          --file cv.docx [--output result.json]
lucidresume evidence       --resume cv.docx [--output ledger.json]
lucidresume match          --resume cv.docx --job "JD text"
lucidresume compound-match --resume cv.docx --jobs-dir jds/
lucidresume explain        --resume cv.docx --job "JD text"
lucidresume tailor         --resume cv.docx --job "JD text" [--full-jobml https://example.net/career.jobml] [--output projected.md]
lucidresume drift          --resume1 old.docx --resume2 new.docx
lucidresume export         --file cv.docx --format pdf|docx|markdown|json
lucidresume validate       --resume cv.docx
lucidresume fix            --resume cv.docx [--output fixed.md]
lucidresume generate       --resume cv.docx --prompt "draft a 2 page cloud resume" [--full-jobml https://example.net/career.jobml]
lucidresume anonymize      --resume cv.docx [--output anon.json]
lucidresume rank           --dir resumes/ --job "JD text"
lucidresume search         --prompt "senior .NET developer remote"
lucidresume extract-jd     --job "JD text" [--output jd.json]
lucidresume github-import  --username scottgal --output repositories.json
lucidresume package-audit  --publisher mostlylucid --output packages.json
lucidresume batch-test     --dir resumes/
lucidresume jobml validate  --file resume.jobml.md
lucidresume jobml reconcile --file resume.jobml.md
lucidresume jobml coverage  --file resume.jobml.md
lucidresume jobml cold-parser-probe --file resume.jobml.md
lucidresume jobml career-record --resume-dir /path/to/resumes --github scottgal --nuget-publisher mostlylucid --output career.jobml.md
lucidresume jobml compact --file resume.jobml.md --full-jobml https://example.net/career.jobml --output resume.md
lucidresume jobml link-post --file resume.jobml.md --claim claim-id --url https://example.net/article --output linked.jobml.md
```

Role projections include compact cJobML citations in Markdown, Word, and PDF by
default. Pass `--cjobml false` to `tailor`, `generate`, or `render` for a
human-only copy. `--full-jobml` gives transcript references an exact deep link.
Compact references can cite transcript sections and public evidence without
copying full passages, selectors, or drift hashes out of the full JobML career
record.

### Career anchors

Relevance and recency should not erase a defining older role. Mark a role as
**Always include** on the My Data page, or place an invisible directive directly
below its Markdown heading:

```markdown
### Program Manager II | Microsoft Corp
<!-- lucidresume:career-anchor -->
```

The full JobML `career_record` publishes this as
`projection: { include: always }`. Compilers reserve anchor sections before
ranking ordinary roles. The preference does not strengthen the role's claims or
bypass evidence validation.

CLI and server configurations may anchor every role for selected companies:

```json
{
  "Tailoring": {
    "CareerAnchorCompanies": ["Microsoft", "Dell"]
  }
}
```

Company anchoring is explicitly configured by the author. lucidRESUME does not
maintain an inferred employer-prestige score.

---

## Desktop workflow

Start in **My career** to import a CV or LinkedIn export, write about each role,
or edit your complete prose. Evidence prompts explain what detail could help and
show the saved account behind each suggestion. **Jobs** is the single workspace
for pasting an advert, extracting and confirming its role/company, keeping an
optional tracking URL, saving the job and creating its resume. Saved resumes and
Applications handle outputs and tracking. Specialist evidence editors, source
inspection and discovery tools are under **More tools**.

Writing drafts survive navigation; use **Save career** or **Save job** to keep
changes across restarts. See [the desktop UX review](docs/ux-workspaces-review.md)
for the screen audit, integrity guarantees and verification workflow.

## Getting Started

### Project from your saved career using the CLI

`tailor` and `generate` accept `--career-store /path/to/lucidRESUME/data.db`
as an alternative to `--resume` or `--resume-dir`. This reads the authoritative
saved complete career through a read-only SQLite connection. It does not migrate
the store, reconcile imports, or change reviewed career prose. A missing complete
career is an error rather than a fallback to older imports.

```bash
dotnet run --project src/lucidRESUME.Cli -- tailor \
  --career-store /path/to/lucidRESUME/data.db \
  --job-file job-description.txt --length concise \
  --format all --output output/application
```

`--length` accepts `automatic`, `concise`, `standard`, or `extended`; it controls
the evidence selection budget, rather than guaranteeing a page count. `--format
all` saves Markdown, Word and PDF plus a `.jobml.md` sidecar retaining the full
evidence graph even when application documents omit citations. Review the prose
and document layout before applying.

For a read-only comparison of saved ledger concepts and advert requirements with
their source text, run `evidence --career-store /path/to/data.db --audit --output
audit.json`. See the [ledger quality review](docs/ledger-quality-review.md) for
findings, refresh behaviour and validation limits.

The [source and prose quality loop](docs/source-and-prose-quality-loop.md) records
parser comparisons, raw versus validated model extraction, and writing reviews
against contrasting job descriptions. Its evaluation commands can be rerun with
local models or recorded outputs.

### Download & Install

1. Go to the [latest release](https://github.com/scottgal/lucidRESUME/releases/latest)
2. Download the archive for your platform:

| Platform | Download |
|----------|----------|
| **Windows** | `lucidRESUME-...-win-x64.zip` or `win-arm64.zip` |
| **macOS** | `lucidRESUME-...-osx-arm64.tar.gz` (Apple Silicon) or `osx-x64.tar.gz` (Intel) |
| **Linux** | `lucidRESUME-...-linux-x64.tar.gz` or `linux-arm64.tar.gz` |

3. Extract the archive. On macOS, open `lucidRESUME.app`. On Windows or Linux,
   run `lucidRESUME.exe` or `lucidRESUME` respectively.

The desktop app downloads its local ONNX models (about 600 MB) on first launch.
They are cached in the user data directory, outside the signed application bundle.
No account or setup wizard is required.

> **macOS users:** the bundle is ad-hoc signed but not notarized. If Gatekeeper blocks it, Control-click the extracted app and choose Open. If needed, run `xattr -dr com.apple.quarantine ./lucidRESUME.app` on that app only.

### AI-assisted ingestion and drafts (Optional)

AI assistance is optional. It can recover structured candidates from difficult
source documents or offer a clearly labelled authoring draft. Every inferred
record is stored with provenance and requires review. Draft prose remains
unaccepted until a person edits and approves it. Resume projection and export
work without a language model.

**Option 1: OpenAI (primary full-strength provider)**
1. Open **Profile → AI Provider**
2. Enter your OpenAI API key and select `openai`
3. Select the model and save. The key is stored in the operating system credential store.

**Option 2: Local AI with LLamaSharp (experimental)**
1. Open **Profile → AI Provider**
2. Select `llamasharp` and click **Download local model**
3. Restart the app after the 5.63 GB Q4_K_M download completes

The model is loaded lazily and uses its embedded chat template. Apple Silicon uses the Metal support included in the LLamaSharp CPU backend; other platforms have a portable CPU fallback. Set `LlamaSharp:ModelPath`, `ContextSize`, or `GpuLayerCount` in `lucidresume.json` to override the defaults. Relative model paths resolve under the user data directory, outside the signed application bundle.

**Option 3: Local AI with [Ollama](https://ollama.ai)**
1. Install Ollama and run `ollama pull qwen3.5:4b`
2. Select `ollama` under **Profile → AI Provider**

**Option 4: Anthropic**
1. Open the **Profile** page in the app
2. Enter your Anthropic API key and select `anthropic`

### Build from Source

For developers who want to build from source:

```bash
git clone https://github.com/scottgal/lucidRESUME
cd lucidRESUME
./run.sh Release
```

The launcher builds and starts the desktop app (Release by default). Use
`./run.sh Debug` for a debug build. Additional arguments are passed to the app,
for example `./run.sh Release --storage-audit`.

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download). The default solution is desktop-only, so `dotnet build lucidRESUME.sln` and `dotnet test lucidRESUME.sln` do not require mobile workloads. See [docs/release.md](docs/release.md) for the release workflow.

---

## How It Works (Technical)

### Extraction

**5-layer RRF fusion** for both resume and JD extraction: structural patterns + ONNX NER (2 models) + **skill taxonomy centroids** (19,983 skills from 1.3M LinkedIn jobs) + optional LLM candidate extraction + **entity lookup** (11K companies, 7K locations). All signals run during ingestion and are fused by reciprocal rank fusion with multi-source confidence boosting. The resulting evidence records are persisted with provenance and review state.

Export is deliberately less clever. It is a projection of accepted ledger records.
It does not rerun NER or an LLM, and it refuses stale evidence. This keeps the
human prose and the JobML evidence graph reversible: each rendered claim points
back to the exact ingested evidence and source revision that justified it.

**Skill taxonomy**: 19,983 preprocessed entries from the documented Kaggle/LinkedIn datasets ship with the application, together with priority data and compact leadership profiles for Lead Developer, Head of Engineering, CTO and VP Engineering. These source terms are the reproducible seeds for exact matching and locally materialised role centroids. No personal résumé, ledger or user database is part of the preload. Used by both resume and JD parsers to find skills embedded in prose.

The exact clean-install boundary, dataset provenance and release audit are documented in [Product data and clean-install contract](docs/product-assets.md).

ONNX embeddings (`all-MiniLM-L6-v2`, 384-dim) power semantic matching throughout. **[DocLayNet YOLO model](docs/layout-detection.md)** detects document structure from rendered page images — titles, section headers, tables, lists — producing a structural hash for template identification. Docling (Docker) adds ML-based PDF layout detection for complex documents; PdfPig with column detection as local fallback.

### Architecture

```
lucidRESUME (Avalonia UI: My CV, JobML Editor, My Data, Career, Jobs, Add Job, Project, Pipeline, Profile, Help)
    ├── Ingestion        Resume parsing, DocLayNet layout detection, Morph preview, LinkedIn import
    ├── Extraction       ONNX NER (2 models) + Microsoft.Recognizers pipeline
    ├── Parsing          DOCX/PDF/TXT extraction, ATS pattern detection, template learning
    ├── JobSpec          JD parsing (5-layer RRF: Structural + NER + Taxonomy + LLM + Entity), URL scraping
    ├── JobSearch        7 job board adapters + orchestrator + deduplicator
    ├── Matching         Skill ledger, skill graph, career planner, taxonomy centroids, entity lookup
    ├── AI               LLamaSharp/Ollama/Anthropic/OpenAI ingestion and draft-authoring providers
    ├── Compiler         Complete JobML master -> deterministic role-specific projection
    ├── Web              Embeddable ASP.NET Core publish, preview, and export control
    ├── EmailTracker     IMAP scanning, email classification, application matching
    ├── Export           JSON Resume + Markdown + DOCX + PDF exporters
    ├── Collabora        Installed-editor discovery and LibreOffice fallback
    ├── Avalonia.UITesting  UI automation framework (REPL, MCP, script runner)
    └── Core             Domain models, interfaces, persistence (SQLite + sqlite-vec)
```

**Dependency rule:** everything depends inward on `Core`. `Core` depends only on `Microsoft.Data.Sqlite` and `sqlite-vec`.

### Key Design Patterns

| Pattern | Where | Why |
|---------|-------|-----|
| **5-Layer RRF Fusion** | Resume + JD extraction | Structural + NER + Taxonomy + LLM + Entity Lookup vote, best candidate wins |
| **Skill Taxonomy** | Matching module | 19,983 skills from 1.3M LinkedIn jobs — cross-industry, not just tech |
| **Entity Lookup** | JD parser | 11K companies + 7K locations from LinkedIn/Adzuna validate NER candidates |
| **Skill Ledger** | Matching module | Every skill backed by evidence with provenance |
| **Skill Graph + Communities** | Career planner | Leiden community detection with UMAP visualisation |
| **Template Learning** | DOCX parser | First parse learns structure, subsequent parses are deterministic |
| **ATS Pattern Detection** | PDF parser | YAML rulesets identify resume templates/ATS systems |

---

## Tests

```bash
dotnet test lucidRESUME.sln    # 677 tests across 12 projects
```

| Project | Tests | Coverage |
|---------|-------|----------|
| Core.Tests | 123 | Persistence, models, reviewed transcript overlays, multi-resume, export, linked posts |
| Extraction.Tests | 25 | NER, recognizers, RRF fusion pipeline |
| AI.Tests | 103 | Providers, embeddings, bounded decisions, deterministic projection, gated live OpenAI checks |
| Matching.Tests | 99 | Skill scoring, filters, voting, job-search resilience, projection quality |
| JobSpec.Tests | 19 | JD parsing, deterministic flattened-title extraction, salary extraction |
| EmailTracker.Tests | 25 | Classifier, matcher |
| GitHub.Tests | 48 | Language map, repository assessment, package families, LinkedIn parsing, reviewed document merge |
| JobML.Tests | 33 | Parsing, validation, drift, reversible links, cJobML projection |
| Compiler.Tests | 36 | Deterministic evidence selection, compact chronology and projection orchestration |
| Web.Tests | 6 | ASP.NET Core endpoint and projection control |
| App.Tests | 13 | Native credentials, complete-career projection and generated archive |
| Avalonia.UITesting.Tests | 98 | Input, scripts, locators, screenshots, REPL |

The Chrome evidence filler has a separate TypeScript suite:

```bash
cd extensions/lucidresume-chrome
npm ci && npm run check && npm test && npm run build
```

---

## UX Testing

[![Mostlylucid.Avalonia.UITesting on NuGet](https://img.shields.io/nuget/v/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=Mostlylucid.Avalonia.UITesting)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)
[![NuGet downloads](https://img.shields.io/nuget/dt/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)

The UI automation layer is published as a standalone NuGet package — **[Mostlylucid.Avalonia.UITesting](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)** — so any Avalonia desktop app can use it. Real pointer/touch/wheel/gesture input via Avalonia's `IInputManager`, region/control snipping for manuals, YAML scripts, GIF video, REPL, and an MCP server. Source lives in [`src/Mostlylucid.Avalonia.UITesting/`](src/Mostlylucid.Avalonia.UITesting/README.md).

```bash
# Run a YAML test script
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- \
  --ux-test --script ux-scripts/e2e-full-flow.yaml --output ux-screenshots

# Interactive REPL
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-repl

# MCP server (for LLM-driven UI control)
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-mcp
```

---

## Roadmap

- [x] Optional resume authoring drafts and suggestions, always subject to human review
- [x] Automated job polling from skill community search queries
- [x] Resume extraction RRF fusion (multi-source confidence boost, same pattern as JD)
- [x] Career planner UI page with gap analysis visualization
- [x] Leiden community detection (refinement phase over Louvain greedy moves)
- [x] Temporal skill drift across resume variants (compare ledgers, detect added/dropped/changed skills)
- [x] DOCX export of projected resumes (pure C# via OpenXml, cross-platform)
- [x] PDF export of projected resumes (QuestPDF, professional formatting)
- [x] LinkedIn data export import (ZIP archive with full profile)
- [x] GitHub repo skills import (languages, topics, README analysis via lucidRAG)
- [x] DocLayNet ONNX model for document layout detection (YOLOv10m, 58MB, structural hashing)
- [x] RRF fusion name extraction (NER + positional + heading + email + LLM backstop — 92% accuracy)
- [x] Full CLI toolkit (17 commands: parse, evidence, match, explain, tailor, rank, fix, generate, etc.)
- [x] Batch testing and quality evaluation across 26 multilingual resumes
- [x] Skill taxonomy centroids (19,983 skills from 1.3M LinkedIn jobs + Kaggle role archetypes)
- [x] Entity lookup (11K companies, 7K locations, 144 industries from LinkedIn/Adzuna via DuckDB)
- [x] 5-layer JD parser (Structural + NER + Taxonomy + LLM + Entity — 0→60+ skills from plain text)

---

## Contributing

PRs welcome. Run the tests before submitting:

```bash
dotnet test
```

The codebase follows a strict inward dependency rule - keep domain logic in `Core` and wire everything in the app shell.

---

## License

This is free and unencumbered software released into the public domain.
See [LICENSE](LICENSE) or [unlicense.org](https://unlicense.org) for details.

**No strings attached. No attribution required. Use it however you like.**
