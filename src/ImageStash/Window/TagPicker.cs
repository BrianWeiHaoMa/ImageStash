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
using Avalonia.Utilities;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>A tag's stash-wide carrier count, whether the opener marks it, and whether its row is inert, as a dead chip place is (G4).</summary>
public sealed record TagPickerEntry(string Tag, int Count, bool Marked, bool Inert = false)
{
    public static IReadOnlyList<TagPickerEntry> FromCounts(IEnumerable<TagCount> counts, IEnumerable<string> markedTags)
    {
        var marks = markedTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return counts.Select(count => new TagPickerEntry(count.Tag, count.Items, marks.Contains(count.Tag))).ToList();
    }

    internal string CountText => TagCountText.Of(Count);
}

/// <summary>
/// The shared tag list (G4, G17): a popup of the window, its entries in the order the opener gives, scrolling inside
/// the display's working area when long. Opening with the pointer leaves focus where it is. Opening from the tag box
/// with Down gives focus to the first entry for arrow navigation and selection with Enter. A pick leaves it open for the
/// next; Esc, which its owner asks it about before anything else Esc does, a press outside it, which then acts as it
/// would have, and the window going away close it. It never reads the store and never changes an item or the query:
/// the opener decides what a pick means, and tells it what to show afterwards. Whoever holds one keeps it in its own
/// logical tree, so it takes the window's theme, and keeps the anchor in the visual tree while it is open, since a
/// popup closes with its anchor. The card measures the names and counts before it opens, keeping both clear of the
/// scrollbar. Counts align at the end of the row.
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
    private const double CountGap = TagCountText.Gap;
    private const double Edge = 1;
    private const double NarrowestCard = 180;
    private const double WidestCard = 320;

    private readonly ObservableCollection<Row> rows = [];
    private readonly Dictionary<string, double> measuredText = new(StringComparer.Ordinal);
    private readonly ItemsControl items;
    private readonly ScrollViewer list;
    private readonly Border card;
    private Action<string> picked = _ => { };
    private Action<Button, Row>? attachTagMenu;
    private PressOnce? heldKeys;
    private Control? keyboardReturn;
    private Button? keyboardEntry;
    private double widestName;
    private double widestCount;
    private TagManagementMenu? renaming;
    private IDisposable? windowPresses;

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
            ItemsSource = rows,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => row is null ? null : MakeEntry(row)),
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
        card.AddHandler(KeyUpEvent, (_, e) =>
        {
            // The chip row's popup can receive releases that do not route through the window.
            heldKeys?.Released(e.Key);
            e.Handled |= e.Key == Key.Space && e.Source is not TagRenameBox;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closed += (_, _) =>
        {
            RestoreBoxFocus();
            FollowScaling(null);
        };
    }

    /// <summary>The entries shown, in order.</summary>
    public IReadOnlyList<TagPickerEntry> Entries { get; private set; } = [];

    internal bool Contains(Visual target) => ReferenceEquals(target, card) || card.IsVisualAncestorOf(target) ||
        OpenedPopups.Any(popup => popup.IsInsidePopup(target));

    /// <summary>A tag renamed from a row's menu or elsewhere to a new word, from and to: the owner's open list renames its row in place (G17).</summary>
    internal event Action<string, string>? RowRenamed;

    internal void ConfigureTagManagement(Listing listing, Organising organising, PressOnce? heldKeys, ClipboardPaste? paste)
    {
        this.heldKeys = heldKeys;
        listing.TagRenamedInLists += (from, to) => RowRenamed?.Invoke(from, to);
        attachTagMenu = (button, row) =>
        {
            var menu = new TagManagementMenu(button, listing, organising, () =>
                IsOpen && rows.Contains(row) ? new TagCount(row.Entry.Tag, row.Entry.Count) : null, heldKeys, paste, () => RowInView(button));
            menu.RenameBegan += began => renaming = began;
            button.ContextMenu = menu;
        };
    }

    /// <summary>
    /// A press elsewhere in the window keeps a rename typed in a row, as Enter would (G17), before the list closes for
    /// it: its rows leave as it closes, which ends a draft unwritten, and the list shows in a window of its own, whose
    /// focus the element pressed never takes. This handler tunnels from the window's root, so it runs before Avalonia's
    /// light dismiss, which the window's dismiss layer hears as the press reaches it. A hide or another app closes the
    /// list with no press here. A press on the window's frame closes it with no routed press, and keeps nothing.
    /// </summary>
    private void PressedInWindow(object? sender, PointerPressedEventArgs e)
    {
        if (IsOpen && e.Source is Visual pressed && !Contains(pressed) && renaming is { } menu)
        {
            renaming = null;
            menu.KeepRename();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty && !change.GetNewValue<bool>())
        {
            // Closed by any way: a row's draft has ended with its row, and no later press keeps it.
            windowPresses?.Dispose();
            windowPresses = null;
            renaming = null;
        }
    }

    private bool RowInView(Button button) => button.IsEffectivelyVisible && button.TranslatePoint(default, list) is { } at
        && at.Y < list.Viewport.Height && at.Y + button.Bounds.Height > 0;

    private Button MakeEntry(Row row)
    {
        var button = new RowButton(row, tag => picked(tag));
        attachTagMenu?.Invoke(button, row);
        return button;
    }

    internal void FocusFirstEntry(Control returnTo)
    {
        if (IsOpen && Entries.Count > 0)
        {
            keyboardReturn = returnTo;
            FocusEntry(0);
        }
    }

    /// <summary>
    /// Opens beside <paramref name="anchor"/> showing <paramref name="entries"/>; a click on one calls
    /// <paramref name="onPick"/> with its tag. A press on the anchor is left to it, so it can close the list. Its width is
    /// found from the names and counts before opening, and grows if a later entry needs more room.
    /// </summary>
    public void Open(Control anchor, IReadOnlyList<TagPickerEntry> entries, Action<string> onPick)
    {
        picked = onPick;
        PlacementTarget = anchor;
        OverlayInputPassThroughElement = anchor;
        list.MaxHeight = TagListHeight.For(anchor);
        measuredText.Clear();
        widestName = widestCount = 0;
        if (TopLevel.GetTopLevel(anchor) is { } host)
        {
            MeasureEntries(entries, host.RenderScaling);
        }

        Show(entries);
        windowPresses?.Dispose();
        // The window's root: the top level the anchor shows in.
        windowPresses = anchor.GetSelfAndVisualAncestors().OfType<Interactive>().Last()
            .AddDisposableHandler(PointerPressedEvent, PressedInWindow, RoutingStrategies.Tunnel, handledEventsToo: true);
        SetCurrentValue(IsOpenProperty, true);
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
            MeasureEntries(Entries, window.RenderScaling);
        }
    }

    private void Rescaled(object? sender, EventArgs e)
    {
        if (openIn is { } window)
        {
            card.Width = CardWidth(widestName, widestCount, window.RenderScaling);
        }
    }

    /// <summary>
    /// Shows the current names, counts and marks. A row whose place stays keeps its button through a refresh.
    /// </summary>
    public void Show(IReadOnlyList<TagPickerEntry> entries)
    {
        if (entries.SequenceEqual(Entries))
        {
            return;
        }

        if (openIn is not null)
        {
            MeasureEntries(entries, openIn.RenderScaling);
        }

        var focusedTag = FocusedEntry?.Tag as string;
        var index = focusedTag is not null ? IndexOf(focusedTag) : -1;
        var replacingFocus = index >= 0 && (index >= entries.Count || entries[index].Tag != Entries[index].Tag);
        Entries = entries;
        if (replacingFocus)
        {
            // Recycling a focused row can send focus out of the field. Keep it in this popup during the refresh.
            card.Focusable = true;
            card.Focus();
        }

        SetRows(entries);

        if (replacingFocus)
        {
            var sameTag = IndexOf(focusedTag!);
            if (entries.Count == 0 || !FocusEntry(sameTag >= 0 ? sameTag : Math.Min(index, entries.Count - 1)))
            {
                keyboardReturn?.Focus();
            }
        }

        card.Focusable = false;
    }

    private void SetRows(IReadOnlyList<TagPickerEntry> entries)
    {
        var commonLength = Math.Min(rows.Count, entries.Count);
        for (var i = 0; i < commonLength; i++)
        {
            var entry = entries[i];
            if (rows[i].Entry.Tag == entry.Tag)
            {
                rows[i].Entry = entry;
            }
            else
            {
                // Recycle only the row that changed. Another row can still hold the person's draft.
                rows[i] = new Row(entry);
            }
        }

        while (rows.Count > entries.Count)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        for (var i = commonLength; i < entries.Count; i++)
        {
            rows.Add(new Row(entries[i]));
        }
    }

    private Button? FocusedEntry
    {
        get
        {
            if (keyboardReturn is null || TopLevel.GetTopLevel(card)?.FocusManager.GetFocusedElement() is not Visual focused ||
                !Contains(focused))
            {
                return null;
            }

            // A child menu borrows focus from the keyboard row. Its anchor may be recycled in this refresh.
            return focused as Button ?? (TagManagementMenu.InTagMenu(focused) ? keyboardEntry : null);
        }
    }

    private int IndexOf(string tag)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(Entries[i].Tag, tag))
            {
                return i;
            }
        }

        return -1;
    }

    private bool FocusEntry(int index)
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

        return entry?.IsFocused == true;
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
        if (e.Source is TagRenameBox || TagManagementMenu.InTagMenu(e.Source))
        {
            return;
        }

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
            FocusEntry(Math.Clamp(index + (e.Key == Key.Up ? -1 : 1), 0, Entries.Count - 1));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Round each fixed measure the same way as layout, with each text column rounded up to a whole physical pixel.
    /// Adding up layout units first can leave either column a fraction of a pixel short at fractional scaling.
    /// </summary>
    public static double CardWidth(double nameWidth, double countWidth, double scaling)
    {
        double Rounded(double length) => Math.Round(length * scaling, MidpointRounding.ToEven);
        var pixels = 2 * Rounded(Edge) + Rounded(EntryLeft) + Rounded(MarkColumn) + Math.Ceiling(nameWidth * scaling)
            + Rounded(CountGap) + Math.Ceiling(countWidth * scaling) + Rounded(EntryRight);
        return Math.Clamp(pixels / scaling, NarrowestCard, WidestCard);
    }

    /// <summary>
    /// The list sizes from text alone. The row controls are made as they scroll into view (E4).
    /// </summary>
    private void MeasureEntries(IReadOnlyList<TagPickerEntry> entries, double scaling)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(list), TextElement.GetFontStyle(list), TextElement.GetFontWeight(list), TextElement.GetFontStretch(list));
        var size = TextElement.GetFontSize(list);
        foreach (var entry in entries)
        {
            widestName = Math.Max(widestName, Measure(entry.Tag));
            widestCount = Math.Max(widestCount, Measure(entry.CountText));
        }

        card.Width = CardWidth(widestName, widestCount, scaling);

        double Measure(string text)
        {
            if (!measuredText.TryGetValue(text, out var width))
            {
                using var layout = new TextLayout(text, typeface, size, null);
                measuredText[text] = width = layout.WidthIncludingTrailingWhitespace;
            }

            return width;
        }
    }

    private sealed class RowButton : Button, IWeakEventSubscriber<AvaloniaPropertyChangedEventArgs>
    {
        private readonly TextBlock mark = IconGlyph.Of("", 12);
        private readonly TextBlock count = TagCountText.Block();
        private TagPickerEntry shown;

        public RowButton(Row row, Action<string> pick)
        {
            var tag = row.Entry.Tag;
            shown = row.Entry;
            Tag = tag;
            var words = new TextBlock { Text = tag, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(words, 1);
            Grid.SetColumn(count, 2);
            Content = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(MarkColumn, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Auto)],
                Children = { mark, words, count },
            };
            // FocusEntry enables focus for the keyboard's current row. Pointer-only lists keep the opener's focus.
            Focusable = false;
            IsTabStop = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Background = Brushes.Transparent;
            BorderThickness = default;
            Height = 30;
            Padding = new Thickness(EntryLeft, 0, EntryRight, 0);

            AutomationProperties.SetName(this, tag);
            ToolTipRule.ShowWhenTrimmed(words, words, tag);
            Click += (_, _) =>
            {
                if (Content is not TagRenameBox)
                {
                    pick(shown.Tag);
                }
            };
            Show(row.Entry);
            // A row model outlives the controls virtualization discards as it scrolls.
            WeakEvents.AvaloniaPropertyChanged.Subscribe(row, this);
        }

        protected override Type StyleKeyOverride => typeof(Button);

        void IWeakEventSubscriber<AvaloniaPropertyChangedEventArgs>.OnEvent(object? sender, WeakEvent ev, AvaloniaPropertyChangedEventArgs e) =>
            Show(e.GetNewValue<TagPickerEntry>());

        private void Show(TagPickerEntry entry)
        {
            // A pick acts on the tag the row shows now.
            shown = entry;
            IsEnabled = !entry.Inert;
            mark.Opacity = entry.Marked ? 1 : 0;
            count.Text = entry.CountText;
            AutomationProperties.SetHelpText(this, entry.CountText + (entry.Count == 1 ? " item" : " items"));
        }
    }

    private sealed class Row(TagPickerEntry entry) : AvaloniaObject
    {
        public static readonly DirectProperty<Row, TagPickerEntry> EntryProperty =
            AvaloniaProperty.RegisterDirect<Row, TagPickerEntry>(nameof(Entry), row => row.Entry);

        private TagPickerEntry current = entry;

        public TagPickerEntry Entry
        {
            get => current;
            set => SetAndRaise(EntryProperty, ref current, value);
        }
    }
}
