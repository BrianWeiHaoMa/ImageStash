using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>One tag in a <see cref="TagPicker"/>, with a check mark while <paramref name="Marked"/>.</summary>
public sealed record TagPickerEntry(string Tag, bool Marked);

/// <summary>
/// The shared tag list (G4, G17): a popup of the window, its entries in the order the opener gives, scrolling inside
/// the display's working area when long. Opening with the pointer leaves focus where it is. Opening from the tag box
/// with Down gives focus to the first entry for arrow navigation and selection with Enter. A pick leaves it open for the
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
    private readonly ItemsControl items;
    private readonly ScrollViewer list;
    private readonly Border card;
    private Action<string> picked = _ => { };
    private Control? keyboardReturn;
    private Button? keyboardEntry;
    private double widestWords;

    /// <summary>The window the list shows in while it is open, whose scaling its width follows; null otherwise.</summary>
    private TopLevel? openIn;

    public TagPicker()
    {
        IsLightDismissEnabled = true;
        OverlayDismissEventPassThrough = true;
        Placement = PlacementMode.BottomEdgeAlignedRight;
        // Made as they scroll into view: every tag of a stash may be thousands (E4).
        items = new ItemsControl
        {
            ItemsSource = shown,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<TagPickerEntry>((entry, _) => EntryButton(entry)),
        };
        list = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = items,
        };
        // No padding beside the scroller, so its bar stands one hairline from the card's edge, as the submenu's does.
        card = CardLook.Dress(new Border
        {
            Child = list, MinWidth = NarrowestCard, MaxWidth = WidestCard, Padding = new Thickness(0, 4),
            IsTabStop = false,
        });
        Child = card;
        card.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        card.AddHandler(KeyUpEvent, (_, e) => e.Handled |= e.Key == Key.Space, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            RestoreBoxFocus();
            FollowScaling(null);
        };
    }

    /// <summary>The entries shown, in order.</summary>
    public IReadOnlyList<TagPickerEntry> Entries => shown;

    internal bool Contains(Visual target) => ReferenceEquals(target, card) || card.IsVisualAncestorOf(target);

    internal void FocusFirstEntry(Control returnTo)
    {
        if (IsOpen && shown.Count > 0)
        {
            keyboardReturn = returnTo;
            FocusEntry(0);
        }
    }

    /// <summary>
    /// Opens beside <paramref name="anchor"/> showing <paramref name="entries"/>; a click on one calls
    /// <paramref name="onPick"/> with its tag. A press on the anchor is left to it, so it can close the list. Its width is
    /// found here, from the widest of every entry's words, and kept while it stays open, made again only for the scaling
    /// of the display it shows on.
    /// </summary>
    public void Open(Control anchor, IReadOnlyList<TagPickerEntry> entries, Action<string> onPick)
    {
        picked = onPick;
        PlacementTarget = anchor;
        OverlayInputPassThroughElement = anchor;
        list.MaxHeight = TagListHeight.For(anchor);
        widestWords = WidestWords(entries);
        card.Width = CardWidth(widestWords, TopLevel.GetTopLevel(anchor)?.RenderScaling ?? 1);
        Show(entries);
        Open();
        // The list shows in a window of its own beside its anchor, which may lie on another display than the opener's
        // window, at another scaling: its width follows that window's.
        FollowScaling(TopLevel.GetTopLevel(card));
    }

    /// <summary>From now on the card's width is made for <paramref name="window"/>'s scaling, now and whenever it changes; none for null.</summary>
    private void FollowScaling(TopLevel? window)
    {
        if (openIn is not null)
        {
            openIn.ScalingChanged -= Rescaled;
        }

        openIn = window;
        if (window is not null)
        {
            window.ScalingChanged += Rescaled;
            Rescaled(window, EventArgs.Empty);
        }
    }

    private void Rescaled(object? sender, EventArgs e)
    {
        if (openIn is { } window)
        {
            card.Width = CardWidth(widestWords, window.RenderScaling);
        }
    }

    /// <summary>
    /// Shows <paramref name="entries"/> instead, the state after a pick: the list keeps its scroll where the tags are the
    /// same ones, only the marks that changed being redrawn.
    /// </summary>
    public void Show(IReadOnlyList<TagPickerEntry> entries)
    {
        if (entries.SequenceEqual(shown))
        {
            return;
        }

        var focusedTag = FocusedEntry?.Tag as string;
        var index = focusedTag is not null ? IndexOf(focusedTag) : -1;
        var sameTags = entries.Select(entry => entry.Tag).SequenceEqual(shown.Select(entry => entry.Tag));
        var replacingFocus = index >= 0 && (!sameTags || entries[index] != shown[index]);
        if (replacingFocus)
        {
            // Recycling a focused row can send focus out of the field. Keep it in this popup during the refresh.
            card.Focusable = true;
            card.Focus();
        }

        if (sameTags)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != shown[i])
                {
                    shown[i] = entries[i];
                }
            }
        }
        else
        {
            shown.Clear();
            foreach (var entry in entries)
            {
                shown.Add(entry);
            }
        }

        if (replacingFocus && shown.Count > 0)
        {
            var sameTag = IndexOf(focusedTag!);
            FocusEntry(sameTag >= 0 ? sameTag : Math.Min(index, shown.Count - 1));
        }

        card.Focusable = false;
    }

    private Button? FocusedEntry => keyboardReturn is not null &&
        TopLevel.GetTopLevel(card)?.FocusManager.GetFocusedElement() is Button button && Contains(button)
            ? button : null;

    private int IndexOf(string tag)
    {
        for (var i = 0; i < shown.Count; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(shown[i].Tag, tag))
            {
                return i;
            }
        }

        return -1;
    }

    private void FocusEntry(int index)
    {
        card.UpdateLayout();
        items.ScrollIntoView(index);
        var container = items.ContainerFromIndex(index);
        var entry = container?.GetVisualDescendants().OfType<Button>().FirstOrDefault();
        if (entry is not null)
        {
            entry.Focusable = true;
            entry.Focus(NavigationMethod.Directional);
            // Avalonia's virtualizing panel keeps the active container realized while the list scrolls.
            KeyboardNavigation.SetTabOnceActiveElement(items, container);
            if (keyboardEntry is not null && keyboardEntry != entry)
            {
                keyboardEntry.Focusable = false;
            }

            keyboardEntry = entry;
        }
    }

    private void RestoreBoxFocus()
    {
        var returnTo = keyboardReturn;
        keyboardReturn = null;
        KeyboardNavigation.SetTabOnceActiveElement(items, null);
        if (keyboardEntry is not null)
        {
            keyboardEntry.Focusable = false;
            keyboardEntry = null;
        }

        // A light-dismiss click can already have focused another control. Leave that focus where it went.
        if (returnTo is not null && TopLevel.GetTopLevel(returnTo) is { } window &&
            (window.FocusManager.GetFocusedElement() is not Visual focused || Contains(focused)))
        {
            returnTo.Focus();
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            // Enter is the picker action. A Space release must not activate a row reached after its press.
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            Close();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down && FocusedEntry?.Tag is string tag)
        {
            var index = IndexOf(tag);
            FocusEntry(Math.Clamp(index + (e.Key == Key.Up ? -1 : 1), 0, shown.Count - 1));
            e.Handled = true;
        }
    }

    /// <summary>
    /// The card's width for words <paramref name="widestWords"/> wide at <paramref name="scaling"/>, between the card's
    /// narrowest and widest (a wider name is trimmed): in physical pixels, the entry's fixed measures as the layout rounds
    /// each to the pixel grid (to the nearest, halves to even: at 1.5 the card's edges take 2 pixels each and the words'
    /// right margin 52) beside the words rounded up to a whole pixel. Their sum in layout units alone falls a fraction of a
    /// pixel short of the words at some fractional scalings, and the widest name then shows trimmed.
    /// </summary>
    public static double CardWidth(double widestWords, double scaling)
    {
        double Rounded(double length) => Math.Round(length * scaling, MidpointRounding.ToEven);
        var pixels = Rounded(Edge) + Rounded(EntryLeft) + Rounded(MarkColumn) + Math.Ceiling(widestWords * scaling) + Rounded(EntryRight) + Rounded(Edge);
        return Math.Clamp(pixels / scaling, NarrowestCard, WidestCard);
    }

    /// <summary>
    /// The widest of <paramref name="entries"/>' words, measured each: the entries themselves are made only as they scroll
    /// into view (E4), so the panel alone would widen the card as a longer name shows.
    /// </summary>
    private double WidestWords(IReadOnlyList<TagPickerEntry> entries)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(list), TextElement.GetFontStyle(list), TextElement.GetFontWeight(list), TextElement.GetFontStretch(list));
        var size = TextElement.GetFontSize(list);
        var widest = 0.0;
        foreach (var entry in entries)
        {
            using var words = new TextLayout(entry.Tag, typeface, size, null);
            widest = Math.Max(widest, words.WidthIncludingTrailingWhitespace);
        }

        return widest;
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
            Tag = tag,
            Content = new Grid { ColumnDefinitions = [new ColumnDefinition(MarkColumn, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star)], Children = { mark, words } },
            // FocusEntry enables focus for the keyboard's current row. Pointer-only lists keep the opener's focus.
            Focusable = false,
            IsTabStop = false,
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
