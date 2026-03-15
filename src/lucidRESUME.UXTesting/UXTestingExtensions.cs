using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.UXTesting;

public static class UXTestingExtensions
{
    public static void AddUXTesting(this IServiceCollection services, Action<UXTestingOptions>? configure = null)
    {
        var options = new UXTestingOptions();
        configure?.Invoke(options);
        
        services.AddSingleton(options);
        services.AddSingleton<UXContext>();
    }
    
    public static void UseUXTesting(this AppBuilder appBuilder, Action<UXTestingOptions>? configure = null)
    {
        var options = new UXTestingOptions();
        configure?.Invoke(options);
        
        appBuilder.AfterSetup(_ =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var args = desktop.Args ?? Array.Empty<string>();
                
                if (args.Contains("--ux-test") || args.Contains("--ux-repl"))
                {
                    var startup = new UXTestingStartup(options, args);
                    startup.AttachToApplication();
                }
            }
        });
    }
    
    internal static string? GetArgValue(this string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index < args.Length - 1 ? args[index + 1] : null;
    }
    
    internal static bool HasArg(this string[] args, string key) => args.Contains(key);
}

public class UXTestingOptions
{
    public string DefaultScreenshotDir { get; set; } = "ux-screenshots";
    public int DefaultDelay { get; set; } = 200;
    public bool CaptureScreenshotsByDefault { get; set; } = true;
    public Action<Window>? ConfigureWindow { get; set; }
    public Action<string>? Log { get; set; } = Console.WriteLine;
}

internal class UXTestingStartup
{
    private readonly UXTestingOptions _options;
    private readonly string[] _args;
    
    public UXTestingStartup(UXTestingOptions options, string[] args)
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
        
        if (_args.HasArg("--ux-repl"))
        {
            var outputDir = _args.GetArgValue("--output") ?? _options.DefaultScreenshotDir;
            
            window.Opened += async (_, _) =>
            {
                await Task.Delay(500);
                
                var ctx = new UXContext
                {
                    MainWindow = window,
                    Navigate = page =>
                    {
                        var navProp = viewModel?.GetType().GetProperty("NavigateCommand");
                        var cmd = navProp?.GetValue(viewModel);
                        cmd?.GetType().GetMethod("Execute")?.Invoke(cmd, new object[] { page });
                    }
                };
                
                var repl = new UXRepl(ctx, outputDir);
                
                Console.WriteLine("\n=== UX Testing REPL ===");
                Console.WriteLine($"Window: {window.Title}");
                if (viewModel != null)
                    Console.WriteLine($"DataContext: {viewModel.GetType().Name}");
                Console.WriteLine("Type 'help' for commands, 'exit' to quit\n");
                
                await repl.RunAsync();
                
                desktop.Shutdown(0);
            };
        }
        else if (_args.HasArg("--ux-test"))
        {
            var scriptPath = _args.GetArgValue("--script");
            var outputDir = _args.GetArgValue("--output") ?? "ux-test-results";
            
            window.Opened += async (_, _) =>
            {
                await RunScriptAsync(window, viewModel, scriptPath, outputDir, desktop);
            };
        }
    }
    
    private async Task RunScriptAsync(Window window, object? viewModel, string? scriptPath, string outputDir, IClassicDesktopStyleApplicationLifetime desktop)
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
            var script = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
                .Build()
                .Deserialize<Scripts.UXScript>(yaml);

            Directory.CreateDirectory(outputDir);
            
            var player = new Players.UXPlayer(outputDir, _options.DefaultDelay, _options.CaptureScreenshotsByDefault);
            
            if (viewModel != null)
            {
                player.SetNavigateAction(page =>
                {
                    var navProp = viewModel.GetType().GetProperty("NavigateCommand");
                    var cmd = navProp?.GetValue(viewModel);
                    cmd?.GetType().GetMethod("Execute")?.Invoke(cmd, new object[] { page });
                });
            }
            
            player.Log += (_, msg) => _options.Log?.Invoke(msg);

            _options.Log?.Invoke($"Running: {script.Name}");
            
            var result = await player.RunScriptAsync(window, script);

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
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
