# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build entire solution
dotnet build lucidRESUME.sln

# Run the desktop app
dotnet run --project src/lucidRESUME/lucidRESUME.csproj

# Run the CLI
dotnet run --project src/lucidRESUME.Cli -- parse --file cv.pdf --output result.json

# Run all tests
dotnet test

# Run a single test project
dotnet test tests/lucidRESUME.Matching.Tests

# Run a specific test
dotnet test tests/lucidRESUME.Core.Tests --filter "FullyQualifiedName~SqliteAppStoreTests"
```

Target framework is **.NET 10**. Avalonia 12.1.2.

## UX Testing

```bash
# Run a YAML test script
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-test --script ux-scripts/profile-full.yaml --output ux-screenshots

# Interactive REPL
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-repl

# MCP server for LLM-driven UI control
dotnet run --project src/lucidRESUME/lucidRESUME.csproj -- --ux-mcp
```

## Architecture

Local-first desktop app. Zero external services required by default. Structured state is stored in a SQLite database at `%AppData%/lucidRESUME/data.db` (auto-migrates from `data.json` on first launch). Generated document files live in `generated-resumes/` and reusable prose in `prose-library/` alongside the database. JSON import/export covers structured state; a full backup must include the archive directories. No account is required.

### Dependency Rule

Everything depends inward on `Core`. `Core` references the independent `JobML` interchange model/parser project, `Microsoft.Data.Sqlite` and `sqlite-vec`. Keep service, provider and UI dependencies out of Core.

```
lucidRESUME (Avalonia app shell - DI wiring, Views, ViewModels)
  ├── Ingestion      Resume parsing, Docling client (Docker), image cache
  ├── Extraction     ONNX NER (2 models: general + resume) + Microsoft.Recognizers
  ├── Parsing        DOCX/PDF/TXT extraction, ATS patterns (YAML), template learning
  ├── JobSpec        JD parsing (RRF fusion: Structural + NER + LLM), URL scraping
  ├── JobSearch      7 job board adapters + orchestrator + deduplicator
  ├── Matching       Skill ledger, skill graph, career planner, coverage, aspect voting
  ├── AI             Ollama/Anthropic/OpenAI, AI detection (5 signals), de-AI, translation
  ├── EmailTracker   IMAP scanning (MailKit), email classification, application matching
  ├── Export         JSON Resume + Markdown exporters
  ├── Collabora      Installed-editor discovery, LibreOffice fallback, macOS native openers
  ├── Avalonia.UITesting  UI automation (REPL, MCP, scripts, ImportFile, PasteJob)
  └── Core           Domain models, interfaces, persistence (SQLite + sqlite-vec)
```

### Complete career and generated archives

`AppState.CompleteCareer` is the authoritative persisted transcript. Reconcile
imports and overrides at save time; generation and matching must read the saved
career rather than rebuild from imported resumes. Accepted concepts establish
skill coverage. Embeddings are derived retrieval aids and cannot satisfy a required
skill by similarity alone. GitHub README/technology nominations require review,
and project age must not become personal skill-years.

Full career JobML preserves claims, sources, review states, semantic indexes and
raw role interviews. Publish reviewed prose back with revision checks. Generated
resumes retain frozen evidence and actual DOCX/PDF exports in a separate archive;
they must not automatically become new input evidence. Derived reusable prose is
validated against its current source and bindings on every cache hit.
See [complete career workflow](docs/complete-career-workflow.md).

### MVVM Pattern

- **6 ViewModels**: MainWindow, ResumePage, JobsPage, SearchPage, ApplyPage, ProfilePage - all extend `ViewModelBase` (wraps CommunityToolkit.Mvvm `ObservableObject`)
- Uses `[ObservableProperty]` and `[RelayCommand]` attributes exclusively (no manual ICommand)
- Compiled bindings enabled (`AvaloniaUseCompiledBindingsByDefault`)
- `ViewLocator` maps VM → View by namespace convention

### Navigation

`MainWindowViewModel` holds a dictionary of page VMs. `NavigateCommand` switches `CurrentPage`. Cross-page communication uses action delegates set during DI wiring in `App.axaml.cs` (e.g., `JobsPageViewModel.NavigateTo` triggers navigation to Apply page with context).

### DI Registration

Each module exposes `ServiceCollectionExtensions.AddXxx(config)`. All wired in `App.axaml.cs.ConfigureServices()`. CLI has its own `ServiceBootstrap.Build()` for headless use.

### Persistence

`SqliteAppStore` implements `IAppStore` (default). Single `data.db` file with sqlite-vec for vector embeddings. Thread-safe via `SemaphoreSlim`. State mutations go through `MutateAsync(Action<AppState>)` for read-modify-write safety within a SQLite transaction. `ExportJsonAsync`/`ImportJsonAsync` for JSON backup/restore.

`JsonAppStore` still exists for backwards compatibility but is no longer the default.

`AppState` contains: `ResumeDocument`, `UserProfile`, `List<JobDescription>`, `List<SavedSearch>`, `List<SearchPreset>`.

### Embeddings

Local ONNX embeddings via `all-MiniLM-L6-v2` (384 dimensions, ~86MB model). `OnnxEmbeddingService` is the default `IEmbeddingService`. Set `Embedding.Provider` to `"ollama"` in config to use Ollama's `nomic-embed-text` instead. Model files ship in `models/` directory.

### Resume Extraction Pipeline

Multi-stage fallback chain: pattern matching → Microsoft.Recognizers.Text → ONNX NER → LLM fallback. Each stage adds confidence-scored extractions.

### Configuration

`appsettings.json` with sections: `Embedding`, `Ollama`, `Docling`, `Collabora`, `Coverage`, `Tailoring`, `OnnxNer`, `Adzuna`, `Reed`, `Findwork`, `CloudflareBrowserRendering`. Uses `IOptions<T>` pattern. User secrets ID: `e7e3de57-7a67-4384-ba07-90139e44ae83`. CLI also reads from `lucidresume.json` and `LUCIDRESUME_*` env vars.

Docling is disabled by default (`Docling.Enabled = false`). Enable it for OCR support on scanned PDFs.

### HTTP Clients

All external HTTP services use typed `HttpClient` registered with `AddStandardResilienceHandler()` (Polly retry/backoff).

## Tests

xUnit 2.9.3. Test projects live under `tests/`; the Avalonia automation framework has its own test project. Direct service instantiation - no mocking framework.

## Key Conventions

- AI tailoring prompts must include honest-only constraints (no fabricating skills/experience)
- Job scraping: prefer text/markdown extraction first, fall back to JSON-LD or Playwright only if needed
- Non-blocking async pattern for long operations (quality checks, coverage analysis) - fire without awaiting, update UI on completion with cancellation support
- `UserProfile` vote lookups use a cached dictionary (`_voteCache`), invalidated on mutation
