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

Target framework is **.NET 10**. Avalonia 11.3.

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

Local-first desktop app. Zero external services required by default. All user data stored in a single SQLite database at `%AppData%/lucidRESUME/data.db` (auto-migrates from `data.json` on first launch). JSON import/export available for backup and portability. No cloud, no accounts.

### Dependency Rule

Everything depends inward on `Core`. `Core` depends only on `Microsoft.Data.Sqlite` and `sqlite-vec`. Never add outward dependencies from Core.

```
lucidRESUME (Avalonia app shell — DI wiring, Views, ViewModels)
  ├── Ingestion      Resume file import, optional Docling client, image cache
  ├── Extraction     ONNX NER + Microsoft.Recognizers pipeline
  ├── Parsing        DOCX/PDF/TXT text extraction, section classification
  ├── JobSpec        Job description parsing (URL scrape + text parse)
  ├── JobSearch      7 job board adapters + orchestrator + deduplicator
  ├── Matching       Skill scoring, coverage analysis, aspect voting
  ├── AI             ONNX embeddings (local), Ollama tailoring + extraction (optional)
  ├── Export         JSON Resume + Markdown exporters
  ├── Collabora      LibreOffice/editor integration, document openers
  ├── UXTesting      UI automation framework (REPL, MCP, script runner)
  └── Core           Domain models, interfaces, persistence (IAppStore)
```

### MVVM Pattern

- **6 ViewModels**: MainWindow, ResumePage, JobsPage, SearchPage, ApplyPage, ProfilePage — all extend `ViewModelBase` (wraps CommunityToolkit.Mvvm `ObservableObject`)
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

xUnit 2.9.3. Five test projects under `tests/`: AI.Tests, Core.Tests, Extraction.Tests, JobSpec.Tests, Matching.Tests. Plus `test/lucidRESUME.UXTesting.Tests`. Direct service instantiation — no mocking framework.

## Key Conventions

- AI tailoring prompts must include honest-only constraints (no fabricating skills/experience)
- Job scraping: prefer text/markdown extraction first, fall back to JSON-LD or Playwright only if needed
- Non-blocking async pattern for long operations (quality checks, coverage analysis) — fire without awaiting, update UI on completion with cancellation support
- `UserProfile` vote lookups use a cached dictionary (`_voteCache`), invalidated on mutation
