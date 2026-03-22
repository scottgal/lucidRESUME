# Mostlylucid.Avalonia.UITesting

UI testing, screenshot snapshots, script playback, recording, video capture, REPL, and MCP server for Avalonia desktop apps.

## Install

```bash
dotnet add package Mostlylucid.Avalonia.UITesting
```

## Quick Start

### Wire up in your app

```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseUITesting(opts => {
        opts.DefaultScreenshotDir = "screenshots";
        opts.Log = Console.WriteLine;
    });
```

### Run modes

```bash
# Interactive REPL
dotnet run -- --ux-repl

# Run a YAML test script
dotnet run -- --ux-test --script tests/all-pages.yaml --output results

# MCP server for LLM-driven testing
dotnet run -- --ux-mcp
```

### Use in tests

```csharp
var session = await UITestSession.AttachAsync(mainWindow);
await session.NavigateAsync("profile");
await session.AssertPropertyAsync("UserName", "Scott");
await session.ScreenshotAsync("profile-verified");
```

### Record interactions

```csharp
var recorder = new UIRecorder();
recorder.StartRecording(window);
// ... user interacts ...
await recorder.StopRecordingAsync();
recorder.SaveAsYaml("recorded-test.yaml");
```

### YAML test scripts

```yaml
name: all-pages
description: Screenshot every page
default_delay: 400
actions:
  - type: Navigate
    value: resume
  - type: Screenshot
    value: page-resume
  - type: Click
    target: ImportButton
  - type: Assert
    target: ResultsList
    value: "visible:true"
```

## MCP Server

The MCP server is the primary interface for LLM-driven UI testing. It exposes 25+ tools over stdio JSON-RPC:

- **`ui_see`** - Screenshot + ASCII art rendering via consoleimage + visual tree. This is how LLMs "see" your app.
- **`ui_screenshot_base64`** - Base64 PNG for multimodal LLMs
- `ui_navigate`, `ui_click`, `ui_type`, `ui_press`, `ui_scroll`
- `ui_tree`, `ui_controls`, `ui_vm`, `ui_get`, `ui_set`
- `ui_assert`, `ui_wait_for`, `ui_assert_control`
- `ui_record_start`, `ui_record_stop`, `ui_record_save`
- `ui_video_start`, `ui_video_stop`
- `ui_run_script`

### LLM Vision

Install [consoleimage](https://github.com/scottgal/consoleimage) for the `ui_see` tool:

```bash
dotnet tool install -g consoleimage
```

This renders screenshots as ASCII art so text-only LLMs can understand the UI layout.

## Features

- Screenshot capture (PNG) with named files
- Animated GIF video recording at configurable FPS
- Optional MP4 export via FFmpeg
- YAML/JSON script playback and recording
- Interactive REPL with 20+ commands
- MCP server (JSON-RPC 2.0 over stdio)
- Cross-window tracking (popups, dialogs)
- ViewModel property inspection and mutation
- Visual tree inspection
- Assertion framework (property values, control state)
- Interaction recording with pause/resume
- DI integration via `IServiceCollection`

## Requirements

- .NET 9.0 or 10.0
- Avalonia 11.3+
- Windows (for desktop screenshot capture)
