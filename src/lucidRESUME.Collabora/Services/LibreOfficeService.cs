using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lucidRESUME.Collabora.Services;

/// <summary>
/// Auto-detects a locally installed LibreOffice and provides document preview/open functionality.
/// No Docker or external server required — if LibreOffice is installed, it works.
/// </summary>
public sealed class LibreOfficeService
{
    private readonly string? _executablePath;

    public bool IsAvailable => _executablePath != null;

    public LibreOfficeService()
    {
        _executablePath = FindLibreOffice();
    }

    private static string? FindLibreOffice()
    {
        string[] candidates;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            candidates =
            [
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
            ];
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates =
            [
                "/Applications/LibreOffice.app/Contents/MacOS/soffice",
            ];
        }
        else
        {
            candidates =
            [
                "/usr/bin/soffice",
                "/usr/bin/libreoffice",
                "/usr/local/bin/soffice",
            ];
        }

        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        // Fall back to PATH lookup
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "soffice.exe" : "soffice";
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
                return exeName;
            }
        }
        catch { /* not in PATH */ }

        return null;
    }

    /// <summary>Open a document in LibreOffice Desktop.</summary>
    public void OpenDocument(string filePath)
    {
        if (_executablePath == null || !File.Exists(filePath)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = $"\"{filePath}\"",
            UseShellExecute = false,
            CreateNoWindow = false
        });
    }

    /// <summary>
    /// Convert a document to PNG page images using LibreOffice headless mode.
    /// For DOCX files, converts to PDF first to get reliable per-page PNGs.
    /// Returns paths to the generated PNG files, sorted by page order.
    /// </summary>
    public async Task<string[]> ConvertToImagesAsync(string filePath, string outputDir, CancellationToken ct = default)
    {
        if (_executablePath == null || !File.Exists(filePath))
            return [];

        Directory.CreateDirectory(outputDir);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var workFile = filePath;
        string? tempPdf = null;

        // Convert non-PDF formats to PDF first for reliable page splitting
        if (ext != ".pdf")
        {
            tempPdf = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(filePath) + ".pdf");
            var ok = await RunAsync($"--headless --convert-to pdf --outdir \"{outputDir}\" \"{filePath}\"", ct);
            if (!ok || !File.Exists(tempPdf)) return [];
            workFile = tempPdf;
        }

        // Convert PDF to PNG (one file per page)
        await RunAsync($"--headless --convert-to png --outdir \"{outputDir}\" \"{workFile}\"", ct);

        if (tempPdf != null && File.Exists(tempPdf))
            File.Delete(tempPdf);

        var baseName = Path.GetFileNameWithoutExtension(workFile);
        return [.. Directory.GetFiles(outputDir, $"{baseName}*.png").Order()];
    }

    private async Task<bool> RunAsync(string arguments, CancellationToken ct)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = _executablePath!,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (p == null) return false;
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
