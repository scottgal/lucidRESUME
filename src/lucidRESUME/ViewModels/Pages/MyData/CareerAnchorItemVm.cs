using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.Core.Persistence;
using System.Globalization;

namespace lucidRESUME.ViewModels.Pages.MyData;

public sealed partial class CareerAnchorItemVm : ObservableObject
{
    private readonly Action<WorkExperience, bool> _changed;
    private readonly Action<CareerAnchorItemVm> _edited;

    public CareerAnchorItemVm(
        WorkExperience experience,
        Action<WorkExperience, bool> changed,
        Action<CareerAnchorItemVm> edited)
    {
        Experience = experience;
        _changed = changed;
        _edited = edited;
        MatchRoleKey = AppState.CareerAnchorRoleKey(experience);
        _isCareerAnchor = experience.IsCareerAnchor;
        CopyFromExperience();
    }

    public WorkExperience Experience { get; }
    public string MatchRoleKey { get; }

    [ObservableProperty] private bool _isCareerAnchor;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _location = "";
    [ObservableProperty] private string _startDate = "";
    [ObservableProperty] private string _endDate = "";
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private string? _validationError;

    partial void OnIsCareerAnchorChanged(bool value) => _changed(Experience, value);

    [RelayCommand]
    private void Edit()
    {
        CopyFromExperience();
        IsEditing = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        CopyFromExperience();
        IsEditing = false;
    }

    [RelayCommand]
    private void Save()
    {
        if (!TryParseMonth(StartDate, required: true, out var start) ||
            !TryParseMonth(EndDate, required: !IsCurrent, out var end))
        {
            ValidationError = "Use YYYY-MM dates; an end date is required unless the role is current.";
            return;
        }
        if (!IsCurrent && start.HasValue && end.HasValue && end < start)
        {
            ValidationError = "The end date cannot precede the start date.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Company) && string.IsNullOrWhiteSpace(Title))
        {
            ValidationError = "A role needs an employer or title.";
            return;
        }

        Experience.Company = NullIfWhiteSpace(Company);
        Experience.Title = NullIfWhiteSpace(Title);
        Experience.Location = NullIfWhiteSpace(Location);
        Experience.StartDate = start;
        Experience.EndDate = IsCurrent ? null : end;
        Experience.IsCurrent = IsCurrent;
        ValidationError = null;
        IsEditing = false;
        _edited(this);
    }

    private void CopyFromExperience()
    {
        Company = Experience.Company ?? "";
        Title = Experience.Title ?? "";
        Location = Experience.Location ?? "";
        StartDate = Experience.StartDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "";
        EndDate = Experience.EndDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "";
        IsCurrent = Experience.IsCurrent;
        ValidationError = null;
    }

    private static bool TryParseMonth(string value, bool required, out DateOnly? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return !required;
        if (!DateOnly.TryParseExact(value.Trim(), ["yyyy-MM", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        result = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
