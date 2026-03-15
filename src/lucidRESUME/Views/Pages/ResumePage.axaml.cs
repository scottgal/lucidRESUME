using Avalonia;
using Avalonia.Controls;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public partial class ResumePage : UserControl
{
    public ResumePage() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ResumePageViewModel vm)
            vm.TopLevel = TopLevel.GetTopLevel(this);
    }
}
