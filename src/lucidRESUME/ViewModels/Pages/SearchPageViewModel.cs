using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.JobSearch;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class SearchPageViewModel : ViewModelBase
{
    private readonly JobSearchService _search;

    [ObservableProperty] private string _keywords = "";
    [ObservableProperty] private string _location = "";
    [ObservableProperty] private List<JobDescription> _results = [];
    [ObservableProperty] private bool _isSearching;

    public SearchPageViewModel(JobSearchService search) => _search = search;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Keywords)) return;
        IsSearching = true;
        Results = [];
        try
        {
            var query = new JobSearchQuery(Keywords, string.IsNullOrWhiteSpace(Location) ? null : Location);
            var found = await _search.SearchAllAsync(query);
            Results = [.. found];
        }
        finally { IsSearching = false; }
    }
}
