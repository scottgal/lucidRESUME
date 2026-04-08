using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Models.Skills;

namespace lucidRESUME.ViewModels.Pages.MyData;

public sealed partial class SkillLedgerEntryVm : ObservableObject
{
    private readonly SkillLedgerEntry _entry;
    private readonly Action<SkillLedgerEntryVm> _onDismiss;
    private readonly Action<SkillLedgerEntryVm> _onUndismiss;
    private readonly Action _onChanged;

    public SkillLedgerEntryVm(SkillLedgerEntry entry, Action<SkillLedgerEntryVm> onDismiss, Action<SkillLedgerEntryVm> onUndismiss, Action onChanged)
    {
        _entry = entry;
        _onDismiss = onDismiss;
        _onUndismiss = onUndismiss;
        _onChanged = onChanged;
        Evidence = entry.Evidence.Select(e => new SkillEvidenceVm(e)).ToList();
    }

    public string SkillName => _entry.SkillName;
    public string? Category => _entry.Category;
    public double Strength => _entry.Strength;
    public double CalculatedYears => _entry.CalculatedYears;
    public int EvidenceCount => _entry.Evidence.Count;
    public bool IsCurrent => _entry.IsCurrent;
    public string? FirstSeen => _entry.FirstSeen?.ToString("MMM yyyy");
    public string? LastSeen => _entry.LastSeen?.ToString("MMM yyyy");
    public List<SkillEvidenceVm> Evidence { get; }

    public string Tier => Strength > 0.5 ? "Strong" : Strength > 0.1 ? "Moderate" : "Weak";

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isDismissed;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = "";

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void Dismiss()
    {
        IsDismissed = true;
        _onDismiss(this);
    }

    [RelayCommand]
    private void Undismiss()
    {
        IsDismissed = false;
        _onUndismiss(this);
    }

    [RelayCommand]
    private void StartEdit()
    {
        EditName = SkillName;
        IsEditing = true;
    }

    [RelayCommand]
    private void CommitEdit()
    {
        IsEditing = false;
        if (!string.IsNullOrWhiteSpace(EditName) && EditName != SkillName)
        {
            _entry.SkillName = EditName.Trim();
            OnPropertyChanged(nameof(SkillName));
            _onChanged();
        }
    }

    public SkillLedgerEntry GetEntry() => _entry;
}
