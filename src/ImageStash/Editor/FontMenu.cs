using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Seams;
using Thickness = Avalonia.Thickness;

namespace ImageStash.Editor;

/// <summary>
/// The font menu the Text tool's font button opens (F8): Impact, Arial Black, Segoe UI, Comic Sans MS, Georgia,
/// Consolas, Bahnschrift, Segoe Script, Ink Free and Verdana, in that order, each in its own face and whether or not this
/// PC has it, the selected layer's checked; the selected layer's font after them, checked, when it is none of the ten;
/// then All fonts…, which lists every family the Fonts seam names on this PC (F13), each in its face, the selected
/// layer's checked, in the editor's pane, scrolling, inside the stash window (F4), and stays open as fonts are chosen
/// from it, as the colour picker does, so one after another can be seen on the picture.
/// </summary>
internal static class FontMenu
{
    public const string AllFonts = "All fonts…";

    /// <summary>What a screen reader calls the list All fonts… shows.</summary>
    public const string AllFontsList = "All fonts";

    /// <summary>F8's ten, in its order; a caption reopened on a PC without one still finds it here.</summary>
    public static readonly IReadOnlyList<string> Listed = ["Impact", "Arial Black", "Segoe UI", "Comic Sans MS", "Georgia", "Consolas", "Bahnschrift", "Segoe Script", "Ink Free", "Verdana"];

    private const double ListHeight = 420, PaneRoom = 48;

    /// <summary>
    /// Fills <paramref name="menu"/>, one of the editor's, with the ten, <paramref name="current"/> when it is none of them,
    /// and All fonts…, and opens it on <paramref name="anchor"/>.
    /// </summary>
    public static void Open(ContextMenu menu, Control anchor, string? current, Action<string> chosen, Action allFonts)
    {
        // A font chosen from All fonts… shows checked in a place of its own, so the menu always says which is chosen.
        foreach (var family in current is not null && !Listed.Contains(current) ? Listed.Append(current) : Listed)
        {
            var item = new MenuItem { Header = family, ToggleType = MenuItemToggleType.Radio, GroupName = "Font", IsChecked = family == current };
            FontFace.Of(family).ApplyTo(item);
            item.Click += (_, _) => chosen(family);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = AllFonts };
        all.Click += (_, _) => allFonts();
        menu.Items.Add(all);
        menu.Open(anchor);
    }

    /// <summary>
    /// Every family <paramref name="fonts"/> names, as the seam orders them, each a choice in its face that chooses it,
    /// checked while it is <paramref name="current"/>'s answer (<see cref="Mark"/> checks again what shows), in a list that
    /// scrolls within <paramref name="room"/>, the height the editor's pane gives it, or less where the pane fits it to a
    /// shorter row; only the entries in view are made.
    /// </summary>
    public static Control AllFontsIn(IFonts fonts, Func<string?> current, double room, Action<string> chosen)
    {
        var families = fonts.Families();
        var list = new ItemsControl
        {
            ItemsSource = families,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<string>((family, _) => Entry(family, family == current(), chosen)),
        };
        var scroller = new ScrollViewer
        {
            Content = list,
            Width = EditorPane.ContentWidth,
            MaxHeight = Math.Max(120, Math.Min(ListHeight, room - PaneRoom)),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(scroller, AllFontsList);
        return scroller;
    }

    /// <summary>Checks, among the entries <paramref name="allFonts"/> (made by <see cref="AllFontsIn"/>) shows, the one for <paramref name="current"/>, and no other.</summary>
    public static void Mark(Control allFonts, string? current)
    {
        foreach (var entry in allFonts.GetVisualDescendants().OfType<ToggleButton>())
        {
            entry.IsChecked = AutomationProperties.GetName(entry) is { } family && family == current;
        }
    }

    private static ToggleButton Entry(string? family, bool current, Action<string> chosen)
    {
        // The editor's choice, checked as a preset or a swatch is, so the selected layer's family stands out; flat, as a
        // menu's entries are, while it is not.
        var entry = EditorButtons.Choice(family ?? "", family ?? "", () =>
        {
            if (family is not null)
            {
                chosen(family);
            }
        });
        entry.IsChecked = current;
        entry.Height = double.NaN;
        entry.HorizontalAlignment = HorizontalAlignment.Stretch;
        entry.HorizontalContentAlignment = HorizontalAlignment.Left;
        entry.BorderThickness = default;
        entry.Padding = new Thickness(10, 6);
        entry.Background = Brushes.Transparent;
        if (family is not null)
        {
            FontFace.Of(family).ApplyTo(entry);
        }

        return entry;
    }
}
