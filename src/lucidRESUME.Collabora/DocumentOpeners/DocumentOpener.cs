using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lucidRESUME.Collabora.DocumentOpeners;

/// <summary>Represents a locally installed app that can open resume documents.</summary>
public sealed class DocumentOpener
{
    private readonly string _executablePath;

    public string Name { get; }

    private DocumentOpener(string name, string executablePath)
    {
        Name = name;
        _executablePath = executablePath;
    }

    public void Open(string filePath)
    {
        if (!File.Exists(filePath)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = $"\"{filePath}\"",
            UseShellExecute = false,
            CreateNoWindow = false
        });
    }

    /// <summary>Try to create an opener if the executable exists.</summary>
    public static DocumentOpener? TryCreate(string name, params string[] candidatePaths)
    {
        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
                return new DocumentOpener(name, path);
        }

        // Try PATH lookup (e.g. soffice, wps on Linux)
        var exeName = candidatePaths.LastOrDefault() ?? "";
        if (!exeName.Contains(Path.DirectorySeparatorChar) && !exeName.Contains('/'))
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exeName,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    p.WaitForExit(2000);
                    return new DocumentOpener(name, exeName);
                }
            }
            catch { /* not in PATH */ }
        }

        return null;
    }

    // ── Platform-specific factory methods ────────────────────────────────────

    public static DocumentOpener? TryCreateLibreOffice() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("LibreOffice",
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("LibreOffice",
                "/Applications/LibreOffice.app/Contents/MacOS/soffice")
        : TryCreate("LibreOffice",
            "/usr/bin/soffice",
            "/usr/bin/libreoffice",
            "/usr/local/bin/soffice",
            "soffice");

    public static DocumentOpener? TryCreateMicrosoftWord() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("Microsoft Word",
                @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE",
                @"C:\Program Files (x86)\Microsoft Office\root\Office16\WINWORD.EXE",
                @"C:\Program Files\Microsoft Office\Office16\WINWORD.EXE",
                @"C:\Program Files (x86)\Microsoft Office\Office16\WINWORD.EXE",
                @"C:\Program Files\Microsoft Office\Office15\WINWORD.EXE",
                @"C:\Program Files\Microsoft Office\Office14\WINWORD.EXE")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("Microsoft Word",
                "/Applications/Microsoft Word.app/Contents/MacOS/Microsoft Word")
        : null; // Word not on Linux

    public static DocumentOpener? TryCreateWpsOffice() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("WPS Office",
                @"C:\Program Files (x86)\Kingsoft\WPS Office\ksolaunch.exe",
                @"C:\Program Files\Kingsoft\WPS Office\ksolaunch.exe")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("WPS Office",
                "/Applications/wpsoffice.app/Contents/MacOS/wpsoffice")
        : TryCreate("WPS Office",
            "/usr/bin/wps",
            "wps");

    public static DocumentOpener? TryCreateOnlyOffice() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("ONLYOFFICE",
                @"C:\Program Files\ONLYOFFICE\DesktopEditors\DesktopEditors.exe",
                @"C:\Program Files (x86)\ONLYOFFICE\DesktopEditors\DesktopEditors.exe")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("ONLYOFFICE",
                "/Applications/ONLYOFFICE Desktop Editors.app/Contents/MacOS/ONLYOFFICE Desktop Editors")
        : TryCreate("ONLYOFFICE",
            "/usr/bin/onlyoffice-desktopeditors",
            "onlyoffice-desktopeditors");
}
