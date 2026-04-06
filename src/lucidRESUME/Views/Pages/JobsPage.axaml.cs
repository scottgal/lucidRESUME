using Avalonia;
using Avalonia.Controls;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public partial class JobsPage : UserControl
{
    public JobsPage() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is JobsPageViewModel vm)
            vm.LoadSavedCommand.Execute(null);
    }
}
