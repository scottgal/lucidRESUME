using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Mostlylucid.Avalonia.UITesting.Players;
using Mostlylucid.Avalonia.UITesting.Recorders;
using Mostlylucid.Avalonia.UITesting.Scripts;

namespace Mostlylucid.Avalonia.UITesting.Repl;

public sealed class UITestRepl
{
    private readonly UITestContext _ctx;
    private readonly string _screenshotDir;
    private readonly string? _consoleImagePath;
    private bool _running = true;
    private UIRecorder? _recorder;
    private readonly Lazy<PointerSimulator> _pointer = new(() => new PointerSimulator());

    public UITestRepl(UITestContext context, string screenshotDir = "ux-screenshots", string? consoleImagePath = null)
    {
        _ctx = context;
        _screenshotDir = screenshotDir;
        _consoleImagePath = consoleImagePath ?? "consoleimage";
        Directory.CreateDirectory(screenshotDir);
    }

    public async Task RunAsync()
    {
        Console.WriteLine("UI Testing REPL - type 'help' for commands");
        Console.WriteLine($"Window: {_ctx.MainWindow?.Title ?? "not set"}");

        while (_running)
        {
            Console.Write("ui> ");
            var line = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var result = await ExecuteCommandAsync(line);
                if (!string.IsNullOrEmpty(result))
                    Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    public async Task<string> ExecuteCommandAsync(string line)
    {
        var parts = SplitCommand(line);
        if (parts.Length == 0) return "";

        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        return cmd switch
        {
            "help" => GetHelp(),
            "exit" or "quit" => Exit(),
            "nav" => await NavAsync(args),
            "click" => await ClickAsync(args),
            "dblclick" => await DoubleClickAsync(args),
            "type" => await TypeAsync(args),
            "press" => await PressAsync(args),
            "get" => await GetAsync(args),
            "set" => await SetAsync(args),
            "list" => await ListAsync(args),
            "tree" => await TreeAsync(args),
            "screenshot" or "shot" => await ScreenshotAsync(args),
            "svg" => await SvgAsync(args),
            "wait" => await WaitAsync(args),
            "assert" => await AssertAsync(args),
            "run" => await RunScriptAsync(args),
            "vm" => await VmAsync(args),
            "service" or "svc" => Service(args),
            "describe" or "desc" => await DescribeAsync(args),
            "waitfor" => await WaitForAsync(args),
            "record" => await RecordAsync(args),
            "stop" => await StopRecordAsync(args),
            "pause" => PauseRecord(),
            "resume" => ResumeRecord(),
            "save" => SaveRecord(args),
            "windows" => await ListWindowsAsync(),
            "move" or "mousemove" => await MouseMoveCmdAsync(args),
            "down" or "mousedown" => await MouseDownCmdAsync(args),
            "up" or "mouseup" => await MouseUpCmdAsync(args),
            "mclick" or "mouseclick" => await MouseClickCmdAsync(args),
            "drag" => await DragCmdAsync(args),
            "wheel" => await WheelCmdAsync(args),
            "pinch" => await PinchCmdAsync(args),
            "rotate" => await RotateCmdAsync(args),
            "swipe" => await SwipeCmdAsync(args),
            "tap" or "touchtap" => await TouchTapCmdAsync(args),
            "tdrag" or "touchdrag" => await TouchDragCmdAsync(args),
            "hover" => await HoverCmdAsync(args),
            "wresize" => await WindowResizeCmdAsync(args),
            "wmove" => await WindowMoveCmdAsync(args),
            "wmin" => await WindowStateCmdAsync(WindowState.Minimized),
            "wmax" => await WindowStateCmdAsync(WindowState.Maximized),
            "wnormal" => await WindowStateCmdAsync(WindowState.Normal),
            "wfull" => await WindowStateCmdAsync(WindowState.FullScreen),
            "wfocus" => await WindowFocusCmdAsync(),
            "wclose" => await WindowCloseCmdAsync(),
            "wtitle" => await WindowTitleCmdAsync(args),
            "winfo" => await WindowInfoCmdAsync(),
            "snip" => await SnipRegionCmdAsync(args),
            "snipctl" => await SnipControlCmdAsync(args),
            "snipgroup" => await SnipGroupCmdAsync(args),
            _ => $"Unknown command: {cmd}. Type 'help' for commands."
        };
    }

    private async Task<string> SnipRegionCmdAsync(string[] args)
    {
        if (args.Length < 4) return "Usage: snip <x> <y> <width> <height> [name] [padding]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var w = double.Parse(args[2]); var h = double.Parse(args[3]);
        var name = args.Length > 4 ? args[4] : $"snip_{DateTime.UtcNow:HHmmss_fff}";
        var padding = args.Length > 5 && double.TryParse(args[5], out var p) ? p : 0;
        var filePath = Path.Combine(_screenshotDir, $"{name}.png");
        var rect = new Rect(x, y, w, h);
        if (padding > 0) rect = rect.Inflate(padding);
        var path = await ScreenshotCapture.CaptureRegionAsync(RequireWindow(), filePath, rect);
        return $"Snip → {path}";
    }

    private async Task<string> SnipControlCmdAsync(string[] args)
    {
        if (args.Length < 1) return "Usage: snipctl <control> [name] [padding]";
        var controlName = args[0];
        var name = args.Length > 1 ? args[1] : $"snip_{controlName}_{DateTime.UtcNow:HHmmss_fff}";
        var padding = args.Length > 2 && double.TryParse(args[2], out var p) ? p : 0;
        var control = await _ctx.RunOnUIThreadAsync(() => _ctx.FindControl(controlName));
        if (control == null) return $"Control not found: {controlName}";
        var filePath = Path.Combine(_screenshotDir, $"{name}.png");
        var path = await ScreenshotCapture.CaptureControlAsync(RequireWindow(), control, filePath, padding);
        return $"Snip {controlName} → {path}";
    }

    private async Task<string> SnipGroupCmdAsync(string[] args)
    {
        if (args.Length < 1) return "Usage: snipgroup <name1,name2,...> [name] [padding]";
        var names = args[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var name = args.Length > 1 ? args[1] : $"snip_group_{DateTime.UtcNow:HHmmss_fff}";
        var padding = args.Length > 2 && double.TryParse(args[2], out var p) ? p : 0;
        var window = RequireWindow();
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
        var filePath = Path.Combine(_screenshotDir, $"{name}.png");
        var path = await ScreenshotCapture.CaptureControlsAsync(window, controls, filePath, padding);
        return $"Snip group → {path}";
    }

    private async Task<string> WindowResizeCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: wresize <width> <height>";
        var w = double.Parse(args[0]); var h = double.Parse(args[1]);
        await _ctx.RunOnUIThreadAsync(() => { var win = RequireWindow(); win.Width = w; win.Height = h; });
        return $"Resized to {w}x{h}";
    }

    private async Task<string> WindowMoveCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: wmove <x> <y>";
        var x = int.Parse(args[0]); var y = int.Parse(args[1]);
        await _ctx.RunOnUIThreadAsync(() => RequireWindow().Position = new PixelPoint(x, y));
        return $"Moved to ({x},{y})";
    }

    private async Task<string> WindowStateCmdAsync(WindowState state)
    {
        await _ctx.RunOnUIThreadAsync(() => RequireWindow().WindowState = state);
        return $"State: {state}";
    }

    private async Task<string> WindowFocusCmdAsync()
    {
        await _ctx.RunOnUIThreadAsync(() => { var w = RequireWindow(); w.Activate(); w.Focus(); });
        return "Focused";
    }

    private async Task<string> WindowCloseCmdAsync()
    {
        await _ctx.RunOnUIThreadAsync(() => RequireWindow().Close());
        return "Closed";
    }

    private async Task<string> WindowTitleCmdAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: wtitle <title>";
        var title = string.Join(" ", args);
        await _ctx.RunOnUIThreadAsync(() => RequireWindow().Title = title);
        return $"Title: {title}";
    }

    private async Task<string> WindowInfoCmdAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var w = RequireWindow();
            return $"size={w.Width}x{w.Height} pos=({w.Position.X},{w.Position.Y}) state={w.WindowState} title=\"{w.Title}\"";
        });
    }

    private Window RequireWindow() => _ctx.MainWindow ?? throw new InvalidOperationException("No main window");

    private async Task<string> MouseMoveCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: move <x> <y>";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        await _pointer.Value.MoveAsync(RequireWindow(), x, y);
        return $"Moved to ({x},{y})";
    }

    private async Task<string> MouseDownCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: down <x> <y> [button]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var btn = args.Length > 2 ? ParseButton(args[2]) : MouseButton.Left;
        await _pointer.Value.DownAsync(RequireWindow(), x, y, btn);
        return $"Down [{btn}] at ({x},{y})";
    }

    private async Task<string> MouseUpCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: up <x> <y> [button]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var btn = args.Length > 2 ? ParseButton(args[2]) : MouseButton.Left;
        await _pointer.Value.UpAsync(RequireWindow(), x, y, btn);
        return $"Up [{btn}] at ({x},{y})";
    }

    private async Task<string> MouseClickCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: mclick <x> <y> [button]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var btn = args.Length > 2 ? ParseButton(args[2]) : MouseButton.Left;
        await _pointer.Value.ClickAsync(RequireWindow(), x, y, btn);
        return $"Click [{btn}] at ({x},{y})";
    }

    private async Task<string> DragCmdAsync(string[] args)
    {
        if (args.Length < 4) return "Usage: drag <x1> <y1> <x2> <y2> [button] [steps]";
        var x1 = double.Parse(args[0]); var y1 = double.Parse(args[1]);
        var x2 = double.Parse(args[2]); var y2 = double.Parse(args[3]);
        var btn = args.Length > 4 ? ParseButton(args[4]) : MouseButton.Left;
        var steps = args.Length > 5 && int.TryParse(args[5], out var s) ? s : 10;
        await _pointer.Value.DragAsync(RequireWindow(), x1, y1, x2, y2, steps, 16, btn);
        return $"Drag [{btn}] ({x1},{y1}) → ({x2},{y2})";
    }

    private async Task<string> WheelCmdAsync(string[] args)
    {
        if (args.Length < 3) return "Usage: wheel <x> <y> <dy> [dx]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var dy = double.Parse(args[2]);
        var dx = args.Length > 3 ? double.Parse(args[3]) : 0;
        await _pointer.Value.WheelAsync(RequireWindow(), x, y, dx, dy);
        return $"Wheel ({x},{y}) Δ=({dx},{dy})";
    }

    private async Task<string> PinchCmdAsync(string[] args)
    {
        if (args.Length < 3) return "Usage: pinch <x> <y> <scale> [steps]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var scale = double.Parse(args[2]);
        var steps = args.Length > 3 && int.TryParse(args[3], out var s) ? s : 10;
        for (int i = 0; i < steps; i++)
            await _pointer.Value.MagnifyAsync(RequireWindow(), x, y, scale / steps);
        return $"Pinch Δ={scale}";
    }

    private async Task<string> RotateCmdAsync(string[] args)
    {
        if (args.Length < 3) return "Usage: rotate <x> <y> <angleDegrees> [steps]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var angle = double.Parse(args[2]);
        var steps = args.Length > 3 && int.TryParse(args[3], out var s) ? s : 10;
        for (int i = 0; i < steps; i++)
            await _pointer.Value.RotateAsync(RequireWindow(), x, y, angle / steps);
        return $"Rotate Δ={angle}°";
    }

    private async Task<string> SwipeCmdAsync(string[] args)
    {
        if (args.Length < 4) return "Usage: swipe <x> <y> <dx> <dy> [steps]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var dx = double.Parse(args[2]); var dy = double.Parse(args[3]);
        var steps = args.Length > 4 && int.TryParse(args[4], out var s) ? s : 5;
        for (int i = 0; i < steps; i++)
            await _pointer.Value.SwipeAsync(RequireWindow(), x, y, dx / steps, dy / steps);
        return $"Swipe Δ=({dx},{dy})";
    }

    private async Task<string> TouchTapCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: tap <x> <y>";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        await _pointer.Value.TouchTapAsync(RequireWindow(), x, y);
        return $"Touch tap ({x},{y})";
    }

    private async Task<string> TouchDragCmdAsync(string[] args)
    {
        if (args.Length < 4) return "Usage: tdrag <x1> <y1> <x2> <y2> [steps]";
        var x1 = double.Parse(args[0]); var y1 = double.Parse(args[1]);
        var x2 = double.Parse(args[2]); var y2 = double.Parse(args[3]);
        var steps = args.Length > 4 && int.TryParse(args[4], out var s) ? s : 10;
        await _pointer.Value.TouchDragAsync(RequireWindow(), x1, y1, x2, y2, steps);
        return $"Touch drag ({x1},{y1}) → ({x2},{y2})";
    }

    private async Task<string> HoverCmdAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: hover <x> <y> [lingerMs]";
        var x = double.Parse(args[0]); var y = double.Parse(args[1]);
        var linger = args.Length > 2 && int.TryParse(args[2], out var ms) ? ms : 250;
        await _pointer.Value.HoverAsync(RequireWindow(), x, y, linger);
        return $"Hover ({x},{y}) {linger}ms";
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

    private static string GetHelp()
    {
        return """
            Commands:
              nav <page>              Navigate to page
              click <control>         Click a control by name
              dblclick <control>      Double-click a control
              type <control> <text>   Type text into a TextBox
              press <key>             Press a key (Enter, Tab, Escape, etc.)
              get <path>              Get property (e.g., get CurrentPage.Name)
              set <path> <value>      Set property value
              list [controls|vms]     List controls or view models
              tree                    Show visual tree
              vm                      Show current VM properties
              screenshot [name]       Capture screenshot
              svg [name]              Export SVG from visual tree
              describe [name]         Capture + consoleimage describe (for LLM vision)
              wait <ms>               Wait for milliseconds
              waitfor <path> <value>  Wait until property equals value
              assert <path> <value>   Assert property equals value
              run <script.yaml>       Run a script file
              service <type>          Get a service from DI container
              record [--video]        Start recording interactions
              stop                    Stop recording
              pause                   Pause recording
              resume                  Resume recording
              save <file.yaml>        Save recorded script
              windows                 List tracked windows
              move <x> <y>            Move pointer to coords
              down <x> <y> [btn]      Press mouse button at coords (left/right/middle/x1/x2)
              up <x> <y> [btn]        Release mouse button at coords
              mclick <x> <y> [btn]    Click at coords with button
              drag <x1> <y1> <x2> <y2> [btn] [steps]   Drag with interpolated movement
              wheel <x> <y> <dy> [dx]  Mouse wheel scroll
              pinch <x> <y> <scale> [steps]   Touchpad pinch (magnify)
              rotate <x> <y> <deg> [steps]    Touchpad rotate
              swipe <x> <y> <dx> <dy> [steps] Touchpad swipe
              tap <x> <y>             Touch tap
              tdrag <x1> <y1> <x2> <y2> [steps]   Touch drag
              hover <x> <y> [ms]      Hover at coords with linger
              wresize <w> <h>         Resize window
              wmove <x> <y>           Move window to screen coords
              wmin / wmax / wnormal / wfull   Window state
              wfocus                  Activate and focus window
              wclose                  Close window
              wtitle <title>          Set window title
              winfo                   Show window size/pos/state
              snip <x> <y> <w> <h> [name] [padding]   Snip a region of the window to PNG
              snipctl <control> [name] [padding]      Snip one control's bounds to PNG
              snipgroup <c1,c2,...> [name] [padding]  Snip the bounding box of multiple controls
              exit                    Exit REPL
            """;
    }

    private string Exit()
    {
        _running = false;
        return "Exiting...";
    }

    private async Task<string> NavAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: nav <page>";
        var page = string.Join(" ", args);
        await _ctx.RunOnUIThreadAsync(() => _ctx.Navigate?.Invoke(page));
        await Task.Delay(100);
        return $"Navigated to: {page}";
    }

    private async Task<string> ClickAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: click <control>";
        var controlName = args[0];
        var result = await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(controlName);
            if (control == null) return $"Control not found: {controlName}";

            if (control is Button button)
            {
                if (button.Command?.CanExecute(button.CommandParameter) == true)
                {
                    button.Command.Execute(button.CommandParameter);
                    return $"Clicked button: {controlName}";
                }
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return $"Raised click on: {controlName}";
            }

            if (control is TabItem tabItem)
            {
                tabItem.IsSelected = true;
                return $"Selected tab: {controlName}";
            }

            control.RaiseEvent(new RoutedEventArgs(InputElement.TappedEvent));
            return $"Tapped: {controlName}";
        });
        await Task.Delay(50);
        return result;
    }

    private async Task<string> DoubleClickAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: dblclick <control>";
        var controlName = args[0];
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(controlName);
            if (control == null) return $"Control not found: {controlName}";
            control.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
            return $"Double-clicked: {controlName}";
        });
    }

    private async Task<string> TypeAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: type <control> <text>";
        var controlName = args[0];
        var text = string.Join(" ", args.Skip(1));
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var control = _ctx.FindControl(controlName);
            if (control is TextBox textBox)
            {
                textBox.Text = text;
                return $"Typed into {controlName}: {text}";
            }
            return $"Control is not a TextBox: {controlName}";
        });
    }

    private async Task<string> PressAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: press <key>";
        var keyName = args[0];
        var key = Enum.Parse<Key>(keyName, true);
        await _ctx.RunOnUIThreadAsync(() =>
        {
            _ctx.MainWindow?.RaiseEvent(new KeyEventArgs
            {
                Key = key,
                RoutedEvent = InputElement.KeyDownEvent
            });
        });
        await Task.Delay(50);
        return $"Pressed: {keyName}";
    }

    private async Task<string> GetAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: get <path>";
        var path = string.Join(" ", args);
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (path.StartsWith("#"))
            {
                var dotIdx = path.IndexOf('.');
                var controlName = dotIdx > 0 ? path[1..dotIdx] : path[1..];
                var propPath = dotIdx > 0 ? path[(dotIdx + 1)..] : "";
                var control = _ctx.FindControl(controlName);
                if (control == null) return $"Control not found: {controlName}";
                if (string.IsNullOrEmpty(propPath)) return $"{control}";
                return $"{propPath} = {_ctx.GetProperty(control, propPath)}";
            }

            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                var value = _ctx.GetProperty(vm, path);
                return $"{path} = {value}";
            }
            return "No DataContext available";
        });
    }

    private async Task<string> SetAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: set <path> <value>";
        var path = args[0];
        var value = string.Join(" ", args.Skip(1));
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
            {
                if (_ctx.SetProperty(vm, path, value))
                    return $"Set {path} = {value}";
                return $"Failed to set {path}";
            }
            return "No DataContext available";
        });
    }

    private async Task<string> ListAsync(string[] args)
    {
        var type = args.Length > 0 ? args[0].ToLowerInvariant() : "controls";
        return type switch
        {
            "vms" or "viewmodels" => await ListViewModelsAsync(),
            _ => await ListControlsAsync()
        };
    }

    private async Task<string> ListControlsAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var controls = _ctx.GetAllControls()
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select(c => $"  {c.Name} ({c.GetType().Name})")
                .ToList();
            if (controls.Count == 0) return "No named controls found";
            return $"Controls ({controls.Count}):\n" + string.Join("\n", controls);
        });
    }

    private async Task<string> ListViewModelsAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is not { } vm) return "No DataContext";
            var props = vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.Name.EndsWith("ViewModel"))
                .Select(p => $"  {p.Name}: {p.PropertyType.Name}")
                .ToList();
            if (props.Count == 0) return "No ViewModel properties found";
            return "ViewModels:\n" + string.Join("\n", props);
        });
    }

    private async Task<string> TreeAsync(string[] args)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow == null) return "No window";
            return BuildTree(_ctx.MainWindow, 0);
        });
    }

    private static string BuildTree(Control control, int depth)
    {
        var indent = new string(' ', depth * 2);
        var name = string.IsNullOrEmpty(control.Name) ? "" : $" #{control.Name}";
        var result = $"{indent}{control.GetType().Name}{name}\n";

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

    private async Task<string> VmAsync(string[] args)
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is not { } vm) return "No DataContext";
            var props = vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"  {p.Name}: {_ctx.GetProperty(vm, p.Name)}")
                .ToList();
            return $"ViewModel ({vm.GetType().Name}):\n" + string.Join("\n", props);
        });
    }

    private string Service(string[] args)
    {
        if (args.Length == 0) return "Usage: service <TypeName>";
        var typeName = string.Join(" ", args);
        var resolvedType = Type.GetType(typeName) ??
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
        if (resolvedType == null) return $"Type not found: {typeName}";
        var service = _ctx.Services?.GetService(resolvedType);
        return service?.ToString() ?? $"Service not found: {typeName}";
    }

    private async Task<string> ScreenshotAsync(string[] args)
    {
        var name = args.Length > 0 ? args[0] : $"screenshot_{DateTime.UtcNow:HHmmss}";
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        var result = await CaptureScreenshotAsync(filePath);
        return $"Screenshot saved: {result}";
    }

    private async Task<string> SvgAsync(string[] args)
    {
        if (_ctx.MainWindow == null) return "No window";
        var name = args.Length > 0 ? args[0] : $"svg_{DateTime.UtcNow:HHmmss}";
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.svg");

        var svgContent = await _ctx.RunOnUIThreadAsync(() =>
        {
            _ctx.MainWindow!.UpdateLayout();
            var exporter = new Svg.SvgExporter();
            return exporter.Export(_ctx.MainWindow);
        });

        await File.WriteAllTextAsync(filePath, svgContent);
        return $"SVG saved: {filePath}";
    }

    private async Task<string> WaitAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: wait <ms>";
        var ms = int.Parse(args[0]);
        await Task.Delay(ms);
        return $"Waited {ms}ms";
    }

    private async Task<string> AssertAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: assert <path> <expected>";
        var path = args[0];
        var expected = string.Join(" ", args.Skip(1));
        var actual = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        if (actual == expected) return $"PASS: {path} = {expected}";
        throw new Exception($"ASSERT FAILED: {path}\n  Expected: {expected}\n  Actual: {actual}");
    }

    private async Task<string> RunScriptAsync(string[] args)
    {
        if (args.Length == 0) return "Usage: run <script.yaml>";
        var scriptPath = args[0];
        if (!File.Exists(scriptPath)) return $"Script not found: {scriptPath}";

        var script = scriptPath.EndsWith(".json")
            ? ScriptLoader.LoadFromJson(scriptPath)
            : ScriptLoader.LoadFromYaml(scriptPath);

        foreach (var action in script.Actions)
        {
            var cmd = action.Type switch
            {
                ActionType.Navigate => $"nav {action.Value}",
                ActionType.Click => $"click {action.Target}",
                ActionType.DoubleClick => $"dblclick {action.Target}",
                ActionType.TypeText => $"type {action.Target} {action.Value}",
                ActionType.PressKey => $"press {action.Value}",
                ActionType.Wait => $"wait {action.Value}",
                ActionType.Screenshot => $"screenshot {action.Value}",
                _ => null
            };

            if (cmd != null)
            {
                Console.WriteLine($"  > {cmd}");
                await ExecuteCommandAsync(cmd);
            }

            var delay = action.DelayMs > 0 ? action.DelayMs : script.DefaultDelay;
            if (delay > 0) await Task.Delay(delay);
        }

        return $"Script completed: {script.Name}";
    }

    private async Task<string> DescribeAsync(string[] args)
    {
        var name = args.Length > 0 ? args[0] : $"describe_{DateTime.UtcNow:HHmmss}";
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_screenshotDir, $"{safeName}.png");
        await CaptureScreenshotAsync(filePath);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _consoleImagePath ?? "consoleimage",
            Arguments = $"\"{filePath}\" --max-width 80 --max-height 25",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return "Failed to start consoleimage";
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return $"\n{output}";
        }
        catch (Exception ex)
        {
            return $"Describe failed (is consoleimage installed?): {ex.Message}";
        }
    }

    private async Task<string> WaitForAsync(string[] args)
    {
        if (args.Length < 2) return "Usage: waitfor <path> <value> [timeout_ms]";
        var path = args[0];
        var expected = args[1];
        var timeoutMs = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 5000;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var actual = await _ctx.RunOnUIThreadAsync(() =>
            {
                if (_ctx.MainWindow?.DataContext is { } vm)
                    return _ctx.GetProperty(vm, path)?.ToString();
                return null;
            });
            if (actual == expected) return $"{path} = {expected}";
            await Task.Delay(100);
        }

        var final = await _ctx.RunOnUIThreadAsync(() =>
        {
            if (_ctx.MainWindow?.DataContext is { } vm)
                return _ctx.GetProperty(vm, path)?.ToString();
            return null;
        });
        return $"Timeout: {path} expected {expected}, got {final}";
    }

    private async Task<string> RecordAsync(string[] args)
    {
        if (_recorder?.IsRecording == true) return "Already recording. Use 'stop' first.";
        var withVideo = args.Contains("--video");
        _recorder = new UIRecorder(new UIRecorderOptions { CaptureMousePositions = true, CrossWindowTracking = true });
        _recorder.Log += (_, msg) => Console.WriteLine($"  [REC] {msg}");

        if (_ctx.MainWindow == null) return "No window attached";
        _recorder.StartRecording(_ctx.MainWindow, withVideo);
        return "Recording started" + (withVideo ? " (with video)" : "");
    }

    private async Task<string> StopRecordAsync(string[] args)
    {
        if (_recorder == null || !_recorder.IsRecording) return "Not recording";
        await _recorder.StopRecordingAsync();
        return $"Recording stopped. {_recorder.Actions.Count} actions captured. Use 'save <file.yaml>' to save.";
    }

    private string PauseRecord()
    {
        if (_recorder == null || !_recorder.IsRecording) return "Not recording";
        _recorder.PauseRecording();
        return "Recording paused";
    }

    private string ResumeRecord()
    {
        if (_recorder == null || !_recorder.IsRecording) return "Not recording";
        _recorder.ResumeRecording();
        return "Recording resumed";
    }

    private string SaveRecord(string[] args)
    {
        if (_recorder == null) return "No recording to save";
        if (args.Length == 0) return "Usage: save <file.yaml|file.json>";
        var path = args[0];
        if (path.EndsWith(".json"))
            _recorder.SaveAsJson(path);
        else
            _recorder.SaveAsYaml(path);
        return $"Script saved: {path}";
    }

    private async Task<string> ListWindowsAsync()
    {
        return await _ctx.RunOnUIThreadAsync(() =>
        {
            var windows = _ctx.TrackedWindows;
            if (windows.Count == 0) return "No tracked windows";
            var lines = windows.Select(w =>
                $"  {w.Name ?? w.GetType().Name}: \"{w.Title}\" ({w.Bounds.Width:F0}x{w.Bounds.Height:F0})");
            return $"Windows ({windows.Count}):\n" + string.Join("\n", lines);
        });
    }

    private async Task<string> CaptureScreenshotAsync(string filePath)
    {
        if (_ctx.MainWindow == null) return "No window";
        var width = (int)_ctx.MainWindow.Bounds.Width;
        var height = (int)_ctx.MainWindow.Bounds.Height;

        await _ctx.RunOnUIThreadAsync(() =>
        {
            _ctx.MainWindow!.UpdateLayout();
            var size = new PixelSize(width, height);
            var dpi = new Vector(96, 96);
            using var bitmap = new RenderTargetBitmap(size, dpi);
            bitmap.Render(_ctx.MainWindow);
            using var stream = File.Create(filePath);
            bitmap.Save(stream);
        });

        return filePath;
    }

    private static string[] SplitCommand(string line)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current);
                    current = "";
                }
            }
            else
            {
                current += c;
            }
        }

        if (current.Length > 0) result.Add(current);
        return result.ToArray();
    }
}
