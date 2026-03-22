# Mostlylucid.Avalonia.UITesting - Design Document

## Overview

Standalone NuGet package for UI testing, screenshot snapshots, script playback, interaction recording, video capture, REPL, and MCP server for Avalonia desktop apps. Windows-only initial target.

## Package

- **ID**: `Mostlylucid.Avalonia.UITesting`
- **Namespace**: `Mostlylucid.Avalonia.UITesting`
- **Targets**: `net9.0;net10.0`
- **Author**: Scott Galloway
- **License**: MIT

## Dependencies

- Avalonia 11.3+ (core, Desktop, Headless, Skia)
- SkiaSharp 3.x (GIF video encoding)
- YamlDotNet 16.x (script loading)
- System.Drawing.Common 9.x
- Microsoft.Extensions.DependencyInjection.Abstractions 9.x

No Newtonsoft.Json - uses System.Text.Json throughout.

## Architecture

```
Mostlylucid.Avalonia.UITesting/
  UITestContext.cs           Core context: window management, control lookup, property access, cross-window tracking
  UITestSession.cs           High-level session API: navigate, click, type, screenshot, video, assert
  UITestingExtensions.cs     DI registration + AppBuilder.UseUITesting() extension
  Players/
    ScriptPlayer.cs          Execute UIScript YAML/JSON scripts against a window
  Recorders/
    UIRecorder.cs            Record interactions: clicks, typing, scrolling, navigation, mouse positions
  Repl/
    UITestRepl.cs            Interactive REPL for exploratory testing
  Mcp/
    UITestMcpServer.cs       MCP server for LLM-driven UI testing (stdio, JSON-RPC 2.0)
  Scripts/
    UIScript.cs              Script models: UIScript, UIAction, ActionType, UITestResult, UIActionResult
    ScriptLoader.cs          Load/save scripts as YAML or JSON
  Video/
    GifRecorder.cs           Capture frames at configurable FPS, encode as animated GIF, optional MP4 via FFmpeg
```

## Key Features

### MCP Server (primary LLM interface)

The MCP server is the star feature. It exposes 25+ tools for LLM-driven UI testing:

- **`ui_see`** - Takes a screenshot, renders via `consoleimage` as ASCII art, returns visual tree + control list + base64 image. This lets LLMs "see" the app.
- **`ui_screenshot_base64`** - Returns base64 PNG for multimodal LLMs
- Navigation, click, type, press, scroll, hover, assert tools
- Recording start/stop/save
- Video start/stop (GIF + MP4)
- Script execution
- Cross-window support

### Recorder (completed)

- Click, double-click tracking with expanded control detection (Button, TabItem, ComboBox, ToggleSwitch, Slider, NumericUpDown, DatePicker, TreeViewItem, etc.)
- Scroll tracking via PointerWheelChanged with direction detection
- Navigation tracking via callback
- Key press recording for special keys
- Text change coalescing (updates last action for same TextBox)
- Mouse position recording (optional, with 10px movement threshold)
- Pause/resume support
- Cross-window tracking (auto-attaches to new windows)
- Video recording during interaction recording
- Direct save to YAML/JSON
- Delay coalescing for consecutive same-type actions

### Video Recording

- GIF encoding via SkiaSharp with LZW compression
- Configurable FPS (1-30, default 5)
- Netscape extension for infinite looping
- Color quantization (256 colors, median-cut-ish)
- Optional MP4 export via FFmpeg (shells out if available)

### Cross-Window Tracking

- Watches Window.Opened/Closed events via class handlers
- Auto-attaches recorder event handlers to new windows
- Actions tagged with window identifier (Name, Title, or class name)
- All tools accept optional `window` parameter

### Pixel-Level Replay

- ActionTypes: MouseMove, MouseDown, MouseUp with X,Y coordinates
- Positions recorded relative to window
- Movement throttled (10px threshold) during recording
- Currently logged for documentation; full replay via automation peers planned

## Consumer API

```csharp
// In App.axaml.cs
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseUITesting(opts => {
        opts.DefaultScreenshotDir = "screenshots";
        opts.Log = Console.WriteLine;
    });

// In tests
var session = await UITestSession.AttachAsync(window);
await session.NavigateAsync("profile");
await session.ScreenshotAsync("profile-page");
await session.AssertPropertyAsync("UserName", "Scott");

// Record
var recorder = new UIRecorder();
recorder.StartRecording(window);
// ... interactions ...
recorder.SaveAsYaml("test.yaml");

// MCP (launch with --ux-mcp flag)
dotnet run --project MyApp -- --ux-mcp
```

## Decisions

1. **System.Text.Json over Newtonsoft** - Zero extra dependency, built into .NET
2. **SkiaSharp for GIF** - Already available via Avalonia.Skia, no new binary deps
3. **FFmpeg optional** - MP4 as bonus, not required
4. **PointerEventArgs not constructable** - Avalonia doesn't expose public constructors. Mouse position actions log intent; actual interaction uses named controls.
5. **consoleimage for LLM vision** - External tool renders screenshots as ASCII art so text-only LLMs can "see" the UI
