# ***lucid*RESUME** - Technical Architecture

## System Overview

lucidRESUME is a local-first evidence editor for resumes. Human-authored prose is
kept for human readers. A separate, higher-resolution JobML layer gives ATS and AI
systems explicit claims with traceable evidence. The app ingests resumes, job
descriptions, LinkedIn data, and repository observations into a reviewed ledger,
then projects both representations from that ledger.

The canonical career ledger is the application's working record. It contains the
complete human career transcript, source catalogue, evidence and skill ledgers,
review decisions, and derived indexes such as embeddings and role centroids.
JobML is its portable interchange envelope, not a synonym for the ledger:

```text
canonical career ledger
    ├── JobML career_record (complete portable projection)
    ├── JobML resume (role-specific projection)
    └── cJobML (compact citation projection)
```

Semantic artefacts can travel in the `career_record` export so consumers do not
need a second exchange format. They remain derived matching aids, never evidence.

The core insight is that **skills are not flat keywords**. They have evidence
(which job, which passage, what dates), strength (years, recency, depth), and
relationships. Matching is useful, but the central invariant is provenance: a
machine-facing claim must explain what supports it.

```
┌──────────────────────────────────────────────────────────┐
│                    lucidRESUME Desktop                    │
│  ┌─────┐ ┌─────┐ ┌───────┐ ┌───────┐ ┌────────┐ ┌────┐ │
│  │My CV│ │Jobs │ │Add Job│ │Project│ │Pipeline│ │Prof│ │
│  └──┬──┘ └──┬──┘ └───┬───┘ └───┬───┘ └───┬────┘ └──┬─┘ │
│     │       │        │         │          │         │    │
│  ┌──▼───────▼────────▼─────────▼──────────▼─────────▼──┐ │
│  │              ViewModels (MVVM)                       │ │
│  └──────────────────────┬──────────────────────────────┘ │
│                         │                                │
│  ┌──────────────────────▼──────────────────────────────┐ │
│  │              Service Layer (DI)                      │ │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐            │ │
│  │  │Extraction│ │ Matching │ │    AI    │            │ │
│  │  │ Pipeline │ │  + Graph │ │ Providers│            │ │
│  │  └────┬─────┘ └────┬─────┘ └────┬─────┘            │ │
│  │       │            │            │                   │ │
│  │  ┌────▼────────────▼────────────▼─────────────────┐ │ │
│  │  │                Core Domain                     │ │ │
│  │  │  Skill Ledger │ Job Desc │ Application │ Store │ │ │
│  │  └────────────────────────────────────────────────┘ │ │
│  └─────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

The desktop shell exposes My CV, JobML Editor, My Data, Career, Jobs, Add Job,
Project, Pipeline, Profile, and Help pages. The JobML editor is the reversible
boundary between human prose and the evidence graph.

---

## 1. Extraction Pipeline

### Design Principle: RRF Signal Fusion

Every extractor runs in parallel. Each produces candidates with confidence scores. Reciprocal Rank Fusion picks the best answer per field. No waterfall, no fallthrough - all signals vote.

```
Document (PDF/DOCX)
    │
    ├─→ Structural Extractor ──→ candidates (title, skills, salary, remote)
    ├─→ NER Detectors (2 models) ──→ candidates (skills, orgs, names, dates)
    ├─→ LLM Extractor (optional) ──→ candidates (structured JSON)
    │
    └─→ RRF Fuser ──→ best candidate per field
```

### Extractors

| Extractor | Speed | What it finds | Confidence |
|-----------|-------|---------------|------------|
| **Structural** | <1ms | Section headings, bullet lists, first-line title, salary regex | 0.8-0.95 |
| **NER (General)** | ~50ms | PER, ORG, LOC, MISC (dslim/bert-base-NER, 125M params) | 0.6-0.95 |
| **NER (Resume)** | ~50ms | Skills, Degrees, JobTitles, Companies (yashpwr/resume-ner-bert-v2) | 0.6-0.95 |
| **LLM** | 1-5s | Full structured extraction via JSON prompt | 0.8-0.85 |

### RRF Fusion

```
For each field (title, company, skill, etc.):
  1. Collect all candidates from all extractors
  2. Group by normalized value
  3. Score: max_confidence + (source_count - 1) × multi_source_boost
  4. If candidate appears from 2+ extractors: boosted
  5. Pick highest-scoring candidate

Weights configurable in appsettings.json → JdFusion section
```

### PDF Extraction Strategy

```
PDF arrives
    │
    ├─→ Docling available? ──YES──→ ML layout detection (DocLayNet model)
    │                                 Handles: 2-column, LaTeX, tables
    │                                 Returns: structured markdown with headings
    │
    └─→ NO ──→ PdfPig with column detection
                 X-position gap analysis detects 2-column layouts
                 Font-size ratio detects headings
                 Falls back gracefully for single-column
```

### DOCX Extraction Strategy

```
DOCX arrives
    │
    ├─→ Template fingerprint match? ──YES──→ Deterministic extraction
    │   (style hashes from prior parses)      (section map already known)
    │
    └─→ NO ──→ Direct parse
                 Word heading styles → markdown headings
                 Bold title-case text → section headings (if matches root tokens)
                 Pipe/dash separators for Company | Title patterns
                 Template learned for future parses
```

### ATS Pattern Detection

YAML rulesets (`ats-patterns.yaml`) identify resume templates:

```yaml
patterns:
  - name: latex-awesome-cv
    signals:
      - type: metadata_contains
        value: "LaTeX"
      - type: heading_count_min
        value: 8
    section_keywords:
      experience: ["experience", "work experience"]

language_signals:
  de:
    keywords: ["berufserfahrung", "ausbildung", "lebenslauf"]
    section_map:
      berufserfahrung: Experience
```

---

## 2. Skill Ledger

The central data structure. Every skill claim is backed by evidence.

```
SkillLedgerEntry
├── SkillName: "Kubernetes"
├── Category: "Technical Architecture"
├── CalculatedYears: 3.2
├── FirstSeen: 2021-06
├── LastSeen: null (current)
├── IsCurrent: true
├── RoleCount: 3
├── Strength: 0.72 (weighted: years×0.3 + roles×0.25 + recency×0.25 + confidence×0.2)
└── Evidence:
    ├── [0] GBG Plc (2022-08 → 2022-11) ".NET 6 microservices on Kubernetes" conf=0.85
    ├── [1] Seamcor Ltd (2022-12 → 2024-04) "Docker Compose and OpenSearch" conf=0.70
    └── [2] Skills Section "Kubernetes" conf=0.90
```

### Evidence Sources

| Source | Where found | Confidence |
|--------|-------------|------------|
| SkillsSection | Listed in Skills/Competencies section | 0.90 |
| JobTechnology | In experience entry's Technologies field | 0.95 |
| AchievementBullet | Mentioned in an achievement (substring or semantic) | 0.70-0.85 |
| Summary | Mentioned in the summary/profile | 0.60 |
| NerExtracted | Detected by NER model | varies |

### Accepted skill coverage and semantic retrieval

For the complete career, `SkillLedgerBuilder` reads accepted persisted claims and
skill concepts with original evidence bindings. It does not scan imported resumes
or use embedding similarity to create personal skill evidence at generation time.
Audited equivalences can establish aliases; related technologies remain adjacent
skills. Docker does not establish Kubernetes, and one .NET version does not establish
experience with another version merely because their vectors are close.

Embeddings retrieve related passages and projects and support gap analysis. The
saved `CareerSemanticIndex` records model ID, digest, normalization and concept
vectors; reuse requires the same embedding space. Role centroids are derived from
available vectors. Derived semantic artefacts never become accepted claims.

### Consistency Checking

The ledger automatically flags:
- **Unsubstantiated skills**: listed in Skills section but never mentioned in experience
- **Years mismatch**: claims "10+ years Python" but calculated evidence shows 3 years
- **Stale skills**: last used 5+ years ago

### GitHub repository evidence

GitHub import uses a staged evidence model:

```text
GitHub API observations
    -> repository quality assessments
    -> candidate skill observations
    -> human-reviewed JobML claims
```

The importer reads repository metadata, Linguist language byte totals, topics,
README content, and dates. For a bounded number of repositories it also inspects
a revision-pinned recursive tree for tests, workflows, release automation,
package manifests, documentation, and browser-extension structure. README
passages are scanned directly with the shared taxonomy for all imported repositories;
optional lucidRAG BERT summaries are bounded separately. Contributor observations
record candidate commits, share, rank, completeness and main-contributor status. The
output contributes candidate ledger evidence and searchable project profiles.

These signals are not interchangeable:

| Signal | Meaning | Must not be presented as |
|---|---|---|
| Linguist byte fraction | Code language present after GitHub's normal exclusions | Authorship or proficiency |
| Topic or README mention | Project self-description | Demonstrated implementation |
| Direct dependency | Technology is declared by the project | Personal expertise |
| Workflow and successful run | Automation exists and ran at a revision | Overall code quality |
| OpenSSF Scorecard check | Versioned security-practice assessment | Developer skill score |
| Stars and forks | Popularity and reuse | Quality |
| Commit attribution | Identity linked to changes | Authorship of the whole repository |

The versioned `lucidresume-repository-assessment` has separate originality,
longevity, engineering-process, documentation, and delivery dimensions. Stars
and forks are retained as popularity observations but do not raise evidence
strength. Forks receive a strong authorship discount unless separate contribution
attribution exists. The assessment ranks evidence for retrieval; it is not a
general code-quality or personal-skill score and is not promoted into accepted
JobML claims.

The audit retains archived and small repositories as dated observations rather
than erasing them. Fork contents and repositories below the configured evidence
threshold cannot nominate personal skills or résumé projects by default. The
observation timestamp keeps age, recency, and assessment results reproducible.
In a full career-record export they remain under `lucidresume.github`, but the
core claim builder and résumé projector exclude them from personal claims.
Repository and package-family project IDs are derived from provider identities,
not generated afresh on import. Re-import refreshes an existing observation in
place so repository activity and assessment metadata cannot silently go stale.

### Package registry evidence

The NuGet audit combines package IDs from the public owner profile with metadata
from the V3 `SearchQueryService`. It groups related IDs by a normalised source
repository or stable package-family prefix. This avoids treating a product with
dozens of modular packages as dozens of unrelated projects.

Each family becomes one project-shaped ledger record containing package count,
cumulative registry downloads at observation time, representative IDs, tags,
and its source repository when published. Downloads describe public registry
activity. They do not establish code quality, authorship, proficiency, or current
usage. Package families participate in semantic project retrieval and only enter
a short résumé when their terms match the target role.

The JobML GitHub extension adds immutable revision capture, observation time,
contributor attribution, direct dependency and workflow evidence, GitHub
community metrics, and versioned OpenSSF Scorecard checks. Unknown or inaccessible
signals stay `unknown`; they do not become negative evidence. See
[`jobml-github-extension-0.1.md`](jobml-github-extension-0.1.md).

---

## 3. Skill Graph & Communities

Skills form a graph where co-occurrence in the same role creates edges.

```
     ┌──── .NET ────── C# ─────── ASP.NET Core
     │         \                /
  Azure ───── Kubernetes ──── Docker
     │
  Cosmos DB ── SQL Server ── PostgreSQL
```

### Community Detection

Louvain algorithm clusters skills into natural neighborhoods:

- **Community 0**: .NET, C#, ASP.NET Core, Azure → ".NET Cloud"
- **Community 1**: Kubernetes, Docker, CI/CD, Bicep → "DevOps/Infrastructure"
- **Community 2**: ML.NET, OpenAI, LLM, RAG → "AI/ML"
- **Community 3**: Engineering Management, Agile, Team Building → "Leadership"

### Applications

| Use | How |
|-----|-----|
| **Search suggestions** | Generate queries from strongest community's top skills |
| **Gap analysis** | "You need 2 skills to enter the AI/ML community" |
| **Bridge roles** | Find roles that combine your strong + target communities |
| **Career pathing** | Minimum-cost path through skill space to target cluster |

---

## 4. Matching & Career Planning

### Resume ↔ JD Matching

Both sides produce skill ledgers. Accepted concepts and audited equivalences
determine coverage; cosine similarity can retrieve adjacent evidence:

```
Resume Ledger          JD Ledger
[C# : 0.85]          [C# : Required]     → cosine = 0.95 ✓
[Azure : 0.72]        [AWS : Required]     → cosine = 0.61 ✗ (near miss)
[Docker : 0.60]       [Kubernetes : Req]   → adjacent; requirement remains a gap
[??? : 0.00]          [Payment : Required] → cosine = 0.00 ✗ (gap)

Semantic Evidence Coverage = (required_coverage × 0.7) + (preferred_coverage × 0.3)
```

This percentage describes term-to-evidence coverage only. It is not an eligibility,
seniority, or overall job-fit probability. Degree, prior-title, scale and other hard
requirements must be presented separately as explicit gates or gaps.

### Career Planner

For each unmatched skill, classifies the gap:

| Gap Type | What it means | Effort | Example |
|----------|---------------|--------|---------|
| **PresentationGap** | Existing evidence needs clearer presentation | Low | Existing Kubernetes work needs a clearer account |
| **WeakEvidence** | You have it but thin evidence | Low | Add specific K8s achievements |
| **AdjacentSkill** | Related skills in same graph community | Medium | Side project with the target skill |
| **TrueGap** | Not in your skill graph at all | High | Dedicated learning needed |

### Semantic Compression

For role-specific projection, the compressor queries the ledger:

```
JD requires: C#, Azure, Kubernetes, Payment systems, AI/ML

Skill Ledger query:
  C# → evidence in 9 roles, pick top 3 by strength
  Azure → evidence in 5 roles, pick top 3
  Kubernetes → evidence in 2 roles, pick both
  Payment systems → evidence in 1 role (Very Jane / Stripe)
  AI/ML → evidence in 2 roles (mostlylucid / Azure OpenAI)

Result: 6 relevant roles out of 13 total
Each role includes only evidence-backed bullets

13 roles → 6 roles → deterministic ledger projection → targeted resume
```

### Adaptive Query Widening

When searches return few results:

```
Level 0: "ASP.NET Core Azure Kubernetes CTO"           → 3 results
Level 1: "Azure Kubernetes CTO"                         → 12 results (dropped specific)
Level 2: "Azure Kubernetes Docker CI/CD"                → 25 results (synonym substitution)
Level 3: "Freelance CTO Technical Consultant"           → 40 results (role-title query)
Level 4: "ML.NET OpenAI LLM"                            → 8 results (adjacent community)
```

---

## 5. AI Providers

Four interchangeable providers, selected at startup via config:

```json
{
  "Tailoring": { "Provider": "llamasharp" },
  "LlamaSharp": { "ModelPath": "models/grug-9b-Q4_K_M.gguf", "GpuLayerCount": -1 },
  "Ollama": { "BaseUrl": "http://localhost:11434", "Model": "qwen3.5:4b" },
  "Anthropic": { "ApiKey": "...", "Model": "claude-haiku-4-5-20251001" },
  "OpenAi": { "ApiKey": "...", "Model": "gpt-4o-mini" }
}
```

All implement the existing `IAiTailoringService` and `ILlmExtractionService`
interfaces. `Tailoring` is retained as a configuration and compatibility name.
Providers may propose ingestion candidates or clearly labelled authoring drafts.
Their output is never accepted automatically.

The planner selects reviewed ledger records and approved human prose deterministically.
An optional source-bound editing stage calls a configured provider to tighten selected
passages. Validation rejects unsupported edits and retains the source wording.
Local reusable prose accounts bind text to skills, source fingerprints, evidence IDs,
model identity, editing instructions and the applicable job brief. Cache hits are
validated again; they never establish skill coverage. The product does not score prose for
whether it appears machine-written and does not offer detector-evasion rewriting.

---

## 6. Personal ATS (Pipeline)

Application tracking with stage pipeline:

```
Saved → Applied → Screening → Interview → Offer → Accepted
                                                 → Rejected
                                                 → Withdrawn
                                                 → Ghosted (auto: 30+ days no response)
```

### Email Integration

```
IMAP scan → classify (rule-based) → match to application → create timeline event
                                                         → auto-advance stage

Classification rules: confirmation, interview, rejection, offer, screening
Matching: sender domain → company, subject → job title, recruiter email → exact
Auto-advance: only forward, never past Offer, flagged IsAutoDetected
```

---

## 7. Persistence

Single SQLite database (`data.db`) in user's AppData:

| Table | Content |
|-------|---------|
| `resumes` | Original imported source records |
| `generated_resumes` | Saved projection metadata and archive references |
| `profile` | JSON blob - singleton |
| `jobs` | JSON blobs - one per job |
| `applications` | JSON blobs - one per tracked application |
| `saved_searches` | JSON blobs |
| `search_presets` | JSON blobs |
| `vec_embeddings` | 384-dim float vectors (sqlite-vec) |
| `vec_meta` | Embedding metadata (source, text) |
| `app_meta` | Key-value settings |

All mutations via `MutateAsync(Action<AppState>)` under `SemaphoreSlim` lock.

`app_meta.complete_career` stores `CompleteCareerRecord`: the definitive transcript,
import/override revisions, review warnings and derived semantic index. SQLite and
JSON stores bootstrap legacy imports once and reconcile changed imports at save
time. Removing an input archive does not retract accepted career facts. The full
JobML projection is materialised per career revision. Reviewed prose publication
uses revision checks to prevent stale overwrites.

Generated archives are separate from inputs and retain frozen job/source/resume
snapshots, DOCX, PDF, JobML, evidence JSON and rendered pages. Files are stored under
`generated-resumes`; derived wording is stored under `prose-library` in the app data
directory. See [complete career workflow](complete-career-workflow.md).


---

## 8. Data Flow: End-to-End

```
1. User imports resume (DOCX/PDF)
   │
   ├─→ Docling or PdfPig extracts markdown
   ├─→ NER detects entities (skills, names, dates)
   ├─→ Section parser builds structured data
   ├─→ Template learned for future imports
   │
   ▼
2. Skill Ledger built
   │
   ├─→ Skills extracted from Skills section, Technologies, Achievements
   ├─→ Reviewed imports reconcile into the saved complete career
   ├─→ Accepted claim concepts retain original role/source bindings
   ├─→ Years calculated, strength scored, consistency checked
   │
   ▼
3. User adds job description
   │
   ├─→ RRF fusion extracts: title, company, skills, salary
   ├─→ JD Skill Ledger built (required/preferred/inferred)
   │
   ▼
4. Matching
   │
   ├─→ Accepted concepts and audited equivalents determine coverage
   ├─→ Semantic retrieval identifies related evidence and honest adjacent gaps
   ├─→ Career plan: gap type, effort, impact per unmatched skill
   ├─→ Skill graph updated, communities re-detected
   │
   ▼
5. Role-specific projection
   │
   ├─→ Semantic compression: 13 roles → 6 relevant
   ├─→ Only evidence-backed bullets included
   ├─→ Markdown and JobML render from the same claim-ID selection
   ├─→ Export rejects stale ledger revisions
   │
   ▼
6. Career Planning
   │
   ├─→ Search suggestions from strongest communities
   ├─→ Adaptive widening when results are sparse
   ├─→ Bridge role identification
   ├─→ "What to learn/build next" recommendations
   │
   ▼
7. Tracking (Pipeline)
   │
   ├─→ Application stage progression
   ├─→ Email scanner auto-updates timeline
   ├─→ Funnel visualization
   └─→ Stale/ghosted detection
```

---

## 9. Module Dependency Graph

```
lucidRESUME (Avalonia UI)
    ├── Ingestion ──→ Core, Parsing
    ├── Extraction ──→ Core
    ├── Parsing ──→ Core
    ├── JobSpec ──→ Core
    ├── JobSearch ──→ Core
    ├── Matching ──→ Core
    ├── AI ──→ Core, Matching
    ├── EmailTracker ──→ Core
    ├── Export ──→ Core
    ├── Collabora (document tools)
    ├── Avalonia.UITesting ──→ (Avalonia)
    └── Core ──→ JobML (independent interchange models), Microsoft.Data.Sqlite, sqlite-vec
```

**Rule:** Everything depends inward on `Core`. `Core` has minimal external dependencies. Services are registered via DI extension methods (`AddXxx(config)`).

---

## 10. Configuration

All tuneable parameters are in `appsettings.json`:

| Section | Controls |
|---------|----------|
| `LlamaSharp` | Local GGUF model identity/path, download URL, context, GPU layers, generation limits |
| `Ollama` | LLM provider URL, model names, context window |
| `Anthropic` | API key, model selection |
| `OpenAi` | API key, base URL, model selection |
| `Tailoring` | Provider selection, term normalization threshold, optional career-anchor companies |
| `Embedding` | Provider (onnx/ollama), model path |
| `JdFusion` | RRF weights, confidence thresholds, NER min length |
| `Coverage` | Skill semantic threshold, keyword overlap |
| `Email` | IMAP host/port, scan days back, folders |
| `Docling` | Enabled flag, base URL |
| `GeneralNer` / `OnnxNer` | Model paths, confidence thresholds |

User secrets (`dotnet user-secrets`) for API keys. Environment variables with `LUCIDRESUME_` prefix.

---

## 11. Testing

597 tests across 12 projects at the 2026-10-12 validation checkpoint. Tests favour direct service instantiation and fixed
fixtures, with fake HTTP handlers where a provider boundary must be exercised.

| Project | What it tests |
|---------|--------------|
| Core.Tests | Persistence, models, multi-resume merge, parsing, and output export |
| Extraction.Tests | NER detector, recognizer pipeline, and date recognition |
| AI.Tests | Providers, embeddings, bounded editing, and deterministic projection |
| Matching.Tests | Skill scoring, filters, aspect voting, role profiles, and output quality |
| JobSpec.Tests | JD parsing, salary extraction |
| EmailTracker.Tests | Email classifier rules, application matcher |
| GitHub.Tests | Repository evidence, LinkedIn parsing, and document merging |
| JobML.Tests | Validation, drift, reversible links, and cJobML projection |
| Compiler.Tests | Deterministic evidence selection and projection orchestration |
| Web.Tests | ASP.NET Core endpoint and compiler control |
| App.Tests | Complete-career editor, role interviews and native Markdown rendering |
| Avalonia.UITesting.Tests | Locators, input, scripts, screenshots, and UI assertions |

### UX Testing

Built-in automation framework with YAML scripts:

```yaml
actions:
  - type: ImportFile
    value: test-data/resumes/Scott_Galloway_CTO.docx
  - type: PasteJob
    value: "@test-data/job-cto.txt"
  - type: Navigate
    value: jobs
  - type: Screenshot
    value: jobs-with-career-plan
```

---

## 12. Releases & Documentation Archives

Release automation lives in `.github/workflows/release.yml`. A `v*` tag builds self-contained app archives for Windows, macOS, and Linux across x64 and ARM64 runtime IDs:

| Runtime family | Runtime IDs |
|----------------|-------------|
| Windows | `win-x64`, `win-arm64` |
| macOS | `osx-x64`, `osx-arm64` |
| Linux | `linux-x64`, `linux-arm64` |

Each runtime produces both `.zip` and `.tar.gz` archives with SHA-256 checksum files. The GitHub release page is populated with Markdown notes covering usage, configuration, and macOS Gatekeeper guidance. The workflow also builds a documentation archive containing `lucidRESUME-docs-single-page.md`, which concatenates the README, architecture guide, in-app user manual, JobML specification, and GitHub extension into a single offline Markdown page. The deterministic JobML JSON Schema is included as a separate file.

Native installers are intentionally out of scope for the current release baseline. MSI, DMG, AppImage, Flatpak, or Snap packaging should be added only after signing, icons, and installer metadata are in place.

---

*Built with .NET 10, Avalonia 11, ONNX Runtime, MailKit, PdfPig, sqlite-vec. Local-first. No cloud required.*
