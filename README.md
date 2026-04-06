# ***lucid*RESUME**

**Your job search. Your data. Your machine. Your career.**

***lucid*RESUME** is a free, open-source desktop app that helps you find jobs, tailor your resume, track applications, and plan your career — entirely on your own computer. No accounts. No subscriptions. No data leaving your machine unless you want it to.

> Built with .NET 10 + Avalonia. Runs on Windows, macOS, and Linux.

---

## Screenshots

| Resume Import | Jobs & Matching |
|--------------|-----------------|
| ![Resume page](docs/screenshots/resume-page.png) | ![Jobs page](docs/screenshots/jobs-page.png) |

| Add Job | Apply & Tailor |
|---------|---------------|
| ![Add Job](docs/screenshots/add-job-page.png) | ![Apply page](docs/screenshots/apply-page.png) |

| Pipeline (ATS) | Profile & AI Settings |
|----------------|----------------------|
| ![Pipeline](docs/screenshots/pipeline-page.png) | ![AI Settings](docs/screenshots/ai-settings.png) |

---

## Why lucidRESUME?

Every major job site wants your email, your browsing history, and permission to sell your profile. AI resume tools send your CV to some SaaS vendor's cloud. Paid tools charge monthly for table-stakes features.

lucidRESUME does things differently:

- **Local-first AI** — tailoring runs through [Ollama](https://ollama.ai) on your hardware. Or use Anthropic/OpenAI APIs if you prefer.
- **No account required** — data stored in a local SQLite database. You own it.
- **Honest tailoring** — the AI never invents skills or experience you don't have.
- **Career navigation** — not just "match this job" but "what to do next to reach your target cluster".
- **Free forever** — Unlicense. Public domain.

---

## Features

### Resume Import & Extraction
- Import PDF or DOCX resumes
- **Multi-signal RRF fusion extraction**: structural analysis + ONNX NER (2 models) + LLM recovery + regex patterns — all run in parallel, fused by reciprocal rank fusion
- Dual NER: `dslim/bert-base-NER` (names, orgs) + `yashpwr/resume-ner-bert-v2` (skills, degrees, job titles)
- ONNX embeddings (`all-MiniLM-L6-v2`, 384-dim) for semantic matching
- **Docling integration** (Docker) for ML-based PDF layout detection — handles two-column resumes, LaTeX CVs, complex formatting
- PdfPig with column detection as local fallback
- Template learning: learns your resume's structure on first parse, 100% confidence on subsequent imports
- **Multilingual**: German, French, Spanish, Portuguese, Chinese language detection with section keyword maps

### Skill Ledger
Every skill claim is backed by evidence:
- **Provenance chain**: skill → which job → which date range → which bullet point
- **Calculated years**: sum of non-overlapping date ranges where the skill appears
- **Evidence strength**: combining years, role count, recency, confidence
- **Consistency checking**: flags skills listed but never demonstrated, claimed years vs calculated years
- **Presentation gap vs true gap**: "you have Kubernetes adjacent skills" vs "you don't have this at all"

### AI Detection & De-AI
- **5-signal AI detection scorer**: embedding variance, stylometric analysis, lexical diversity, local LLM judge, ONNX RoBERTa classifier (opt-in)
- **De-AI rewrite button**: rewrites AI-sounding bullets to be more human and specific via LLM
- Score shown as banner on resume page with per-signal findings

### Job Description Parsing
- **Parallel signal fusion** (same as resume): Structural + NER + LLM extractors run simultaneously
- Handles pipe-delimited, em-dash, and heading-based JD formats
- Salary, remote/hybrid, years of experience detection
- **Configurable fusion weights** in `appsettings.json` (`JdFusion` section)

### Smart Matching & Career Planning
- **Skill ledger matching**: multi-vector cosine similarity between resume and JD skill ledgers
- **Skill graph** with co-occurrence edges and Louvain community detection
- **Career planner**: computes minimum-cost path through skill space
  - 4 gap types: PresentationGap, WeakEvidence, AdjacentSkill, TrueGap
  - 3 effort levels: Low (rewording), Medium (side project), High (new learning)
  - Recommendations ranked by impact/effort ratio
- **Search query generator**: suggests job search queries from your strongest skill communities

### Personal ATS (Pipeline)
Track applications through your job hunt:
- **Stage pipeline**: Saved → Applied → Screening → Interview → Offer → Accepted/Rejected/Withdrawn/Ghosted
- **Timeline**: chronological events per application (stage changes, emails, notes)
- **Funnel visualization**: horizontal bar showing application flow by stage
- **Stale detection**: flags applications with no activity for 14+ days
- **Ghosted auto-detection**: 30+ days in Applied → auto-marked as Ghosted

### Email Integration
- **IMAP scanner** (MailKit) — connects to Gmail, Outlook, etc.
- **Rule-based email classifier**: detects confirmations, interview invites, rejections, offers
- **Email-to-application matcher**: matches emails by sender domain, subject, recruiter email
- Auto-creates timeline events and advances stages (flagged as auto-detected)

### AI Provider Selection
- **Ollama** (default, local) — any installed model
- **Anthropic API** — Claude Haiku/Sonnet/Opus
- **OpenAI API** — GPT-4o, GPT-4o-mini, etc.
- Dynamic model listing from each provider
- Cost estimates per model
- Configurable via Profile page or `appsettings.json`

### Translation
- Translate resume to any language via LLM
- **Sliding context with glossary**: maintains term consistency across sections
- Technical terms (Kubernetes, ASP.NET, etc.) preserved in English

### Document Openers
Auto-detects installed editors:
- LibreOffice, Microsoft Word, WPS Office, ONLYOFFICE
- macOS native: Preview, Pages, TextEdit, System Default
- Split button with dropdown for multiple options

### Job Search
Seven job board adapters, searched in parallel:

| Source | Auth | Notes |
|--------|------|-------|
| Adzuna | API key | UK, US, DE, FR, etc. |
| Reed | API key | UK |
| Findwork | API key | Global |
| Arbeitnow | None | EU focus |
| JoinRise | None | |
| Jobicy | None (RSS) | Remote |
| Remotive | None (RSS) | Remote |

### Export
- **JSON Resume** — standard schema
- **Markdown** — clean, structured

---

## Architecture

```
lucidRESUME (Avalonia UI — 6 pages: My CV, Jobs, Add Job, Apply, Pipeline, Profile)
    ├── Ingestion        Resume parsing, Docling client, image cache
    ├── Extraction       ONNX NER (2 models) + Microsoft.Recognizers pipeline
    ├── Parsing          DOCX/PDF/TXT extraction, ATS pattern detection, template learning
    ├── JobSpec          JD parsing (RRF fusion: Structural + NER + LLM), URL scraping
    ├── JobSearch        7 job board adapters + orchestrator + deduplicator
    ├── Matching         Skill ledger, skill graph, career planner, coverage analysis
    ├── AI               Ollama/Anthropic/OpenAI providers, AI detection, de-AI, translation
    ├── EmailTracker     IMAP scanning, email classification, application matching
    ├── Export           JSON Resume + Markdown exporters
    ├── Collabora        LibreOffice/editor integration, document openers
    ├── UXTesting        UI automation framework (REPL, MCP, script runner)
    └── Core             Domain models, interfaces, persistence (SQLite + sqlite-vec)
```

**Dependency rule:** everything depends inward on `Core`. `Core` depends only on `Microsoft.Data.Sqlite` and `sqlite-vec`.

### Key Design Patterns

| Pattern | Where | Why |
|---------|-------|-----|
| **RRF Signal Fusion** | Resume + JD extraction | Multiple extractors vote, best candidate wins |
| **Skill Ledger** | Matching module | Every skill backed by evidence with provenance |
| **Skill Graph + Communities** | Career planner | Louvain clustering reveals skill neighborhoods |
| **Template Learning** | DOCX parser | First parse learns structure, subsequent parses are deterministic |
| **ATS Pattern Detection** | PDF parser | YAML rulesets identify resume templates/ATS systems |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- (Optional) [Ollama](https://ollama.ai) — `ollama pull qwen3.5:4b` for AI tailoring
- (Optional) Docker — for Docling PDF layout detection
- (Optional) LibreOffice — for document preview and "Open in..." button

ONNX models (NER + embeddings) are **auto-downloaded on first run** (~600MB total).

### Build & Run

```bash
git clone https://github.com/scottgal/lucidRESUME
cd lucidRESUME
dotnet run --project src/lucidRESUME/lucidRESUME.csproj
```

### Configure AI Provider

**Ollama (default, local):**
```bash
ollama pull qwen3.5:4b
# App uses http://localhost:11434 by default
```

**Anthropic or OpenAI:**
```bash
dotnet user-secrets --project src/lucidRESUME set "Anthropic:ApiKey" "sk-ant-..."
dotnet user-secrets --project src/lucidRESUME set "OpenAi:ApiKey" "sk-..."
```

Then set `Tailoring.Provider` to `"anthropic"` or `"openai"` in `appsettings.json` or via the Profile page.

### Enable Docling (better PDF parsing)

```bash
docker run -d --name docling-serve -p 5001:5001 quay.io/docling-project/docling-serve:latest
```

Set `Docling.Enabled = true` in `appsettings.json`. PDFs will use ML-based layout detection.

### CLI

```bash
# Parse a resume
dotnet run --project src/lucidRESUME.Cli -- parse --file cv.pdf --output result.json

# Analyse resume vs job description
dotnet run --project src/lucidRESUME.Cli -- analyse --resume cv.docx --job "$(cat jd.txt)"

# Export as Markdown
dotnet run --project src/lucidRESUME.Cli -- export --file cv.docx --format markdown
```

---

## UX Testing

Built-in UI automation for scripted testing:

```bash
# Run a YAML test script
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- \
  --ux-test --script ux-scripts/e2e-full-flow.yaml --output ux-screenshots

# Interactive REPL
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-repl

# MCP server (for LLM-driven UI control)
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-mcp
```

Test actions include: `Navigate`, `Click`, `TypeText`, `Scroll`, `Screenshot`, `ImportFile`, `PasteJob`, `Assert`, `Svg`.

---

## Tests

```bash
dotnet test    # 159 tests across 6 projects
```

| Project | Tests | Coverage |
|---------|-------|----------|
| Core.Tests | 44 | Persistence, models, round-trip |
| Extraction.Tests | 23 | NER, recognizers, pipeline |
| AI.Tests | 16 | Embeddings, matching |
| Matching.Tests | 48 | Skill scoring, filters, voting |
| JobSpec.Tests | 3 | JD parsing, salary extraction |
| EmailTracker.Tests | 25 | Classifier, matcher |

---

## Roadmap

- [ ] Refactor resume extraction to full RRF fusion (same pattern as JD extraction)
- [ ] Resume improvement UX — synthesized suggestions instead of raw findings list
- [ ] Career planner UI page with gap analysis visualization
- [ ] Automated job polling from skill community search queries
- [ ] Leiden community detection (upgrade from Louvain)
- [ ] Temporal skill drift comparison across resume variants
- [ ] DOCX/PDF export of tailored resumes
- [ ] LinkedIn JSON import
- [ ] DocLayNet ONNX model for offline PDF layout detection (no Docker)

---

## Contributing

PRs welcome. Run the tests before submitting:

```bash
dotnet test
```

The codebase follows a strict inward dependency rule — keep domain logic in `Core` and wire everything in the app shell.

---

## License

This is free and unencumbered software released into the public domain.
See [LICENSE](LICENSE) or [unlicense.org](https://unlicense.org) for details.

**No strings attached. No attribution required. Use it however you like.**
