using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Matching;
using lucidRESUME.ViewModels.Pages.MyData;
using SkiaSharp;

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
    [ObservableProperty] private ObservableCollection<GanttBarVm> _ganttBars = [];
    [ObservableProperty] private string _ganttStartLabel = "";
    [ObservableProperty] private string _ganttEndLabel = "";

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

    // ── Charts ─────────────────────────────────────────────────────────────
    [ObservableProperty] private ISeries[] _categoryPieSeries = [];
    [ObservableProperty] private ISeries[] _strengthBarSeries = [];
    [ObservableProperty] private ISeries[] _topSkillsRadarSeries = [];
    [ObservableProperty] private PolarAxis[] _topSkillsRadarAxes = [];

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

            // Build charts
            BuildCharts(allVms, _resume.Experience);
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

    private void BuildCharts(List<SkillLedgerEntryVm> skills, IEnumerable<WorkExperience> experience)
    {
        // 1. Category pie chart
        var catColors = new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["Language"] = new(30, 136, 229),
            ["Framework"] = new(156, 39, 176),
            ["Cloud & DevOps"] = new(255, 152, 0),
            ["Database"] = new(0, 188, 212),
            ["Tool"] = new(76, 175, 80),
            ["AI/ML"] = new(233, 30, 99),
            ["Methodology"] = new(121, 85, 72),
            ["Security"] = new(244, 67, 54),
        };

        CategoryPieSeries = skills
            .GroupBy(s => s.Category ?? "Other")
            .Where(g => g.Count() >= 1)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                var color = catColors.GetValueOrDefault(g.Key, new SKColor(158, 158, 158));
                return (ISeries)new PieSeries<int>
                {
                    Values = [g.Count()],
                    Name = g.Key,
                    Fill = new SolidColorPaint(color),
                    InnerRadius = 40,
                };
            })
            .ToArray();

        // 2. Strength distribution
        StrengthBarSeries =
        [
            new StackedRowSeries<int>
            {
                Values = [StrongCount],
                Name = "Strong",
                Fill = new SolidColorPaint(new SKColor(76, 175, 80)),
            },
            new StackedRowSeries<int>
            {
                Values = [ModerateCount],
                Name = "Moderate",
                Fill = new SolidColorPaint(new SKColor(255, 152, 0)),
            },
            new StackedRowSeries<int>
            {
                Values = [WeakCount],
                Name = "Weak",
                Fill = new SolidColorPaint(new SKColor(244, 67, 54)),
            },
        ];

        // 3. Top skills radar
        var topSkills = skills.OrderByDescending(s => s.Strength).Take(8).ToList();
        if (topSkills.Count >= 3)
        {
            TopSkillsRadarSeries =
            [
                new PolarLineSeries<double>
                {
                    Values = topSkills.Select(s => s.Strength).ToArray(),
                    Name = "Strength",
                    Fill = new SolidColorPaint(new SKColor(30, 136, 229, 60)),
                    Stroke = new SolidColorPaint(new SKColor(30, 136, 229), 2),
                    GeometrySize = 6,
                    LineSmoothness = 0,
                    IsClosed = true,
                }
            ];
            TopSkillsRadarAxes = topSkills.Select(s => new PolarAxis
            {
                Name = s.SkillName,
                NameTextSize = 10,
                MinLimit = 0,
                MaxLimit = 1,
            }).ToArray();
        }

        // 4. Career Gantt chart
        var expList = experience.OrderBy(e => e.StartDate).ToList();
        if (expList.Count > 0)
        {
            var earliest = expList.Min(e => e.StartDate ?? DateOnly.FromDateTime(DateTime.Today));
            var now = DateOnly.FromDateTime(DateTime.Today);
            var totalDays = Math.Max(1, now.DayNumber - earliest.DayNumber);

            var colors = new[] { "#1E88E5", "#8E24AA", "#43A047", "#FB8C00", "#E53935", "#00ACC1", "#5E35B1", "#F4511E" };
            GanttStartLabel = earliest.Year.ToString();
            GanttEndLabel = now.Year.ToString();

            GanttBars = new ObservableCollection<GanttBarVm>(expList.Select((exp, i) =>
            {
                var start = (exp.StartDate ?? earliest).DayNumber - earliest.DayNumber;
                var end = (exp.IsCurrent ? now : exp.EndDate ?? now).DayNumber - earliest.DayNumber;
                var dates = $"{exp.StartDate?.ToString("MMM yyyy") ?? "?"} – {(exp.IsCurrent ? "Present" : exp.EndDate?.ToString("MMM yyyy") ?? "?")}";
                return new GanttBarVm
                {
                    Title = exp.Title ?? "",
                    Company = exp.Company ?? "",
                    DateRange = dates,
                    LeftPercent = (double)start / totalDays * 100,
                    WidthPercent = Math.Max(1, (double)(end - start) / totalDays * 100),
                    Color = colors[i % colors.Length],
                    IsCurrent = exp.IsCurrent,
                };
            }));
        }
    }
}
