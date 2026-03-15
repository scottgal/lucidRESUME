using CommunityToolkit.Mvvm.ComponentModel;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ApplyPageViewModel : ViewModelBase
{
    [ObservableProperty] private string? _statusMessage = "Select a job to generate a tailored application.";
}
