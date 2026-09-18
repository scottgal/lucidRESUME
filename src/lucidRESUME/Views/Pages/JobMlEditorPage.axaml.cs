using Avalonia;
using Avalonia.Controls;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public partial class JobMlEditorPage : UserControl
{
    private bool _synchronizingSelection;

    public JobMlEditorPage()
    {
        InitializeComponent();
        HumanEditor.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.CaretIndexProperty) HumanEditor_OnCaretChanged();
        };
        JobMlEditor.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.CaretIndexProperty) JobMlEditor_OnCaretChanged();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is JobMlEditorPageViewModel vm)
            vm.TopLevel = TopLevel.GetTopLevel(this);
    }

    private void EvidenceList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection || EvidenceList.SelectedItem is not JobMlEvidenceItem item) return;
        Synchronize(item, selectHuman: true, selectJobMl: true);
    }

    private void HumanEditor_OnCaretChanged()
    {
        if (_synchronizingSelection || DataContext is not JobMlEditorPageViewModel vm) return;
        var item = vm.FindEvidenceAtHumanPosition(HumanEditor.SelectionStart);
        if (item is not null) Synchronize(item, selectHuman: false, selectJobMl: true);
    }

    private void JobMlEditor_OnCaretChanged()
    {
        if (_synchronizingSelection || DataContext is not JobMlEditorPageViewModel vm) return;
        var item = vm.FindEvidenceAtJobMlPosition(JobMlEditor.SelectionStart);
        if (item is not null) Synchronize(item, selectHuman: true, selectJobMl: false);
    }

    private void Synchronize(JobMlEvidenceItem item, bool selectHuman, bool selectJobMl)
    {
        _synchronizingSelection = true;
        try
        {
            if (DataContext is JobMlEditorPageViewModel vm) vm.SelectedEvidence = item;
            EvidenceList.SelectedItem = item;
            if (selectHuman && item.SourceStart >= 0)
            {
                HumanTabs.SelectedIndex = 0;
                SelectRange(HumanEditor, item.SourceStart, item.SourceLength);
            }
            if (selectJobMl && item.JobMlStart >= 0)
                SelectRange(JobMlEditor, item.JobMlStart, item.JobMlLength);
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private static void SelectRange(TextBox textBox, int start, int length)
    {
        var textLength = textBox.Text?.Length ?? 0;
        var safeStart = Math.Clamp(start, 0, textLength);
        textBox.SelectionStart = safeStart;
        textBox.SelectionEnd = Math.Clamp(safeStart + Math.Max(1, length), safeStart, textLength);
    }
}
