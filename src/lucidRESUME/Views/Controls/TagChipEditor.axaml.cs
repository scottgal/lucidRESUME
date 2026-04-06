using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using lucidRESUME.Core.Models.Profile;

namespace lucidRESUME.Views.Controls;

/// <summary>
/// Displays a list of <see cref="TagItem"/> values as chip badges with an inline add TextBox.
/// Chips are rendered programmatically into a <see cref="WrapPanel"/> alongside the add TextBox.
/// </summary>
public partial class TagChipEditor : UserControl
{
    // Tracks reason-expansion state per TagItem (keyed by reference)
    private readonly Dictionary<TagItem, bool> _reasonVisible = new(ReferenceEqualityComparer.Instance);

    // ── DirectProperty: Items ────────────────────────────────────────────────

    private ObservableCollection<TagItem> _items = [];

    public static readonly DirectProperty<TagChipEditor, ObservableCollection<TagItem>> ItemsProperty =
        AvaloniaProperty.RegisterDirect<TagChipEditor, ObservableCollection<TagItem>>(
            nameof(Items),
            o => o.Items,
            (o, v) => o.Items = v);

    public ObservableCollection<TagItem> Items
    {
        get => _items;
        set
        {
            if (ReferenceEquals(_items, value)) return;
            // Unsubscribe from old collection
            _items.CollectionChanged -= OnItemsCollectionChanged;
            SetAndRaise(ItemsProperty, ref _items, value);
            _items.CollectionChanged += OnItemsCollectionChanged;
            RebuildChips();
        }
    }

    // ── DirectProperty: Placeholder ─────────────────────────────────────────

    private string _placeholder = "Add...";

    public static readonly DirectProperty<TagChipEditor, string> PlaceholderProperty =
        AvaloniaProperty.RegisterDirect<TagChipEditor, string>(
            nameof(Placeholder),
            o => o.Placeholder,
            (o, v) => o.Placeholder = v);

    public string Placeholder
    {
        get => _placeholder;
        set
        {
            SetAndRaise(PlaceholderProperty, ref _placeholder, value);
            AddTagBox.Watermark = value;
        }
    }

    // ── DirectProperty: AccentColor ─────────────────────────────────────────

    private IBrush _accentColor = Brushes.CornflowerBlue; // overridden by theme at runtime

    public static readonly DirectProperty<TagChipEditor, IBrush> AccentColorProperty =
        AvaloniaProperty.RegisterDirect<TagChipEditor, IBrush>(
            nameof(AccentColor),
            o => o.AccentColor,
            (o, v) => o.AccentColor = v);

    public IBrush AccentColor
    {
        get => _accentColor;
        set
        {
            SetAndRaise(AccentColorProperty, ref _accentColor, value);
            RebuildChips();
        }
    }

    // ── DirectProperty: AllowReasons ────────────────────────────────────────

    private bool _allowReasons;

    public static readonly DirectProperty<TagChipEditor, bool> AllowReasonsProperty =
        AvaloniaProperty.RegisterDirect<TagChipEditor, bool>(
            nameof(AllowReasons),
            o => o.AllowReasons,
            (o, v) => o.AllowReasons = v);

    public bool AllowReasons
    {
        get => _allowReasons;
        set
        {
            SetAndRaise(AllowReasonsProperty, ref _allowReasons, value);
            RebuildChips();
        }
    }

    // ── Constructor ─────────────────────────────────────────────────────────

    public TagChipEditor()
    {
        InitializeComponent();

        _items.CollectionChanged += OnItemsCollectionChanged;

        AddTagBox.Watermark = _placeholder;
        AddTagBox.KeyDown += OnAddTagBoxKeyDown;
        AddTagBox.TextChanged += OnAddTagBoxTextChanged;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Trims, deduplicates, and adds a tag to <see cref="Items"/>.</summary>
    private void AddTag(string value)
    {
        var trimmed = value.Trim().Trim(',').Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;
        if (_items.Any(t => string.Equals(t.Value, trimmed, StringComparison.OrdinalIgnoreCase))) return;
        _items.Add(new TagItem { Value = trimmed });
    }

    /// <summary>Removes a tag from <see cref="Items"/>.</summary>
    private void RemoveTag(TagItem item)
    {
        _items.Remove(item);
        _reasonVisible.Remove(item);
    }

    // ── Collection change handling ───────────────────────────────────────────

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ChipWrapPanel is null) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) break;
                // Insert each new chip before AddTagBox (at end of chips, not end of panel)
                int insertAt = ChipWrapPanel.Children.Count - 1; // position before AddTagBox
                foreach (TagItem tag in e.NewItems)
                {
                    var chipHost = BuildChipHost(tag); // Tag property set inside BuildChipHost
                    ChipWrapPanel.Children.Insert(insertAt++, chipHost);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) break;
                foreach (TagItem tag in e.OldItems)
                {
                    var toRemove = ChipWrapPanel.Children
                        .FirstOrDefault(c => c is StackPanel sp && ReferenceEquals(sp.Tag, tag));
                    if (toRemove is not null)
                        ChipWrapPanel.Children.Remove(toRemove);
                }
                break;

            default:
                // Reset and any other actions fall back to a full rebuild
                RebuildChips();
                break;
        }
    }

    // ── Chip building ────────────────────────────────────────────────────────

    /// <summary>
    /// Clears and re-renders all chip controls into <see cref="ChipWrapPanel"/>,
    /// keeping the <see cref="AddTagBox"/> at the end.
    /// </summary>
    private void RebuildChips()
    {
        if (ChipWrapPanel is null) return;

        // Remove all children except AddTagBox
        var toRemove = ChipWrapPanel.Children
            .Where(c => c != AddTagBox)
            .ToList();
        foreach (var child in toRemove)
            ChipWrapPanel.Children.Remove(child);

        // Re-insert chips before AddTagBox
        int insertIndex = 0;
        foreach (var tag in _items)
        {
            var chipHost = BuildChipHost(tag);
            ChipWrapPanel.Children.Insert(insertIndex++, chipHost);
        }

        // Ensure AddTagBox is last
        if (ChipWrapPanel.Children.Contains(AddTagBox))
        {
            ChipWrapPanel.Children.Remove(AddTagBox);
        }
        ChipWrapPanel.Children.Add(AddTagBox);
    }

    private Control BuildChipHost(TagItem tag)
    {
        // Outer vertical stack (chip + optional reason box).
        // Tag stores the TagItem reference for surgical removal in OnItemsCollectionChanged.
        var host = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 2, 4, 2),
            Tag = tag
        };

        // Chip border
        var chipBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 3, 4, 3),
            BorderThickness = new Thickness(1),
            BorderBrush = _accentColor,
            Background = BuildChipBackground(),
        };

        var chipInner = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        var label = new TextBlock
        {
            Text = tag.Value,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("LrPrimaryTextBrush"));

        var subtleBrush = this.FindResource("LrSubtleTextBrush") as IBrush ?? Brushes.Gray;
        var redBrush = this.FindResource("LrAccentRedBrush") as IBrush ?? Brushes.Red;

        var deleteBtn = new Button
        {
            Content = "×",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(2, 0),
            Foreground = subtleBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Hover: turn delete button red
        deleteBtn.PointerEntered += (_, _) =>
            deleteBtn.Foreground = redBrush;
        deleteBtn.PointerExited += (_, _) =>
            deleteBtn.Foreground = subtleBrush;
        deleteBtn.Click += (_, _) => RemoveTag(tag);

        chipInner.Children.Add(label);
        chipInner.Children.Add(deleteBtn);
        chipBorder.Child = chipInner;
        host.Children.Add(chipBorder);

        // Reason expansion (only wired when AllowReasons=true)
        if (_allowReasons)
        {
            _reasonVisible.TryGetValue(tag, out var isVisible);

            var reasonBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3),
                Margin = new Thickness(0, 2, 0, 0),
                MaxWidth = 200,
                IsVisible = isVisible
            };
            reasonBorder.Bind(Border.BackgroundProperty, reasonBorder.GetResourceObservable("LrCardBgBrush"));
            reasonBorder.Bind(Border.BorderBrushProperty, reasonBorder.GetResourceObservable("LrBorderBrush"));

            var reasonBox = new TextBox
            {
                Text = tag.Reason,
                Watermark = "Reason (optional)",
                FontSize = 11,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 1)
            };
            reasonBox.TextChanged += (_, _) => tag.Reason = reasonBox.Text;
            reasonBorder.Child = reasonBox;
            host.Children.Add(reasonBorder);

            // Clicking the chip toggles the reason box
            chipBorder.PointerPressed += (_, _) =>
            {
                var next = !(_reasonVisible.TryGetValue(tag, out var cur) && cur);
                _reasonVisible[tag] = next;
                reasonBorder.IsVisible = next;
            };
        }

        return host;
    }

    private IBrush BuildChipBackground()
    {
        // 15% opacity tint of accent color
        if (_accentColor is SolidColorBrush solidBrush)
        {
            var c = solidBrush.Color;
            return new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B)); // 38 ≈ 15% of 255
        }
        // Fallback: #313244 (Catppuccin surface1) at ~15% opacity. Avalonia uses #AARRGGBB,
        // so 0x26 (~15% of 255) alpha, R=0x31, G=0x32, B=0x44.
        return new SolidColorBrush(Color.Parse("#26313244"));
    }

    // ── AddTagBox event handlers ─────────────────────────────────────────────

    private void OnAddTagBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            CommitAddBox();
            e.Handled = true;
        }
    }

    private void OnAddTagBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = AddTagBox.Text ?? string.Empty;
        if (text.Contains(','))
        {
            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
                AddTag(part);
            AddTagBox.Text = string.Empty;
        }
    }

    private void CommitAddBox()
    {
        var text = AddTagBox.Text ?? string.Empty;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddTag(part);
        AddTagBox.Text = string.Empty;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        RebuildChips();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_items != null)
            _items.CollectionChanged -= OnItemsCollectionChanged;
    }
}
