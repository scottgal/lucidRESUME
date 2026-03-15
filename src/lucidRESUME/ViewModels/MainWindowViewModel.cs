using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string _selectedNav = "Resume";

    private readonly Dictionary<string, ViewModelBase> _pages;

    public MainWindowViewModel(
        ResumePageViewModel resumePage,
        JobsPageViewModel jobsPage,
        SearchPageViewModel searchPage,
        ApplyPageViewModel applyPage,
        ProfilePageViewModel profilePage)
    {
        _pages = new Dictionary<string, ViewModelBase>
        {
            ["Resume"] = resumePage,
            ["Jobs"] = jobsPage,
            ["Search"] = searchPage,
            ["Apply"] = applyPage,
            ["Profile"] = profilePage
        };
        _currentPage = resumePage;
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        if (!_pages.TryGetValue(page, out var vm)) return;
        SelectedNav = page;
        CurrentPage = vm;
    }
}
