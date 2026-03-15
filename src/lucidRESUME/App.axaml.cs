using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using lucidRESUME.AI;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Export;
using lucidRESUME.Extraction;
using lucidRESUME.Ingestion;
using lucidRESUME.JobSearch;
using lucidRESUME.JobSpec;
using lucidRESUME.Matching;
using lucidRESUME.ViewModels;
using lucidRESUME.ViewModels.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = provider.GetRequiredService<MainWindow>();

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        services.AddLogging();
        services.AddIngestion(config);
        services.AddExtraction(config);
        services.AddJobSpec();
        services.AddJobSearch(config);
        services.AddMatching();
        services.AddAiTailoring(config);
        services.AddExport();

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "lucidRESUME", "data.json");
        services.AddSingleton<IAppStore>(_ => new JsonAppStore(appDataPath));

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ResumePageViewModel>();
        services.AddTransient<JobsPageViewModel>();
        services.AddTransient<SearchPageViewModel>();
        services.AddTransient<ApplyPageViewModel>();
        services.AddTransient<ProfilePageViewModel>();

        // Window
        services.AddTransient<MainWindow>();
    }
}
