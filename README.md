# ***lucid*RESUME**

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
- Import PDF or DOCX resumes
- Handles two-column, LaTeX, complex formatting
- Template learning: learns your resume's structure on first parse, deterministic on subsequent imports
- Multilingual: German, French, Spanish, Portuguese, Chinese, Dutch, Japanese, Korean

### Skill Ledger
- **Provenance chain**: skill -> which job -> which date range -> which bullet point
- **Calculated years**: sum of non-overlapping date ranges where the skill appears
- **Evidence strength**: combining years, role count, recency, confidence
- **Consistency checking**: flags skills listed but never demonstrated, claimed vs calculated years
- **Presentation gap vs true gap**: "you have adjacent skills" vs "you don't have this at all"

### Smart Matching
- Multi-vector cosine similarity between resume and JD skill ledgers
- 3-layer matching: substring -> embedding similarity -> achievement-text keyword search
- Per-skill match detail with similarity scores, evidence strength, calculated years

### Career Direction
- **Skill graph** with co-occurrence edges and Louvain community detection
- **Career planner**: 4 gap types (PresentationGap, WeakEvidence, AdjacentSkill, TrueGap)
- **Effort/impact ranking**: Low (rewording), Medium (side project), High (new learning)
- **Search query generator**: suggests job searches from your strongest skill communities

### AI Tailoring
- Rewrites resume for specific JDs using skill ledger evidence (not hallucination)
- Semantic compression: 13 roles -> 6 relevant -> filtered to evidence-backed bullets
- AI detection scorer (5 signals) + de-AI rewrite button
- Translation with sliding context and glossary

### Personal ATS (Pipeline)
- **Stage pipeline**: Saved -> Applied -> Screening -> Interview -> Offer -> Accepted/Rejected/Withdrawn/Ghosted
- Timeline per application, funnel visualization, stale detection
- **Email integration** (IMAP via MailKit): auto-detects confirmations, interviews, rejections, offers

### Job Search
Seven job board adapters searched in parallel (Adzuna, Reed, Findwork, Arbeitnow, JoinRise, Jobicy, Remotive). Near-duplicate detection via embedding similarity. Hoover role flagging.

### Export
JSON Resume (standard schema) and Markdown.

### Documentation
- [Release & Archive Guide](docs/release.md) - release workflow, platform archives, and single-page docs archive.
- [Technical Architecture](docs/architecture.md) - modules, data flow, persistence, and extraction pipeline.
- [In-App User Manual](src/lucidRESUME/Resources/user-manual.md) - the same help content embedded in the desktop app.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- (Optional) [Ollama](https://ollama.ai) - `ollama pull qwen3.5:4b` for AI tailoring
- (Optional) Docker - for Docling PDF layout detection
- (Optional) LibreOffice - for document preview

ONNX models (NER + embeddings) are **auto-downloaded on first run** (~600MB total).

### Build & Run

```bash
git clone https://github.com/scottgal/lucidRESUME
cd lucidRESUME
dotnet run --project src/lucidRESUME/lucidRESUME.csproj
```

### Release Archives

Tagged releases publish self-contained app archives from GitHub Actions. Download the archive for your platform, extract it, and run the `lucidRESUME` executable inside:

| Platform | Runtime IDs |
|----------|-------------|
| Windows | `win-x64`, `win-arm64` |
| macOS | `osx-x64`, `osx-arm64` |
| Linux | `linux-x64`, `linux-arm64` |

Each runtime is published as both `.zip` and `.tar.gz`, with `.sha256` checksum files. The GitHub release page includes basic usage, configuration, and macOS Gatekeeper guidance. Releases also include a documentation archive containing `lucidRESUME-docs-single-page.md` for offline reading.

Maintainers: see [docs/release.md](docs/release.md) for the release workflow and archive policy.

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

## How It Works (Technical)

### Extraction

**Multi-signal RRF fusion**: structural analysis + ONNX NER (2 models: `dslim/bert-base-NER` + `yashpwr/resume-ner-bert-v2`) + LLM recovery + regex patterns - all run in parallel, fused by reciprocal rank fusion. Same pattern for both resume and JD extraction.

ONNX embeddings (`all-MiniLM-L6-v2`, 384-dim) power semantic matching throughout. Docling (Docker) adds ML-based PDF layout detection for complex documents; PdfPig with column detection as local fallback.

### Architecture

```
lucidRESUME (Avalonia UI - 6 pages: My CV, Jobs, Add Job, Apply, Pipeline, Profile)
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

## Tests

```bash
dotnet test    # 161 tests across 6 projects
```

| Project | Tests | Coverage |
|---------|-------|----------|
| Core.Tests | 44 | Persistence, models, round-trip |
| Extraction.Tests | 23 | NER, recognizers, pipeline |
| AI.Tests | 16 | Embeddings, matching |
| Matching.Tests | 50 | Skill scoring, filters, voting, quality word lists |
| JobSpec.Tests | 3 | JD parsing, salary extraction |
| EmailTracker.Tests | 25 | Classifier, matcher |

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

---

## Roadmap

- [ ] Resume extraction full RRF fusion (same pattern as JD extraction)
- [ ] Resume improvement UX - synthesized suggestions
- [ ] Career planner UI page with gap analysis visualization
- [ ] Automated job polling from skill community search queries
- [ ] Leiden community detection (upgrade from Louvain)
- [ ] Temporal skill drift across resume variants
- [ ] DOCX/PDF export of tailored resumes
- [ ] LinkedIn JSON import
- [ ] DocLayNet ONNX model for offline PDF layout detection (no Docker)

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
