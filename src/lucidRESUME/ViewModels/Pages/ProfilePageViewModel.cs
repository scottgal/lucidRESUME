using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.AI;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Persistence;
using lucidRESUME.GitHub;
using lucidRESUME.Services;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ProfilePageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
    private readonly ModelDiscoveryService _modelDiscovery;
    private readonly AiSettingsPath _aiSettingsPath;
    private readonly GitHubSkillImporter _gitHubImporter;
    private readonly LlamaSharpModelManager _llamaSharpModels;
    private CancellationTokenSource? _saveCts;
    private bool _isLoading;

    // ── Who they are ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private int? _yearsOfExperience;
    [ObservableProperty] private string _careerGoals = "";
    [ObservableProperty] private string _additionalContext = "";

    // ── Work preferences (scalar) ────────────────────────────────────────────
    [ObservableProperty] private bool _openToRemote = true;
    [ObservableProperty] private bool _openToHybrid = true;
    [ObservableProperty] private bool _openToOnsite = true;
    [ObservableProperty] private decimal? _minSalary;
    [ObservableProperty] private string _preferredCurrency = "GBP";
    [ObservableProperty] private int? _maxCommuteMinutes;

    // ── Tag collections ──────────────────────────────────────────────────────
    public ObservableCollection<TagItem> PreferredLocations { get; } = [];
    public ObservableCollection<TagItem> TargetRoles { get; } = [];
    public ObservableCollection<TagItem> TargetIndustries { get; } = [];
    public ObservableCollection<TagItem> BlockedIndustries { get; } = [];
    public ObservableCollection<TagItem> BlockedCompanies { get; } = [];
    public ObservableCollection<TagItem> SkillsToEmphasise { get; } = [];
    public ObservableCollection<TagItem> SkillsToAvoid { get; } = [];

    // ── Theme ─────────────────────────────────────────────────────────────────
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];
    [ObservableProperty] private string _selectedTheme = "System";

    partial void OnSelectedThemeChanged(string value)
    {
        if (_isLoading) return;
        var app = Avalonia.Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = value switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    // ── Toast ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSaved;

    // ── AI Provider Settings ────────────────────────────────────────────────
    public IReadOnlyList<string> AiProviders { get; } = ["llamasharp", "ollama", "anthropic", "openai"];
    [ObservableProperty] private string _aiProvider = "llamasharp";
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private string _selectedModel = "";
    [ObservableProperty] private ObservableCollection<string> _availableModelIds = [];
    [ObservableProperty] private ObservableCollection<ModelInfo> _availableModels = [];
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string? _aiSettingsStatus;
    [ObservableProperty] private bool _isLlamaSharp = true;
    [ObservableProperty] private bool _isDownloadingLlamaSharp;
    [ObservableProperty] private double _llamaSharpDownloadProgress;

    // ── GitHub Import ──────────────────────────────────────────────────────
    [ObservableProperty] private string _gitHubUsername = "";
    [ObservableProperty] private bool _isImportingGitHub;
    [ObservableProperty] private string? _gitHubImportStatus;

    public ProfilePageViewModel(
        IAppStore store,
        ModelDiscoveryService modelDiscovery,
        AiSettingsPath aiSettingsPath,
        GitHubSkillImporter gitHubImporter,
        LlamaSharpModelManager llamaSharpModels)
    {
        _store = store;
        _modelDiscovery = modelDiscovery;
        _aiSettingsPath = aiSettingsPath;
        _gitHubImporter = gitHubImporter;
        _llamaSharpModels = llamaSharpModels;

        SubscribeCollections();

        _ = LoadAsync();
        _ = LoadAiSettingsAsync();
    }

    // ── Collection-change subscriptions ──────────────────────────────────────

    private void SubscribeCollections()
    {
        PreferredLocations.CollectionChanged += (_, _) => ScheduleSave();
        TargetRoles.CollectionChanged += (_, _) => ScheduleSave();
        TargetIndustries.CollectionChanged += (_, _) => ScheduleSave();
        BlockedIndustries.CollectionChanged += (_, _) => ScheduleSave();
        BlockedCompanies.CollectionChanged += (_, _) => ScheduleSave();
        SkillsToEmphasise.CollectionChanged += (_, _) => ScheduleSave();
        SkillsToAvoid.CollectionChanged += (_, _) => ScheduleSave();
    }

    // ── Property-change hook ──────────────────────────────────────────────────

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Don't trigger autosave while loading, and don't react to the toast flag itself
        if (_isLoading || e.PropertyName == nameof(IsSaved))
            return;

        ScheduleSave();
    }

    // ── Autosave ──────────────────────────────────────────────────────────────

    private void ScheduleSave()
    {
        if (_isLoading) return;

        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        Task.Delay(800, token).ContinueWith(
            _ => SaveAsync(),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var state = await _store.LoadAsync();
            var profile = state.Profile;

            FullName = profile.DisplayName ?? "";
            CurrentTitle = profile.CurrentTitle ?? "";
            YearsOfExperience = profile.YearsOfExperience;
            CareerGoals = profile.CareerGoals ?? "";
            AdditionalContext = profile.AdditionalContext ?? "";

            OpenToRemote = profile.Preferences.OpenToRemote;
            OpenToHybrid = profile.Preferences.OpenToHybrid;
            OpenToOnsite = profile.Preferences.OpenToOnsite;
            MinSalary = profile.Preferences.MinSalary;
            PreferredCurrency = profile.Preferences.PreferredCurrency ?? "GBP";
            MaxCommuteMinutes = profile.Preferences.MaxCommuteMinutes;

            ReplaceCollection(PreferredLocations, ToTagCollection(profile.Preferences.PreferredLocations));
            ReplaceCollection(TargetRoles, ToTagCollection(profile.Preferences.TargetRoles));
            ReplaceCollection(TargetIndustries, ToTagCollection(profile.Preferences.TargetIndustries));
            ReplaceCollection(BlockedIndustries, ToTagCollection(profile.Preferences.BlockedIndustries));
            ReplaceCollection(BlockedCompanies, ToTagCollection(profile.BlockedCompanies));
            ReplaceCollection(SkillsToEmphasise, ToTagCollection(profile.SkillsToEmphasise));
            ReplaceCollection(SkillsToAvoid, ToTagCollection(profile.SkillsToAvoid));
        }
        catch
        {
            // Leave defaults if load fails
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        try
        {
            await _store.MutateAsync(state =>
            {
                var profile = state.Profile;

                profile.DisplayName = NullIfBlank(FullName);
                profile.CurrentTitle = NullIfBlank(CurrentTitle);
                profile.YearsOfExperience = YearsOfExperience;
                profile.CareerGoals = NullIfBlank(CareerGoals);
                profile.AdditionalContext = NullIfBlank(AdditionalContext);

                profile.Preferences.OpenToRemote = OpenToRemote;
                profile.Preferences.OpenToHybrid = OpenToHybrid;
                profile.Preferences.OpenToOnsite = OpenToOnsite;
                profile.Preferences.MinSalary = MinSalary;
                profile.Preferences.PreferredCurrency = NullIfBlank(PreferredCurrency) ?? "GBP";
                profile.Preferences.MaxCommuteMinutes = MaxCommuteMinutes;

                profile.Preferences.PreferredLocations = ToStringList(PreferredLocations);
                profile.Preferences.TargetRoles = ToStringList(TargetRoles);
                profile.Preferences.TargetIndustries = ToStringList(TargetIndustries);
                profile.Preferences.BlockedIndustries = ToStringList(BlockedIndustries);
                profile.BlockedCompanies = ToStringList(BlockedCompanies);
                profile.SkillsToEmphasise = ToSkillList(SkillsToEmphasise);
                profile.SkillsToAvoid = ToSkillList(SkillsToAvoid);
            });

            IsSaved = true;
            await Task.Delay(2000);
            IsSaved = false;
        }
        catch
        {
            // Silently ignore save errors for now
        }
    }

    // ── GitHub Import ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ImportGitHub()
    {
        if (string.IsNullOrWhiteSpace(GitHubUsername)) return;
        IsImportingGitHub = true;
        GitHubImportStatus = "Importing...";
        try
        {
            var result = await _gitHubImporter.ImportAsync(GitHubUsername.Trim());

            // Store projects and profile data into the resume
            await _store.MutateAsync(state =>
            {
                var resume = state.SelectedResume ?? Core.Models.Resume.ResumeDocument.Create(
                    "profile-enrichment", "application/vnd.lucidresume.profile", 0);

                // Add GitHub projects that aren't already present
                var existingUrls = resume.Projects.Select(p => p.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var project in result.Projects.Where(p => !existingUrls.Contains(p.Url)))
                    resume.Projects.Add(project);

                // Enrich PersonalInfo from GitHub profile
                if (result.Profile is { } profile)
                {
                    resume.Personal.GitHubUrl ??= profile.HtmlUrl;
                    resume.Personal.FullName ??= profile.Name;
                    resume.Personal.WebsiteUrl ??= profile.Blog;
                }

                state.AddOrReplaceResume(resume, select: true);
            });

            var skillCount = result.SkillEntries.Count;
            var repoCount = result.ReposAnalysed;
            var projectCount = result.Projects.Count;
            GitHubImportStatus = $"Imported {skillCount} skills from {repoCount} repos, {projectCount} projects added";

            if (result.Warnings.Count > 0)
                GitHubImportStatus += $" ({result.Warnings.Count} warnings)";
        }
        catch (GitHubRateLimitException ex)
        {
            GitHubImportStatus = $"Rate limited. Resets at {ex.ResetsAt:HH:mm UTC}";
        }
        catch (Exception ex)
        {
            GitHubImportStatus = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsImportingGitHub = false;
        }
    }

    // ── AI Provider Settings ───────────────────────────────────────────────────

    private async Task LoadAiSettingsAsync()
    {
        try
        {
            if (File.Exists(_aiSettingsPath.Path))
            {
                var json = await File.ReadAllTextAsync(_aiSettingsPath.Path);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Tailoring", out var tailoring) &&
                    tailoring.TryGetProperty("Provider", out var provider))
                    AiProvider = provider.GetString() ?? "llamasharp";
                if (doc.RootElement.TryGetProperty("Anthropic", out var anthropic) &&
                    anthropic.TryGetProperty("ApiKey", out var aKey))
                    AnthropicApiKey = aKey.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("OpenAi", out var openai) &&
                    openai.TryGetProperty("ApiKey", out var oKey))
                    OpenAiApiKey = oKey.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("Anthropic", out var a2) &&
                    a2.TryGetProperty("Model", out var model))
                    SelectedModel = model.GetString() ?? "";
                else if (doc.RootElement.TryGetProperty("OpenAi", out var o2) &&
                         o2.TryGetProperty("Model", out var oModel))
                    SelectedModel = oModel.GetString() ?? "";
            }
        }
        catch { /* use defaults */ }

        await RefreshModelsAsync();
    }

    [RelayCommand]
    private async Task RefreshModels()
    {
        await RefreshModelsAsync();
    }

    private async Task RefreshModelsAsync()
    {
        IsLoadingModels = true;
        try
        {
            var models = AiProvider.ToLowerInvariant() switch
            {
                "llamasharp" => _modelDiscovery.ListLlamaSharpModels(),
                "anthropic" => await _modelDiscovery.ListAnthropicModelsAsync(),
                "openai" => await _modelDiscovery.ListOpenAiModelsAsync(),
                _ => await _modelDiscovery.ListOllamaModelsAsync()
            };
            AvailableModels = new ObservableCollection<ModelInfo>(models);
            AvailableModelIds = new ObservableCollection<string>(models.Select(m => m.Id));

            if (AvailableModelIds.Count > 0 && !AvailableModelIds.Contains(SelectedModel))
                SelectedModel = AvailableModelIds[0];
        }
        catch { AvailableModels = []; }
        finally { IsLoadingModels = false; }
    }

    [RelayCommand]
    private async Task SaveAiSettings()
    {
        try
        {
            var settings = new Dictionary<string, object>
            {
                ["Tailoring"] = new Dictionary<string, string> { ["Provider"] = AiProvider },
                ["Anthropic"] = new Dictionary<string, string>
                {
                    ["ApiKey"] = AnthropicApiKey,
                    ["Model"] = AiProvider == "anthropic" ? SelectedModel : "",
                    ["ExtractionModel"] = AiProvider == "anthropic" ? SelectedModel : ""
                },
                ["OpenAi"] = new Dictionary<string, string>
                {
                    ["ApiKey"] = OpenAiApiKey,
                    ["Model"] = AiProvider == "openai" ? SelectedModel : "",
                    ["ExtractionModel"] = AiProvider == "openai" ? SelectedModel : ""
                },
                ["Ollama"] = new Dictionary<string, string>
                {
                    ["Model"] = AiProvider == "ollama" ? SelectedModel : "",
                    ["ExtractionModel"] = AiProvider == "ollama" ? SelectedModel : ""
                },
                ["LlamaSharp"] = new Dictionary<string, string>
                {
                    ["ModelId"] = _llamaSharpModels.ModelId,
                    ["ModelPath"] = _llamaSharpModels.ModelPath
                }
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_aiSettingsPath.Path)
                            ?? throw new InvalidOperationException("AI settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".ai-settings-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporaryPath, json);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(temporaryPath, _aiSettingsPath.Path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            AiSettingsStatus = $"Saved. Restart app to apply {AiProvider}/{SelectedModel}.";
        }
        catch (Exception ex)
        {
            AiSettingsStatus = $"Save failed: {ex.Message}";
        }
    }

    partial void OnAiProviderChanged(string value)
    {
        IsLlamaSharp = value.Equals("llamasharp", StringComparison.OrdinalIgnoreCase);
        if (!_isLoading) _ = RefreshModelsAsync();
    }

    [RelayCommand]
    private async Task DownloadLlamaSharpModel()
    {
        if (_llamaSharpModels.IsModelPresent)
        {
            LlamaSharpDownloadProgress = 1;
            AiSettingsStatus = $"Local model is ready at {_llamaSharpModels.ModelPath}.";
            return;
        }

        IsDownloadingLlamaSharp = true;
        AiSettingsStatus = "Downloading grug 9B Q4_K_M (5.63 GB)...";
        try
        {
            var progress = new Progress<double>(value => LlamaSharpDownloadProgress = value);
            await _llamaSharpModels.DownloadAsync(progress);
            AiSettingsStatus = $"Local model ready at {_llamaSharpModels.ModelPath}. Restart to apply.";
        }
        catch (Exception ex)
        {
            AiSettingsStatus = $"Model download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingLlamaSharp = false;
        }
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static ObservableCollection<TagItem> ToTagCollection(IEnumerable<string> source)
        => new(source.Select(v => new TagItem { Value = v }));

    private static ObservableCollection<TagItem> ToTagCollection(IEnumerable<SkillPreference> source)
        => new(source.Select(s => new TagItem { Value = s.SkillName, Reason = s.Reason }));

    private static List<string> ToStringList(IEnumerable<TagItem> source)
        => source.Select(t => t.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

    private static List<SkillPreference> ToSkillList(IEnumerable<TagItem> source)
        => source.Where(t => !string.IsNullOrWhiteSpace(t.Value))
                 .Select(t => new SkillPreference { SkillName = t.Value, Reason = t.Reason })
                 .ToList();

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Replaces all items in <paramref name="target"/> with <paramref name="source"/>.
    /// Fires CollectionChanged for each Clear and Add operation;
    /// save triggers are suppressed by the <see cref="_isLoading"/> guard.
    /// </summary>
    private static void ReplaceCollection(ObservableCollection<TagItem> target,
                                          ObservableCollection<TagItem> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
