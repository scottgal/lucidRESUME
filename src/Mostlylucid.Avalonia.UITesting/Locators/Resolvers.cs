using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Mostlylucid.Avalonia.UITesting.Locators;

// ============================================================================
// Atom locators — find controls directly from a property
// ============================================================================

/// <summary>Match controls whose <see cref="StyledElement.Name"/> equals a value.</summary>
public sealed class NameLocator : Locator
{
    public string Name { get; }
    public NameLocator(string name) { Name = name; Source = $"name={name}"; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        foreach (var c in Walk(root))
            if (c.Name == Name) yield return c;
    }

    /// <summary>
    /// Walk every control reachable from <paramref name="root"/>, regardless of
    /// XAML namescope, template materialization, or container realization.
    ///
    /// Avalonia's namescopes are per-XAML-file, so a UserControl's named children
    /// are invisible to the parent Window's <c>FindControl&lt;T&gt;(name)</c> —
    /// which is why locators can't rely on namescope lookup. This walker traverses
    /// the union of:
    ///
    /// <list type="bullet">
    ///   <item>The visual tree (rendered children, including templated parts)</item>
    ///   <item>The logical tree (XAML structure even before measure/arrange)</item>
    ///   <item>Realized <see cref="ItemsControl"/> containers</item>
    ///   <item>Templated parts produced by calling <c>ApplyTemplate</c></item>
    ///   <item><see cref="Popup.Child"/> when popups are present (closed or open)</item>
    /// </list>
    ///
    /// A reference-equality visited set prevents cycles when the visual and
    /// logical trees overlap.
    /// </summary>
    internal static IEnumerable<Control> Walk(Control root)
    {
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Control>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;
            yield return current;

            // Force template materialization so templated parts (children inside
            // ControlTemplates) become reachable. This is a no-op if the template
            // has already been applied.
            if (current is TemplatedControl tc)
            {
                try { tc.ApplyTemplate(); } catch { /* template not yet ready */ }
            }

            // Visual tree children — covers everything actually rendered
            foreach (var v in current.GetVisualChildren())
            {
                if (v is Control c && !visited.Contains(c))
                    stack.Push(c);
            }

            // Logical tree children — catches controls registered in XAML before
            // they enter the visual tree (e.g. items in a not-yet-arranged panel)
            foreach (var l in current.GetLogicalChildren())
            {
                if (l is Control c && !visited.Contains(c))
                    stack.Push(c);
            }

            // ItemsControl: enter realized item containers explicitly. The visual
            // tree walk picks these up too, but we add them defensively in case
            // a virtualizing presenter prevents the visual descent.
            if (current is ItemsControl ic)
            {
                foreach (var i in ic.GetRealizedContainers())
                {
                    if (i is Control c && !visited.Contains(c))
                        stack.Push(c);
                }
            }

            // Popups: their Child is reachable via the logical tree only when
            // open. Pull it in directly so closed popups still resolve.
            if (current is Popup popup && popup.Child is Control popupChild && !visited.Contains(popupChild))
            {
                stack.Push(popupChild);
            }
        }
    }
}

/// <summary>Match controls assignable to a runtime type by short name (e.g. <c>Button</c>).</summary>
public sealed class TypeLocator : Locator
{
    public string TypeName { get; }
    public TypeLocator(string typeName) { TypeName = typeName; Source = $"type={typeName}"; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        foreach (var c in NameLocator.Walk(root))
        {
            for (var t = c.GetType(); t != null; t = t.BaseType)
            {
                if (t.Name == TypeName) { yield return c; break; }
            }
        }
    }
}

/// <summary>
/// Match controls by displayed text. Looks at <see cref="TextBlock.Text"/>,
/// <see cref="TextBox.Text"/>, <see cref="ContentControl.Content"/> when string,
/// and <see cref="HeaderedContentControl.Header"/> when string. Substring match
/// by default; pass <c>exact: true</c> for exact equality.
/// </summary>
public sealed class TextLocator : Locator
{
    public string Text { get; }
    public bool Exact { get; }

    public TextLocator(string text, bool exact = false)
    {
        Text = text;
        Exact = exact;
        Source = exact ? $"text='{text}'" : $"text={text}";
    }

    public override IEnumerable<Control> Resolve(Control root)
    {
        foreach (var c in NameLocator.Walk(root))
        {
            var displayed = GetDisplayedText(c);
            if (displayed == null) continue;
            var match = Exact ? displayed == Text : displayed.Contains(Text, StringComparison.Ordinal);
            if (match) yield return c;
        }
    }

    internal static string? GetDisplayedText(Control c) => c switch
    {
        TextBlock tb => tb.Text,
        TextBox tx => tx.Text,
        HeaderedContentControl hcc when hcc.Header is string hs => hs,
        HeaderedItemsControl hic when hic.Header is string hs => hs,
        ContentControl cc when cc.Content is string s => s,
        _ => null
    };
}

/// <summary>
/// Match controls by <see cref="AutomationProperties.AutomationId"/>. This is the
/// recommended stable selector for tests — set <c>AutomationProperties.AutomationId</c>
/// in XAML and locate by <c>testid=...</c>.
/// </summary>
public sealed class TestIdLocator : Locator
{
    public string TestId { get; }
    public TestIdLocator(string testId) { TestId = testId; Source = $"testid={testId}"; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        foreach (var c in NameLocator.Walk(root))
            if (AutomationProperties.GetAutomationId(c) == TestId) yield return c;
    }
}

/// <summary>
/// Match controls by automation peer role (<c>button</c>, <c>checkbox</c>, etc.),
/// optionally also matching the accessible name from <see cref="AutomationProperties.Name"/>
/// or the peer's name.
/// </summary>
public sealed class RoleLocator : Locator
{
    public string Role { get; }
    public string? AccessibleName { get; }

    public RoleLocator(string role, string? accessibleName = null)
    {
        Role = role;
        AccessibleName = accessibleName;
        Source = accessibleName is null ? $"role={role}" : $"role={role} name={accessibleName}";
    }

    public override IEnumerable<Control> Resolve(Control root)
    {
        foreach (var c in NameLocator.Walk(root))
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(c);
            if (peer is null) continue;

            var peerRole = peer.GetAutomationControlType().ToString();
            if (!string.Equals(peerRole, Role, StringComparison.OrdinalIgnoreCase)) continue;

            if (AccessibleName != null)
            {
                var name = AutomationProperties.GetName(c) ?? peer.GetName();
                if (!string.Equals(name, AccessibleName, StringComparison.Ordinal)) continue;
            }

            yield return c;
        }
    }
}

/// <summary>
/// Match the input control associated with a label by
/// <see cref="AutomationProperties.LabeledBy"/>. Falls back to "the next focusable
/// control after the labelled-by candidate" if no inputs declare LabeledBy.
/// </summary>
public sealed class LabelLocator : Locator
{
    public string LabelText { get; }
    public LabelLocator(string label) { LabelText = label; Source = $"label={label}"; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        // Phase 1: find the label control by displayed text
        var labelCandidates = new TextLocator(LabelText, exact: false)
            .Resolve(root)
            .Where(c => c is TextBlock or Label)
            .ToList();

        if (labelCandidates.Count == 0) yield break;

        // Phase 2: find inputs whose LabeledBy points at any of those candidates
        foreach (var c in NameLocator.Walk(root))
        {
            var labeledBy = AutomationProperties.GetLabeledBy(c);
            if (labeledBy != null && labelCandidates.Contains(labeledBy))
                yield return c;
        }

        // Phase 3 fallback: pick the next focusable input after the first label
        // candidate in tab order, when nothing declared LabeledBy.
        var anyDeclared = false;
        foreach (var c in NameLocator.Walk(root))
        {
            if (AutomationProperties.GetLabeledBy(c) != null) { anyDeclared = true; break; }
        }
        if (anyDeclared) yield break;

        var allControls = NameLocator.Walk(root).ToList();
        var labelIdx = allControls.IndexOf(labelCandidates[0]);
        if (labelIdx < 0) yield break;
        for (var i = labelIdx + 1; i < allControls.Count; i++)
        {
            var c = allControls[i];
            if (c is TextBox or ComboBox or CheckBox or RadioButton or NumericUpDown or DatePicker or TimePicker)
            {
                yield return c;
                yield break;
            }
        }
    }
}

// ============================================================================
// Composite locators — wrap another locator
// ============================================================================

/// <summary>Pick the Nth match (zero-based) of the inner locator.</summary>
public sealed class IndexLocator : Locator
{
    public Locator Inner { get; }
    public int Index { get; }

    public IndexLocator(Locator inner, int index) { Inner = inner; Index = index; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        var matches = Inner.Resolve(root).ToList();
        if (Index >= 0 && Index < matches.Count) yield return matches[Index];
    }
}

/// <summary>Pick the last match of the inner locator.</summary>
public sealed class LastLocator : Locator
{
    public Locator Inner { get; }
    public LastLocator(Locator inner) { Inner = inner; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        var last = Inner.Resolve(root).LastOrDefault();
        if (last != null) yield return last;
    }
}

/// <summary>Filter inner matches by an arbitrary predicate.</summary>
public sealed class FilterLocator : Locator
{
    public Locator Inner { get; }
    public Func<Control, bool> Predicate { get; }
    public string? Description { get; }

    public FilterLocator(Locator inner, Func<Control, bool> predicate, string? description = null)
    {
        Inner = inner; Predicate = predicate; Description = description;
    }

    public override IEnumerable<Control> Resolve(Control root)
        => Inner.Resolve(root).Where(Predicate);
}

/// <summary>
/// Filter inner matches to those whose visual subtree contains the given text.
/// Equivalent to Playwright's <c>:has-text(...)</c> pseudo.
/// </summary>
public sealed class HasTextLocator : Locator
{
    public Locator Inner { get; }
    public string Text { get; }
    public bool Exact { get; }

    public HasTextLocator(Locator inner, string text, bool exact)
    {
        Inner = inner; Text = text; Exact = exact;
    }

    public override IEnumerable<Control> Resolve(Control root)
    {
        foreach (var match in Inner.Resolve(root))
        {
            foreach (var c in NameLocator.Walk(match))
            {
                var text = TextLocator.GetDisplayedText(c);
                if (text == null) continue;
                var hit = Exact ? text == Text : text.Contains(Text, StringComparison.Ordinal);
                if (hit) { yield return match; break; }
            }
        }
    }
}

/// <summary>
/// Match only inner controls that are visual descendants of any control matched
/// by the container locator.
/// </summary>
public sealed class InsideLocator : Locator
{
    public Locator Inner { get; }
    public Locator Container { get; }

    public InsideLocator(Locator inner, Locator container) { Inner = inner; Container = container; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        var containers = Container.Resolve(root).ToList();
        if (containers.Count == 0) yield break;

        foreach (var match in Inner.Resolve(root))
        {
            for (var ancestor = match.GetVisualParent(); ancestor != null; ancestor = ancestor.GetVisualParent())
            {
                if (ancestor is Control ctl && containers.Contains(ctl))
                {
                    yield return match;
                    break;
                }
            }
        }
    }
}

/// <summary>
/// Reorder inner matches by spatial proximity to the centre of the first anchor
/// match (closest first).
/// </summary>
public sealed class NearLocator : Locator
{
    public Locator Inner { get; }
    public Locator Anchor { get; }

    public NearLocator(Locator inner, Locator anchor) { Inner = inner; Anchor = anchor; }

    public override IEnumerable<Control> Resolve(Control root)
    {
        var anchor = Anchor.Resolve(root).FirstOrDefault();
        if (anchor == null) return Inner.Resolve(root);

        var anchorCentre = CentreOf(anchor, root);
        return Inner.Resolve(root)
            .Select(c => (control: c, distance: Distance(CentreOf(c, root), anchorCentre)))
            .OrderBy(t => t.distance)
            .Select(t => t.control);
    }

    private static Point CentreOf(Control c, Control reference)
    {
        var topLeft = c.TranslatePoint(new Point(0, 0), reference) ?? new Point(0, 0);
        return new Point(topLeft.X + c.Bounds.Width / 2, topLeft.Y + c.Bounds.Height / 2);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
