# Mostlylucid.Avalonia.UITesting

[![NuGet](https://img.shields.io/nuget/v/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)
[![NuGet downloads](https://img.shields.io/nuget/dt/Mostlylucid.Avalonia.UITesting.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/Mostlylucid.Avalonia.UITesting)

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
| `Screenshot` | control name (opt) | filename | Capture PNG. Snips a single control if `target` set, an X/Y/X2/Y2 rect if all four coords set, otherwise the full window. `padding` inflates the snip. |
| `Svg` | | filename | Export SVG |
| `Assert` | control name | visible:true/enabled:true/text:value | Assert control state |
| `StartVideo` | | fps (default 5) | Start GIF recording |
| `StopVideo` | | filename | Stop and save video |
| `MouseMove` | | | Real pointer move to x,y |
| `MouseDown` | | button (opt) | Real pointer press at x,y (left/right/middle/x1/x2) |
| `MouseUp` | | button (opt) | Real pointer release at x,y |
| `Drag` | | button (opt) | Press at x,y → drag to x2,y2 with `steps` interpolated moves |
| `Wheel` | | "dy" or "dx,dy" | Real mouse wheel at x,y |
| `Pinch` | | scale delta | Touchpad pinch (magnify) at x,y |
| `Rotate` | | angle degrees | Touchpad rotate at x,y |
| `Swipe` | | "dx,dy" or x2/y2 | Touchpad swipe at x,y |
| `TouchTap` | | | Single-finger touch tap at x,y |
| `TouchDown` / `TouchMove` / `TouchUp` | | | Single-finger touch primitives |
| `TouchDrag` | | | Touch press at x,y → drag to x2,y2 |
| `WindowResize` | | | Resize window to x (width) × y (height) |
| `WindowMove` | | | Move window to screen coords (x, y) |
| `WindowMinimize` / `WindowMaximize` / `WindowRestore` / `WindowSetFullScreen` | | | Change window state |
| `WindowFocus` | | | Activate and focus window |
| `WindowClose` | | | Close window |
| `WindowSetTitle` | | new title | Set window title |

All pointer/touch/gesture/wheel actions are dispatched through Avalonia's real input pipeline (`IInputManager.ProcessInput`), so they trigger hit-testing, `IsPointerOver`, capture, click counting, drag detection, and gesture recognition exactly the same as real OS input. Cross-platform on Windows, macOS, and Linux.

### Locators (Playwright-style)

The `target:` field on every action accepts a Playwright-flavoured selector string. A bare word with no operators is treated as `name=...` for backwards compatibility.

| Selector | Meaning |
|---|---|
| `name=SaveBtn` | by `Control.Name` |
| `type=Button` | by short type name (matches the type or any base) |
| `text=Save` | by displayed text content (substring) |
| `text='Save Resume'` | by displayed text content (exact, quoted) |
| `role=button` | by automation peer role |
| `role=button name=Save` | role + accessible name |
| `testid=save-btn` | by `AutomationProperties.AutomationId` |
| `label=Email` | the input associated with a label via `AutomationProperties.LabeledBy` |
| `first(type=Button)` | first match |
| `last(type=Button)` | last match |
| `nth(2, type=ListBoxItem)` | zero-based index match |
| `inside(name=Header) type=TextBlock` | restricted to a container's visual subtree |
| `near(name=JobList) type=Button` | reordered by spatial proximity to an anchor |
| `type=Button:has-text(Save Resume)` | filter by descendant text (Playwright `:has-text` pseudo) |

Composition: multiple atoms separated by spaces compose with implicit AND — `type=Button text=Save` matches buttons whose displayed text contains "Save".

Locators auto-retry. When a script tries to click `name=SaveBtn` while the control is still being created by an async DataContext bind, the locator engine polls the visual tree until the control appears or a timeout (default 5s) elapses. No more `Wait` actions before clicking.

Programmatic equivalent for in-process tests:

```csharp
var save = page.LocateAsync("type=Button text=Save");
var first = page.LocateAsync(By.Type<Button>().First());
var nearJobs = page.LocateAsync(By.Type("Button").Near(By.Name("JobList")));
```

#### Host CLI flags

When you call `.UseUITesting()` on your `AppBuilder`, the new locator-aware engine
binds to two sets of CLI flags:

- `--ux-test` / `--ux-repl` / `--ux-mcp` — the original triggers; use these if your app
  doesn't have an existing UI testing entry point with the same names
- `--mlui-test` / `--mlui-repl` / `--mlui-mcp` — explicit triggers when your app
  already wires `--ux-test` to a different player and you want both side by side

### Auto-waiting expectations (Playwright `expect`)

The `Expect` action and the `session.Expect(...)` fluent API run a matcher
against a locator and **auto-retry** until the matcher passes or a timeout
elapses. No more `Wait` actions before checking state.

```yaml
- type: Click
  target: "type=Button:has-text(Save)"

- type: Expect
  target: "name=Status"
  matcher: HasText
  value: "Saved"
  timeout: 5000

- type: Expect
  target: "name=SaveBtn"
  matcher: IsEnabled

- type: Expect
  target: "name=JobList"
  matcher: HasCount
  value: "12"
```

Programmatic equivalent:

```csharp
await session.Click("type=Button:has-text(Save)");
await session.Expect("name=Status").ToHaveText("Saved");
await session.Expect("name=SaveBtn").ToBeEnabled();
await session.Expect("name=JobList").ToHaveCount(12);
await session.Expect("name=ErrorBanner").Not.ToBeVisible();
```

Built-in matchers:

| Matcher | YAML name | Programmatic | Notes |
|---|---|---|---|
| Visibility | `IsVisible` / `IsHidden` | `ToBeVisible()` / `ToBeHidden()` | |
| Enabled state | `IsEnabled` / `IsDisabled` | `ToBeEnabled()` / `ToBeDisabled()` | |
| Toggle state | `IsChecked` / `IsUnchecked` | `ToBeChecked()` / `ToBeUnchecked()` | CheckBox, RadioButton, ToggleButton, ToggleSwitch |
| Focus | `IsFocused` | `ToBeFocused()` | |
| Exact text | `HasText` | `ToHaveText("...")` | TextBlock.Text, TextBox.Text, Button.Content (string), HeaderedControl.Header (string) |
| Substring text | `ContainsText` | `ToContainText("...")` | |
| Regex | `MatchesRegex` | `ToMatchRegex(@"#\d+")` | |
| Item count | `HasCount` | `ToHaveCount(12)` | ItemsControl item count or Panel children count |
| Value | `HasValue` | `ToHaveValue("42")` | TextBox/NumericUpDown/Slider/ComboBox |
| Generic property | `HasProperty` | `ToHaveProperty("Title", "lucidRESUME")` | Reflection-based fallback for any control property |

Negation: prefix the matcher name with `Not.` or `!` in YAML, or chain
`.Not` in the programmatic API. The `timeout:` field overrides the
default 5000ms per expectation.

Failure messages include the locator description, the matcher description,
the timeout budget, and the last seen detail — so test runs report
behavioural drift in human-readable form rather than `Assert.IsTrue` stack
traces.

### Snipping (regions, controls, manuals)

The `Screenshot` action and the `UITestSession.Snip*` / MCP `ui_snip_*` / REPL `snip*` commands can capture a region of a window instead of the whole thing — useful when you're producing manuals or docs and want a single button or panel rather than the whole window. Snipping renders the window once via `RenderTargetBitmap`, then crops the rendered bitmap with SkiaSharp.

```yaml
# Snip one control with 8px padding
- type: Screenshot
  target: SaveButton
  value: save-button
  padding: 8

# Snip an explicit rect
- type: Screenshot
  value: header-area
  x: 0
  y: 0
  x2: 1200
  y2: 80
```

```bash
# REPL
ui> snipctl SaveButton save-button 8
ui> snip 0 0 1200 80 header-area
ui> snipgroup HeaderLogo,HeaderTitle,HeaderNav header

# MCP
ui_snip_control name=SaveButton padding=8
ui_snip_region x=0 y=0 width=1200 height=80
ui_snip_controls names="HeaderLogo,HeaderTitle,HeaderNav"
```

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
