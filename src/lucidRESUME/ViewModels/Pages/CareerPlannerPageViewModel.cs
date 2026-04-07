using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Skills;
using lucidRESUME.Core.Persistence;
using lucidRESUME.Matching;
using lucidRESUME.Matching.Graph;
using lucidRESUME.Core.Models.Extraction;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class CareerPlannerPageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
    private readonly SkillLedgerBuilder _ledgerBuilder;
    private readonly JdSkillLedgerBuilder _jdLedgerBuilder;
    private readonly CareerPlanner _planner;

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;

    // Job selection
    [ObservableProperty] private ObservableCollection<JobDescription> _jobs = [];
    [ObservableProperty] private JobDescription? _selectedJob;

    // Plan results
    [ObservableProperty] private string _targetTitle = "";
    [ObservableProperty] private double _currentFit;
    [ObservableProperty] private double _requiredCoverage;
    [ObservableProperty] private int _actionsToGoodFit;
    [ObservableProperty] private ObservableCollection<CareerRecommendation> _lowEffort = [];
    [ObservableProperty] private ObservableCollection<CareerRecommendation> _mediumEffort = [];
    [ObservableProperty] private ObservableCollection<CareerRecommendation> _highEffort = [];
    [ObservableProperty] private bool _hasPlan;

    public CareerPlannerPageViewModel(IAppStore store, SkillLedgerBuilder ledgerBuilder,
        JdSkillLedgerBuilder jdLedgerBuilder, CareerPlanner planner)
    {
        _store = store;
        _ledgerBuilder = ledgerBuilder;
        _jdLedgerBuilder = jdLedgerBuilder;
        _planner = planner;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var state = await _store.LoadAsync();
        var resume = state.BuildAggregateResume();
        HasData = resume != null;
        Jobs = new ObservableCollection<JobDescription>(state.Jobs);

        if (Jobs.Count > 0)
            SelectedJob = Jobs[0];
    }

    partial void OnSelectedJobChanged(JobDescription? value)
    {
        if (value != null) _ = PlanForJobAsync(value);
    }

    [RelayCommand]
    private async Task PlanForJob()
    {
        if (SelectedJob != null) await PlanForJobAsync(SelectedJob);
    }

    private async Task PlanForJobAsync(JobDescription jd)
    {
        IsLoading = true;
        StatusMessage = "Analysing gaps...";
        try
        {
            var state = await _store.LoadAsync();
            var resume = state.BuildAggregateResume();
            if (resume == null) { StatusMessage = "No resume data"; return; }

            var ledger = await _ledgerBuilder.BuildAsync(resume);
            SkillLedgerBuilder.ApplyOverrides(ledger, state.Overrides);

            var jdLedger = await _jdLedgerBuilder.BuildAsync(jd);

            var graph = new SkillGraph();
            graph.AddResumeLedger(ledger);
            graph.AddJdLedger(jdLedger);
            graph.DetectCommunities();

            var plan = await _planner.PlanAsync(ledger, jdLedger, graph);

            TargetTitle = plan.TargetTitle;
            CurrentFit = plan.CurrentFit;
            RequiredCoverage = plan.RequiredCoverage;
            ActionsToGoodFit = plan.ActionsToGoodFit;

            LowEffort = new ObservableCollection<CareerRecommendation>(
                plan.Recommendations.Where(r => r.Effort == EffortLevel.Low));
            MediumEffort = new ObservableCollection<CareerRecommendation>(
                plan.Recommendations.Where(r => r.Effort == EffortLevel.Medium));
            HighEffort = new ObservableCollection<CareerRecommendation>(
                plan.Recommendations.Where(r => r.Effort == EffortLevel.High));

            HasPlan = plan.Recommendations.Count > 0;
            StatusMessage = $"{plan.Recommendations.Count} recommendations";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
