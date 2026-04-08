using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace lucidRESUME.Android;

[Activity(
    Label = "lucidRESUME",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
}
