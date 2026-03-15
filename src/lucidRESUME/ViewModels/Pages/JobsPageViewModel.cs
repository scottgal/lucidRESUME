using CommunityToolkit.Mvvm.ComponentModel;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class JobsPageViewModel : ViewModelBase
{
    [ObservableProperty] private List<JobDescription> _jobs = [];
    [ObservableProperty] private string? _statusMessage;
}
