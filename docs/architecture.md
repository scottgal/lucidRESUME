# ***lucid*RESUME** — Technical Architecture

## System Overview

lucidRESUME is a local-first career navigation engine. It ingests resumes and job descriptions, builds evidence-backed skill profiles, matches candidates to roles via multi-vector similarity, and generates actionable career plans — all on the user's own hardware.

The core insight: **skills are not flat keywords**. They have evidence (which job, which bullet, what dates), strength (years, recency, depth), and relationships (co-occurrence in roles creates a graph). Matching isn't string comparison — it's finding the nearest point in a high-dimensional skill space.

```
┌──────────────────────────────────────────────────────────┐
│                    lucidRESUME Desktop                    │
│  ┌─────┐ ┌─────┐ ┌───────┐ ┌───────┐ ┌────────┐ ┌────┐ │
│  │My CV│ │Jobs │ │Add Job│ │ Apply │ │Pipeline│ │Prof│ │
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

---

## 1. Extraction Pipeline

### Design Principle: RRF Signal Fusion

Every extractor runs in parallel. Each produces candidates with confidence scores. Reciprocal Rank Fusion picks the best answer per field. No waterfall, no fallthrough — all signals vote.

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

### Semantic Skill Matching

Skills in achievement bullets are matched both by substring AND embedding similarity:

```
Achievement: "Deployed Docker Compose for container orchestration"
Skills list includes: "Kubernetes"

Substring match: "Kubernetes" not in text → miss
Embedding match: embed("Deployed Docker Compose for container orchestration")
                  vs embed("Kubernetes")
                  cosine = 0.78 → match at 0.78 × 0.8 = 0.62 confidence
```

This catches: "K8s" ↔ "Kubernetes", ".NET Framework" ↔ ".NET 8", "Stripe Connect" ↔ "Payment systems"

### Consistency Checking

The ledger automatically flags:
- **Unsubstantiated skills**: listed in Skills section but never mentioned in experience
- **Years mismatch**: claims "10+ years Python" but calculated evidence shows 3 years
- **Stale skills**: last used 5+ years ago

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

Both sides produce skill ledgers. Matching is multi-vector cosine similarity:

```
Resume Ledger          JD Ledger
[C# : 0.85]          [C# : Required]     → cosine = 0.95 ✓
[Azure : 0.72]        [AWS : Required]     → cosine = 0.61 ✗ (near miss)
[Docker : 0.60]       [Kubernetes : Req]   → cosine = 0.78 ✓
[??? : 0.00]          [Payment : Required] → cosine = 0.00 ✗ (gap)

Overall Fit = (required_coverage × 0.7) + (preferred_coverage × 0.3)
```

### Career Planner

For each unmatched skill, classifies the gap:

| Gap Type | What it means | Effort | Example |
|----------|---------------|--------|---------|
| **PresentationGap** | You have adjacent skill, surface it differently | Low | "Docker Compose" → show for "Kubernetes" |
| **WeakEvidence** | You have it but thin evidence | Low | Add specific K8s achievements |
| **AdjacentSkill** | Related skills in same graph community | Medium | Side project with the target skill |
| **TrueGap** | Not in your skill graph at all | High | Dedicated learning needed |

### Semantic Compression

For tailoring, the compressor queries the ledger:

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

13 roles → 6 roles → LLM polishes → 2-page targeted resume
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

Three interchangeable providers, selected at startup via config:

```json
{
  "Tailoring": { "Provider": "ollama" },  // or "anthropic" or "openai"
  "Ollama": { "BaseUrl": "http://localhost:11434", "Model": "qwen3.5:4b" },
  "Anthropic": { "ApiKey": "...", "Model": "claude-haiku-4-5-20251001" },
  "OpenAi": { "ApiKey": "...", "Model": "gpt-4o-mini" }
}
```

All implement `IAiTailoringService` and `ILlmExtractionService`. Prompt construction (`TailoringPromptBuilder`) is provider-agnostic.

### AI Detection (5 Signals)

| Signal | How | Weight |
|--------|-----|--------|
| **Embedding variance** | Cosine similarity clustering of bullet embeddings | 30% |
| **Stylometric** | Buzzword density, sentence uniformity, action verb patterns | 25% |
| **Lexical diversity** | Type-token ratio | 15% |
| **LLM judge** | Ask the LLM "rate 0-100 how AI-generated" | 15% |
| **ONNX detector** | RoBERTa classifier (opt-in, 126MB) | 15% |

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
| `resume` | JSON blob — singleton |
| `profile` | JSON blob — singleton |
| `jobs` | JSON blobs — one per job |
| `applications` | JSON blobs — one per tracked application |
| `saved_searches` | JSON blobs |
| `search_presets` | JSON blobs |
| `vec_embeddings` | 384-dim float vectors (sqlite-vec) |
| `vec_meta` | Embedding metadata (source, text) |
| `app_meta` | Key-value settings |

All mutations via `MutateAsync(Action<AppState>)` under `SemaphoreSlim` lock.

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
   ├─→ Semantic matching links skills to experience date ranges
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
   ├─→ Multi-vector cosine similarity between ledgers
   ├─→ Career plan: gap type, effort, impact per unmatched skill
   ├─→ Skill graph updated, communities re-detected
   │
   ▼
5. Tailoring
   │
   ├─→ Semantic compression: 13 roles → 6 relevant
   ├─→ Only evidence-backed bullets included
   ├─→ LLM polishes pre-filtered content
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
    ├── Collabora ──→ Core
    ├── UXTesting ──→ (Avalonia)
    └── Core ──→ Microsoft.Data.Sqlite, sqlite-vec
```

**Rule:** Everything depends inward on `Core`. `Core` has minimal external dependencies. Services are registered via DI extension methods (`AddXxx(config)`).

---

## 10. Configuration

All tuneable parameters are in `appsettings.json`:

| Section | Controls |
|---------|----------|
| `Ollama` | LLM provider URL, model names, context window |
| `Anthropic` | API key, model selection |
| `OpenAi` | API key, base URL, model selection |
| `Tailoring` | Provider selection, term normalization threshold |
| `Embedding` | Provider (onnx/ollama), model path |
| `JdFusion` | RRF weights, confidence thresholds, NER min length |
| `Coverage` | Skill semantic threshold, keyword overlap |
| `Email` | IMAP host/port, scan days back, folders |
| `Docling` | Enabled flag, base URL |
| `GeneralNer` / `OnnxNer` | Model paths, confidence thresholds |

User secrets (`dotnet user-secrets`) for API keys. Environment variables with `LUCIDRESUME_` prefix.

---

## 11. Testing

159 tests across 6 projects. No mocking frameworks — direct service instantiation.

| Project | What it tests |
|---------|--------------|
| Core.Tests | AppState persistence, JobApplication model, SQLite round-trip |
| Extraction.Tests | NER detector, recognizer pipeline |
| AI.Tests | Embedding service, similarity scoring |
| Matching.Tests | Skill scoring, filters, aspect voting |
| JobSpec.Tests | JD parsing, salary extraction |
| EmailTracker.Tests | Email classifier rules, application matcher |

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

*Built with .NET 10, Avalonia 11, ONNX Runtime, MailKit, PdfPig, sqlite-vec. Local-first. No cloud required.*
