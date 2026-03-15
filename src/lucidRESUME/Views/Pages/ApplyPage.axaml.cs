using Avalonia;
using Avalonia.Controls;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public partial class ApplyPage : UserControl
{
    public ApplyPage() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ApplyPageViewModel vm)
            vm.TopLevel = TopLevel.GetTopLevel(this);
    }
}
