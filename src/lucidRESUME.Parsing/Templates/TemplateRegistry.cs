using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Parsing.Templates;

/// <summary>
/// Persists and queries learned DOCX templates.
/// Registry file: %AppData%/lucidRESUME/templates.json (default)
///
/// Matching: returns the best known template whose fingerprint Jaccard similarity
/// exceeds <see cref="MatchThreshold"/>. Returns null when nothing matches.
/// </summary>
public sealed class TemplateRegistry
{
    public double MatchThreshold { get; set; } = 0.80;

    private readonly string _registryPath;
    private readonly ILogger<TemplateRegistry> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<KnownTemplate>? _templates;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public TemplateRegistry(string? registryPath, ILogger<TemplateRegistry> logger)
    {
        _registryPath = registryPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "lucidRESUME", "templates.json");
        _logger = logger;
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the best-matching known template, or null if none match above threshold.
    /// Updates <see cref="KnownTemplate.MatchCount"/> on a hit.
    /// </summary>
    public async Task<KnownTemplate?> FindMatchAsync(TemplateFingerprint fingerprint, CancellationToken ct = default)
    {
        var templates = await LoadAsync(ct);

        KnownTemplate? best = null;
        var bestScore = 0.0;

        foreach (var t in templates)
        {
            var score = t.Fingerprint.SimilarityTo(fingerprint);
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        if (best is null || bestScore < MatchThreshold)
        {
            _logger.LogDebug("No template match (best={Score:P0})", bestScore);
            return null;
        }

        _logger.LogInformation("Matched template '{Name}' (similarity={Score:P0})", best.Name, bestScore);
        best.MatchCount++;
        await SaveAsync(ct);
        return best;
    }

    // ── Learning ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a new template from a successfully-parsed document.
    /// If a very similar template already exists (≥ 0.95 similarity), increments its
    /// count instead of creating a duplicate.
    /// </summary>
    public async Task LearnAsync(TemplateFingerprint fingerprint, string name, CancellationToken ct = default)
    {
        var templates = await LoadAsync(ct);

        // Avoid near-duplicates
        var existing = templates.FirstOrDefault(t => t.Fingerprint.SimilarityTo(fingerprint) >= 0.95);
        if (existing is not null)
        {
            _logger.LogDebug("Template '{Name}' already known (id={Id})", existing.Name, existing.Id);
            existing.MatchCount++;
            await SaveAsync(ct);
            return;
        }

        templates.Add(new KnownTemplate
        {
            Name = name,
            Fingerprint = fingerprint,
            ConfidenceBoost = 0.15,
            LearnedAt = DateTimeOffset.UtcNow
        });

        _logger.LogInformation("Learned new template '{Name}' (hash={Hash})", name, fingerprint.Hash);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<KnownTemplate>> GetAllAsync(CancellationToken ct = default) =>
        await LoadAsync(ct);

    /// <summary>
    /// Updates (or creates) parsing hints for the template with <paramref name="templateId"/>
    /// by analysing the provided sample file paths.
    /// </summary>
    public async Task UpdateHintsAsync(
        string templateId,
        IEnumerable<string> sampleFilePaths,
        CancellationToken ct = default)
    {
        var templates = await LoadAsync(ct);
        var template = templates.FirstOrDefault(t => t.Id == templateId);
        if (template is null)
        {
            _logger.LogWarning("Template id '{Id}' not found - cannot update hints", templateId);
            return;
        }

        var paths = sampleFilePaths.ToList();
        template.Hints = template.Hints is null
            ? TemplateHintsBuilder.BuildFromFiles(paths)
            : TemplateHintsBuilder.RefineHints(template.Hints, paths);

        _logger.LogInformation(
            "Updated hints for '{Name}' ({Samples} samples, {Sections} section mappings)",
            template.Name, template.Hints.SampleCount, template.Hints.SectionMap.Count);

        await SaveAsync(ct);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private async Task<List<KnownTemplate>> LoadAsync(CancellationToken ct)
    {
        if (_templates is not null) return _templates;

        await _lock.WaitAsync(ct);
        try
        {
            if (_templates is not null) return _templates;

            if (!File.Exists(_registryPath))
            {
                _templates = [];
                return _templates;
            }

            await using var stream = File.OpenRead(_registryPath);
            _templates = await JsonSerializer.DeserializeAsync<List<KnownTemplate>>(stream, JsonOpts, ct)
                         ?? [];
            return _templates;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load template registry from {Path}", _registryPath);
            _templates = [];
            return _templates;
        }
        finally { _lock.Release(); }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
            await using var stream = File.Create(_registryPath);
            await JsonSerializer.SerializeAsync(stream, _templates, JsonOpts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save template registry");
        }
        finally { _lock.Release(); }
    }
}