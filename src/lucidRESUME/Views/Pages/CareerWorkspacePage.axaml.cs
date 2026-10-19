using Avalonia.Controls;
using Avalonia;
using lucidRESUME.ViewModels.Pages;
namespace lucidRESUME.Views.Pages;
public partial class CareerWorkspacePage : UserControl
{
    public CareerWorkspacePage() => InitializeComponent();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is CareerWorkspacePageViewModel vm) vm.TopLevel = TopLevel.GetTopLevel(this);
    }
}
