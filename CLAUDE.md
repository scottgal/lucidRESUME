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
dotnet test tests/lucidRESUME.Core.Tests --filter "FullyQualifiedName~JsonAppStoreTests"
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

Local-first desktop app. All user data stored in a single JSON file at `%AppData%/lucidRESUME/data.json`. No cloud, no accounts.

### Dependency Rule

Everything depends inward on `Core`. `Core` has zero external dependencies. Never add outward dependencies from Core.

```
lucidRESUME (Avalonia app shell — DI wiring, Views, ViewModels)
  ├── Ingestion      Resume file import, Docling client, image cache
  ├── Extraction     ONNX NER + Microsoft.Recognizers pipeline
  ├── Parsing        DOCX/PDF text extraction, section classification
  ├── JobSpec        Job description parsing (URL scrape + text parse)
  ├── JobSearch      7 job board adapters + orchestrator + deduplicator
  ├── Matching       Skill scoring, coverage analysis, aspect voting
  ├── AI             Ollama tailoring + extraction services
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

`JsonAppStore` implements `IAppStore`. Thread-safe via `SemaphoreSlim`. Atomic writes (temp file → rename). State mutations go through `MutateAsync(Action<AppState>)` for read-modify-write safety.

`AppState` contains: `ResumeDocument`, `UserProfile`, `List<JobDescription>`, `List<SavedSearch>`, `List<SearchPreset>`.

### Resume Extraction Pipeline

Multi-stage fallback chain: pattern matching → Microsoft.Recognizers.Text → ONNX NER → LLM fallback. Each stage adds confidence-scored extractions.

### Configuration

`appsettings.json` with sections: `Ollama`, `Docling`, `Collabora`, `Coverage`, `Tailoring`, `OnnxNer`, `Adzuna`, `Reed`, `Findwork`, `CloudflareBrowserRendering`. Uses `IOptions<T>` pattern. User secrets ID: `e7e3de57-7a67-4384-ba07-90139e44ae83`. CLI also reads from `lucidresume.json` and `LUCIDRESUME_*` env vars.

### HTTP Clients

All external HTTP services use typed `HttpClient` registered with `AddStandardResilienceHandler()` (Polly retry/backoff).

## Tests

xUnit 2.9.3. Four test projects under `tests/`: Core.Tests, Extraction.Tests, JobSpec.Tests, Matching.Tests. Plus `test/lucidRESUME.UXTesting.Tests`. Direct service instantiation — no mocking framework.

## Key Conventions

- AI tailoring prompts must include honest-only constraints (no fabricating skills/experience)
- Job scraping: prefer text/markdown extraction first, fall back to JSON-LD or Playwright only if needed
- Non-blocking async pattern for long operations (quality checks, coverage analysis) — fire without awaiting, update UI on completion with cancellation support
- `UserProfile` vote lookups use a cached dictionary (`_voteCache`), invalidated on mutation
