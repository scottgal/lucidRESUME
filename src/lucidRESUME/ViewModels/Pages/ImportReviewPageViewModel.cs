using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ImportReviewPageViewModel : ViewModelBase
{
    private readonly IAppStore _store;
    private readonly ResumeDocumentMerger _merger;
    private ResumeDocument? _target;
    private ImportPreview? _preview;

    /// <summary>Set by the resume page before navigating here.</summary>
    public Action? OnApplied { get; set; }
    public Action? OnCancelled { get; set; }

    [ObservableProperty] private string _sourceName = "";
    [ObservableProperty] private int _newItemCount;
    [ObservableProperty] private int _mergeItemCount;
    [ObservableProperty] private int _anomalyCount;

    // Personal info
    [ObservableProperty] private ObservableCollection<FieldChange> _personalInfoChanges = [];

    // Experience
    [ObservableProperty] private ObservableCollection<ReviewableItem<WorkExperience>> _newExperience = [];
    [ObservableProperty] private ObservableCollection<ExperienceMergePreview> _mergedExperience = [];

    // Skills
    [ObservableProperty] private ObservableCollection<ReviewableItem<Skill>> _newSkills = [];
    [ObservableProperty] private ObservableCollection<SkillUpdatePreview> _updatedSkills = [];

    // Education + Projects
    [ObservableProperty] private ObservableCollection<ReviewableItem<Education>> _newEducation = [];
    [ObservableProperty] private ObservableCollection<ReviewableItem<Project>> _newProjects = [];

    // Anomalies
    [ObservableProperty] private ObservableCollection<ImportAnomaly> _anomalies = [];

    public ImportReviewPageViewModel(IAppStore store, ResumeDocumentMerger merger)
    {
        _store = store;
        _merger = merger;
    }

    public void LoadPreview(ResumeDocument target, ImportPreview preview)
    {
        _target = target;
        _preview = preview;

        SourceName = preview.SourceName;
        NewItemCount = preview.TotalNewItems;
        MergeItemCount = preview.TotalMergeItems;
        AnomalyCount = preview.TotalAnomalies;

        PersonalInfoChanges = new(preview.PersonalInfoChanges);
        NewExperience = new(preview.NewExperience);
        MergedExperience = new(preview.MergedExperience);
        NewSkills = new(preview.NewSkills);
        UpdatedSkills = new(preview.UpdatedSkills);
        NewEducation = new(preview.NewEducation);
        NewProjects = new(preview.NewProjects);
        Anomalies = new(preview.Anomalies);
    }

    [RelayCommand]
    private async Task ApplySelected()
    {
        if (_target == null || _preview == null) return;
        _merger.ApplyPreview(_target, _preview);
        await _store.MutateAsync(s => s.AddOrReplaceResume(_target, select: true));
        OnApplied?.Invoke();
    }

    [RelayCommand]
    private async Task ApplyAll()
    {
        if (_target == null || _preview == null) return;
        // Accept everything
        foreach (var c in _preview.PersonalInfoChanges) c.IsAccepted = true;
        foreach (var e in _preview.NewExperience) e.IsAccepted = true;
        foreach (var e in _preview.MergedExperience) e.IsAccepted = true;
        foreach (var s in _preview.NewSkills) s.IsAccepted = true;
        foreach (var s in _preview.UpdatedSkills) s.IsAccepted = true;
        foreach (var e in _preview.NewEducation) e.IsAccepted = true;
        foreach (var p in _preview.NewProjects) p.IsAccepted = true;

        _merger.ApplyPreview(_target, _preview);
        await _store.MutateAsync(s => s.AddOrReplaceResume(_target, select: true));
        OnApplied?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancelled?.Invoke();
    }
}
