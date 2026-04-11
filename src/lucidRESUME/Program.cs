using Avalonia;
using System;
using Mostlylucid.Avalonia.UITesting;

namespace lucidRESUME;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            // Wire the new Mostlylucid.Avalonia.UITesting engine. Triggered by
            // --mlui-test / --mlui-repl / --mlui-mcp so it does not collide with
            // the legacy lucidRESUME.UXTesting --ux-test path in App.axaml.cs.
            .UseUITesting(opts =>
            {
                opts.DefaultScreenshotDir = "ux-screenshots";
                opts.EnableCrossWindowTracking = true;
                opts.Log = Console.WriteLine;
            });
}
