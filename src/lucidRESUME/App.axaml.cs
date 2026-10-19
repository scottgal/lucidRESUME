using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using lucidRESUME.AI;
using lucidRESUME.Collabora;
using lucidRESUME.Core.Configuration;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.Extraction;
using lucidRESUME.Ingestion;
using lucidRESUME.JobSearch;
using lucidRESUME.JobSpec;
using lucidRESUME.EmailTracker;
using lucidRESUME.GitHub;
using lucidRESUME.Matching;
using lucidRESUME.Services;
using lucidRESUME.ViewModels;
using lucidRESUME.ViewModels.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME;

public partial class App : Application
{
    private IServiceProvider? _provider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private void Settings_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel vm })
            vm.NavigateCommand.Execute("Profile");
    }

    private async void About_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }) return;
        using var icon = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://lucidRESUME/Assets/lucidresume.png")));
        var close = new Button { Content = "Close", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var dialog = new Window
        {
            Title = "About lucidRESUME",
            Width = 360,
            Height = 330,
            CanResize = false,
            Icon = owner.Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 14,
                Children =
                {
                    new Image { Source = icon, Height = 96 },
                    new TextBlock { Text = "lucidRESUME", FontSize = 24, FontWeight = Avalonia.Media.FontWeight.SemiBold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3)}", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "Your career. Clear evidence. Better resumes.", TextAlignment = Avalonia.Media.TextAlignment.Center },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _provider = services.BuildServiceProvider();

        var jobsPage = _provider.GetRequiredService<JobsPageViewModel>();
        var mainVm = _provider.GetRequiredService<MainWindowViewModel>();
        var resumePage = _provider.GetRequiredService<ResumePageViewModel>();
        jobsPage.NavigateTo = page => mainVm.NavigateCommand.Execute(page);

        resumePage.OpenTranscriptBuilder = resume =>
        {
            var apply = _provider.GetRequiredService<ApplyPageViewModel>();
            apply.SetContext(resume, null);
            apply.BuildCompleteTranscriptCommand.Execute(null);
            mainVm.NavigateCommand.Execute("Apply");
        };

        // Wire import review: parse → preview → review page → apply/cancel → back to resume
        resumePage.ShowImportReview = (target, preview) =>
        {
            var reviewVm = _provider.GetRequiredService<ImportReviewPageViewModel>();
            reviewVm.LoadPreview(target, preview);
            reviewVm.OnApplied = () =>
            {
                mainVm.NavigateCommand.Execute("CareerHome");
                _ = resumePage.ReloadAsync();
            };
            reviewVm.OnCancelled = () => mainVm.NavigateCommand.Execute("CareerHome");
            mainVm.ShowTransientPage(reviewVm);
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += (_, _) => MacApplicationIcon.Apply();

        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var appDataDir = AppDataPaths.Root;
        Directory.CreateDirectory(appDataDir);
        var aiSettingsPath = Path.Combine(appDataDir, "ai-settings.json");
        var secretStore = new PlatformSecretStore();
        var storedSecrets = LoadStoredSecrets(secretStore);

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(aiSettingsPath, optional: true, reloadOnChange: true)
            .AddInMemoryCollection(storedSecrets)
            .AddUserSecrets(typeof(App).Assembly, optional: true)
            .AddEnvironmentVariables("LUCIDRESUME_")
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ISecretStore>(secretStore);
        services.AddLogging();
        services.AddIngestion(config);
        services.AddExtraction(config);
        services.AddJobSpec(config);
        services.AddJobSearch(config);
        services.AddMatching(config);
        services.AddAiTailoring(config);
        services.AddExport();
        services.AddDocumentTools();
        services.AddEmailTracker(config);
        services.AddGitHub(config);

        var dbPath = Path.Combine(appDataDir, "data.db");
        var jsonPath = Path.Combine(appDataDir, "data.json");
        services.AddSingleton<IAppStore>(sp => new SqliteAppStore(dbPath,
            jsonMigrationPath: File.Exists(jsonPath) ? jsonPath : null,
            proseEnricher: sp.GetRequiredService<lucidRESUME.AI.CareerProseAnalyser>()));
        services.AddSingleton<lucidRESUME.Core.Interfaces.IProseSearch>(sp => (SqliteAppStore)sp.GetRequiredService<IAppStore>());

        services.AddHttpClient<Services.StartupHealthCheck>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ResumePageViewModel>();
        services.AddSingleton<CareerWorkspacePageViewModel>();
        services.AddSingleton(new Services.JobMlWorkspacePath(Path.Combine(appDataDir, "jobml-workspace.md")));
        services.AddSingleton<JobMlEditorPageViewModel>();
        services.AddSingleton<JobsPageViewModel>();
        services.AddSingleton<SearchPageViewModel>();
        services.AddSingleton<ApplyPageViewModel>();
        services.AddSingleton<lucidRESUME.Export.GeneratedResumeArchive>();
        services.AddSingleton<lucidRESUME.Export.CompleteCareerJobMlService>();
        services.AddSingleton<GeneratedResumesPageViewModel>();
        services.AddSingleton(new Services.AiSettingsPath(aiSettingsPath));
        services.AddSingleton<ProfilePageViewModel>();
        services.AddSingleton<MyDataPageViewModel>();
        services.AddSingleton<CareerPlannerPageViewModel>();
        services.AddTransient<ImportReviewPageViewModel>();
        services.AddTransient<lucidRESUME.Core.Models.Resume.ResumeDocumentMerger>();
        services.AddSingleton<PipelinePageViewModel>();
        services.AddSingleton<HelpPageViewModel>();

        services.AddTransient<MainWindow>();
    }

    private static Dictionary<string, string?> LoadStoredSecrets(ISecretStore store)
    {
        if (!store.IsAvailable) return [];
        try
        {
            return new Dictionary<string, string?>
            {
                ["OpenAi:ApiKey"] = store.GetAsync(AiSecretNames.OpenAiApiKey).GetAwaiter().GetResult(),
                ["Anthropic:ApiKey"] = store.GetAsync(AiSecretNames.AnthropicApiKey).GetAwaiter().GetResult()
            }.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        catch
        {
            // A locked or unavailable keyring must not prevent the local-only app
            // from starting. The settings page reports the backend on save.
            return [];
        }
    }
}
