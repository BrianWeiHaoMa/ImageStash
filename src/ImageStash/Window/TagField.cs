using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Look;
using Avalonia.Controls.Documents;
using System.Text.RegularExpressions;

namespace ImageStash.Window;

/// <summary>
/// The tag field (G17, G19): the tags it is given, each with its ×; a text box; while nothing is typed a button listing
/// every existing tag, marking the tags it is told every item carries, whose pick is raised for its owner to toggle as
/// Add tag ▸ does; and under it the existing tags it does not show that
/// contain the word being typed, those starting with it first. Enter commits what the box shows, a chosen suggestion in
/// place of the word being typed, and Esc closes the open list, else cancels; Down and Up choose a suggestion, and a
/// click on one commits it.
/// One control for every place that edits tags: it raises its commit, its cancel and its × and never reaches the store
/// itself, so whoever places it decides what they act on. Inside it the keys it uses are its own, and it has no paste
/// of its own and no menu offering one, as the search box has none: ImageStash reads the clipboard only for its three
/// actions (N9, R5), and the window's <c>Ctrl+V</c>, the one read, hands it the clipboard's words (G13).
/// </summary>
public sealed class TagField : StackPanel
{
    /// <summary>How many suggestions show at most.</summary>
    public const int MostSuggestions = 5;

    private readonly WrapPanel tagsAndBox;
    private readonly TextBox box;
    private readonly Button everyTag;
    private readonly WrapPanel suggestionRow;
    private IReadOnlyList<string> existing = [];
    private IReadOnlyList<string> marked = [];
    private int chosen = -1;

    /// <summary>Counts the edits the box has ended, by Enter, Esc or emptying, so a paste asked in one never lands in the next.</summary>
    private int edits;

    public TagField()
    {
        Spacing = 8;
        box = new TextBox { PlaceholderText = "Type a tag", MinWidth = 160, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2) };
        AutomationProperties.SetName(box, "Type a tag");
        // The text box's own Paste (Ctrl+V, Shift+Insert, its menu) would read the clipboard's text (N9, R5); its menu
        // would then offer a Paste that does nothing (G35). Cut and Copy of its text stay on their keys.
        box.PastingFromClipboard += (_, e) => e.Handled = true;
        box.ContextFlyout = null;
        // TextChanging, raised as the text changes, so the suggestions always belong to what the box shows.
        box.TextChanging += (_, _) =>
        {
            // Typing drops the choice, and its highlight with it: a suggestion lit is one Enter takes (G17).
            Choose(-1);
            ShowSuggestions();
            ShowEveryTag();
        };
        // Not focusable, so a click leaves focus in the box and the field open (G17).
        everyTag = new Button { Content = IconGlyph.Of("\uE70D", 12), Width = 32, Height = 32, Padding = default, Focusable = false, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        AutomationProperties.SetName(everyTag, "Every tag");
        ToolTipRule.Set(everyTag, "Every tag");
        everyTag.Click += (_, _) => ToggleList();
        tagsAndBox = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { box, everyTag } };
        suggestionRow = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4, IsVisible = false };
        Children.Add(tagsAndBox);
        Children.Add(suggestionRow);
        LogicalChildren.Add(Picker);
        // Tunnelling, so Enter, Esc and the arrows reach the field before the text box treats them as its own.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Enter, or a click on a suggestion: the words to commit, as typed, one tag per word.</summary>
    public event Action<string>? Committed;

    /// <summary>A pick in the list of every tag: that tag is to be given to every item, or taken off all of them when all carry it (G17).</summary>
    public event Action<string>? Picked;

    /// <summary>Esc, with no list open: nothing is committed.</summary>
    public event Action? Cancelled;

    /// <summary>A shown tag's ×: that tag is to be taken off.</summary>
    public event Action<string>? Removed;

    /// <summary>The words in the box.</summary>
    public string Text => box.Text ?? "";

    /// <summary>The tags shown with their ×, in order.</summary>
    public IReadOnlyList<string> Tags { get; private set; } = [];

    /// <summary>The suggestions shown for the word being typed, in order.</summary>
    public IReadOnlyList<string> Suggestions { get; private set; } = [];

    /// <summary>The suggestion Down or Up has chosen, which Enter commits in place of the word being typed, or null.</summary>
    public string? Chosen => chosen >= 0 ? Suggestions[chosen] : null;

    /// <summary>The list of every existing tag, offered on one click while nothing is typed (G17).</summary>
    public TagPicker Picker { get; } = new();

    /// <summary>
    /// Every existing tag in <paramref name="existingTags"/>' order, the commonest first as the store answers them, those
    /// in <paramref name="markedTags"/>, the tags every item carries, marked, in any spelling (G17).
    /// </summary>
    public static IReadOnlyList<TagPickerEntry> Listed(IEnumerable<string> existingTags, IEnumerable<string> markedTags)
    {
        var marks = markedTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return existingTags.Select(tag => new TagPickerEntry(tag, marks.Contains(tag))).ToList();
    }

    /// <summary>
    /// The existing tags, which suggestions come from, but for the tags the field shows, which are carried already (G17);
    /// for a word being typed, those containing it, ignoring case and its leading <c>#</c>s as a tag drops them, the ones
    /// that start with it first, then by spelling; at most <see cref="MostSuggestions"/>. Only existing tags' spellings
    /// are offered, never a name or anything else (N6). A shown tag typed whole still commits as typed.
    /// </summary>
    public static IReadOnlyList<string> SuggestionsFor(string text, IEnumerable<string> existingTags, IEnumerable<string> shownTags)
    {
        var word = text[(text.LastIndexOf(' ') + 1)..].TrimStart('#');
        if (word.Length == 0)
        {
            return [];
        }

        var shown = shownTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return existingTags
            .Where(tag => !shown.Contains(tag) && tag.Contains(word, StringComparison.OrdinalIgnoreCase))
            .OrderBy(tag => tag.StartsWith(word, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Take(MostSuggestions)
            .ToList();
    }

    /// <summary><paramref name="text"/> with the word being typed, the one after its last space, replaced by <paramref name="suggestion"/>.</summary>
    public static string WithSuggestion(string text, string suggestion) => text[..(text.LastIndexOf(' ') + 1)] + suggestion;

    /// <summary>
    /// Shows <paramref name="tags"/>, each with its ×, takes the existing tags the suggestions and the list come from, and
    /// marks in the list <paramref name="carriedByAll"/>, the tags every item carries; an open list follows at once.
    /// </summary>
    public void Show(IReadOnlyList<string> tags, IReadOnlyList<string> existingTags, IReadOnlyList<string> carriedByAll)
    {
        existing = existingTags;
        marked = carriedByAll;
        if (!tags.SequenceEqual(Tags))
        {
            Tags = tags;
            tagsAndBox.Children.RemoveRange(0, tagsAndBox.Children.Count - 2);
            tagsAndBox.Children.InsertRange(0, tags.Select(TagWithRemove));
        }

        ShowSuggestions();
        ShowEveryTag();
    }

    /// <summary>Empties the box, so the next showing starts with nothing typed, and ends the edit.</summary>
    public void Clear()
    {
        edits++;
        box.Text = "";
        Choose(-1);
        ShowSuggestions();
    }

    /// <summary>
    /// The taker for a paste asked now: it puts the words in the box over its selection or at its caret, as typed, so the
    /// suggestions follow and Enter adds them as tags (G13); once the edit it was asked in has ended, by Enter, Esc or the
    /// field closing, while the clipboard was being read, it puts nothing, since that edit was committed or cancelled (G19).
    /// Words on several lines or between tabs go in with a space between each, as they would be typed: the box is one
    /// line, and Avalonia keeps of a one-line box's input only what comes before its first line break (G13, G17).
    /// </summary>
    public Action<string> PasteNow()
    {
        var edit = edits;
        return words =>
        {
            if (edit == edits)
            {
                box.SelectedText = Regex.Replace(words, @"\s+", " ");
            }
        };
    }

    /// <summary>Gives the text box focus.</summary>
    public bool FocusBox() => box.Focus();

    /// <summary>Closes the list of every tag, as the field going away does.</summary>
    public void CloseList() => Picker.Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Enter, KeyModifiers.None):
                Commit(Chosen);
                break;
            case (Key.Escape, KeyModifiers.None) when Picker.IsOpen:
                // One thing at a time (G2): the list, and Esc again the field.
                Picker.Close();
                break;
            case (Key.Escape, KeyModifiers.None):
                Cancelled?.Invoke();
                break;
            case (Key.Down, KeyModifiers.None) when Suggestions.Count > 0:
                Choose(Math.Min(chosen + 1, Suggestions.Count - 1));
                break;
            case (Key.Up, KeyModifiers.None) when Suggestions.Count > 0:
                Choose(Math.Max(chosen - 1, -1));
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Commit(string? suggestion) => Committed?.Invoke(suggestion is null ? Text : WithSuggestion(Text, suggestion));

    private void ToggleList()
    {
        if (Picker.IsOpen)
        {
            Picker.Close();
        }
        else
        {
            Picker.Open(everyTag, Listed(existing, marked), tag => Picked?.Invoke(tag));
        }
    }

    /// <summary>The button while nothing is typed and there is a tag to pick (G17); typing hides it and closes its list, and an open list shows what the field now shows.</summary>
    private void ShowEveryTag()
    {
        everyTag.IsVisible = Text.Length == 0 && existing.Count > 0;
        if (!everyTag.IsVisible)
        {
            Picker.Close();
        }
        else if (Picker.IsOpen)
        {
            Picker.Show(Listed(existing, marked));
        }
    }

    private void Choose(int index)
    {
        chosen = index;
        for (var i = 0; i < suggestionRow.Children.Count; i++)
        {
            suggestionRow.Children[i].Classes.Set("accent", i == chosen);
        }
    }

    private void ShowSuggestions()
    {
        var now = SuggestionsFor(Text, existing, Tags);
        if (now.SequenceEqual(Suggestions))
        {
            return;
        }

        Suggestions = now;
        chosen = -1;
        suggestionRow.Children.Clear();
        foreach (var suggestion in now)
        {
            // Not focusable, so a click commits it without taking focus from the box.
            var button = new Button { Content = suggestion, Height = 28, Padding = new Thickness(10, 0), VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
            button.Classes.Add(LookStyles.TagChip);
            button.Click += (_, _) => Commit(suggestion);
            suggestionRow.Children.Add(button);
        }

        suggestionRow.IsVisible = now.Count > 0;
    }

    private Control TagWithRemove(string tag)
    {
        var remove = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center },
            Padding = new Thickness(5),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        };
        remove.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(remove, $"Remove tag {tag}");
        ToolTipRule.Set(remove, "Remove tag");
        remove.Click += (_, _) => Removed?.Invoke(tag);
        var text = new TextBlock { Text = tag, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        ToolTipRule.ShowWhole(text, tag);
        var chip = new Border
        {
            // A long tag is trimmed, its whole word in its tip, so its × never leaves the field (G17).
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { text, remove } },
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable(LookKeys.TagChipGround));
        chip.Bind(TextElement.ForegroundProperty, chip.GetResourceObservable(LookKeys.TagChipWords));
        chip.Bind(Border.CornerRadiusProperty, chip.GetResourceObservable(LookKeys.ControlRadius));
        return chip;
    }
}

/// <summary>
/// A tag field scrolling within a bounded height, however many tags it shows, so what is placed below it stays in the
/// window (G17, G19, E4). The box and its suggestions end the field: while focus is in it, they are kept in view as it
/// grows.
/// </summary>
public sealed class TagFieldScroller : ScrollViewer
{
    private readonly Func<bool> focusInField;

    public TagFieldScroller(TagField field, Func<bool> focusInField)
    {
        this.focusInField = focusInField;
        Content = field;
        MaxHeight = 148;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        field.SizeChanged += (_, _) => KeepBoxInView();
    }

    /// <summary>Drawn as the theme draws any scroller.</summary>
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>Scrolls to the box and its suggestions while focus is in the field.</summary>
    public void KeepBoxInView()
    {
        if (focusInField())
        {
            ScrollToEnd();
        }
    }
}

/// <summary>
/// Type a tag… (G17): the tag field over the grid, inside the window's own tree as the drop card and the notices are,
/// never a window of its own (F4), so the flyout keeps focus and stays (G2). It acts on the items it was opened on: it
/// shows the tags they carry, a × taking that tag off every one of them, and scrolls within a bounded height however
/// many they are; its list of every tag marks the tags every one of them carries, and a pick there gives the tag to every
/// one, or takes it off all of them when all carry it, as Add tag ▸ does; Enter gives every one the tags typed, one per word, and leaves it open with the box empty for the
/// next, so several are added in a row; Esc closes it and gives focus back to where it was. Every change goes through
/// the window's one organising entry, which names each refusal (G35). What it shows is read from the store when it
/// opens, after each change and whenever the listing changes, never kept, so a tag no item carries is neither shown
/// nor suggested, and one carried is not suggested (G17). Focus moving elsewhere in the window, by a click or a
/// summon, closes it as Esc would, and so does an import beginning to replace the stash, so nothing typed into it lands
/// on an item the imported store gives the same number (G36).
/// </summary>
public sealed class TagFieldOverlay : Border
{
    private readonly Listing listing;
    private readonly Organising organising;
    private readonly Notices notices;
    private readonly TextBlock heading;
    private readonly TagFieldScroller scroller;
    private IReadOnlyList<StashItem> items = [];
    private IInputElement? returnTo;
    private int reads;

    public TagFieldOverlay(Listing listing, Organising organising, Notices notices)
    {
        this.listing = listing;
        this.organising = organising;
        this.notices = notices;
        heading = new TextBlock { FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
        Field = new TagField();
        scroller = new TagFieldScroller(Field, () => IsVisible);
        Child = new StackPanel { Spacing = 8, Children = { heading, scroller } };
        Padding = new Thickness(14, 10, 14, 12);
        Margin = new Thickness(12, 4, 12, 0);
        MaxWidth = 460;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        CardLook.Dress(this);
        IsVisible = false;

        Field.Committed += text => _ = Commit(text);
        Field.Picked += tag => _ = Toggle(tag);
        Field.Cancelled += () => Close(giveFocusBack: true);
        Field.Removed += tag => _ = Remove(tag);
        listing.Changed += () =>
        {
            if (IsVisible)
            {
                _ = Read();
            }
        };
        // Its items are the replaced stash's: the imported store reuses their numbers for other items (Item identity).
        listing.LetGoWhenWithdrawn(() =>
        {
            Close(giveFocusBack: true);
            return Task.CompletedTask;
        });
        AttachedToVisualTree += (_, e) => (e.RootVisual as InputElement)?.AddHandler(GotFocusEvent, FocusMoved, handledEventsToo: true);
        DetachedFromVisualTree += (_, e) => (e.RootVisual as InputElement)?.RemoveHandler(GotFocusEvent, FocusMoved);
    }

    public TagField Field { get; }

    /// <summary>The items it acts on while open; none while closed.</summary>
    public IReadOnlyList<StashItem> Items => items;

    /// <summary>
    /// Opens over <paramref name="opened"/>, showing the tags they carry and nothing typed, with focus in the box; on
    /// closing by Esc, focus goes back to <paramref name="returnFocusTo"/>.
    /// </summary>
    public void Open(IReadOnlyList<StashItem> opened, IInputElement returnFocusTo)
    {
        items = opened;
        returnTo = returnFocusTo;
        heading.Text = "Tags of " + Notices.Describe(opened);
        Field.Clear();
        Field.Show(Organising.CarriedTags(opened), [], CarriedByAll(opened));
        IsVisible = true;
        // Laid out first: the scroller places the field in the tree only once its template applies, and only a box in
        // the tree takes focus.
        UpdateLayout();
        Field.FocusBox();
        scroller.KeepBoxInView();
        _ = Read();
    }

    /// <summary>
    /// Enter: the box emptied, focus left in it, and every item given the tags typed, the field then showing what they
    /// carry; nothing typed, or only <c>#</c>s, changes nothing. The field stays open either way (G17).
    /// </summary>
    private async Task Commit(string text)
    {
        Field.Clear();
        if (ItemTags.Words(text).Count == 0)
        {
            return;
        }

        var opened = items;
        await organising.AddTag(opened, text);
        if (IsVisible && ReferenceEquals(items, opened))
        {
            await Read();
        }
    }

    /// <summary>A pick in the list: the tag toggled for every item as Add tag ▸ toggles it, the field and the list then showing what they carry (G17).</summary>
    private async Task Toggle(string tag)
    {
        var opened = items;
        await organising.ToggleTag(opened, tag);
        if (IsVisible && ReferenceEquals(items, opened))
        {
            await Read();
        }
    }

    /// <summary>The tags every one of <paramref name="carriers"/> carries, which the list marks (G17).</summary>
    private static IReadOnlyList<string> CarriedByAll(IReadOnlyList<StashItem> carriers) =>
        Organising.CarriedTags(carriers).Where(tag => Organising.AllCarry(carriers, tag)).ToList();

    /// <summary>A ×: the tag off every item, the field still open on what remains.</summary>
    private async Task Remove(string tag)
    {
        await organising.RemoveTag(items, tag);
        if (IsVisible)
        {
            await Read();
        }
    }

    private void Close(bool giveFocusBack)
    {
        if (!IsVisible)
        {
            return;
        }

        Field.CloseList();
        // Hidden first, so the focus given back is not taken for focus moving away from an open field.
        IsVisible = false;
        items = [];
        reads++;
        Field.Clear();
        if (giveFocusBack)
        {
            returnTo?.Focus();
        }

        returnTo = null;
    }

    /// <summary>
    /// The tags the items carry now and every tag that exists, read from the store, the latest read alone applied; an
    /// item no longer in the stash carries none, and Enter on it is refused by name.
    /// </summary>
    private async Task Read()
    {
        if (listing.Store is not { } store)
        {
            return;
        }

        var read = ++reads;
        var ids = items.Select(item => item.Id).ToHashSet();
        var all = await store.All();
        var counts = await store.TagCounts();
        if (read != reads)
        {
            return;
        }

        if (!all.Succeeded || !counts.Succeeded)
        {
            notices.Raise(all.Failure ?? counts.Failure!);
            return;
        }

        var present = all.Value!.Where(item => ids.Contains(item.Id)).ToList();
        Field.Show(Organising.CarriedTags(present), counts.Value!.Select(count => count.Tag).ToList(), CarriedByAll(present));
    }

    private void FocusMoved(object? sender, FocusChangedEventArgs e)
    {
        if (IsVisible && e.Source is Visual focused && !this.IsVisualAncestorOf(focused))
        {
            Close(giveFocusBack: false);
        }
    }
}
