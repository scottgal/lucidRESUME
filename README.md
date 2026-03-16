# lucidRESUME

**Your job search. Your data. Your machine.**

lucidRESUME is a free, open-source desktop app that helps you find jobs, tailor your resume, and track applications — entirely on your own computer. No accounts. No subscriptions. No data leaving your machine unless you want it to.

> Built with .NET 10 + Avalonia. Runs on Windows, macOS, and Linux.

---

## Why lucidRESUME?

Every major job site wants your email address, your browsing history, and permission to sell your profile to recruiters. AI resume tools send your CV to some SaaS vendor's cloud. Paid tools charge monthly for features that should be table stakes.

lucidRESUME does things differently:

- **Local AI** — LLM tailoring runs through [Ollama](https://ollama.ai) on your own hardware. Your resume never leaves your machine.
- **No account required** — data is stored in a plain JSON file in your AppData folder. You own it.
- **Honest tailoring** — the AI prompt explicitly instructs the model not to invent skills or experience you don't have.
- **Free forever** — Unlicense. Public domain. Do whatever you want with it.

---

## Screenshots

| My CV | Profile |
|-------|---------|
| ![My CV page](ux-screenshots/resume-libreoffice.png) | ![Profile page](ux-screenshots/profile-current.png) |

---

## Features

### Resume Import & Parsing
- Import PDF or DOCX resumes
- Multi-stage extraction pipeline: pattern matching → Microsoft Recognizers → ONNX NER → LLM fallback
- Extracts name, contact info, work history, education, skills, certifications, projects
- Structures everything into a rich internal model with confidence scores and source metadata

### Document Preview
- Page-by-page visual preview via Docling (if running) or LibreOffice (if installed)
- Auto-detects installed editors: LibreOffice, Microsoft Word, WPS Office, ONLYOFFICE
- "Open in…" split button with dropdown — opens in whichever editor you have

### Job Search
Seven job board adapters, searched in parallel:

| Source | Auth |
|--------|------|
| Adzuna | API key |
| Reed | API key |
| Findwork | API key |
| Arbeitnow | None |
| JoinRise | None |
| Jobicy | None (RSS) |
| Remotive | None (RSS) |

Results are deduplicated, scored against your resume, and filtered by your preferences.

### Smart Matching
- Skill-based match score (required vs possessed skills)
- Blocked company filtering
- Penalises jobs that require skills you've flagged as "want to avoid"
- Vote-weighted adjustment: upvote/downvote job aspects to tune future scores

### User Profile
Configure once, applied everywhere:
- Skills to emphasise or avoid (with reasons)
- Blocked companies and industries
- Work style preferences (remote / hybrid / onsite)
- Target roles, locations, salary range
- Career goals and free-text context fed to the AI

### AI Resume Tailoring
- Powered by any Ollama model (default: `llama3.1:8b`)
- Builds a context-aware prompt from your profile + the job description
- Creates a new tailored copy — your original is never modified
- Honest-only prompt constraints: no fabrication of skills or experience

### Export
- **JSON Resume** — standard schema, importable everywhere
- **Markdown** — clean, readable, paste into any field

### Job Tracking
- Paste or scrape job descriptions (URL or text)
- Track application status per role
- Notes and aspect voting per job

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | [Avalonia 11](https://avaloniaui.net) + CommunityToolkit.MVVM |
| Runtime | .NET 10 |
| AI / LLM | [Ollama](https://ollama.ai) (local HTTP API) |
| NER | Microsoft.ML.OnnxRuntime + custom ONNX model |
| Entity extraction | Microsoft.Recognizers.Text (dates, numbers, sequences) |
| Document parsing | PdfPig (PDF), DocumentFormat.OpenXml (DOCX) |
| Document conversion | [Docling](https://github.com/DS4SD/docling) (optional self-hosted) |
| Web scraping | Microsoft.Playwright + AngleSharp |
| HTTP resilience | Microsoft.Extensions.Http.Resilience (Polly) |
| RSS parsing | System.ServiceModel.Syndication |
| Persistence | System.Text.Json — flat file in AppData |
| Export | JSON Resume spec, Markdown |
| Tests | xUnit |

---

## Architecture

```
lucidRESUME (Avalonia UI)
    │
    ├── lucidRESUME.Ingestion      Resume parsing, Docling client, image cache
    ├── lucidRESUME.Extraction     ONNX NER, Recognizers pipeline
    ├── lucidRESUME.Parsing        Direct DOCX/PDF text extraction
    ├── lucidRESUME.JobSpec        Job description parsing + URL scraping
    ├── lucidRESUME.JobSearch      7 job board adapters + orchestrator
    ├── lucidRESUME.Matching       Skill scoring, aspect voting, filter eval
    ├── lucidRESUME.AI             Ollama tailoring service
    ├── lucidRESUME.Export         JSON Resume + Markdown exporters
    ├── lucidRESUME.Collabora      LibreOffice integration, document openers
    └── lucidRESUME.Core           Domain models, interfaces, persistence
```

**One rule:** everything depends inward on `Core`. `Core` has no external dependencies. Services are wired by DI in the app shell — nothing is hardcoded.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- (Optional) [Ollama](https://ollama.ai) for AI tailoring — `ollama pull llama3.1:8b`
- (Optional) [Docling](https://github.com/DS4SD/docling) for high-quality document conversion
- (Optional) LibreOffice, Word, or another editor for the "Open in…" button

### Build & Run

```bash
git clone https://github.com/yourname/lucidRESUME
cd lucidRESUME
dotnet run --project src/lucidRESUME/lucidRESUME.csproj
```

### Configure

Edit `src/lucidRESUME/appsettings.json`:

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.1:8b"
  },
  "Docling": {
    "BaseUrl": "http://localhost:5001"
  },
  "Adzuna": { "AppId": "...", "AppKey": "..." },
  "Reed":   { "ApiKey": "..." }
}
```

Free API keys: [Adzuna](https://developer.adzuna.com) · [Reed](https://www.reed.co.uk/developers) · [Findwork](https://findwork.dev)

Job search works without any keys — Arbeitnow, JoinRise, Jobicy, and Remotive need no authentication.

### CLI

Parse a resume to JSON without launching the UI:

```bash
dotnet run --project src/lucidRESUME.Cli -- parse --file cv.pdf --output result.json
```

---

## UX Testing

lucidRESUME ships with a built-in UI automation framework for scripted testing and LLM-driven control.

**Run a YAML script:**
```bash
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- \
  --ux-test --script ux-scripts/profile-full.yaml --output ux-screenshots
```

**Interactive REPL:**
```bash
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-repl
# ux> nav Profile
# ux> screenshot profile-check
# ux> assert FullName "Scott Galloway"
```

**MCP server** (for LLM control):
```bash
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-mcp
```

Scripts live in `ux-scripts/`. Screenshots are written to `ux-screenshots/`.

---

## Roadmap

- [ ] Job Description CRUD UI (paste / URL / search)
- [ ] Match dashboard with visual skill gap analysis
- [ ] AI tailoring diff view (original vs tailored, side-by-side)
- [ ] LinkedIn JSON import
- [ ] DOCX / PDF export
- [ ] Reed and Indeed search adapters
- [ ] Company research via web fetch (culture, reviews, red flags)
- [ ] PdfPig rendering for PDF preview without LibreOffice

---

## Contributing

PRs welcome. The codebase follows a strict inward dependency rule — keep domain logic in `Core` and wire everything in the app shell. Run the tests before submitting:

```bash
dotnet test
```

---

## License

This is free and unencumbered software released into the public domain.
See [LICENSE](LICENSE) or [unlicense.org](https://unlicense.org) for details.

**No strings attached. No attribution required. Use it however you like.**
