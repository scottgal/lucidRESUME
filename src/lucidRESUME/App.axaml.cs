using Avalonia;
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
using lucidRESUME.ViewModels;
using lucidRESUME.ViewModels.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        var resumePage = _provider.GetRequiredService<ResumePageViewModel>();
        jobsPage.NavigateTo = page => mainVm.NavigateCommand.Execute(page);

        // Wire import review: parse → preview → review page → apply/cancel → back to resume
        resumePage.ShowImportReview = (target, preview) =>
        {
            var reviewVm = _provider.GetRequiredService<ImportReviewPageViewModel>();
            reviewVm.LoadPreview(target, preview);
            reviewVm.OnApplied = () =>
            {
                mainVm.NavigateCommand.Execute("Resume");
                _ = resumePage.ReloadAsync();
            };
            reviewVm.OnCancelled = () => mainVm.NavigateCommand.Execute("Resume");
            mainVm.ShowTransientPage(reviewVm);
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var appDataDir = AppDataPaths.Root;
        Directory.CreateDirectory(appDataDir);
        var aiSettingsPath = Path.Combine(appDataDir, "ai-settings.json");

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(aiSettingsPath, optional: true, reloadOnChange: true)
            .AddUserSecrets(typeof(App).Assembly, optional: true)
            .AddEnvironmentVariables("LUCIDRESUME_")
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddIngestion(config);
        services.AddExtraction(config);
        services.AddJobSpec(config);
        services.AddJobSearch(config);
        services.AddMatching(config);
        services.AddAiTailoring(config);
        services.AddExport();
        services.AddCollabora(config);
        services.AddEmailTracker(config);
        services.AddGitHub(config);

        var dbPath = Path.Combine(appDataDir, "data.db");
        var jsonPath = Path.Combine(appDataDir, "data.json");
        services.AddSingleton<IAppStore>(_ => new SqliteAppStore(dbPath,
            jsonMigrationPath: File.Exists(jsonPath) ? jsonPath : null));

        services.AddHttpClient<Services.StartupHealthCheck>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ResumePageViewModel>();
        services.AddSingleton(new Services.JobMlWorkspacePath(Path.Combine(appDataDir, "jobml-workspace.md")));
        services.AddSingleton<JobMlEditorPageViewModel>();
        services.AddSingleton<JobsPageViewModel>();
        services.AddSingleton<SearchPageViewModel>();
        services.AddSingleton<ApplyPageViewModel>();
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
}
