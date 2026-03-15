using CommunityToolkit.Mvvm.ComponentModel;
using lucidRESUME.Core.Models.Profile;

namespace lucidRESUME.ViewModels.Pages;

public sealed partial class ProfilePageViewModel : ViewModelBase
{
    [ObservableProperty] private UserProfile _profile = new();
}
