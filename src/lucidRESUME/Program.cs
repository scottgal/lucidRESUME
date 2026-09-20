using Avalonia;
using System;
using Mostlylucid.Avalonia.UITesting;
using System.Text.Json;
using lucidRESUME.Matching;

namespace lucidRESUME;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--asset-audit", StringComparer.OrdinalIgnoreCase))
        {
            var audit = ProductAssetInventory.Audit(AppContext.BaseDirectory);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                complete = audit.IsComplete,
                files = audit.Resolved.Keys.Order().ToArray(),
                missing = audit.Missing,
                bytes = audit.TotalBytes,
                excludesUserData = true
            }));
            return audit.IsComplete ? 0 : 2;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            // The shared UI testing engine owns the ux/mlui command-line modes.
            .UseUITesting(opts =>
            {
                opts.DefaultScreenshotDir = "ux-screenshots";
                opts.EnableCrossWindowTracking = true;
                opts.Log = Console.WriteLine;
            });
}
