using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;

namespace Mostlylucid.Avalonia.UITesting;

public static class UITestingExtensions
{
    public static void AddUITesting(this IServiceCollection services, Action<UITestingOptions>? configure = null)
    {
        var options = new UITestingOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<UITestContext>();
    }

    public static AppBuilder UseUITesting(this AppBuilder appBuilder, Action<UITestingOptions>? configure = null)
    {
        var options = new UITestingOptions();
        configure?.Invoke(options);

        appBuilder.AfterSetup(_ =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var args = desktop.Args ?? Array.Empty<string>();

            // Triggered by either the original ux-* flags or the explicit mlui-* flags.
            // Apps that already use lucidRESUME-style --ux-test on a different player can
            // still wire the new engine via --mlui-test to avoid the collision.
            if (!args.HasArg("--ux-test") && !args.HasArg("--ux-repl") && !args.HasArg("--ux-mcp")
                && !args.HasArg("--mlui-test") && !args.HasArg("--mlui-repl") && !args.HasArg("--mlui-mcp"))
                return;

            // AfterSetup runs BEFORE OnFrameworkInitializationCompleted, so desktop.MainWindow
            // is still null at this point. Hook the Startup event, which fires after
            // App.OnFrameworkInitializationCompleted has set MainWindow but before the main loop runs.
            void OnStartup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
            {
                desktop.Startup -= OnStartup;
                var startup = new UITestingStartup(options, args);
                startup.AttachToApplication();
            }
            desktop.Startup += OnStartup;
        });

        return appBuilder;
    }

    internal static string? GetArgValue(this string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index < args.Length - 1 ? args[index + 1] : null;
    }

    internal static bool HasArg(this string[] args, string key) => args.Contains(key);
}

public class UITestingOptions
{
    public string DefaultScreenshotDir { get; set; } = "ux-screenshots";
    public int DefaultDelay { get; set; } = 200;
    public bool CaptureScreenshotsByDefault { get; set; } = true;
    public Action<Window>? ConfigureWindow { get; set; }
    public Action<string>? Log { get; set; } = Console.WriteLine;
    public string? ConsoleImagePath { get; set; }
    public bool EnableCrossWindowTracking { get; set; } = true;
}

internal class UITestingStartup
{
    private readonly UITestingOptions _options;
    private readonly string[] _args;

    public UITestingStartup(UITestingOptions options, string[] args)
    {
        _options = options;
        _args = args;
    }

    public void AttachToApplication()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        if (desktop.MainWindow is not Window window)
            return;

        var viewModel = window.DataContext;

        Action<string>? navigateAction = null;
        if (viewModel != null)
        {
            navigateAction = page =>
            {
                var navProp = viewModel.GetType().GetProperty("NavigateCommand");
                var cmd = navProp?.GetValue(viewModel);
                cmd?.GetType().GetMethod("Execute")?.Invoke(cmd, new object[] { page });
            };
        }

        var ctx = new UITestContext
        {
            MainWindow = window,
            Navigate = navigateAction
        };

        if (_options.EnableCrossWindowTracking)
            ctx.EnableCrossWindowTracking();

        if (_args.HasArg("--ux-mcp") || _args.HasArg("--mlui-mcp"))
        {
            var outputDir = _args.GetArgValue("--output") ?? _options.DefaultScreenshotDir;

            window.Opened += async (_, _) =>
            {
                await Task.Delay(500);

                var mcp = new Mcp.UITestMcpServer(ctx, outputDir, _options.ConsoleImagePath);
                Console.Error.WriteLine($"Window: {window.Title}");

                await mcp.RunStdioAsync();

                desktop.Shutdown(0);
            };
        }
        else if (_args.HasArg("--ux-repl") || _args.HasArg("--mlui-repl"))
        {
            var outputDir = _args.GetArgValue("--output") ?? _options.DefaultScreenshotDir;

            window.Opened += async (_, _) =>
            {
                await Task.Delay(500);

                var repl = new Repl.UITestRepl(ctx, outputDir, _options.ConsoleImagePath);

                Console.WriteLine("\n=== UI Testing REPL ===");
                Console.WriteLine($"Window: {window.Title}");
                if (viewModel != null)
                    Console.WriteLine($"DataContext: {viewModel.GetType().Name}");
                Console.WriteLine("Type 'help' for commands, 'exit' to quit\n");

                await repl.RunAsync();

                desktop.Shutdown(0);
            };
        }
        else if (_args.HasArg("--ux-test") || _args.HasArg("--mlui-test"))
        {
            var scriptPath = _args.GetArgValue("--script");
            var outputDir = _args.GetArgValue("--output") ?? "ux-test-results";

            window.Opened += async (_, _) =>
            {
                await RunScriptAsync(window, viewModel, navigateAction, scriptPath, outputDir, desktop);
            };
        }
    }

    private async Task RunScriptAsync(Window window, object? viewModel, Action<string>? navigateAction,
        string? scriptPath, string outputDir, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await Task.Delay(500);

            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                _options.Log?.Invoke($"Script not found: {scriptPath}");
                desktop.Shutdown(1);
                return;
            }

            var yaml = await File.ReadAllTextAsync(scriptPath);
            var script = Scripts.ScriptLoader.ParseYaml(yaml);

            Directory.CreateDirectory(outputDir);

            var ctx = new UITestContext { MainWindow = window, Navigate = navigateAction };
            if (_options.EnableCrossWindowTracking)
                ctx.EnableCrossWindowTracking();

            var player = new Players.ScriptPlayer(outputDir, _options.DefaultDelay, _options.CaptureScreenshotsByDefault, ctx);

            if (navigateAction != null)
                player.SetNavigateAction(navigateAction);

            player.Log += (_, msg) => _options.Log?.Invoke(msg);

            _options.Log?.Invoke($"Running: {script.Name}");

            var result = await player.RunScriptAsync(window, script);

            var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var resultPath = Path.Combine(outputDir, "result.json");
            await File.WriteAllTextAsync(resultPath, json);

            _options.Log?.Invoke($"Result: {(result.Success ? "PASS" : "FAIL")}");
            _options.Log?.Invoke($"Screenshots: {outputDir}");
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"Error: {ex.Message}");
        }
        finally
        {
            await Task.Delay(500);
            desktop.Shutdown(0);
        }
    }
}
