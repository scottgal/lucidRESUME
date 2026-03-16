using Avalonia;
using Avalonia.Controls;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public partial class ResumePage : UserControl
{
    public ResumePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildOpenerFlyout();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ResumePageViewModel vm)
            vm.TopLevel = TopLevel.GetTopLevel(this);
        RebuildOpenerFlyout();
    }

    private void RebuildOpenerFlyout()
    {
        if (DataContext is not ResumePageViewModel vm) return;

        var flyout = OpenInButton?.Flyout as MenuFlyout;
        if (flyout == null) return;

        flyout.Items.Clear();
        foreach (var item in vm.OpenerItems)
            flyout.Items.Add(new MenuItem { Header = item.Name, Command = item.Command });
    }
}
