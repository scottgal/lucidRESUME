using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ProfilePageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
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

    public ProfilePageViewModel(IAppStore store)
    {
        _store = store;

        SubscribeCollections();

        _ = LoadAsync();
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
