using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.AI;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Services;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ProfilePageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
    private readonly ModelDiscoveryService _modelDiscovery;
    private readonly AiSettingsPath _aiSettingsPath;
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

    // ── Toast ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSaved;

    // ── AI Provider Settings ────────────────────────────────────────────────
    public IReadOnlyList<string> AiProviders { get; } = ["ollama", "anthropic", "openai"];
    [ObservableProperty] private string _aiProvider = "ollama";
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private string _selectedModel = "";
    [ObservableProperty] private ObservableCollection<string> _availableModelIds = [];
    [ObservableProperty] private ObservableCollection<ModelInfo> _availableModels = [];
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string? _aiSettingsStatus;

    public ProfilePageViewModel(IAppStore store, ModelDiscoveryService modelDiscovery, AiSettingsPath aiSettingsPath)
    {
        _store = store;
        _modelDiscovery = modelDiscovery;
        _aiSettingsPath = aiSettingsPath;

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
                    AiProvider = provider.GetString() ?? "ollama";
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
                }
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_aiSettingsPath.Path, json);
            AiSettingsStatus = $"Saved. Restart app to apply {AiProvider}/{SelectedModel}.";
        }
        catch (Exception ex)
        {
            AiSettingsStatus = $"Save failed: {ex.Message}";
        }
    }

    partial void OnAiProviderChanged(string value)
    {
        if (!_isLoading) _ = RefreshModelsAsync();
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
