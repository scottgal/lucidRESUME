using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mostlylucid.Avalonia.UITesting.Players;
using Mostlylucid.Avalonia.UITesting.Recorders;
using Mostlylucid.Avalonia.UITesting.Video;

namespace Mostlylucid.Avalonia.UITesting.Mcp;

public sealed class UITestMcpServer
{
    private readonly UITestContext _ctx;
    private readonly string _screenshotDir;
    private readonly string _consoleImagePath;
    private bool _running = true;
    private UIRecorder? _recorder;
    private GifRecorder? _videoRecorder;
    private readonly Lazy<PointerSimulator> _pointer = new(() => new PointerSimulator());

    public UITestMcpServer(UITestContext context, string screenshotDir = "ux-screenshots", string? consoleImagePath = null)
    {
        _ctx = context;
        _screenshotDir = screenshotDir;
        _consoleImagePath = consoleImagePath ?? "consoleimage";
        Directory.CreateDirectory(screenshotDir);
    }

    public async Task RunStdioAsync()
    {
        Console.Error.WriteLine("UI Test MCP Server starting (stdio mode)...");

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdin, Encoding.UTF8);
        using var writer = new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true };

        while (_running)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            try
            {
                var request = JsonDocument.Parse(line);
                await HandleRequestAsync(writer, request);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(StreamWriter writer, JsonDocument request)
    {
        var root = request.RootElement;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        var id = root.TryGetProperty("id", out var i) ? i : default;

        switch (method)
        {
            case "initialize":
                await SendResultAsync(writer, id, new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { listChanged = false }
                    },
                    serverInfo = new { name = "mostlylucid-ui-testing", version = "1.0.0" }
                });
                break;

            case "notifications/initialized":
                // No response needed for notifications
                break;

            case "tools/list":
                await SendResultAsync(writer, id, new { tools = GetToolDefinitions() });
                break;

            case "tools/call":
                var toolName = root.GetProperty("params").GetProperty("name").GetString();
                var args = root.GetProperty("params").TryGetProperty("arguments", out var a) ? a : default;
                var result = await ExecuteToolAsync(toolName ?? "", args);
                await SendResultAsync(writer, id, new
                {
                    content = result
                });
                break;

            default:
                await SendErrorAsync(writer, id, -32601, $"Method not found: {method}");
                break;
        }
    }

    private static object[] GetToolDefinitions()
    {
        return new object[]
        {
            // Navigation & Interaction
            Tool("ui_navigate", "Navigate to a named page/view in the application",
                ("page", "string", "Page name to navigate to", true)),

            Tool("ui_click", "Click a named control (button, tab, etc.)",
                ("name", "string", "Control name", true),
                ("window", "string", "Window name/title (optional, defaults to main)", false)),

            Tool("ui_double_click", "Double-click a named control",
                ("name", "string", "Control name", true),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_type", "Type text into a named TextBox control",
                ("name", "string", "TextBox control name", true),
                ("text", "string", "Text to type", true),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_press", "Press a keyboard key (Enter, Tab, Escape, etc.)",
                ("key", "string", "Key name (Enter, Tab, Escape, F1-F12, etc.)", true),
                ("target", "string", "Control name to send key to (optional)", false)),

            Tool("ui_scroll", "Scroll a ScrollViewer control",
                ("direction", "string", "Direction: up, down, top, bottom", true),
                ("target", "string", "ScrollViewer name (optional, uses first found)", false)),

            Tool("ui_hover", "Hover over a named control",
                ("name", "string", "Control name", true)),

            // Mouse (pixel-level — real input dispatched through Avalonia's input pipeline)
            Tool("ui_mouse_move", "Move mouse to exact coordinates on window",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_mouse_click", "Click at exact coordinates on window",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("button", "string", "Mouse button: left (default), right, middle, x1, x2", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_mouse_down", "Press a mouse button at exact coordinates (does not release)",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("button", "string", "Mouse button: left (default), right, middle, x1, x2", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_mouse_up", "Release a mouse button at exact coordinates",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("button", "string", "Mouse button: left (default), right, middle, x1, x2", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_drag", "Press, drag with interpolated movement, then release",
                ("x1", "number", "Start X", true),
                ("y1", "number", "Start Y", true),
                ("x2", "number", "End X", true),
                ("y2", "number", "End Y", true),
                ("button", "string", "Mouse button (default left)", false),
                ("steps", "number", "Number of intermediate moves (default 10)", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_wheel", "Inject a real mouse wheel event at coordinates",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("dx", "number", "Horizontal delta (default 0)", false),
                ("dy", "number", "Vertical delta (positive=up, default -1)", false),
                ("window", "string", "Window name/title (optional)", false)),

            // Touchpad gestures (cross-platform)
            Tool("ui_pinch", "Touchpad pinch (magnify) gesture at coordinates",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("scale", "number", "Total scale delta (e.g. 0.2 = 20% larger)", true),
                ("steps", "number", "Number of gesture frames (default 10)", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_rotate", "Touchpad rotate gesture at coordinates",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("angle", "number", "Total rotation in degrees", true),
                ("steps", "number", "Number of gesture frames (default 10)", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_swipe", "Touchpad swipe gesture",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("dx", "number", "Horizontal swipe delta", true),
                ("dy", "number", "Vertical swipe delta", true),
                ("steps", "number", "Number of gesture frames (default 5)", false),
                ("window", "string", "Window name/title (optional)", false)),

            // Touch input
            Tool("ui_touch_tap", "Single-finger touch tap at coordinates",
                ("x", "number", "X coordinate", true),
                ("y", "number", "Y coordinate", true),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_touch_drag", "Single-finger touch drag",
                ("x1", "number", "Start X", true),
                ("y1", "number", "Start Y", true),
                ("x2", "number", "End X", true),
                ("y2", "number", "End Y", true),
                ("steps", "number", "Number of intermediate moves (default 10)", false),
                ("window", "string", "Window name/title (optional)", false)),

            // Window operations
            Tool("ui_window_resize", "Resize a window",
                ("width", "number", "Width in DIPs", true),
                ("height", "number", "Height in DIPs", true),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_move", "Move a window to screen coordinates",
                ("x", "number", "Screen X (pixels)", true),
                ("y", "number", "Screen Y (pixels)", true),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_minimize", "Minimize a window",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_maximize", "Maximize a window",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_restore", "Restore a window to normal state",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_fullscreen", "Put window into full screen",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_focus", "Activate and focus a window",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_close", "Close a window",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_set_title", "Set the window title",
                ("title", "string", "New title", true),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_window_info", "Get window size, position, and state",
                ("window", "string", "Window name/title (optional)", false)),

            // Vision - THE KEY FEATURE for LLMs
            Tool("ui_see", "Take a screenshot and render it as ASCII art via consoleimage so you can SEE the current UI state. This is your primary way to understand what the application looks like. Returns both the screenshot path and a text description of the UI.",
                ("name", "string", "Optional screenshot name", false),
                ("window", "string", "Window name/title (optional)", false),
                ("max_width", "number", "Max ASCII art width in chars (default 120)", false),
                ("max_height", "number", "Max ASCII art height in chars (default 40)", false)),

            Tool("ui_screenshot", "Capture a PNG screenshot without ASCII rendering",
                ("name", "string", "Optional filename", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_snip_region", "Snip a rectangular region of the window to PNG (for manuals/docs)",
                ("x", "number", "X in DIPs", true),
                ("y", "number", "Y in DIPs", true),
                ("width", "number", "Width in DIPs", true),
                ("height", "number", "Height in DIPs", true),
                ("name", "string", "Optional filename", false),
                ("padding", "number", "Extra DIPs around the region (default 0)", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_snip_control", "Snip a single named control's bounds to PNG",
                ("name", "string", "Control name", true),
                ("file", "string", "Optional output filename", false),
                ("padding", "number", "Extra DIPs around the control (default 0)", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_snip_controls", "Snip the bounding box of multiple controls (comma-separated names)",
                ("names", "string", "Comma-separated control names", true),
                ("file", "string", "Optional output filename", false),
                ("padding", "number", "Extra DIPs around the group (default 0)", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_svg", "Export the current UI as an SVG file by walking the visual tree. Produces a scalable vector representation of the UI layout, text, shapes, and colors - no Skia dependency.",
                ("name", "string", "Optional filename", false),
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_screenshot_base64", "Capture a screenshot and return it as base64-encoded PNG data for direct image analysis",
                ("name", "string", "Optional filename", false),
                ("window", "string", "Window name/title (optional)", false)),

            // Inspection
            Tool("ui_tree", "Get the visual tree of the current window showing all controls and their hierarchy",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_controls", "List all named controls in the current window with their types and bounds",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_vm", "Get all ViewModel properties and their current values",
                ("window", "string", "Window name/title (optional)", false)),

            Tool("ui_get", "Get a ViewModel property value by path (e.g., 'CurrentPage.Name')",
                ("path", "string", "Property path using dot notation", true)),

            Tool("ui_set", "Set a ViewModel property value",
                ("path", "string", "Property path", true),
                ("value", "string", "Value to set", true)),

            Tool("ui_windows", "List all tracked windows (main + popups/dialogs)"),

            // Assertions & Waiting
            Tool("ui_wait", "Wait for a specified number of milliseconds",
                ("ms", "number", "Milliseconds to wait", true)),

            Tool("ui_wait_for", "Wait for a property to reach a specific value (with timeout)",
                ("path", "string", "Property path", true),
                ("value", "string", "Expected value", true),
                ("timeout", "number", "Timeout in ms (default 5000)", false)),

            Tool("ui_assert", "Assert that a property equals an expected value",
                ("path", "string", "Property path", true),
                ("value", "string", "Expected value", true)),

            Tool("ui_assert_control", "Assert a control property (visible, enabled, text)",
                ("name", "string", "Control name", true),
                ("property", "string", "Property to check: visible, enabled, text", true),
                ("value", "string", "Expected value", true)),

            // Recording
            Tool("ui_record_start", "Start recording user interactions as a replayable script",
                ("video", "boolean", "Also record video as GIF (default false)", false),
                ("positions", "boolean", "Record mouse positions (default false)", false)),

            Tool("ui_record_stop", "Stop recording and return action count"),

            Tool("ui_record_save", "Save the recorded interactions as a YAML or JSON script",
                ("path", "string", "File path (e.g., test.yaml or test.json)", true),
                ("name", "string", "Script name (optional)", false)),

            // Video
            Tool("ui_video_start", "Start recording the UI as an animated GIF",
                ("fps", "number", "Frames per second (1-30, default 5)", false),
                ("window", "string", "Window to record (optional)", false)),

            Tool("ui_video_stop", "Stop video recording and save as GIF (and MP4 if ffmpeg available)",
                ("name", "string", "Output filename without extension", false)),

            // Script
            Tool("ui_run_script", "Run a YAML/JSON test script file",
                ("path", "string", "Path to script file", true)),

            // Exit
            Tool("ui_exit", "Shut down the MCP server and exit the application")
        };
    }

    private async Task<object[]> ExecuteToolAsync(string tool, JsonElement args)
    {
        try
        {
            return tool switch
            {
                "ui_navigate" => TextResult(await NavigateAsync(GetArg(args, "page"))),
                "ui_click" => TextResult(await ClickAsync(GetArg(args, "name"), GetArg(args, "window"))),
                "ui_double_click" => TextResult(await DoubleClickAsync(GetArg(args, "name"), GetArg(args, "window"))),
                "ui_type" => TextResult(await TypeAsync(GetArg(args, "name"), GetArg(args, "text"), GetArg(args, "window"))),
                "ui_press" => TextResult(await PressAsync(GetArg(args, "key"), GetArg(args, "target"))),
                "ui_scroll" => TextResult(await ScrollAsync(GetArg(args, "direction"), GetArg(args, "target"))),
                "ui_hover" => TextResult(await HoverAsync(GetArg(args, "name"))),
                "ui_mouse_move" => TextResult(await MouseMoveAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetArg(args, "window"))),
                "ui_mouse_click" => TextResult(await MouseClickAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetArg(args, "button"), GetArg(args, "window"))),
                "ui_mouse_down" => TextResult(await MouseDownAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetArg(args, "button"), GetArg(args, "window"))),
                "ui_mouse_up" => TextResult(await MouseUpAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetArg(args, "button"), GetArg(args, "window"))),
                "ui_drag" => TextResult(await DragAsync(GetDouble(args, "x1"), GetDouble(args, "y1"), GetDouble(args, "x2"), GetDouble(args, "y2"), GetArg(args, "button"), GetInt(args, "steps", 10), GetArg(args, "window"))),
                "ui_wheel" => TextResult(await WheelAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetDouble(args, "dx", 0), GetDouble(args, "dy", -1), GetArg(args, "window"))),
                "ui_pinch" => TextResult(await PinchAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetDouble(args, "scale"), GetInt(args, "steps", 10), GetArg(args, "window"))),
                "ui_rotate" => TextResult(await RotateAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetDouble(args, "angle"), GetInt(args, "steps", 10), GetArg(args, "window"))),
                "ui_swipe" => TextResult(await SwipeAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetDouble(args, "dx"), GetDouble(args, "dy"), GetInt(args, "steps", 5), GetArg(args, "window"))),
                "ui_touch_tap" => TextResult(await TouchTapAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetArg(args, "window"))),
                "ui_touch_drag" => TextResult(await TouchDragAsync(GetDouble(args, "x1"), GetDouble(args, "y1"), GetDouble(args, "x2"), GetDouble(args, "y2"), GetInt(args, "steps", 10), GetArg(args, "window"))),
                "ui_window_resize" => TextResult(await WindowResizeAsync(GetDouble(args, "width"), GetDouble(args, "height"), GetArg(args, "window"))),
                "ui_window_move" => TextResult(await WindowMoveAsync(GetInt(args, "x"), GetInt(args, "y"), GetArg(args, "window"))),
                "ui_window_minimize" => TextResult(await WindowStateAsync(WindowState.Minimized, GetArg(args, "window"))),
                "ui_window_maximize" => TextResult(await WindowStateAsync(WindowState.Maximized, GetArg(args, "window"))),
                "ui_window_restore" => TextResult(await WindowStateAsync(WindowState.Normal, GetArg(args, "window"))),
                "ui_window_fullscreen" => TextResult(await WindowStateAsync(WindowState.FullScreen, GetArg(args, "window"))),
                "ui_window_focus" => TextResult(await WindowFocusAsync(GetArg(args, "window"))),
                "ui_window_close" => TextResult(await WindowCloseAsync(GetArg(args, "window"))),
                "ui_window_set_title" => TextResult(await WindowSetTitleAsync(GetArg(args, "title"), GetArg(args, "window"))),
                "ui_window_info" => TextResult(await WindowInfoAsync(GetArg(args, "window"))),
                "ui_see" => await SeeAsync(GetArg(args, "name"), GetArg(args, "window"), GetInt(args, "max_width", 120), GetInt(args, "max_height", 40)),
                "ui_screenshot" => TextResult(await CaptureScreenshotAsync(GetArg(args, "name"), GetArg(args, "window"))),
                "ui_snip_region" => TextResult(await SnipRegionAsync(GetDouble(args, "x"), GetDouble(args, "y"), GetDouble(args, "width"), GetDouble(args, "height"), GetArg(args, "name"), GetDouble(args, "padding", 0), GetArg(args, "window"))),
                "ui_snip_control" => TextResult(await SnipControlAsync(GetArg(args, "name"), GetArg(args, "file"), GetDouble(args, "padding", 0), GetArg(args, "window"))),
                "ui_snip_controls" => TextResult(await SnipControlsAsync(GetArg(args, "names"), GetArg(args, "file"), GetDouble(args, "padding", 0), GetArg(args, "window"))),
                "ui_svg" => TextResult(await CaptureSvgAsync(GetArg(args, "name"), GetArg(args, "window"))),
                "ui_screenshot_base64" => await ScreenshotBase64Async(GetArg(args, "name"), GetArg(args, "window")),
                "ui_tree" => TextResult(await GetTreeAsync(GetArg(args, "window"))),
                "ui_controls" => TextResult(await GetControlsAsync(GetArg(args, "window"))),
                "ui_vm" => TextResult(await GetVmAsync(GetArg(args, "window"))),
                "ui_get" => TextResult(await GetAsync(GetArg(args, "path"))),
                "ui_set" => TextResult(await SetAsync(GetArg(args, "path"), GetArg(args, "value"))),
                "ui_windows" => TextResult(await GetWindowsAsync()),
                "ui_wait" => TextResult(await WaitAsync(GetInt(args, "ms", 1000))),
                "ui_wait_for" => TextResult(await WaitForAsync(GetArg(args, "path"), GetArg(args, "value"), GetInt(args, "timeout", 5000))),
                "ui_assert" => TextResult(await AssertAsync(GetArg(args, "path"), GetArg(args, "value"))),
                "ui_assert_control" => TextResult(await AssertControlAsync(GetArg(args, "name"), GetArg(args, "property"), GetArg(args, "value"))),
                "ui_record_start" => TextResult(await RecordStartAsync(GetBool(args, "video"), GetBool(args, "positions"))),
                "ui_record_stop" => TextResult(await RecordStopAsync()),
                "ui_record_save" => TextResult(await RecordSaveAsync(GetArg(args, "path"), GetArg(args, "name"))),
                "ui_video_start" => TextResult(await VideoStartAsync(GetInt(args, "fps", 5), GetArg(args, "window"))),
                "ui_video_stop" => TextResult(await VideoStopAsync(GetArg(args, "name"))),
                "ui_run_script" => TextResult(await RunScriptAsync(GetArg(args, "path"))),
                "ui_exit" => TextResult(Exit()),
                _ => TextResult($"Unknown tool: {tool}")
            };
        }
        catch (Exception ex)
        {
            return TextResult($"Error: {ex.Message}");
        }
    }

    // === Navigation & Interaction ===

    private async Task<string> NavigateAsync(string page)
    {
        await _ctx.RunOnUIThreadAsync(() => _ctx.Navigate?.Invoke(page));
        await Task.Delay(200);
        return $"Navigated to: {page}";
    }

    private async Task<string> ClickAsync(string name, string? windowId)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var window = _ctx.FindWindow(windowId);
            var control = _ctx.FindControl(name, window);
            if (control == null) return $"Control not found: {name}";

            if (control is Button button && button.Command?.CanExecute(button.CommandParameter) == true)
            {
                button.Command.Execute(button.CommandParameter);
                return $"Clicked button: {name}";
            }

            if (control is TabItem tabItem)
            {
                tabItem.IsSelected = true;
                return $"Selected tab: {name}";
            }

            control.RaiseEvent(new RoutedEventArgs(InputElement.TappedEvent));
            return $"Tapped: {name}";
        });
    }

    private async Task<string> DoubleClickAsync(string name, string? windowId)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var window = _ctx.FindWindow(windowId);
            var control = _ctx.FindControl(name, window);
            if (control == null) return $"Control not found: {name}";
            control.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
            return $"Double-clicked: {name}";
        });
    }

    private async Task<string> TypeAsync(string name, string text, string? windowId)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var window = _ctx.FindWindow(windowId);
            var control = _ctx.FindControl(name, window);
            if (control is TextBox textBox)
            {
                textBox.Text = text;
                return $"Typed into {name}: {text}";
            }
            return $"Control is not a TextBox: {name}";
        });
    }

    private async Task<string> PressAsync(string key, string? target)
    {
        var keyEnum = Enum.Parse<Key>(key, true);
        await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = target != null ? _ctx.FindControl(target) : _ctx.MainWindow;
            control?.RaiseEvent(new KeyEventArgs
            {
                Key = keyEnum,
                RoutedEvent = InputElement.KeyDownEvent
            });
        });
        return $"Pressed: {key}";
    }

    private async Task<string> ScrollAsync(string direction, string? target)
    {
        await _ctx.RunOnUIThreadAsync(() =>
        {
            // Use the locator engine so namescope-isolated ScrollViewers (inside
            // UserControls, popups, templated parts) are found regardless of
            // XAML name scope. The locator returns Control; we then descend to a
            // ScrollViewer if the resolved control is not one itself.
            ScrollViewer? sv;
            if (!string.IsNullOrEmpty(target))
            {
                var resolved = _ctx.FindControl(target);
                sv = resolved as ScrollViewer ?? resolved?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            }
            else
            {
                sv = _ctx.MainWindow?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            }

            if (sv != null)
            {
                switch (direction.ToLowerInvariant())
                {
                    case "top": sv.ScrollToHome(); break;
                    case "bottom": sv.ScrollToEnd(); break;
                    case "up": sv.LineUp(); break;
                    default: sv.LineDown(); break;
                }
            }
        });
        return $"Scrolled: {direction}";
    }

    private async Task<string> HoverAsync(string name)
    {
        var control = await _ctx.RunOnUIThreadAsync(() => _ctx.FindControl(name));
        if (control == null) return $"Control not found: {name}";
        var window = await _ctx.RunOnUIThreadAsync(() => FindWindowOf(control));
        if (window == null) return $"No parent window for control: {name}";

        var (cx, cy) = await GetControlCenterAsync(control, window);
        await _pointer.Value.HoverAsync(window, cx, cy);
        return $"Hovered over {name} at ({cx:F0}, {cy:F0})";
    }

    private async Task<string> MouseMoveAsync(double x, double y, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _pointer.Value.MoveAsync(window, x, y);
        return $"Mouse moved to ({x}, {y})";
    }

    private async Task<string> MouseClickAsync(double x, double y, string? buttonName, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        var button = ParseButton(buttonName);
        await _pointer.Value.ClickAsync(window, x, y, button);
        return $"Mouse click [{button}] at ({x}, {y})";
    }

    private async Task<string> MouseDownAsync(double x, double y, string? buttonName, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        var button = ParseButton(buttonName);
        await _pointer.Value.DownAsync(window, x, y, button);
        return $"Mouse down [{button}] at ({x}, {y})";
    }

    private async Task<string> MouseUpAsync(double x, double y, string? buttonName, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        var button = ParseButton(buttonName);
        await _pointer.Value.UpAsync(window, x, y, button);
        return $"Mouse up [{button}] at ({x}, {y})";
    }

    private async Task<string> DragAsync(double x1, double y1, double x2, double y2, string? buttonName, int steps, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        var button = ParseButton(buttonName);
        await _pointer.Value.DragAsync(window, x1, y1, x2, y2, steps, 16, button);
        return $"Dragged [{button}] ({x1},{y1}) → ({x2},{y2}) in {steps} steps";
    }

    private async Task<string> WheelAsync(double x, double y, double dx, double dy, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _pointer.Value.WheelAsync(window, x, y, dx, dy);
        return $"Wheel at ({x},{y}) Δ=({dx},{dy})";
    }

    private async Task<string> PinchAsync(double x, double y, double scale, int steps, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        for (int i = 0; i < steps; i++)
            await _pointer.Value.MagnifyAsync(window, x, y, scale / steps);
        return $"Pinch at ({x},{y}) Δ={scale} over {steps} frames";
    }

    private async Task<string> RotateAsync(double x, double y, double angle, int steps, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        for (int i = 0; i < steps; i++)
            await _pointer.Value.RotateAsync(window, x, y, angle / steps);
        return $"Rotate at ({x},{y}) Δ={angle}° over {steps} frames";
    }

    private async Task<string> SwipeAsync(double x, double y, double dx, double dy, int steps, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        for (int i = 0; i < steps; i++)
            await _pointer.Value.SwipeAsync(window, x, y, dx / steps, dy / steps);
        return $"Swipe at ({x},{y}) Δ=({dx},{dy})";
    }

    private async Task<string> TouchTapAsync(double x, double y, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _pointer.Value.TouchTapAsync(window, x, y);
        return $"Touch tap at ({x},{y})";
    }

    private async Task<string> TouchDragAsync(double x1, double y1, double x2, double y2, int steps, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _pointer.Value.TouchDragAsync(window, x1, y1, x2, y2, steps);
        return $"Touch drag ({x1},{y1}) → ({x2},{y2}) in {steps} steps";
    }

    // === Window operations ===

    private async Task<string> WindowResizeAsync(double width, double height, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _ctx.RunOnUIThreadAsync(() => { window.Width = width; window.Height = height; });
        return $"Window resized to {width}x{height}";
    }

    private async Task<string> WindowMoveAsync(int x, int y, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _ctx.RunOnUIThreadAsync(() => window.Position = new PixelPoint(x, y));
        return $"Window moved to ({x},{y})";
    }

    private async Task<string> WindowStateAsync(WindowState state, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _ctx.RunOnUIThreadAsync(() => window.WindowState = state);
        return $"Window state: {state}";
    }

    private async Task<string> WindowFocusAsync(string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _ctx.RunOnUIThreadAsync(() => { window.Activate(); window.Focus(); });
        return "Window focused";
    }

    private async Task<string> WindowCloseAsync(string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _ctx.RunOnUIThreadAsync(() => window.Close());
        return "Window closed";
    }

    private async Task<string> WindowSetTitleAsync(string title, string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        await _ctx.RunOnUIThreadAsync(() => window.Title = title);
        return $"Window title: {title}";
    }

    private async Task<string> WindowInfoAsync(string? windowId)
    {
        var window = ResolveWindow(windowId) ?? throw new InvalidOperationException("No window");
        return await _ctx.RunOnUIThreadAsync(() =>
            $"size={window.Width}x{window.Height} pos=({window.Position.X},{window.Position.Y}) state={window.WindowState} title=\"{window.Title}\"");
    }

    private Window? ResolveWindow(string? windowId) => _ctx.FindWindow(windowId) ?? _ctx.MainWindow;

    private static Window? FindWindowOf(Control? control)
    {
        while (control != null)
        {
            if (control is Window w) return w;
            control = control.Parent as Control;
        }
        return null;
    }

    private static async Task<(double X, double Y)> GetControlCenterAsync(Control control, Window window)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var topLeft = control.TranslatePoint(new Point(0, 0), window) ?? new Point(0, 0);
            var b = control.Bounds;
            return (topLeft.X + b.Width / 2, topLeft.Y + b.Height / 2);
        });
    }

    private static MouseButton ParseButton(string? button)
    {
        if (string.IsNullOrEmpty(button)) return MouseButton.Left;
        return button.ToLowerInvariant() switch
        {
            "right" or "rmb" => MouseButton.Right,
            "middle" or "mmb" => MouseButton.Middle,
            "x1" or "back" => MouseButton.XButton1,
            "x2" or "forward" => MouseButton.XButton2,
            _ => MouseButton.Left
        };
    }

    // === Vision (the key feature for LLMs) ===

    private async Task<object[]> SeeAsync(string? name, string? windowId, int maxWidth, int maxHeight)
    {
        var screenshotPath = await CaptureScreenshotAsync(name ?? $"see_{DateTime.UtcNow:HHmmss}", windowId);

        // Also get the control tree for structured context
        var tree = await GetTreeAsync(windowId);
        var controls = await GetControlsAsync(windowId);

        // Try consoleimage for ASCII rendering
        string asciiArt;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _consoleImagePath,
                Arguments = $"\"{screenshotPath}\" --max-width {maxWidth} --max-height {maxHeight}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                asciiArt = "[consoleimage not available - install it for visual UI rendering]";
            }
            else
            {
                asciiArt = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    asciiArt = "[consoleimage returned an error]";
            }
        }
        catch
        {
            asciiArt = "[consoleimage not found - install via: dotnet tool install -g consoleimage]";
        }

        // Try to also return the image as base64 for multimodal LLMs
        string? base64 = null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(screenshotPath);
            base64 = Convert.ToBase64String(bytes);
        }
        catch { }

        var content = new List<object>
        {
            new { type = "text", text = $"=== UI Screenshot: {screenshotPath} ===\n\n{asciiArt}\n\n=== Named Controls ===\n{controls}\n\n=== Visual Tree ===\n{tree}" }
        };

        // If we got base64, include as image content for multimodal
        if (base64 != null)
        {
            content.Add(new
            {
                type = "image",
                data = base64,
                mimeType = "image/png"
            });
        }

        return content.ToArray();
    }

    private async Task<object[]> ScreenshotBase64Async(string? name, string? windowId)
    {
        var path = await CaptureScreenshotAsync(name ?? $"shot_{DateTime.UtcNow:HHmmss}", windowId);
        var bytes = await File.ReadAllBytesAsync(path);
        var base64 = Convert.ToBase64String(bytes);

        return new object[]
        {
            new { type = "text", text = $"Screenshot saved: {path}" },
            new { type = "image", data = base64, mimeType = "image/png" }
        };
    }

    // === Inspection ===

    private async Task<string> GetTreeAsync(string? windowId)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow;
            if (window == null) return "No window";
            return BuildTree(window, 0);
        });
    }

    private async Task<string> GetControlsAsync(string? windowId)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow;
            if (window == null) return "No window";

            var controls = _ctx.GetAllControls(window)
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select(c => $"  {c.Name} ({c.GetType().Name}) [{c.Bounds.X:F0},{c.Bounds.Y:F0} {c.Bounds.Width:F0}x{c.Bounds.Height:F0}] visible={c.IsVisible} enabled={c.IsEnabled}")
                .ToList();

            if (controls.Count == 0) return "No named controls found";
            return $"Controls ({controls.Count}):\n" + string.Join("\n", controls);
        });
    }

    private async Task<string> GetVmAsync(string? windowId)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow;
            if (window?.DataContext is not { } vm) return "No DataContext";

            var props = vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p =>
                {
                    try { return $"  {p.Name}: {_ctx.GetProperty(vm, p.Name)}"; }
                    catch { return $"  {p.Name}: <error>"; }
                })
                .ToList();

            return $"ViewModel ({vm.GetType().Name}):\n" + string.Join("\n", props);
        });
    }

    private async Task<string> GetAsync(string path)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                var value = _ctx.GetProperty(vm, path);
                return $"{path} = {value ?? "null"}";
            }
            return "No DataContext";
        });
    }

    private async Task<string> SetAsync(string path, string value)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                if (_ctx.SetProperty(vm, path, value))
                    return $"Set {path} = {value}";
                return $"Failed to set {path}";
            }
            return "No DataContext";
        });
    }

    private async Task<string> GetWindowsAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var windows = _ctx.TrackedWindows;
            if (windows.Count == 0) return "No tracked windows (call EnableCrossWindowTracking first)";

            var lines = windows.Select(w =>
            {
                var id = w.Name ?? w.GetType().Name;
                return $"  {id}: \"{w.Title}\" ({w.Bounds.Width:F0}x{w.Bounds.Height:F0}) visible={w.IsVisible}";
            });
            return $"Windows ({windows.Count}):\n" + string.Join("\n", lines);
        });
    }

    // === Assertions & Waiting ===

    private async Task<string> WaitAsync(int ms)
    {
        await Task.Delay(ms);
        return $"Waited {ms}ms";
    }

    private async Task<string> WaitForAsync(string path, string value, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var current = await _ctx.RunOnUIThreadAsync(() =>
            {
                if (_ctx.MainWindow?.DataContext is { } vm)
                    return _ctx.GetProperty(vm, path)?.ToString();
                return null;
            });
            if (current == value) return $"OK: {path} = {value}";
            await Task.Delay(100);
        }

        var actual = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        return $"TIMEOUT: {path} expected \"{value}\", got \"{actual}\"";
    }

    private async Task<string> AssertAsync(string path, string value)
    {
        var actual = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        if (actual == value) return $"PASS: {path} = {value}";
        return $"FAIL: {path} expected \"{value}\", got \"{actual}\"";
    }

    private async Task<string> AssertControlAsync(string name, string property, string value)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(name);
            if (control == null) return $"FAIL: Control not found: {name}";

            return property.ToLowerInvariant() switch
            {
                "visible" =>
                    control.IsVisible.ToString().Equals(value, StringComparison.OrdinalIgnoreCase)
                        ? $"PASS: {name}.IsVisible = {value}"
                        : $"FAIL: {name}.IsVisible = {control.IsVisible}, expected {value}",
                "enabled" =>
                    control.IsEnabled.ToString().Equals(value, StringComparison.OrdinalIgnoreCase)
                        ? $"PASS: {name}.IsEnabled = {value}"
                        : $"FAIL: {name}.IsEnabled = {control.IsEnabled}, expected {value}",
                "text" when control is TextBox tb =>
                    tb.Text == value
                        ? $"PASS: {name}.Text = {value}"
                        : $"FAIL: {name}.Text = \"{tb.Text}\", expected \"{value}\"",
                _ => $"FAIL: Unknown property: {property}"
            };
        });
    }

    // === Recording ===

    private async Task<string> RecordStartAsync(bool withVideo, bool withPositions)
    {
        if (_recorder?.IsRecording == true) return "Already recording. Stop first.";

        _recorder = new UIRecorder(new UIRecorderOptions
        {
            CaptureMousePositions = withPositions,
            CrossWindowTracking = true
        });

        if (_ctx.MainWindow == null) return "No window attached";
        _recorder.StartRecording(_ctx.MainWindow, withVideo);
        return "Recording started" + (withVideo ? " (with video)" : "") + (withPositions ? " (with mouse positions)" : "");
    }

    private async Task<string> RecordStopAsync()
    {
        if (_recorder == null || !_recorder.IsRecording) return "Not recording";
        await _recorder.StopRecordingAsync();
        return $"Recording stopped. {_recorder.Actions.Count} actions captured.";
    }

    private async Task<string> RecordSaveAsync(string path, string? name)
    {
        if (_recorder == null) return "No recording to save";

        if (path.EndsWith(".json"))
            _recorder.SaveAsJson(path, name);
        else
            _recorder.SaveAsYaml(path, name);

        return $"Script saved: {path} ({_recorder.Actions.Count} actions)";
    }

    // === Video ===

    private async Task<string> VideoStartAsync(int fps, string? windowId)
    {
        if (_videoRecorder != null) return "Video already recording. Stop first.";

        var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow;
        if (window == null) return "No window";

        _videoRecorder = new GifRecorder(fps, msg => Console.Error.WriteLine($"[video] {msg}"));
        _videoRecorder.StartRecording(window);
        return $"Video recording started at {fps} fps";
    }

    private async Task<string> VideoStopAsync(string? name)
    {
        if (_videoRecorder == null) return "No video recording in progress";

        await _videoRecorder.StopRecordingAsync();

        var safeName = name ?? $"video_{DateTime.UtcNow:HHmmss_fff}";
        var gifPath = Path.Combine(_screenshotDir, $"{safeName}.gif");
        await _videoRecorder.SaveAsync(gifPath);

        var mp4Result = await _videoRecorder.TryExportMp4Async(Path.Combine(_screenshotDir, $"{safeName}.mp4"));

        await _videoRecorder.DisposeAsync();
        _videoRecorder = null;

        var result = $"GIF saved: {gifPath} ({_videoRecorder?.FrameCount ?? 0} frames)";
        if (mp4Result != null) result += $"\nMP4 saved: {mp4Result}";
        return result;
    }

    // === Script ===

    private async Task<string> RunScriptAsync(string path)
    {
        if (!File.Exists(path)) return $"Script not found: {path}";

        var script = path.EndsWith(".json")
            ? Scripts.ScriptLoader.LoadFromJson(path)
            : Scripts.ScriptLoader.LoadFromYaml(path);

        var player = new Players.ScriptPlayer(_screenshotDir, context: _ctx);
        if (_ctx.Navigate != null) player.SetNavigateAction(_ctx.Navigate);

        var result = await player.RunScriptAsync(_ctx.MainWindow!, script);

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        return $"Script: {script.Name}\nResult: {(result.Success ? "PASS" : "FAIL")}\nActions: {result.ActionResults.Count}\nDuration: {result.Duration.TotalSeconds:F2}s\n\n{json}";
    }

    // === Exit ===

    private string Exit()
    {
        _running = false;
        return "Shutting down...";
    }

    // === Helpers ===

    private async Task<string> CaptureScreenshotAsync(string name, string? windowId = null)
    {
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow!;
        return await ScreenshotCapture.CaptureWindowAsync(window, filePath);
    }

    private async Task<string> SnipRegionAsync(double x, double y, double width, double height, string? name, double padding, string? windowId)
    {
        var safeName = string.Join("_", (string.IsNullOrEmpty(name) ? $"snip_{DateTime.UtcNow:HHmmss_fff}" : name).Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow ?? throw new InvalidOperationException("No window");
        var rect = new Rect(x, y, width, height);
        if (padding > 0) rect = rect.Inflate(padding);
        var path = await ScreenshotCapture.CaptureRegionAsync(window, filePath, rect);
        return $"Snipped region {rect}: {path}";
    }

    private async Task<string> SnipControlAsync(string controlName, string? file, double padding, string? windowId)
    {
        var safeName = string.Join("_", (string.IsNullOrEmpty(file) ? $"snip_{controlName}_{DateTime.UtcNow:HHmmss_fff}" : file).Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow ?? throw new InvalidOperationException("No window");
        var control = await _ctx.RunOnUIThreadAsync(() => _ctx.FindControl(controlName, window));
        if (control == null) return $"Control not found: {controlName}";
        var path = await ScreenshotCapture.CaptureControlAsync(window, control, filePath, padding);
        return $"Snipped {controlName}: {path}";
    }

    private async Task<string> SnipControlsAsync(string namesCsv, string? file, double padding, string? windowId)
    {
        if (string.IsNullOrEmpty(namesCsv)) return "Need at least one control name";
        var safeName = string.Join("_", (string.IsNullOrEmpty(file) ? $"snip_group_{DateTime.UtcNow:HHmmss_fff}" : file).Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow ?? throw new InvalidOperationException("No window");

        var names = namesCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var controls = await _ctx.RunOnUIThreadAsync(() =>
        {
            var list = new List<Control>();
            foreach (var n in names)
            {
                var c = _ctx.FindControl(n, window);
                if (c == null) throw new InvalidOperationException($"Control not found: {n}");
                list.Add(c);
            }
            return list;
        });
        var path = await ScreenshotCapture.CaptureControlsAsync(window, controls, filePath, padding);
        return $"Snipped group [{namesCsv}]: {path}";
    }

    private async Task<string> CaptureSvgAsync(string name, string? windowId = null)
    {
        var safeName = string.Join("_", (name.Length > 0 ? name : $"svg_{DateTime.UtcNow:HHmmss}").Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.svg");

        var svgContent = await _ctx.RunOnUIThreadAsync(() =>
        {
            var window = _ctx.FindWindow(windowId) ?? _ctx.MainWindow!;
            window.UpdateLayout();
            var exporter = new Svg.SvgExporter();
            return exporter.Export(window);
        });

        await File.WriteAllTextAsync(filePath, svgContent);
        return $"SVG saved: {filePath}";
    }

    private static string BuildTree(Control control, int depth)
    {
        var indent = new string(' ', depth * 2);
        var name = string.IsNullOrEmpty(control.Name) ? "" : $" #{control.Name}";
        var vis = control.IsVisible ? "" : " [hidden]";
        var result = $"{indent}{control.GetType().Name}{name}{vis}\n";

        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
                result += BuildTree(child, depth + 1);
        }
        else if (control is ContentControl cc && cc.Content is Control content)
        {
            result += BuildTree(content, depth + 1);
        }
        else if (control is Decorator d && d.Child is Control child)
        {
            result += BuildTree(child, depth + 1);
        }

        return result;
    }

    private static string GetArg(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return "";
        return args.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";
    }

    private static int GetInt(JsonElement args, string name, int defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (args.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt32();
            if (int.TryParse(prop.GetString(), out var v)) return v;
        }
        return defaultValue;
    }

    private static double GetDouble(JsonElement args, string name, double defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (args.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetDouble();
            if (double.TryParse(prop.GetString(), out var v)) return v;
        }
        return defaultValue;
    }

    private static bool GetBool(JsonElement args, string name, bool defaultValue = false)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (args.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (bool.TryParse(prop.GetString(), out var v)) return v;
        }
        return defaultValue;
    }

    private static object[] TextResult(string text) => new object[] { new { type = "text", text } };

    private static async Task SendResultAsync(StreamWriter writer, JsonElement id, object result)
    {
        var response = new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };

        if (id.ValueKind == JsonValueKind.Number)
            response["id"] = id.GetInt32();
        else if (id.ValueKind == JsonValueKind.String)
            response["id"] = id.GetString()!;

        var json = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(json);
    }

    private static async Task SendErrorAsync(StreamWriter writer, JsonElement id, int code, string message)
    {
        var response = new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new { code, message }
        };

        if (id.ValueKind == JsonValueKind.Number)
            response["id"] = id.GetInt32();
        else if (id.ValueKind == JsonValueKind.String)
            response["id"] = id.GetString()!;

        var json = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(json);
    }

    private static object Tool(string name, string description, params (string Name, string Type, string Description, bool Required)[] args)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var (argName, argType, argDesc, isRequired) in args)
        {
            properties[argName] = new { type = argType, description = argDesc };
            if (isRequired) required.Add(argName);
        }

        return new
        {
            name,
            description,
            inputSchema = new
            {
                type = "object",
                properties,
                required = required.ToArray()
            }
        };
    }

    private static object Tool(string name, string description)
    {
        return new
        {
            name,
            description,
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>(),
                required = Array.Empty<string>()
            }
        };
    }
}