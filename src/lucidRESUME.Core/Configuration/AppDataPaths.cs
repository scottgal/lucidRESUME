namespace lucidRESUME.Core.Configuration;

/// <summary>
/// Resolves mutable application data outside the installation directory. This is
/// especially important for signed macOS bundles, whose contents must not change.
/// </summary>
public static class AppDataPaths
{
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("LUCIDRESUME_DATA_DIR");
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "lucidRESUME")
                : configured;

            return Path.GetFullPath(root);
        }
    }

    /// <summary>Resolve a configured mutable-data path. Absolute paths remain explicit overrides.</summary>
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(Root, path));
}
