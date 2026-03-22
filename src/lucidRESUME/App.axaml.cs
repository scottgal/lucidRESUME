using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using lucidRESUME.AI;
using lucidRESUME.Collabora;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.Extraction;
using lucidRESUME.Ingestion;
using lucidRESUME.JobSearch;
using lucidRESUME.JobSpec;
using lucidRESUME.Matching;
using lucidRESUME.UXTesting;
using lucidRESUME.UXTesting.Players;
using lucidRESUME.UXTesting.Scripts;
using lucidRESUME.ViewModels;
using lucidRESUME.ViewModels.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace lucidRESUME;

public partial class App : Application
{
    private IServiceProvider? _provider;
    
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _provider = services.BuildServiceProvider();

        var jobsPage = _provider.GetRequiredService<JobsPageViewModel>();
        var mainVm = _provider.GetRequiredService<MainWindowViewModel>();
        jobsPage.NavigateTo = page => mainVm.NavigateCommand.Execute(page);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            
            var args = desktop.Args ?? Array.Empty<string>();
            
            if (args.Contains("--ux-test"))
            {
                var scriptPath = GetArgValue(args, "--script");
                var outputDir = GetArgValue(args, "--output") ?? "ux-test-results";
                
                mainWindow.Opened += async (_, _) =>
                {
                    await RunUxTestAsync(mainWindow, mainVm, scriptPath, outputDir);
                };
            }
            else if (args.Contains("--ux-repl"))
            {
                var outputDir = GetArgValue(args, "--output") ?? "ux-screenshots";
                
                mainWindow.Opened += async (_, _) =>
                {
                    await RunUxReplAsync(mainWindow, mainVm, outputDir);
                };
            }
            else if (args.Contains("--ux-mcp"))
            {
                var outputDir = GetArgValue(args, "--output") ?? "ux-screenshots";
                mainWindow.Opened += async (_, _) =>
                {
                    await RunUxMcpAsync(mainWindow, mainVm, outputDir);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? GetArgValue(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index < args.Length - 1 ? args[index + 1] : null;
    }

    private static async Task RunUxTestAsync(
        MainWindow window, 
        MainWindowViewModel viewModel, 
        string? scriptPath, 
        string outputDir)
    {
        try
        {
            await Task.Delay(1000);
            
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                Console.WriteLine($"Script not found: {scriptPath}");
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown(1);
                return;
            }

            var yaml = await File.ReadAllTextAsync(scriptPath);
            var script = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
                .Build()
                .Deserialize<UXScript>(yaml);

            Directory.CreateDirectory(outputDir);
            
            var player = new UXPlayer(outputDir, 200, true);
            player.SetNavigateAction(page => viewModel.NavigateCommand.Execute(page));
            player.Log += (_, msg) => Console.WriteLine(msg);

            Console.WriteLine($"Running: {script.Name}");
            
            var result = await player.RunScriptAsync(window, script);

            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            var resultPath = Path.Combine(outputDir, "result.json");
            await File.WriteAllTextAsync(resultPath, json);
            
            Console.WriteLine($"Result: {(result.Success ? "PASS" : "FAIL")}");
            Console.WriteLine($"Screenshots: {outputDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            await Task.Delay(500);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown(0);
        }
    }

    private static async Task RunUxReplAsync(
        MainWindow window, 
        MainWindowViewModel viewModel, 
        string outputDir)
    {
        await Task.Delay(500);
        
        var ctx = new UXContext
        {
            MainWindow = window,
            Services = (App.Current as App)?._provider,
            Navigate = page => viewModel.NavigateCommand.Execute(page)
        };
        
        var repl = new UXRepl(ctx, outputDir);
        
        Console.WriteLine("\n=== UX Testing REPL ===");
        Console.WriteLine($"Window: {window.Title}");
        Console.WriteLine($"DataContext: {viewModel.GetType().Name}");
        Console.WriteLine("Type 'help' for commands, 'exit' to quit\n");
        
        await repl.RunAsync();
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(0);
    }

    private static async Task RunUxMcpAsync(
        MainWindow window, 
        MainWindowViewModel viewModel, 
        string outputDir)
    {
        await Task.Delay(500);
        
        var ctx = new UXContext
        {
            MainWindow = window,
            Services = (App.Current as App)?._provider,
            Navigate = page => viewModel.NavigateCommand.Execute(page)
        };
        
        var mcp = new UXMcpServer(ctx, outputDir);
        
        Console.Error.WriteLine("=== UX MCP Server ===");
        Console.Error.WriteLine($"Window: {window.Title}");
        Console.Error.WriteLine("Listening on stdin/stdout...\n");
        
        await mcp.RunStdioAsync();
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(0);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        services.AddLogging();
        services.AddIngestion(config);
        services.AddExtraction(config);
        services.AddJobSpec(config);
        services.AddJobSearch(config);
        services.AddMatching(config);
        services.AddAiTailoring(config);
        services.AddExport();
        services.AddCollabora(config);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "lucidRESUME");
        var dbPath = Path.Combine(appDataDir, "data.db");
        var jsonPath = Path.Combine(appDataDir, "data.json");
        services.AddSingleton<IAppStore>(_ => new SqliteAppStore(dbPath,
            jsonMigrationPath: File.Exists(jsonPath) ? jsonPath : null));

        services.AddHttpClient<Services.StartupHealthCheck>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ResumePageViewModel>();
        services.AddSingleton<JobsPageViewModel>();
        services.AddSingleton<SearchPageViewModel>();
        services.AddSingleton<ApplyPageViewModel>();
        services.AddSingleton<ProfilePageViewModel>();

        services.AddTransient<MainWindow>();
    }
}
