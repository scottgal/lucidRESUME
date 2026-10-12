using Avalonia;
using Avalonia.Controls;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public partial class GeneratedResumesPage : UserControl
{
    public GeneratedResumesPage() => InitializeComponent();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is GeneratedResumesPageViewModel vm)
        {
            vm.TopLevel = TopLevel.GetTopLevel(this);
            _ = vm.ReloadAsync();
        }
    }
}
