# lucidRESUME.UXTesting

A UX testing framework for Avalonia applications. Provides interactive REPL, script execution, and MCP server modes for automated UI testing and LLM-driven interaction.

## Installation

Add to your Avalonia project:

```xml
<PackageReference Include="lucidRESUME.UXTesting" Version="1.0.0" />
```

## Quick Start

### Interactive REPL

```bash
# Start your app with the UX REPL
dotnet run -- --ux-repl

=== UX Testing REPL ===
Window: MyApp
DataContext: MainWindowViewModel
Type 'help' for commands, 'exit' to quit

ux> vm
ViewModel (MainWindowViewModel):
  CurrentPage: ResumePageViewModel
  SelectedNav: Resume

ux> nav Jobs
Navigated to: Jobs

ux> screenshot jobs-page
Screenshot: ux-screenshots/jobs-page.png

ux> exit
```

### Script Execution

```bash
# Run automated test script
dotnet run -- --ux-test --script tests/navigation.yaml --output test-results/
```

### MCP Server (for LLMs)

```bash
# Start MCP server for LLM control
dotnet run -- --ux-mcp
```

## CLI Reference

| Flag | Description |
|------|-------------|
| `--ux-repl` | Start interactive REPL mode |
| `--ux-test` | Run test script (requires `--script`) |
| `--ux-mcp` | Start MCP server on stdio |
| `--script <path>` | Path to YAML test script |
| `--output <dir>` | Output directory for screenshots (default: `ux-screenshots` or `ux-test-results`) |

## REPL Commands

### Navigation

| Command | Description | Example |
|---------|-------------|---------|
| `nav <page>` | Navigate to a page | `nav Jobs` |
| `click <name>` | Click a named control | `nav SubmitButton` |
| `type <name> <text>` | Type text into TextBox | `type SearchBox "developer"` |
| `press <key>` | Press a key | `press Enter` |

### Inspection

| Command | Description | Example |
|---------|-------------|---------|
| `get <path>` | Get property value | `get CurrentPage.FullName` |
| `set <path> <value>` | Set property value | `set CurrentPage.SearchText "test"` |
| `vm` | Show ViewModel properties | `vm` |
| `tree` | Show visual tree | `tree` |
| `list [controls\|vms]` | List controls or ViewModels | `list controls` |

### Screenshots

| Command | Description | Example |
|---------|-------------|---------|
| `screenshot [name]` | Capture PNG screenshot | `screenshot login-page` |
| `describe [name]` | Render screenshot as ASCII (via consoleimage) | `describe login` |

### Control Flow

| Command | Description | Example |
|---------|-------------|---------|
| `wait <ms>` | Wait for milliseconds | `wait 500` |
| `waitfor <path> <value> [timeout]` | Wait until property equals value | `waitfor CurrentPage.IsLoading false 5000` |
| `assert <path> <value>` | Assert property value | `assert SelectedNav Jobs` |
| `run <script.yaml>` | Run another script | `run tests/common.yaml` |
| `exit` | Exit REPL | `exit` |

### Advanced

| Command | Description | Example |
|---------|-------------|---------|
| `service <type>` | Get service from DI container | `service IAppStore` |

## Script Format

Scripts are YAML files with the following structure:

```yaml
name: my-test-script
description: Test user registration flow
default_delay: 200  # Default delay between actions (ms)

actions:
  # Navigate to a page
  - type: Navigate
    value: Register
    description: Go to registration page
    
  # Type into a TextBox
  - type: TypeText
    target: EmailTextBox
    value: "user@example.com"
    delay_ms: 100
    
  # Click a button
  - type: Click
    target: SubmitButton
    
  # Press a key
  - type: PressKey
    value: Enter
    
  # Wait for a duration
  - type: Wait
    value: "1000"  # Can be string or number
    
  # Capture screenshot
  - type: Screenshot
    value: registration-complete
    
  # Assert state
  - type: Assert
    target: CurrentPage.IsSuccess
    value: "true"
```

### Action Types

| Type | Properties | Description |
|------|------------|-------------|
| `Navigate` | `value` (page name) | Navigate to a page |
| `Click` | `target` (control name) | Click a control |
| `DoubleClick` | `target` | Double-click a control |
| `TypeText` | `target`, `value` | Type text into TextBox |
| `PressKey` | `value` (key name) | Press a key (Enter, Tab, Escape, etc.) |
| `Wait` | `value` (ms) | Wait for duration |
| `Screenshot` | `value` (filename) | Capture screenshot |
| `Assert` | `target`, `value` | Assert property value |

### Action Properties

| Property | Type | Description |
|----------|------|-------------|
| `type` | string | Action type (required) |
| `target` | string | Control name or property path |
| `value` | string | Value for action |
| `delay_ms` | int | Override default delay |
| `description` | string | Human-readable description |

## Programmatic API

### Basic Usage

```csharp
using lucidRESUME.UXTesting;

// Attach to existing window
var session = await UXSession.AttachAsync(window, options =>
{
    options.ScreenshotDir = "screenshots";
    options.Log = msg => Console.WriteLine(msg);
});

// Navigate
await session.NavigateAsync("Jobs");

// Click
await session.ClickAsync("SearchButton");

// Type
await session.TypeAsync("SearchBox", "developer jobs");

// Get property
var page = await session.GetPropertyAsync<string>("CurrentPage");
Console.WriteLine($"Current page: {page}");

// Screenshot
var path = await session.ScreenshotAsync("jobs-search");

// Assert
await session.AssertPropertyAsync("CurrentPage.HasResults", true);

// Wait for condition
await session.WaitForPropertyAsync("CurrentPage.IsLoading", false, timeoutMs: 5000);

// Cleanup
await session.DisposeAsync();
```

### With Unit Test Framework

```csharp
using lucidRESUME.UXTesting;

[TestFixture]
public class NavigationTests
{
    private UXSession _session;
    
    [SetUp]
    public async Task Setup()
    {
        var window = CreateMainWindow();
        _session = await UXSession.AttachAsync(window);
    }
    
    [TearDown]
    public async Task Teardown()
    {
        await _session.DisposeAsync();
    }
    
    [Test]
    public async Task Navigate_ShouldChangeCurrentPage()
    {
        await _session.NavigateAsync("Jobs");
        
        var page = await _session.GetPropertyAsync<string>("CurrentPage");
        Assert.That(page, Does.Contain("JobsPageViewModel"));
    }
    
    [Test]
    public async Task Search_ShouldShowResults()
    {
        await _session.NavigateAsync("Search");
        await _session.TypeAsync("SearchBox", "developer");
        await _session.ClickAsync("SearchButton");
        await _session.WaitForPropertyAsync("CurrentPage.IsLoading", false);
        
        await _session.AssertPropertyAsync("CurrentPage.HasResults", true);
    }
}
```

### Get ViewModel Properties

```csharp
// Get all ViewModel properties
var props = await session.GetViewModelPropertiesAsync();
foreach (var (name, value) in props)
{
    Console.WriteLine($"{name} = {value}");
}

// Get specific property
var name = await session.GetPropertyAsync<string>("CurrentPage.FullName");
var count = await session.GetPropertyAsync<int>("CurrentPage.ItemCount");
```

### Get Visual Tree

```csharp
// Get visual tree as string
var tree = await session.GetTreeAsync();
Console.WriteLine(tree);

// Output:
// MainWindow
//   Grid
//     Border
//       StackPanel
//         TextBlock
//         Button
//         Button
//     ContentControl

// Get controls as data
var controls = await session.GetControlsAsync();
foreach (var control in controls)
{
    Console.WriteLine($"{control.Name} ({control.Type}) at {control.Bounds}");
}
```

## MCP Server

The MCP server mode allows LLMs to control your application via the Model Context Protocol.

### Starting the Server

```bash
dotnet run -- --ux-mcp
```

The server communicates via stdio using JSON-RPC 2.0.

### Available Tools

| Tool | Parameters | Description |
|------|------------|-------------|
| `ux_navigate` | `page` | Navigate to page |
| `ux_click` | `name` | Click control |
| `ux_type` | `name`, `text` | Type text |
| `ux_press` | `key` | Press key |
| `ux_get` | `path` | Get property |
| `ux_set` | `path`, `value` | Set property |
| `ux_screenshot` | `name` (optional) | Capture screenshot |
| `ux_describe` | `name` (optional) | Render as ASCII |
| `ux_tree` | - | Get visual tree |
| `ux_vm` | - | Get ViewModel properties |
| `ux_wait` | `ms` | Wait duration |
| `ux_waitfor` | `path`, `value`, `timeout` | Wait for property |
| `ux_assert` | `path`, `value` | Assert property |
| `ux_exit` | - | Exit session |

### Example MCP Interaction

```json
// Request
{"jsonrpc": "2.0", "method": "tools/call", "params": {"name": "ux_navigate", "arguments": {"page": "Jobs"}}, "id": 1}

// Response
{"jsonrpc": "2.0", "result": {"content": [{"type": "text", "text": "Navigated to: Jobs"}]}, "id": 1}
```

## Integration with App

### Method 1: CLI Arguments (No Code Changes)

Simply run your app with UX testing flags:

```bash
dotnet run -- --ux-repl
dotnet run -- --ux-test --script tests/smoke.yaml
dotnet run -- --ux-mcp
```

### Method 2: UseUXTesting Extension

Add to `App.axaml.cs`:

```csharp
using lucidRESUME.UXTesting;

public override void OnFrameworkInitializationCompleted()
{
    // Your existing setup...
    
    this.UseUXTesting(options =>
    {
        options.DefaultScreenshotDir = "screenshots";
        options.DefaultDelay = 200;
        options.CaptureScreenshotsByDefault = true;
        options.Log = msg => Debug.WriteLine(msg);
    });
    
    base.OnFrameworkInitializationCompleted();
}
```

### Method 3: Direct Integration

```csharp
using lucidRESUME.UXTesting;

private async Task RunCustomTestAsync(MainWindow window)
{
    var ctx = new UXContext
    {
        MainWindow = window,
        Services = _serviceProvider,
        Navigate = page => _viewModel.NavigateCommand.Execute(page)
    };
    
    var repl = new UXRepl(ctx, "screenshots");
    
    // Execute commands programmatically
    await repl.ExecuteCommandAsync("nav Jobs");
    await repl.ExecuteCommandAsync("screenshot jobs");
    
    // Or run interactive
    await repl.RunAsync();
}
```

## Requirements

- .NET 8.0 or later
- Avalonia 11.0 or later
- Optional: [consoleimage](https://github.com/scottgal/mostlylucid.consoleimage) for `describe` command

## Troubleshooting

### Screenshots are blank

Ensure the window is fully loaded before capturing. Add a small delay:

```yaml
actions:
  - type: Navigate
    value: Jobs
  - type: Wait
    value: "500"
  - type: Screenshot
    value: jobs-page
```

### Navigation doesn't work

Page names are case-sensitive and must match your ViewModel's navigation command. Check with:

```
ux> vm
ViewModel (MainWindowViewModel):
  CurrentPage: ResumePageViewModel
```

### Control not found

Named controls must have `x:Name` set in XAML:

```xml
<Button x:Name="SubmitButton" Content="Submit" />
```

List available controls:

```
ux> list controls
```

### describe command fails

Install consoleimage:

```bash
dotnet tool install -g mostlylucid.consoleimage
```

## License

MIT
