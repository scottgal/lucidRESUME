using System.Diagnostics;
using System.Text.Json;

/// <summary>Demo adapter for sentence-pair inference. No fabricated expected labels or confidence.</summary>
public sealed class NliCli(string python, string worker)
{
    public async Task<NliScores> CompareAsync(string evidence, string claim, CancellationToken ct = default)
    {
        var input = Path.Combine(Path.GetTempPath(), "nli-input-" + Guid.NewGuid().ToString("N") + ".json");
        var output = Path.Combine(Path.GetTempPath(), "nli-output-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new[]
            {
                new { id = "claim", premise = evidence, hypothesis = claim }
            }), ct);
            var start = new ProcessStartInfo(python) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { worker, "--fixtures", input, "--output", output }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start NLI worker.");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            await stdout;
            var errors = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("NLI worker failed: " + errors);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output, ct));
            var probabilities = report.RootElement.GetProperty("rows")[0].GetProperty("probabilities");
            return new(probabilities.GetProperty("entailment").GetDouble(),
                probabilities.GetProperty("neutral").GetDouble(), probabilities.GetProperty("contradiction").GetDouble());
        }
        finally { File.Delete(input); File.Delete(output); }
    }
}

public sealed record NliScores(double Entailment, double Neutral, double Contradiction);
