using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.AI;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using lucidRESUME.GitHub;
using lucidRESUME.Export;
using lucidRESUME.Services;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ProfilePageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
    private readonly ModelDiscoveryService _modelDiscovery;
    private readonly AiSettingsPath _aiSettingsPath;
    private readonly ISecretStore _secretStore;
    private readonly GitHubSkillImporter _gitHubImporter;
    private readonly NuGetPackageAuditService _nuGetPackageAudit;
    private readonly LlamaSharpModelManager _llamaSharpModels;
    private CancellationTokenSource? _saveCts;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _isLoading;
    private readonly PublicationDiscoveryService _publicationDiscovery;
    private readonly CareerProseAnalyser? _proseAnalyser;
    private IReadOnlyList<Project> _publicationCatalog = [];
    [ObservableProperty] private string _publicationMarkdownDirectory = "";
    [ObservableProperty] private string _publicationFilter = "";
    [ObservableProperty] private string _publicationTopics = "";
    [ObservableProperty] private string _publicationStatus = "";
    [ObservableProperty] private bool _isImportingPublications;
    [ObservableProperty] private IReadOnlyList<Project> _publicationCandidates = [];
    [ObservableProperty] private Project? _selectedPublication;
    [ObservableProperty] private string _publicationPreview = "";

    partial void OnSelectedPublicationChanged(Project? value) => PublicationPreview = value is null ? "" :
        $"{value.Name}\n{value.Url}\nReview: {value.EvidenceMetadata.GetValueOrDefault("review", "accepted")}\n\n" +
        string.Join("\n\n", value.EvidencePassages.Select(passage => passage.Text).Prepend(value.Description ?? "").Distinct());

    partial void OnPublicationFilterChanged(string value) => FilterPublications();

    [RelayCommand]
    private async Task RefreshProseAnalysisAsync()
    {
        if (IsImportingPublications || _proseAnalyser is null) return;
        IsImportingPublications = true;
        try
        {
            PublicationStatus = "Analysing the saved career's prose and reusing unchanged embeddings…";
            var state = await _store.LoadAsync();
            var source = state.GetCompleteTranscript();
            if (source is null) { PublicationStatus = "Import a career record first."; return; }
            var index = await _proseAnalyser.AnalyseAsync(source, force: true);
            await _store.MutateAsync(current =>
            {
                var saved = current.GetCompleteTranscript();
                if (saved is not null && lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(saved).SourceRevision == index.SourceRevision)
                {
                    saved.ProseIndex = index;
                    CareerRecordJobMlBuilder.RefreshDerivedExtensions(saved);
                }
                else throw new InvalidOperationException("Career changed during analysis. Refresh again.");
            });
            PublicationStatus = $"{index.Segments.Count} scoped prose segments analysed; {index.Segments.Count(segment => segment.Embedding.Length > 0)} cached vectors. Skills remain linked to accepted evidence.";
        }
        catch (Exception ex) { PublicationStatus = "Prose analysis failed: " + ex.Message; }
        finally { IsImportingPublications = false; }
    }

    private void FilterPublications() => PublicationCandidates = _publicationCatalog
        .Where(article => string.IsNullOrWhiteSpace(PublicationFilter) ||
            (article.Name + " " + article.EvidenceMetadata.GetValueOrDefault("categories")).Contains(PublicationFilter, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(article => article.Date).Take(100).ToList();

    [RelayCommand]
    private async Task RefreshPublicationsAsync()
    {
        if (IsImportingPublications) return;
        IsImportingPublications = true;
        PublicationStatus = "Reading article metadata and passages…";
        try
        {
            var state = await _store.LoadAsync();
            var previous = state.GetCompleteTranscript()?.Publications ?? [];
            var result = await Task.Run(() => PublicationCatalog.ImportAsync(PublicationMarkdownDirectory, previous));
            await _store.MutateAsync(current =>
            {
                var resume = current.GetCompleteTranscript() ?? ResumeDocument.Create("Complete career transcript", CompleteCareerRecord.ContentType, 0);
                foreach (var article in result.Articles)
                {
                    resume.Publications.RemoveAll(existing => existing.Url == article.Url);
                    resume.Publications.Add(article);
                }
                resume.JobMlSource = resume.JobMlRevision = null;
                lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(resume);
                current.AddOrReplaceResume(resume);
            });
            var selectedId = SelectedPublication?.Id;
            _publicationCatalog = result.Articles;
            FilterPublications();
            SelectedPublication = _publicationCatalog.FirstOrDefault(article => article.Id == selectedId);
            PublicationStatus = $"{result.Articles.Count} articles cached; {result.Reused} unchanged. Review an article before using it. {result.Warnings.Count} read warning(s).";
        }
        catch (Exception ex) { PublicationStatus = "Article import failed: " + ex.Message; }
        finally { IsImportingPublications = false; }
    }

    [RelayCommand]
    private async Task ApprovePublicationAsync()
    {
        if (SelectedPublication is not { } selected || IsImportingPublications) return;
        IsImportingPublications = true;
        try
        {
            PublicationStatus = "Checking the published article…";
            if (!await _publicationDiscovery.VerifyAsync(selected.Url ?? ""))
            { PublicationStatus = "Could not verify the published URL. Article remains available for review; try again later."; return; }
            await _store.MutateAsync(state =>
            {
                var resume = state.GetCompleteTranscript();
                var article = resume?.Publications.FirstOrDefault(item => item.Id == selected.Id);
                if (resume is null || article is null || article.EvidenceMetadata.GetValueOrDefault("content_hash") != selected.EvidenceMetadata.GetValueOrDefault("content_hash"))
                    throw new InvalidOperationException("The article changed. Reload and review the current passage.");
                article.EvidenceMetadata["review"] = "accepted";
                article.EvidenceMetadata["live_url_verified"] = "true";
                article.EvidenceMetadata["reviewed_at"] = DateTimeOffset.UtcNow.ToString("O");
                resume.JobMlSource = resume.JobMlRevision = null;
                lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(resume);
                state.AddOrReplaceResume(resume);
            });
            var updated = await _store.LoadAsync();
            _publicationCatalog = updated.GetCompleteTranscript()?.Publications ?? [];
            FilterPublications();
            SelectedPublication = _publicationCatalog.FirstOrDefault(article => article.Id == selected.Id);
            PublicationStatus = "Article approved as authored-publication evidence. Its technology mentions do not declare skills.";
        }
        catch (Exception ex) { PublicationStatus = "Article approval failed: " + ex.Message; }
        finally { IsImportingPublications = false; }
    }

    [RelayCommand]
    private async Task DiscoverPublicationsAsync()
    {
        if (IsImportingPublications || string.IsNullOrWhiteSpace(PublicationTopics)) return;
        IsImportingPublications = true;
        try
        {
            PublicationStatus = "Searching the blog for the supplied public topics…";
            var slugs = await _publicationDiscovery.SuggestAsync(PublicationTopics.Split(';', StringSplitOptions.TrimEntries));
            PublicationCandidates = _publicationCatalog.Where(article => slugs.Contains(new Uri(article.Url!).Segments[^1], StringComparer.OrdinalIgnoreCase)).ToList();
            PublicationStatus = $"{PublicationCandidates.Count} cached articles suggested. Review their passages; semantic scores do not establish relevance. Empty or unavailable search leaves the local catalog intact.";
        }
        catch (Exception ex) { PublicationStatus = "Discovery failed; use the local filter. " + ex.Message; }
        finally { IsImportingPublications = false; }
    }

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
    [ObservableProperty] private string? _saveError;

    // ── AI Provider Settings ────────────────────────────────────────────────
    public IReadOnlyList<string> AiProviders { get; } = ["openai", "anthropic", "ollama", "llamasharp"];
    [ObservableProperty] private bool _composeResumeProse = true;
    [ObservableProperty] private string _aiProvider = "openai";
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private bool _nimbleEnabled;
    [ObservableProperty] private string _nimbleBaseUrl = "http://localhost:11434";
    [ObservableProperty] private string _selectedModel = "";
    [ObservableProperty] private ObservableCollection<string> _availableModelIds = [];
    [ObservableProperty] private ObservableCollection<ModelInfo> _availableModels = [];
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string? _aiSettingsStatus;
    [ObservableProperty] private bool _isLlamaSharp;
    [ObservableProperty] private bool _isDownloadingLlamaSharp;
    [ObservableProperty] private double _llamaSharpDownloadProgress;

    // ── GitHub Import ──────────────────────────────────────────────────────
    [ObservableProperty] private string _gitHubUsername = "";
    [ObservableProperty] private bool _isImportingGitHub;
    [ObservableProperty] private string? _gitHubImportStatus;
    [ObservableProperty] private string _nuGetPublisher = "";
    [ObservableProperty] private bool _isImportingNuGet;
    [ObservableProperty] private string? _nuGetImportStatus;

    public ProfilePageViewModel(
        IAppStore store,
        ModelDiscoveryService modelDiscovery,
        AiSettingsPath aiSettingsPath,
        ISecretStore secretStore,
        GitHubSkillImporter gitHubImporter,
        NuGetPackageAuditService nuGetPackageAudit,
        LlamaSharpModelManager llamaSharpModels, PublicationDiscoveryService? publicationDiscovery = null, CareerProseAnalyser? proseAnalyser = null)
    {
        _store = store;
        _modelDiscovery = modelDiscovery;
        _aiSettingsPath = aiSettingsPath;
        _secretStore = secretStore;
        _gitHubImporter = gitHubImporter;
        _nuGetPackageAudit = nuGetPackageAudit;
        _llamaSharpModels = llamaSharpModels;
        _publicationDiscovery = publicationDiscovery ?? new PublicationDiscoveryService(new HttpClient());
        _proseAnalyser = proseAnalyser;

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
        if (_isLoading || e.PropertyName is nameof(IsSaved) or nameof(PublicationCandidates) or nameof(PublicationFilter)
            or nameof(PublicationTopics) or nameof(PublicationPreview) or nameof(PublicationStatus) or nameof(SelectedPublication) or nameof(IsImportingPublications))
            return;

        ScheduleSave();
    }

    // ── Autosave ──────────────────────────────────────────────────────────────

    private void ScheduleSave()
    {
        if (_isLoading) return;

        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_saveCts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(800, cancellationToken);
            await _saveGate.WaitAsync(cancellationToken);
            try { await SaveAsync(); }
            finally { _saveGate.Release(); }
        }
        catch (OperationCanceledException) { }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            SaveError = null;
            var state = await _store.LoadAsync();
            var profile = state.Profile;

            FullName = profile.DisplayName ?? "";
            CurrentTitle = profile.CurrentTitle ?? "";
            YearsOfExperience = profile.YearsOfExperience;
            CareerGoals = profile.CareerGoals ?? "";
            AdditionalContext = profile.AdditionalContext ?? "";
            PublicationMarkdownDirectory = profile.PublicationMarkdownDirectory ?? "";
            _publicationCatalog = state.GetCompleteTranscript()?.Publications ?? [];
            FilterPublications();

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
                profile.PublicationMarkdownDirectory = NullIfBlank(PublicationMarkdownDirectory);

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
        catch (Exception ex)
        {
            SaveError = $"Autosave failed: {ex.Message}";
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
                var resume = state.GetCompleteTranscript() ?? Core.Models.Resume.ResumeDocument.Create(
                    "profile-enrichment", "application/vnd.lucidresume.profile", 0);

                ProjectEvidenceMerger.MergeAuditObservations(resume, result.Projects);
                resume.JobMlSource = null;
                resume.JobMlRevision = null;
                lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(resume);

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
            var eligibleCount = result.ProjectProfiles.Count(profile => profile.EligibleForPersonalEvidence);
            var observationOnly = repoCount - eligibleCount;
            var deeplyInspected = result.ProjectProfiles.Count(profile => profile.Engineering.TreeObserved);
            GitHubImportStatus = $"Audited {repoCount} repos ({eligibleCount} eligible, {observationOnly} observation-only, {deeplyInspected} deeply inspected); refreshed {skillCount} skill candidates";

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

    [RelayCommand]
    private async Task ImportNuGet()
    {
        if (string.IsNullOrWhiteSpace(NuGetPublisher)) return;
        IsImportingNuGet = true;
        NuGetImportStatus = "Auditing public packages...";
        try
        {
            var result = await _nuGetPackageAudit.AuditAsync(NuGetPublisher.Trim());
            await _store.MutateAsync(state =>
            {
                var resume = state.GetCompleteTranscript() ?? Core.Models.Resume.ResumeDocument.Create(
                    "profile-enrichment", "application/vnd.lucidresume.profile", 0);
                ProjectEvidenceMerger.MergeAuditObservations(resume, result.Projects);
                resume.JobMlSource = null;
                resume.JobMlRevision = null;
                lucidRESUME.Core.Models.Evidence.EvidenceLedgerBuilder.EnsureCurrent(resume);
                state.AddOrReplaceResume(resume, select: true);
            });
            NuGetImportStatus = $"Imported {result.PackageCount} packages as {result.Families.Count} searchable product families ({result.TotalDownloads:N0} registry downloads observed)";
        }
        catch (Exception ex)
        {
            NuGetImportStatus = $"Package audit failed: {ex.Message}";
        }
        finally
        {
            IsImportingNuGet = false;
        }
    }

    // ── AI Provider Settings ───────────────────────────────────────────────────

    private async Task LoadAiSettingsAsync()
    {
        var legacyAnthropicKey = "";
        var legacyOpenAiKey = "";
        try
        {
            if (File.Exists(_aiSettingsPath.Path))
            {
                var json = await File.ReadAllTextAsync(_aiSettingsPath.Path);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Tailoring", out var tailoring) &&
                    tailoring.TryGetProperty("Provider", out var provider))
                    AiProvider = provider.GetString() ?? "openai";
                if (doc.RootElement.TryGetProperty("Tailoring", out var editing) && editing.TryGetProperty("ComposeProse", out var compose))
                    ComposeResumeProse = compose.GetBoolean();
                if (doc.RootElement.TryGetProperty("Anthropic", out var anthropic) &&
                    anthropic.TryGetProperty("ApiKey", out var aKey))
                    legacyAnthropicKey = aKey.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("OpenAi", out var openai) &&
                    openai.TryGetProperty("ApiKey", out var oKey))
                    legacyOpenAiKey = oKey.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("Nimble", out var nimble))
                {
                    if (nimble.TryGetProperty("Enabled", out var enabled)) NimbleEnabled = enabled.GetBoolean();
                    if (nimble.TryGetProperty("BaseUrl", out var baseUrl))
                        NimbleBaseUrl = baseUrl.GetString() ?? NimbleBaseUrl;
                }
                var modelSection = AiProvider.ToLowerInvariant() switch
                { "anthropic" => "Anthropic", "ollama" => "Ollama", "llamasharp" => "LlamaSharp", _ => "OpenAi" };
                if (doc.RootElement.TryGetProperty(modelSection, out var selectedProvider) &&
                    selectedProvider.TryGetProperty("Model", out var model))
                    SelectedModel = model.GetString() ?? "";
            }

            if (_secretStore.IsAvailable)
            {
                AnthropicApiKey = await LoadOrMigrateSecretAsync(AiSecretNames.AnthropicApiKey, legacyAnthropicKey);
                OpenAiApiKey = await LoadOrMigrateSecretAsync(AiSecretNames.OpenAiApiKey, legacyOpenAiKey);
                if (legacyAnthropicKey.Length > 0 || legacyOpenAiKey.Length > 0)
                    await RemoveLegacySecretsAsync();
            }
            else
            {
                // Retain legacy values in memory so the user can migrate them after
                // installing a supported keyring. Never write them back to JSON.
                AnthropicApiKey = legacyAnthropicKey;
                OpenAiApiKey = legacyOpenAiKey;
            }
        }
        catch (Exception ex)
        {
            AiSettingsStatus = $"Could not load or migrate AI settings: {ex.Message}";
        }

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
            if (!Uri.TryCreate(NimbleBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var nimbleUri)
                || nimbleUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(nimbleUri.UserInfo))
                throw new InvalidOperationException("Nimble URL must be an HTTP or HTTPS server address without embedded credentials.");
            if (_secretStore.IsAvailable)
            {
                await SaveSecretAsync(AiSecretNames.AnthropicApiKey, AnthropicApiKey);
                await SaveSecretAsync(AiSecretNames.OpenAiApiKey, OpenAiApiKey);
            }
            else if (!string.IsNullOrWhiteSpace(AnthropicApiKey) || !string.IsNullOrWhiteSpace(OpenAiApiKey))
                throw new InvalidOperationException("An OS credential store is required to save API keys.");

            var existing = File.Exists(_aiSettingsPath.Path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(_aiSettingsPath.Path))?.AsObject() ?? new JsonObject()
                : new JsonObject();
            var settings = new Dictionary<string, object>
            {
                ["Tailoring"] = new Dictionary<string, object>
                {
                    ["ComposeProse"] = ComposeResumeProse,
                    ["Provider"] = AiProvider,
                    ["ExtractionProvider"] = existing["Tailoring"]?["ExtractionProvider"]?.GetValue<string>() ?? "none"
                },
                ["Anthropic"] = new Dictionary<string, string>
                {
                    ["Model"] = AiProvider == "anthropic" ? SelectedModel : "",
                    ["ExtractionModel"] = AiProvider == "anthropic" ? SelectedModel : ""
                },
                ["OpenAi"] = new Dictionary<string, string>
                {
                    ["Model"] = AiProvider == "openai" ? SelectedModel : "",
                    ["ExtractionModel"] = AiProvider == "openai" ? SelectedModel : ""
                },
                ["Nimble"] = new Dictionary<string, object>
                {
                    ["Enabled"] = NimbleEnabled,
                    ["BaseUrl"] = nimbleUri.ToString().TrimEnd('/'),
                    ["Model"] = "nimble"
                },
                ["Ollama"] = new Dictionary<string, object>
                {
                    ["CompositionEnabled"] = ComposeResumeProse && AiProvider == "ollama",
                    ["Model"] = AiProvider == "ollama" ? SelectedModel : "",
                    ["ExtractionModel"] = AiProvider == "ollama" ? SelectedModel : ""
                },
                ["LlamaSharp"] = new Dictionary<string, string>
                {
                    ["ModelId"] = _llamaSharpModels.ModelId,
                    ["ModelPath"] = _llamaSharpModels.ModelPath
                }
            };

            // Preserve configured server URLs, context windows and other settings absent from this form.
            var edited = JsonSerializer.SerializeToNode(settings)!.AsObject();
            foreach (var (sectionName, section) in edited)
            {
                if (section is not JsonObject fields) { existing[sectionName] = section?.DeepClone(); continue; }
                var target = existing[sectionName] as JsonObject;
                if (target is null) existing[sectionName] = target = new JsonObject();
                foreach (var (name, value) in fields) target[name] = value?.DeepClone();
            }
            foreach (var sectionName in new[] { "Anthropic", "OpenAi", "Jev" })
                if (existing[sectionName] is JsonObject section) section.Remove("ApiKey");
            await WriteSettingsAtomicallyAsync(existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            AiSettingsStatus = "Saved. Restart to apply settings.";
        }
        catch (Exception ex)
        {
            AiSettingsStatus = $"Save failed: {ex.Message}";
        }
    }

    private async Task<string> LoadOrMigrateSecretAsync(string name, string legacyValue)
    {
        var stored = await _secretStore.GetAsync(name);
        if (!string.IsNullOrWhiteSpace(stored)) return stored;
        if (string.IsNullOrWhiteSpace(legacyValue)) return "";
        await _secretStore.SetAsync(name, legacyValue);
        return legacyValue;
    }

    private async Task SaveSecretAsync(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) await _secretStore.DeleteAsync(name);
        else await _secretStore.SetAsync(name, value.Trim());
    }

    private async Task RemoveLegacySecretsAsync()
    {
        var json = await File.ReadAllTextAsync(_aiSettingsPath.Path);
        var root = JsonNode.Parse(json)?.AsObject()
                   ?? throw new InvalidDataException("AI settings are not a JSON object.");
        foreach (var sectionName in new[] { "Anthropic", "OpenAi", "Jev" })
            if (root[sectionName] is JsonObject section)
                section.Remove("ApiKey");
        await WriteSettingsAtomicallyAsync(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task WriteSettingsAtomicallyAsync(string json)
    {
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
