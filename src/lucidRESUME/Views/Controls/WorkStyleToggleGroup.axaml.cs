using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace lucidRESUME.Views.Controls;

/// <summary>
/// Three checkboxes for work style preferences (Remote / Hybrid / Onsite)
/// plus a "Remote Only" quick-set button.
/// The three bool properties are exposed as Avalonia DirectProperties so that
/// parent ViewModels can bind to them via TwoWay bindings.
/// </summary>
public partial class WorkStyleToggleGroup : UserControl
{
    private bool _suppressSync; // prevents feedback loops when syncing checkbox ↔ property

    // ── DirectProperty: OpenToRemote ────────────────────────────────────────

    private bool _openToRemote;

    public static readonly DirectProperty<WorkStyleToggleGroup, bool> OpenToRemoteProperty =
        AvaloniaProperty.RegisterDirect<WorkStyleToggleGroup, bool>(
            nameof(OpenToRemote),
            o => o.OpenToRemote,
            (o, v) => o.OpenToRemote = v,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool OpenToRemote
    {
        get => _openToRemote;
        set
        {
            SetAndRaise(OpenToRemoteProperty, ref _openToRemote, value);
            SyncCheckbox(RemoteCheckBox, value);
        }
    }

    // ── DirectProperty: OpenToHybrid ────────────────────────────────────────

    private bool _openToHybrid;

    public static readonly DirectProperty<WorkStyleToggleGroup, bool> OpenToHybridProperty =
        AvaloniaProperty.RegisterDirect<WorkStyleToggleGroup, bool>(
            nameof(OpenToHybrid),
            o => o.OpenToHybrid,
            (o, v) => o.OpenToHybrid = v,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool OpenToHybrid
    {
        get => _openToHybrid;
        set
        {
            SetAndRaise(OpenToHybridProperty, ref _openToHybrid, value);
            SyncCheckbox(HybridCheckBox, value);
        }
    }

    // ── DirectProperty: OpenToOnsite ────────────────────────────────────────

    private bool _openToOnsite;

    public static readonly DirectProperty<WorkStyleToggleGroup, bool> OpenToOnsiteProperty =
        AvaloniaProperty.RegisterDirect<WorkStyleToggleGroup, bool>(
            nameof(OpenToOnsite),
            o => o.OpenToOnsite,
            (o, v) => o.OpenToOnsite = v,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool OpenToOnsite
    {
        get => _openToOnsite;
        set
        {
            SetAndRaise(OpenToOnsiteProperty, ref _openToOnsite, value);
            SyncCheckbox(OnsiteCheckBox, value);
        }
    }

    // ── Constructor ─────────────────────────────────────────────────────────

    public WorkStyleToggleGroup()
    {
        InitializeComponent();

        RemoteCheckBox.IsCheckedChanged += (_, _) => OnCheckboxChanged(RemoteCheckBox, ref _openToRemote, OpenToRemoteProperty);
        HybridCheckBox.IsCheckedChanged += (_, _) => OnCheckboxChanged(HybridCheckBox, ref _openToHybrid, OpenToHybridProperty);
        OnsiteCheckBox.IsCheckedChanged += (_, _) => OnCheckboxChanged(OnsiteCheckBox, ref _openToOnsite, OpenToOnsiteProperty);

        RemoteOnlyButton.Click += OnRemoteOnlyClicked;
    }

    // ── Sync helpers ─────────────────────────────────────────────────────────

    private void SyncCheckbox(CheckBox? box, bool value)
    {
        if (box is null || _suppressSync) return;
        _suppressSync = true;
        try { box.IsChecked = value; }
        finally { _suppressSync = false; }
    }

    private void OnCheckboxChanged(CheckBox box, ref bool field,
        DirectProperty<WorkStyleToggleGroup, bool> property)
    {
        if (_suppressSync) return;
        var newValue = box.IsChecked == true;
        SetAndRaise(property, ref field, newValue);
    }

    // ── Button handler ───────────────────────────────────────────────────────

    private void OnRemoteOnlyClicked(object? sender, RoutedEventArgs e)
    {
        OpenToRemote = true;
        OpenToHybrid = false;
        OpenToOnsite = false;
    }
}
