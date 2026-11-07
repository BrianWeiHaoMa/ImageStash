using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>One tag in a <see cref="TagPicker"/>, with a check mark while <paramref name="Marked"/>.</summary>
public sealed record TagPickerEntry(string Tag, bool Marked);

/// <summary>
/// The one list of tags a click opens (G4, G17): a popup of the window, never a window of its own (F4), its entries in
/// the order the opener gives, scrolling inside the display's working area when long. Its entries take no focus and a
/// popup's click activates nothing, so the opener keeps focus and the flyout stays (G2). A pick leaves it open for the
/// next; Esc, which its owner asks it about before anything else Esc does, a press outside it, which then acts as it
/// would have, and the window going away close it. It never reads the store and never changes an item or the query:
/// the opener decides what a pick means, and tells it what to show afterwards. Whoever holds one keeps it in its own
/// logical tree, so it takes the window's theme, and keeps the anchor in the visual tree while it is open, since a
/// popup closes with its anchor. It stands as the item menu's Add tag ▸ list does: its scroll bar at its edge, its words
/// clear of the bar, and as wide, from the moment it opens, as its widest name needs.
/// </summary>
public sealed class TagPicker : Popup
{
    // The item menu's lists' measures, which this list takes so the lists read as one kind. Fluent's submenu puts its bar
    // inside a one-pixel edge with no padding (MenuFlyoutPresenterBorderThemeThickness 1, MenuFlyoutPresenterThemePadding
    // 0), and ends an entry's words MenuInputGestureTextMargin 24 plus MenuFlyoutItemThemePaddingNarrow's 11 before the
    // entry's edge, the last sixteen pixels of which the scroller's bar lies over (ScrollBarSize): 19 clear of the bar.
    // The values are held here because the look's one home lets no site read a Fluent key by name; the desktop check
    // compares them with the item menu's shown list. The entry's mark column and left padding are this list's own.
    private const double MarkColumn = 20;
    private const double EntryLeft = 8;
    private const double EntryRight = 24 + 11;
    private const double Edge = 1;
    private const double NarrowestCard = 180;
    private const double WidestCard = 320;

    private readonly ObservableCollection<TagPickerEntry> shown = [];
    private readonly ScrollViewer list;
    private readonly Border card;
    private Action<string> picked = _ => { };

    public TagPicker()
    {
        IsLightDismissEnabled = true;
        OverlayDismissEventPassThrough = true;
        Placement = PlacementMode.BottomEdgeAlignedRight;
        // Made as they scroll into view: every tag of a stash may be thousands (E4).
        list = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new ItemsControl
            {
                ItemsSource = shown,
                ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
                ItemTemplate = new FuncDataTemplate<TagPickerEntry>((entry, _) => EntryButton(entry)),
            },
        };
        // No padding beside the scroller, so its bar stands one hairline from the card's edge, as the submenu's does.
        card = CardLook.Dress(new Border { Child = list, MinWidth = NarrowestCard, MaxWidth = WidestCard, Padding = new Thickness(0, 4) });
        Child = card;
    }

    /// <summary>The entries shown, in order.</summary>
    public IReadOnlyList<TagPickerEntry> Entries => shown;

    /// <summary>
    /// Opens beside <paramref name="anchor"/> showing <paramref name="entries"/>; a click on one calls
    /// <paramref name="onPick"/> with its tag. A press on the anchor is left to it, so it can close the list. Its width is
    /// found here, from the widest of every entry's words, and kept while it stays open.
    /// </summary>
    public void Open(Control anchor, IReadOnlyList<TagPickerEntry> entries, Action<string> onPick)
    {
        picked = onPick;
        PlacementTarget = anchor;
        OverlayInputPassThroughElement = anchor;
        list.MaxHeight = TagListHeight.For(anchor);
        card.Width = WidthFor(entries);
        Show(entries);
        Open();
    }

    /// <summary>
    /// Shows <paramref name="entries"/> instead, the state after a pick: the list keeps its scroll where the tags are the
    /// same ones, only the marks that changed being redrawn.
    /// </summary>
    public void Show(IReadOnlyList<TagPickerEntry> entries)
    {
        if (!entries.Select(entry => entry.Tag).SequenceEqual(shown.Select(entry => entry.Tag)))
        {
            shown.Clear();
            foreach (var entry in entries)
            {
                shown.Add(entry);
            }

            return;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] != shown[i])
            {
                shown[i] = entries[i];
            }
        }
    }

    /// <summary>
    /// The card's width for <paramref name="entries"/>: the widest of their words, measured each (the entries themselves
    /// are made only as they scroll into view, E4, so the panel alone would widen the card as a longer name shows), beside
    /// the entry's fixed measures, between the card's narrowest and widest; a wider name is trimmed.
    /// </summary>
    private double WidthFor(IReadOnlyList<TagPickerEntry> entries)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(list), TextElement.GetFontStyle(list), TextElement.GetFontWeight(list), TextElement.GetFontStretch(list));
        var size = TextElement.GetFontSize(list);
        var widest = 0.0;
        foreach (var entry in entries)
        {
            using var words = new TextLayout(entry.Tag, typeface, size, null);
            widest = Math.Max(widest, words.WidthIncludingTrailingWhitespace);
        }

        return Math.Clamp(Math.Ceiling(Edge + EntryLeft + MarkColumn + widest + EntryRight + Edge), NarrowestCard, WidestCard);
    }

    private Button EntryButton(TagPickerEntry? entry)
    {
        var tag = entry?.Tag ?? "";
        var mark = IconGlyph.Of("", 12);
        mark.Opacity = entry is { Marked: true } ? 1 : 0;
        var words = new TextBlock { Text = tag, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(words, 1);
        var button = new Button
        {
            Content = new Grid { ColumnDefinitions = [new ColumnDefinition(MarkColumn, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star)], Children = { mark, words } },
            // Not focusable, so a click picks without taking focus from the opener (G7, G17).
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Height = 30,
            Padding = new Thickness(EntryLeft, 0, EntryRight, 0),
        };
        AutomationProperties.SetName(button, tag);
        ToolTipRule.ShowWhenTrimmed(words, words, tag);
        button.Click += (_, _) => picked(tag);
        return button;
    }
}
