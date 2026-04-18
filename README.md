# ***lucid*RESUME**

> NOTE: THIS IS CURRENTLY A RESEARCH PREVIEW. IT SHOULD NOT BE 1.x YET. SORRY!


[![Mostlylucid.Avalonia.UITesting on NuGet](https://img.shields.io/nuget/v/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=Mostlylucid.Avalonia.UITesting)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)
[![NuGet downloads](https://img.shields.io/nuget/dt/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)

**Your job search. Your data. Your machine.**

***lucid*RESUME** is a free, open-source desktop app that builds a structured, evidence-based model of your skills from your actual work - then uses it to:

- **Tailor resumes** to specific job descriptions
- **Match you to relevant roles** with per-skill similarity scoring
- **Show what you're missing** (and what you're not)
- **Plan your next move** based on your actual skill graph

Everything runs locally on your machine. No accounts. No data leaving your device unless you choose to.

> Built with .NET 10 + Avalonia. Runs on Windows, macOS, and Linux.

---

## Core Idea

**Every skill is backed by evidence.**

***lucid*RESUME** doesn't just list what you *say* you know - it builds a **skill ledger** where each skill is tied to:

- **Where** it appeared (job, project, repo)
- **When** you used it (date ranges, calculated years)
- **How often** it shows up across roles
- **How strong** the evidence is (recency, frequency, confidence)

No invented skills. No guessing. Just structured inference over your actual work.

This is the foundation everything else builds on - matching, tailoring, gap analysis, career direction.

| | |
|---|---|
| ![DOCX Preview](docs/screenshots/docx-preview.png) | ![My Data Dashboard](docs/screenshots/my-data-page.png) |

---

## Why ***lucid*RESUME**?

Every major job site wants your email, your browsing history, and permission to sell your profile. AI resume tools send your CV to some SaaS vendor's cloud. Paid tools charge monthly for table-stakes features.

***lucid*RESUME** does things differently:

- **Local-first AI** - tailoring runs through [Ollama](https://ollama.ai) on your hardware. Or use Anthropic/OpenAI APIs if you prefer.
- **No account required** - data stored in a local SQLite database. You own it.
- **Honest tailoring** - the AI never invents skills or experience you don't have.
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

- Multi-vector cosine similarity between resume and JD skill ledgers
- 3-layer matching: substring -> embedding similarity -> achievement-text keyword search
- Per-skill match detail with similarity scores, evidence strength, calculated years

### Career Direction
- **Skill graph** with co-occurrence edges and Leiden community detection
- **Career planner**: 4 gap types (PresentationGap, WeakEvidence, AdjacentSkill, TrueGap)
- **Effort/impact ranking**: Low (rewording), Medium (side project), High (new learning)
- **Search query generator**: suggests job searches from your strongest skill communities

### AI Tailoring
- Rewrites resume for specific JDs using skill ledger evidence (not hallucination)
- Semantic compression: 13 roles -> 6 relevant -> filtered to evidence-backed bullets
- AI detection scorer (5 signals) + de-AI rewrite button
- Translation with sliding context and glossary

### Personal ATS (Pipeline)

![Pipeline tracking](docs/screenshots/pipeline-page.png)

- **Stage pipeline**: Saved -> Applied -> Screening -> Interview -> Offer -> Accepted/Rejected/Withdrawn/Ghosted
- Timeline per application, funnel visualization, stale detection
- **Email integration** (IMAP via MailKit): auto-detects confirmations, interviews, rejections, offers

### Job Search
Seven job board adapters searched in parallel (Adzuna, Reed, Findwork, Arbeitnow, JoinRise, Jobicy, Remotive). Near-duplicate detection via embedding similarity. Hoover role flagging.

### Export
JSON Resume (standard schema), Markdown, **DOCX** (Word via OpenXml), and **PDF** ([QuestPDF](https://www.questpdf.com/) — professional formatting, cross-platform).

### Documentation
- [Release & Archive Guide](docs/release.md) - release workflow, platform archives, and single-page docs archive.
- [Technical Architecture](docs/architecture.md) - modules, data flow, persistence, and extraction pipeline.
- [Document Layout Detection](docs/layout-detection.md) - DocLayNet YOLO model, structural hashing, template communities.
- [In-App User Manual](src/lucidRESUME/Resources/user-manual.md) - the same help content embedded in the desktop app.

### CLI (17 Commands)

```bash
lucidresume parse          --file cv.docx [--output result.json]
lucidresume evidence       --resume cv.docx [--output ledger.json]
lucidresume match          --resume cv.docx --job "JD text"
lucidresume compound-match --resume cv.docx --jobs-dir jds/
lucidresume explain        --resume cv.docx --job "JD text"
lucidresume tailor         --resume cv.docx --job "JD text" [--output tailored.md]
lucidresume drift          --resume1 old.docx --resume2 new.docx
lucidresume export         --file cv.docx --format pdf|docx|markdown|json
lucidresume validate       --resume cv.docx
lucidresume fix            --resume cv.docx [--output fixed.md]
lucidresume generate       --resume cv.docx --prompt "2 page cloud resume"
lucidresume anonymize      --resume cv.docx [--output anon.json]
lucidresume rank           --dir resumes/ --job "JD text"
lucidresume search         --prompt "senior .NET developer remote"
lucidresume extract-jd     --job "JD text" [--output jd.json]
lucidresume github-import  --username scottgal
lucidresume batch-test     --dir resumes/
```

---

## Getting Started

### Download & Install

1. Go to the [latest release](https://github.com/scottgal/lucidRESUME/releases/latest)
2. Download the archive for your platform:

| Platform | Download |
|----------|----------|
| **Windows** | `lucidRESUME-...-win-x64.zip` or `win-arm64.zip` |
| **macOS** | `lucidRESUME-...-osx-arm64.tar.gz` (Apple Silicon) or `osx-x64.tar.gz` (Intel) |
| **Linux** | `lucidRESUME-...-linux-x64.tar.gz` or `linux-arm64.tar.gz` |

3. Extract and run `lucidRESUME` (or `lucidRESUME.exe` on Windows)

That's it. ONNX models (~600MB) are downloaded automatically on first launch. No accounts, no setup wizards.

> **macOS users:** if Gatekeeper blocks the app, right-click → Open, or run `xattr -cr lucidRESUME.app` from Terminal.

### AI Tailoring (Optional)

AI tailoring is optional — everything else works without it.

**Option 1: Local AI with [Ollama](https://ollama.ai) (recommended)**
1. Install Ollama
2. Pull a model: `ollama pull qwen3.5:4b`
3. That's it — lucidRESUME connects to `localhost:11434` automatically

**Option 2: Cloud AI (Anthropic or OpenAI)**
1. Open the **Profile** page in the app
2. Enter your API key and select the provider

### Build from Source

For developers who want to build from source:

```bash
git clone https://github.com/scottgal/lucidRESUME
cd lucidRESUME
dotnet run --project src/lucidRESUME/lucidRESUME.csproj
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download). See [docs/release.md](docs/release.md) for the release workflow.

---

## How It Works (Technical)

### Extraction

**5-layer RRF fusion** for both resume and JD extraction: structural patterns + ONNX NER (2 models) + **skill taxonomy centroids** (19,983 skills from 1.3M LinkedIn jobs) + LLM backstop + **entity lookup** (11K companies, 7K locations). All signals run in parallel, fused by reciprocal rank fusion with multi-source confidence boosting.

**Skill taxonomy**: preloaded from Kaggle datasets via DuckDB — 16 role archetypes, cross-industry coverage (not just tech), priority classification. Used by both resume AND JD parsers to find skills embedded in prose. Plain-text JDs went from 0 to 60+ extracted skills.

ONNX embeddings (`all-MiniLM-L6-v2`, 384-dim) power semantic matching throughout. **[DocLayNet YOLO model](docs/layout-detection.md)** detects document structure from rendered page images — titles, section headers, tables, lists — producing a structural hash for template identification. Docling (Docker) adds ML-based PDF layout detection for complex documents; PdfPig with column detection as local fallback.

### Architecture

```
lucidRESUME (Avalonia UI - 9 pages: My CV, My Data, Career, Jobs, Add Job, Apply, Pipeline, Profile, Help)
    ├── Ingestion        Resume parsing, DocLayNet layout detection, Morph preview, LinkedIn import
    ├── Extraction       ONNX NER (2 models) + Microsoft.Recognizers pipeline
    ├── Parsing          DOCX/PDF/TXT extraction, ATS pattern detection, template learning
    ├── JobSpec          JD parsing (5-layer RRF: Structural + NER + Taxonomy + LLM + Entity), URL scraping
    ├── JobSearch        7 job board adapters + orchestrator + deduplicator
    ├── Matching         Skill ledger, skill graph, career planner, taxonomy centroids, entity lookup
    ├── AI               Ollama/Anthropic/OpenAI providers, AI detection, de-AI, translation
    ├── EmailTracker     IMAP scanning, email classification, application matching
    ├── Export           JSON Resume + Markdown + DOCX + PDF exporters
    ├── Collabora        LibreOffice/editor integration, document openers
    ├── UXTesting        UI automation framework (REPL, MCP, script runner)
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
dotnet test    # 183+ tests across 7 projects
```

| Project | Tests | Coverage |
|---------|-------|----------|
| Core.Tests | 47 | Persistence, models, multi-resume, round-trip |
| Extraction.Tests | 23 | NER, recognizers, RRF fusion pipeline |
| AI.Tests | 16 | Embeddings, matching |
| Matching.Tests | 50 | Skill scoring, filters, voting, quality word lists |
| JobSpec.Tests | 3 | JD parsing, salary extraction |
| EmailTracker.Tests | 25 | Classifier, matcher |
| GitHub.Tests | 19 | Language map, LinkedIn parser, document merger |

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

- [x] Resume improvement UX - synthesized suggestions
- [x] Automated job polling from skill community search queries
- [x] Resume extraction RRF fusion (multi-source confidence boost, same pattern as JD)
- [x] Career planner UI page with gap analysis visualization
- [x] Leiden community detection (refinement phase over Louvain greedy moves)
- [x] Temporal skill drift across resume variants (compare ledgers, detect added/dropped/changed skills)
- [x] DOCX export of tailored resumes (pure C# via OpenXml, cross-platform)
- [x] PDF export of tailored resumes (QuestPDF, professional formatting)
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
