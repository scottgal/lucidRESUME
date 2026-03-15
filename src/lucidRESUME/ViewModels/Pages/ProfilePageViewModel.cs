using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Persistence;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ProfilePageViewModel : ViewModelBase
{
    private readonly IAppStore _store;

    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _preferredLocation = "";
    [ObservableProperty] private bool _openToRemote = true;
    [ObservableProperty] private string _careerGoals = "";
    [ObservableProperty] private string _skillsToEmphasise = "";
    [ObservableProperty] private string _skillsToAvoid = "";
    [ObservableProperty] private string _blockedCompanies = "";
    [ObservableProperty] private bool _isSaved;

    public ProfilePageViewModel(IAppStore store)
    {
        _store = store;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var state = await _store.LoadAsync();
            var profile = state.Profile;

            FullName = profile.DisplayName ?? "";
            Email = "";  // UserProfile doesn't have Email; map to AdditionalContext or leave blank
            PreferredLocation = profile.Preferences.PreferredLocations.FirstOrDefault() ?? "";
            OpenToRemote = profile.Preferences.OpenToRemote;
            CareerGoals = profile.CareerGoals ?? "";
            SkillsToEmphasise = string.Join(", ", profile.SkillsToEmphasise.Select(s => s.SkillName));
            SkillsToAvoid = string.Join(", ", profile.SkillsToAvoid.Select(s => s.SkillName));
            BlockedCompanies = string.Join(", ", profile.BlockedCompanies);
        }
        catch
        {
            // Leave defaults if load fails
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var state = await _store.LoadAsync();
            var profile = state.Profile;

            profile.DisplayName = string.IsNullOrWhiteSpace(FullName) ? null : FullName;
            profile.CareerGoals = string.IsNullOrWhiteSpace(CareerGoals) ? null : CareerGoals;
            profile.Preferences.OpenToRemote = OpenToRemote;

            profile.Preferences.PreferredLocations.Clear();
            if (!string.IsNullOrWhiteSpace(PreferredLocation))
                profile.Preferences.PreferredLocations.Add(PreferredLocation);

            profile.SkillsToEmphasise = ParseCommaSeparated(SkillsToEmphasise)
                .Select(s => new SkillPreference { SkillName = s })
                .ToList();

            profile.SkillsToAvoid = ParseCommaSeparated(SkillsToAvoid)
                .Select(s => new SkillPreference { SkillName = s })
                .ToList();

            profile.BlockedCompanies = ParseCommaSeparated(BlockedCompanies).ToList();

            await _store.SaveAsync(state);

            IsSaved = true;
            await Task.Delay(2000);
            IsSaved = false;
        }
        catch
        {
            // Silently ignore save errors for now
        }
    }

    private static IEnumerable<string> ParseCommaSeparated(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Where(s => !string.IsNullOrWhiteSpace(s));
}
