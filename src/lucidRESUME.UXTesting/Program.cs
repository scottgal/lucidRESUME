using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommandLine;
using lucidRESUME.UXTesting.Players;
using lucidRESUME.UXTesting.Scripts;
using Newtonsoft.Json;

namespace lucidRESUME.UXTesting;

public sealed class Options
{
    [Option('m', "mode", Default = "play", HelpText = "Mode: play, record, list")]
    public string Mode { get; set; } = "play";
    
    [Option('s', "script", HelpText = "Script file or directory")]
    public string? Script { get; set; }
    
    [Option('o', "output", Default = "ux-test-results", HelpText = "Output directory")]
    public string OutputDir { get; set; } = "ux-test-results";
    
    [Option("screenshots", Default = true, HelpText = "Capture screenshots")]
    public bool Screenshots { get; set; } = true;
    
    [Option("delay", Default = 200, HelpText = "Default delay between actions (ms)")]
    public int Delay { get; set; } = 200;
    
    [Option("exe", HelpText = "Path to app executable")]
    public string? ExePath { get; set; }
    
    [Option("headless", Default = false, HelpText = "Run in headless mode")]
    public bool Headless { get; set; }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<Options>(args)
            .MapResult(
                async options => await RunAsync(options),
                _ => Task.FromResult(1));
    }

    static async Task<int> RunAsync(Options options)
    {
        Directory.CreateDirectory(options.OutputDir);
        
        return options.Mode.ToLowerInvariant() switch
        {
            "play" => await PlayMode(options),
            "record" => RecordMode(options),
            "list" => ListMode(options),
            "new" => await NewScript(options),
            _ => ShowHelp()
        };
    }

    static async Task<int> PlayMode(Options options)
    {
        if (string.IsNullOrEmpty(options.Script))
        {
            Console.WriteLine("Error: --script required for play mode");
            return 1;
        }

        Console.WriteLine("UX Test Player");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDir)}");
        Console.WriteLine($"Script: {options.Script}");
        Console.WriteLine();

        var scripts = new List<UXScript>();
        
        if (Directory.Exists(options.Script))
        {
            scripts.AddRange(ScriptLoader.LoadFromDirectory(options.Script));
        }
        else if (File.Exists(options.Script))
        {
            scripts.Add(options.Script.EndsWith(".json") 
                ? ScriptLoader.LoadFromJson(options.Script) 
                : ScriptLoader.LoadFromYaml(options.Script));
        }
        else
        {
            Console.WriteLine($"Script not found: {options.Script}");
            return 1;
        }

        Console.WriteLine($"Loaded {scripts.Count} script(s)");
        Console.WriteLine();

        var results = new List<UXTestResult>();
        
        foreach (var script in scripts)
        {
            Console.WriteLine($"=== {script.Name} ===");
            
            var result = await RunScriptInAppAsync(script, options);
            results.Add(result);
            
            Console.WriteLine();
        }

        var reportPath = Path.Combine(options.OutputDir, "report.json");
        await File.WriteAllTextAsync(reportPath, JsonConvert.SerializeObject(results, Formatting.Indented));
        
        Console.WriteLine($"Report saved: {reportPath}");
        
        var passed = results.Count(r => r.Success);
        var failed = results.Count - passed;
        
        Console.WriteLine();
        Console.WriteLine($"Results: {passed} passed, {failed} failed");
        
        return failed > 0 ? 1 : 0;
    }

    static async Task<UXTestResult> RunScriptInAppAsync(UXScript script, Options options)
    {
        var exePath = options.ExePath ?? FindAppExecutable();
        
        if (!File.Exists(exePath))
        {
            return new UXTestResult
            {
                ScriptName = script.Name,
                Success = false,
                ErrorMessage = $"App not found: {exePath}",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow
            };
        }

        var scriptPath = Path.Combine(options.OutputDir, "current-script.ux.yaml");
        ScriptLoader.SaveAsYaml(script, scriptPath);

        var resultPath = Path.Combine(options.OutputDir, "current-result.json");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--ux-test --script \"{scriptPath}\" --output \"{options.OutputDir}\"",
            UseShellExecute = !options.Headless,
            RedirectStandardOutput = options.Headless,
            RedirectStandardError = options.Headless
        };

        using var process = Process.Start(psi);
        
        if (process == null)
        {
            return new UXTestResult
            {
                ScriptName = script.Name,
                Success = false,
                ErrorMessage = "Failed to start app",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow
            };
        }

        await process.WaitForExitAsync();

        if (File.Exists(resultPath))
        {
            var json = await File.ReadAllTextAsync(resultPath);
            return JsonConvert.DeserializeObject<UXTestResult>(json) 
                ?? new UXTestResult { ScriptName = script.Name, Success = false };
        }

        return new UXTestResult
        {
            ScriptName = script.Name,
            Success = process.ExitCode == 0,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow
        };
    }

    static int RecordMode(Options options)
    {
        var exePath = options.ExePath ?? FindAppExecutable();
        
        if (!File.Exists(exePath))
        {
            Console.WriteLine($"App not found: {exePath}");
            return 1;
        }

        Console.WriteLine("Recording mode - interact with the app");
        Console.WriteLine("Press Ctrl+C to stop recording");
        Console.WriteLine();

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--ux-record --output \"{options.OutputDir}\"",
            UseShellExecute = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit();
        
        return 0;
    }

    static int ListMode(Options options)
    {
        var script = options.Script ?? "scripts";
        
        if (!Directory.Exists(script))
        {
            Console.WriteLine($"Directory not found: {script}");
            return 1;
        }

        Console.WriteLine($"Scripts in {script}:");
        Console.WriteLine();

        foreach (var file in Directory.GetFiles(script, "*.ux.yaml"))
        {
            try
            {
                var s = ScriptLoader.LoadFromYaml(file);
                Console.WriteLine($"  {s.Name}");
                Console.WriteLine($"    {s.Description}");
                Console.WriteLine($"    Actions: {s.Actions.Count}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {Path.GetFileName(file)} - Error: {ex.Message}");
            }
        }

        return 0;
    }

    static async Task<int> NewScript(Options options)
    {
        var name = options.Script ?? "new-script";
        var filePath = Path.Combine(options.OutputDir, $"{name}.ux.yaml");
        
        var script = new UXScript
        {
            Name = name,
            Description = "Generated script template",
            DefaultDelay = 200,
            Actions = new List<UXAction>
            {
                new() { Type = ActionType.Navigate, Value = "resume", Description = "Navigate to resume page" },
                new() { Type = ActionType.Screenshot, Value = "resume-page", Description = "Capture screenshot" },
                new() { Type = ActionType.Wait, Value = "1000", Description = "Wait 1 second" }
            }
        };

        ScriptLoader.SaveAsYaml(script, filePath);
        
        Console.WriteLine($"Created: {filePath}");
        Console.WriteLine();
        Console.WriteLine("Example script structure:");
        Console.WriteLine(@"
name: my-test
description: Test description
default_delay: 200
actions:
  - type: Navigate
    value: resume
    description: Go to resume page
  - type: Click
    target: ImportButton
    description: Click import
  - type: TypeText
    target: SearchBox
    value: test query
  - type: Screenshot
    value: after-search
  - type: Assert
    target: ResultsList
    value: visible:true
");
        
        return 0;
    }

    static int ShowHelp()
    {
        Console.WriteLine(@"
UX Testing Tool for Avalonia Apps

Modes:
  play    Run test scripts
  record  Record interactions (coming soon)
  list    List available scripts
  new     Create a new script template

Examples:
  uxtest play -s scripts/                    # Run all scripts in directory
  uxtest play -s test.ux.yaml                # Run single script
  uxtest record --exe bin/app.exe            # Record interactions
  uxtest list -s scripts/                    # List scripts
  uxtest new -s my-test -o scripts/          # Create new script template
");
        return 0;
    }

    static string FindAppExecutable()
    {
        var solutionRoot = FindSolutionRoot();
        return Path.Combine(solutionRoot, "src", "lucidRESUME", "bin", "Debug", "net10.0", "lucidRESUME.exe");
    }

    static string FindSolutionRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "lucidRESUME.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? Directory.GetCurrentDirectory();
    }
}
