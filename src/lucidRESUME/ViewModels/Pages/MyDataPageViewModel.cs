using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Matching;
using lucidRESUME.ViewModels.Pages.MyData;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class MyDataPageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
    private readonly SkillLedgerBuilder _ledgerBuilder;
    private SkillLedger? _ledger;
    private ResumeDocument? _resume;
    private UserOverrides _overrides = new();
    private CancellationTokenSource? _saveCts;

    // ── Personal Info ──────────────────────────────────────────────────────
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _location = "";
    [ObservableProperty] private string _linkedIn = "";
    [ObservableProperty] private string _gitHub = "";
    [ObservableProperty] private string _website = "";
    [ObservableProperty] private string _summary = "";

    // ── Skill Ledger ───────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<SkillLedgerEntryVm> _allSkills = [];
    [ObservableProperty] private ObservableCollection<SkillLedgerEntryVm> _filteredSkills = [];
    [ObservableProperty] private string _skillSearch = "";
    [ObservableProperty] private string? _selectedCategory;
    [ObservableProperty] private bool _showDismissed;
    public ObservableCollection<string> Categories { get; } = ["All"];

    // ── Experience ─────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<WorkExperience> _experience = [];

    // ── Education ──────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<Education> _education = [];

    // ── Projects ───────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<Project> _projects = [];

    // ── Consistency Issues ─────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ConsistencyIssue> _issues = [];

    // ── State ──────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _strongCount;
    [ObservableProperty] private int _moderateCount;
    [ObservableProperty] private int _weakCount;

    // ── Add Skill ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _newSkillName = "";
    [ObservableProperty] private string _newSkillCategory = "Language";

    public MyDataPageViewModel(IAppStore store, SkillLedgerBuilder ledgerBuilder)
    {
        _store = store;
        _ledgerBuilder = ledgerBuilder;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var state = await _store.LoadAsync();
            _resume = state.BuildAggregateResume();
            _overrides = state.Overrides;

            if (_resume == null) { HasData = false; return; }
            HasData = true;

            // Personal info
            FullName = _resume.Personal.FullName ?? "";
            Email = _resume.Personal.Email ?? "";
            Phone = _resume.Personal.Phone ?? "";
            Location = _resume.Personal.Location ?? "";
            LinkedIn = _resume.Personal.LinkedInUrl ?? "";
            GitHub = _resume.Personal.GitHubUrl ?? "";
            Website = _resume.Personal.WebsiteUrl ?? "";
            Summary = _resume.Personal.Summary ?? "";

            // Apply overrides to personal info
            foreach (var (field, value) in _overrides.PersonalInfoOverrides)
            {
                switch (field)
                {
                    case nameof(FullName): FullName = value; break;
                    case nameof(Email): Email = value; break;
                    case nameof(Phone): Phone = value; break;
                    case nameof(Location): Location = value; break;
                    case nameof(Summary): Summary = value; break;
                }
            }

            // Build ledger
            _ledger = await _ledgerBuilder.BuildAsync(_resume);
            SkillLedgerBuilder.ApplyOverrides(_ledger, _overrides);

            // Populate skills
            var allVms = _ledger.Entries.Select(e =>
                new SkillLedgerEntryVm(e, OnSkillDismissed, ScheduleSave)).ToList();
            AllSkills = new ObservableCollection<SkillLedgerEntryVm>(allVms);

            // Categories
            var cats = allVms
                .Select(s => s.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
            Categories.Clear();
            Categories.Add("All");
            foreach (var cat in cats) Categories.Add(cat!);

            ApplyFilters();

            // Counts
            StrongCount = allVms.Count(s => s.Strength > 0.5);
            ModerateCount = allVms.Count(s => s.Strength is > 0.1 and <= 0.5);
            WeakCount = allVms.Count(s => s.Strength <= 0.1);

            // Experience, education, projects
            Experience = new ObservableCollection<WorkExperience>(_resume.Experience);
            Education = new ObservableCollection<Education>(_resume.Education);
            Projects = new ObservableCollection<Project>(_resume.Projects);
            Issues = new ObservableCollection<ConsistencyIssue>(_ledger.Issues);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSkillSearchChanged(string value) => ApplyFilters();
    partial void OnSelectedCategoryChanged(string? value) => ApplyFilters();
    partial void OnShowDismissedChanged(bool value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = AllSkills.AsEnumerable();

        if (!ShowDismissed)
            filtered = filtered.Where(s => !s.IsDismissed);

        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All")
            filtered = filtered.Where(s =>
                string.Equals(s.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SkillSearch))
            filtered = filtered.Where(s =>
                s.SkillName.Contains(SkillSearch, StringComparison.OrdinalIgnoreCase));

        FilteredSkills = new ObservableCollection<SkillLedgerEntryVm>(filtered);
    }

    private void OnSkillDismissed(SkillLedgerEntryVm skill)
    {
        _overrides.DismissedSkills.Add(skill.SkillName);
        ApplyFilters();
        ScheduleSave();
    }

    [RelayCommand]
    private void AddSkill()
    {
        if (string.IsNullOrWhiteSpace(NewSkillName)) return;

        var manual = new ManualSkillEntry
        {
            SkillName = NewSkillName.Trim(),
            Category = NewSkillCategory,
        };
        _overrides.ManualSkills.Add(manual);

        var entry = new SkillLedgerEntry
        {
            SkillName = manual.SkillName,
            Category = manual.Category,
            Evidence =
            [
                new SkillEvidence
                {
                    SourceText = "Added manually",
                    Source = EvidenceSource.Manual,
                    Confidence = 1.0,
                }
            ],
        };

        var vm = new SkillLedgerEntryVm(entry, OnSkillDismissed, ScheduleSave);
        AllSkills.Insert(0, vm);
        ApplyFilters();
        NewSkillName = "";
        ScheduleSave();
    }

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        Task.Delay(800, token).ContinueWith(_ => SaveOverridesAsync(), token,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private async Task SaveOverridesAsync()
    {
        await _store.MutateAsync(state => state.Overrides = _overrides);
    }
}
