# Mostlylucid.Avalonia.UITesting

A complete UI testing toolkit for [Avalonia](https://avaloniaui.net/) desktop apps. Screenshot snapshots, YAML script playback, interaction recording, animated GIF video capture, interactive REPL, and an MCP server that lets LLMs see and drive your UI.

Built for [lucidRESUME](https://github.com/scottgal/lucidRESUME) and extracted as a standalone package so any Avalonia app can use it.

## Install

```bash
dotnet add package Mostlylucid.Avalonia.UITesting
```

Targets **.NET 9.0** and **.NET 10.0**. Requires **Avalonia 11.3+**.

---

## Getting Started

### 1. Wire up in your app

Add one line to your `AppBuilder` pipeline. This enables `--ux-test`, `--ux-repl`, and `--ux-mcp` command-line modes automatically:

```csharp
// Program.cs or App.axaml.cs
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseUITesting(opts =>
    {
        opts.DefaultScreenshotDir = "ux-screenshots";
        opts.Log = Console.WriteLine;
        opts.EnableCrossWindowTracking = true;
    })
    .StartWithClassicDesktopLifetime(args);
```

### 2. Choose your mode

```bash
# Run a YAML test script (CI-friendly, exits when done)
dotnet run -- --ux-test --script ux-scripts/all-pages.yaml --output results

# Interactive REPL for exploratory testing
dotnet run -- --ux-repl

# MCP server for LLM-driven UI testing (Claude, GPT, etc.)
dotnet run -- --ux-mcp
```

### 3. (Optional) Register in DI

```csharp
services.AddUITesting(opts =>
{
    opts.DefaultScreenshotDir = "screenshots";
});
```

---

## Use Cases

All examples below are from [lucidRESUME](https://github.com/scottgal/lucidRESUME), a local-first desktop app for job seekers.

### Visual regression testing

Capture screenshots of every page and compare them across builds:

```yaml
# ux-scripts/all-pages.yaml
name: all-pages
description: Screenshot every page for visual regression
default_delay: 400

actions:
  - type: Wait
    value: "1000"

  - type: Navigate
    value: resume
  - type: Wait
    value: "800"
  - type: Screenshot
    value: page-resume

  - type: Navigate
    value: jobs
  - type: Wait
    value: "800"
  - type: Screenshot
    value: page-jobs

  - type: Navigate
    value: apply
  - type: Wait
    value: "800"
  - type: Screenshot
    value: page-apply

  - type: Navigate
    value: profile
  - type: Wait
    value: "800"
  - type: Screenshot
    value: page-profile-top
  - type: Scroll
    value: bottom
  - type: Wait
    value: "400"
  - type: Screenshot
    value: page-profile-bottom
```

### Form interaction testing

Test that typing into form fields works and UI state updates correctly:

```yaml
name: apply-form-test
description: Fill in the Apply page form and verify state
default_delay: 300

actions:
  - type: Navigate
    value: apply
  - type: Wait
    value: "600"
  - type: Screenshot
    value: apply-empty

  - type: TypeText
    target: JobTitleInput
    value: "Senior C# Developer"
  - type: TypeText
    target: CompanyInput
    value: "Anthropic"
  - type: TypeText
    target: JobDescriptionInput
    value: "5+ years .NET, Avalonia UI, cloud services"
  - type: Screenshot
    value: apply-filled

  - type: Assert
    target: TailorButton
    value: "enabled:true"
```

### Empty state verification

Verify that empty states display properly (a common UX blind spot):

```yaml
name: empty-states
description: Verify empty state messages are visible
default_delay: 400

actions:
  - type: Navigate
    value: jobs
  - type: Wait
    value: "600"
  - type: Screenshot
    value: jobs-empty
  - type: Assert
    target: EmptyStateText
    value: "visible:true"

  - type: Navigate
    value: apply
  - type: Wait
    value: "600"
  - type: Screenshot
    value: apply-empty
```

### Profile page scroll test with video recording

Record a video of scrolling through the Profile page:

```yaml
name: profile-scroll-video
description: Record GIF of scrolling through profile
default_delay: 200

actions:
  - type: Navigate
    value: profile
  - type: Wait
    value: "800"

  - type: StartVideo
    value: "5"

  - type: Scroll
    value: down
  - type: Wait
    value: "500"
  - type: Scroll
    value: down
  - type: Wait
    value: "500"
  - type: Scroll
    value: bottom
  - type: Wait
    value: "500"
  - type: Scroll
    value: top
  - type: Wait
    value: "500"

  - type: StopVideo
    value: profile-scroll
```

---

## Programmatic API

### UITestSession

The high-level API for writing tests in C#:

```csharp
// Attach to a running window
var session = await UITestSession.AttachAsync(mainWindow, opts =>
{
    opts.ScreenshotDir = "test-output";
    opts.Log = Console.WriteLine;
    opts.EnableCrossWindowTracking = true;
    opts.NavigateAction = page => viewModel.NavigateCommand.Execute(page);
});

// Navigate between pages
await session.NavigateAsync("profile");

// Interact with controls
await session.ClickAsync("SearchButton");
await session.TypeAsync("SearchBox", "C# developer London");
await session.PressAsync("Enter");
await session.DoubleClickAsync("FirstResult");

// Inspect ViewModel state
var userName = await session.GetPropertyAsync<string>("FullName");
await session.AssertPropertyAsync("CurrentPage", "Profile");

// Wait for async operations
await session.WaitForPropertyAsync("IsLoading", false, timeoutMs: 10000);

// Capture screenshots
await session.ScreenshotAsync("after-search");

// Record video
var recorder = await session.StartVideoAsync(fps: 10);
// ... interact ...
var gifPath = await session.StopVideoAsync("search-flow");

// Inspect the visual tree
var tree = await session.GetTreeAsync();
var controls = await session.GetControlsAsync();
var vmProps = await session.GetViewModelPropertiesAsync();

// Clean up
await session.DisposeAsync();
```

### ScriptPlayer

Run YAML/JSON scripts programmatically:

```csharp
var script = ScriptLoader.LoadFromYaml("ux-scripts/all-pages.yaml");

var player = new ScriptPlayer("output-dir", defaultDelay: 200, captureScreenshots: true);
player.SetNavigateAction(page => vm.NavigateCommand.Execute(page));
player.Log += (_, msg) => Console.WriteLine(msg);
player.ActionCompleted += (_, result) =>
{
    if (!result.Success)
        Console.WriteLine($"FAILED: {result.ErrorMessage}");
};

var result = await player.RunScriptAsync(window, script);

Console.WriteLine($"Result: {(result.Success ? "PASS" : "FAIL")}");
Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F1}s");
Console.WriteLine($"Video: {result.VideoPath}");
```

### UIRecorder

Record user interactions and save as replayable scripts:

```csharp
var recorder = new UIRecorder(new UIRecorderOptions
{
    CaptureMousePositions = true,   // record x,y coords
    CrossWindowTracking = true,     // track popups/dialogs
    CoalesceThresholdMs = 50        // merge rapid actions
});

recorder.Log += (_, msg) => Console.WriteLine($"[REC] {msg}");
recorder.ActionRecorded += (_, action) => Console.WriteLine($"  {action.Type}: {action.Target}");

// Start recording (optionally with video)
recorder.StartRecording(mainWindow, recordVideo: true, videoFps: 5);

// Wire up navigation tracking
viewModel.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == "CurrentPage")
        recorder.OnNavigated(viewModel.CurrentPage);
};

// Pause/resume mid-recording
recorder.PauseRecording();
// ... setup that shouldn't be recorded ...
recorder.ResumeRecording();

// Stop and save
await recorder.StopRecordingAsync();
recorder.SaveAsYaml("recorded-test.yaml");
await recorder.SaveVideoAsync("recording.gif");
await recorder.TryExportVideoMp4Async("recording.mp4");
```

### GifRecorder

Standalone video recording:

```csharp
await using var gif = new GifRecorder(fps: 10, log: Console.WriteLine);
gif.StartRecording(window);

// ... do things ...
await Task.Delay(5000);

await gif.StopRecordingAsync();
await gif.SaveAsync("demo.gif");                    // always works
await gif.TryExportMp4Async("demo.mp4");            // works if ffmpeg is installed
```

---

## REPL Commands

Launch with `dotnet run -- --ux-repl`. Available commands:

| Command | Description | Example |
|---------|-------------|---------|
| `nav <page>` | Navigate to page | `nav profile` |
| `click <control>` | Click a named control | `click SearchButton` |
| `dblclick <control>` | Double-click | `dblclick FirstResult` |
| `type <control> <text>` | Type into TextBox | `type SearchBox "C# London"` |
| `press <key>` | Press a key | `press Enter` |
| `get <path>` | Get ViewModel property | `get CurrentPage` |
| `set <path> <value>` | Set ViewModel property | `set SearchQuery "test"` |
| `get #<control>.<prop>` | Get control property | `get #SearchBox.Text` |
| `list` | List named controls | `list` |
| `list vms` | List ViewModel properties | `list vms` |
| `tree` | Show visual tree | `tree` |
| `vm` | Show all VM properties | `vm` |
| `screenshot [name]` | Capture PNG | `shot profile-page` |
| `svg [name]` | Export SVG snapshot | `svg profile-layout` |
| `describe [name]` | Screenshot + ASCII art | `desc current-state` |
| `wait <ms>` | Wait | `wait 1000` |
| `waitfor <path> <val>` | Wait for property | `waitfor IsLoading False` |
| `assert <path> <val>` | Assert property value | `assert FullName "Scott"` |
| `run <script.yaml>` | Run a script | `run ux-scripts/all-pages.yaml` |
| `record [--video]` | Start recording | `record --video` |
| `stop` | Stop recording | `stop` |
| `pause` / `resume` | Pause/resume recording | `pause` |
| `save <file>` | Save recording | `save test.yaml` |
| `windows` | List tracked windows | `windows` |
| `service <type>` | Get DI service | `service IAppStore` |
| `exit` | Exit | `exit` |

---

## MCP Server

The MCP server is the star feature. It lets LLMs (Claude, GPT, etc.) **see and drive** your Avalonia app over JSON-RPC 2.0 stdio.

### Setup

```bash
# Launch your app with the MCP server
dotnet run -- --ux-mcp --output screenshots
```

Then configure your LLM tool to connect via stdio to the process.

### Key tool: `ui_see`

This is how an LLM "sees" your app. It:

1. Takes a screenshot
2. Renders it as ASCII art via [consoleimage](https://github.com/scottgal/consoleimage)
3. Returns the visual tree and named controls list
4. Includes base64 PNG for multimodal models

```bash
# Install consoleimage for ASCII rendering
dotnet tool install -g consoleimage
```

### All MCP Tools

**Navigation & Interaction:**
| Tool | Description |
|------|-------------|
| `ui_navigate` | Navigate to a named page |
| `ui_click` | Click a named control |
| `ui_double_click` | Double-click a control |
| `ui_type` | Type text into a TextBox |
| `ui_press` | Press a keyboard key |
| `ui_scroll` | Scroll up/down/top/bottom |
| `ui_hover` | Hover over a control |

**Vision:**
| Tool | Description |
|------|-------------|
| `ui_see` | Screenshot + ASCII art + tree + controls + base64 |
| `ui_screenshot` | Capture PNG only |
| `ui_screenshot_base64` | PNG as base64 for multimodal LLMs |

**Inspection:**
| Tool | Description |
|------|-------------|
| `ui_tree` | Visual tree hierarchy |
| `ui_controls` | Named controls with bounds/visibility |
| `ui_vm` | ViewModel properties and values |
| `ui_get` | Get a specific property by path |
| `ui_set` | Set a property value |
| `ui_windows` | List all tracked windows |

**Assertions & Waiting:**
| Tool | Description |
|------|-------------|
| `ui_wait` | Wait N milliseconds |
| `ui_wait_for` | Wait for property to equal value |
| `ui_assert` | Assert ViewModel property |
| `ui_assert_control` | Assert control visible/enabled/text |

**Recording & Video:**
| Tool | Description |
|------|-------------|
| `ui_record_start` | Start recording interactions |
| `ui_record_stop` | Stop recording |
| `ui_record_save` | Save as YAML/JSON script |
| `ui_video_start` | Start GIF recording |
| `ui_video_stop` | Stop and save GIF (+MP4 if ffmpeg) |

**Script & Lifecycle:**
| Tool | Description |
|------|-------------|
| `ui_run_script` | Run a YAML/JSON test script |
| `ui_exit` | Shut down server and app |

### Example MCP Session

An LLM using the MCP server to test lucidRESUME:

```
LLM: ui_see → sees the Resume page with ASCII art
LLM: ui_navigate(page: "profile") → navigates to Profile
LLM: ui_see → sees Profile page, notices FullName field
LLM: ui_get(path: "FullName") → "Scott Galloway"
LLM: ui_controls → sees all named controls with bounds
LLM: ui_navigate(page: "jobs") → goes to Jobs page
LLM: ui_see → sees empty state with "No jobs yet"
LLM: ui_assert_control(name: "EmptyStateText", property: "visible", value: "true")
LLM: ui_screenshot(name: "jobs-verified")
```

---

## Script Reference

### Action Types

| Type | Target | Value | Description |
|------|--------|-------|-------------|
| `Navigate` | | page name | Navigate to a page |
| `Click` | control name | | Click a control |
| `DoubleClick` | control name | | Double-click |
| `RightClick` | control name | | Right-click |
| `TypeText` | control name | text | Type into TextBox |
| `PressKey` | control name (opt) | key name | Press keyboard key |
| `Hover` | control name | | Hover over control |
| `Scroll` | ScrollViewer (opt) | up/down/top/bottom | Scroll |
| `Wait` | | milliseconds | Wait |
| `Screenshot` | | filename | Capture PNG |
| `Svg` | | filename | Export SVG |
| `Assert` | control name | visible:true/enabled:true/text:value | Assert control state |
| `StartVideo` | | fps (default 5) | Start GIF recording |
| `StopVideo` | | filename | Stop and save video |
| `MouseMove` | | | Move to x,y coords |
| `MouseDown` | | | Press at x,y |
| `MouseUp` | | | Release at x,y |

### Script Structure

```yaml
name: my-test                    # Script name
description: What this tests     # Description
default_delay: 200               # Default delay between actions (ms)
variables:                       # Variables (for future use)
  base_url: "https://example.com"
actions:                         # List of actions
  - type: Navigate
    value: profile
    delay_ms: 500                # Override delay for this action
    description: Go to profile   # Human-readable description
    window_id: MainWindow        # Target window (cross-window)
    x: 100                       # Mouse X coordinate (pixel-level)
    y: 200                       # Mouse Y coordinate (pixel-level)
```

---

## Architecture

```
Mostlylucid.Avalonia.UITesting/
  UITestContext.cs           Window management, control lookup, property reflection
  UITestSession.cs           High-level session API (navigate, click, screenshot, video)
  UITestingExtensions.cs     DI + AppBuilder.UseUITesting() extension
  Players/
    ScriptPlayer.cs          Execute YAML/JSON test scripts
  Recorders/
    UIRecorder.cs            Record interactions with pause/resume/cross-window
  Repl/
    UITestRepl.cs            Interactive REPL (25+ commands)
  Mcp/
    UITestMcpServer.cs       MCP server (25+ tools, JSON-RPC 2.0 stdio)
  Scripts/
    UIScript.cs              Models: UIScript, UIAction, ActionType, UITestResult
    ScriptLoader.cs          Load/save YAML and JSON scripts
  Video/
    GifRecorder.cs           Animated GIF capture + optional MP4 via FFmpeg
  Svg/
    SvgExporter.cs           SVG export of visual tree
    SvgColorHelper.cs        Color conversion utilities
```

## Dependencies

| Package | Purpose |
|---------|---------|
| Avalonia 11.3+ | UI framework |
| Avalonia.Desktop | Desktop lifetime |
| Avalonia.Headless | Headless rendering |
| Avalonia.Skia | Skia rendering backend |
| SkiaSharp 3.x | GIF encoding |
| YamlDotNet 16.x | YAML script loading |
| System.Drawing.Common | Image utilities |
| M.E.DI.Abstractions | DI integration |

No Newtonsoft.Json dependency. Uses `System.Text.Json` throughout.

## Requirements

- .NET 9.0 or 10.0
- Avalonia 11.3+
- Windows (for desktop screenshot capture via RenderTargetBitmap)
- Optional: [consoleimage](https://github.com/scottgal/consoleimage) for `ui_see` ASCII rendering
- Optional: FFmpeg for MP4 video export

## License

MIT
