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
/// place of the word being typed, and Esc closes the open list, else cancels. The arrows choose a suggestion, or Down
/// opens the list from an empty box. Empty Backspace first highlights the last tag, then acts like its ×.
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
    private readonly Grid pickerAnchor;
    private readonly WrapPanel suggestionRow;
    private IReadOnlyList<TagCount>? existing = [];
    private IReadOnlyList<string> marked = [];
    private readonly OpenTagList everyTagList = new();
    private IReadOnlyList<TagCount> suggested = [];
    private int chosen = -1;
    // True for a keyboard request, false for a pointer request, null while no open is waiting.
    private bool? openPickerAfterRead;
    private bool catalogueNeedsRetry;
    private readonly PressOnce backspace = new();
    private Border? armedRemoval;

    /// <summary>Counts the edits the box has ended, by Enter, Esc or emptying, so a paste asked in one never lands in the next.</summary>
    private int edits;

    public TagField()
    {
        Spacing = 8;
        Picker.RowRenamed += everyTagList.Renamed;
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
            openPickerAfterRead = null;
            ClearRemovalHighlight();
            Choose(-1);
            ShowSuggestions();
            ShowEveryTag();
        };
        box.LostFocus += (_, _) =>
        {
            backspace.Released();
            openPickerAfterRead = null;
            ClearRemovalHighlight();
        };
        // Not focusable, so a click leaves focus in the box and the field open (G17).
        everyTag = new Button { Content = IconGlyph.Of("\uE70D", 12), Width = 32, Height = 32, Padding = default, Focusable = false, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        AutomationProperties.SetName(everyTag, "Every tag");
        ToolTipRule.Set(everyTag, "Every tag");
        everyTag.Click += (_, _) => ToggleList();
        // A native popup routes keys to its Popup control. Keeping that control in the visual tree gives those keys
        // the same window and field handlers as keys from the box.
        pickerAnchor = new Grid { IsVisible = false, Children = { everyTag, Picker } };
        // A list kept open on tags that lost their last items leaves nothing to offer once it closes.
        Picker.PropertyChanged += (_, e) =>
        {
            if (e.Property == Popup.IsOpenProperty && !Picker.IsOpen && existing is { Count: 0 })
            {
                ShowEveryTag();
            }
        };
        tagsAndBox = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { box, pickerAnchor } };
        suggestionRow = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4, IsVisible = false };
        Children.Add(tagsAndBox);
        Children.Add(suggestionRow);
        // Tunnelling, so Enter, Esc and the arrows reach the field before the text box treats them as its own.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Enter, or a click on a suggestion: the words to commit, as typed, one tag per word.</summary>
    public event Action<string>? Committed;

    /// <summary>A pick in the list of every tag: that tag is to be given to every item, or taken off all of them when all carry it, through <see cref="Pick"/> (G17).</summary>
    public event Action<string>? Picked;

    internal event Action? CatalogueRequested;

    /// <summary>Esc, with no list open: nothing is committed.</summary>
    public event Action? Cancelled;

    /// <summary>A shown tag's ×: that tag is to be taken off.</summary>
    public event Action<string>? Removed;

    /// <summary>The words in the box.</summary>
    public string Text => box.Text ?? "";

    /// <summary>What the empty box reads, until something is typed.</summary>
    public string Placeholder
    {
        get => box.PlaceholderText ?? "";
        set => box.PlaceholderText = value;
    }

    /// <summary>The tags shown with their ×, in order.</summary>
    public IReadOnlyList<string> Tags { get; private set; } = [];

    /// <summary>The suggestions shown for the word being typed, in order.</summary>
    public IReadOnlyList<string> Suggestions { get; private set; } = [];

    /// <summary>The suggestion Down or Up has chosen, which Enter commits in place of the word being typed, or null.</summary>
    public string? Chosen => chosen >= 0 ? Suggestions[chosen] : null;

    /// <summary>What Enter would commit now: the words in the box, the chosen suggestion in place of the word being typed; focus leaving commits the same (G17).</summary>
    public string EnterCommits => Chosen is { } suggestion ? WithSuggestion(Text, suggestion) : Text;

    /// <summary>The list of every existing tag, offered on one click while nothing is typed (G17).</summary>
    public TagPicker Picker { get; } = new();

    internal Control? FocusedPart => TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement() as Control;
    internal bool BoxFocused => box.IsFocused;

    internal bool OwnsFocus(object? source) =>
        source is Visual focused && (this.IsVisualAncestorOf(focused) || Picker.Contains(focused));

    /// <summary>
    /// The existing tags, which suggestions come from, but for the tags the field shows, which are carried already (G17);
    /// for a word being typed, those containing it, ignoring case and its leading <c>#</c>s as a tag drops them, the ones
    /// that start with it first, then the commonest first, then by spelling; at most <see cref="MostSuggestions"/>, each
    /// with the number of items in the stash that carry it. Only existing tags' spellings are offered, never a name or
    /// anything else (N6). A shown tag typed whole still commits as typed.
    /// </summary>
    public static IReadOnlyList<TagCount> SuggestionsFor(string text, IEnumerable<TagCount> existingTags, IEnumerable<string> shownTags)
    {
        var word = text[(text.LastIndexOf(' ') + 1)..].TrimStart('#');
        if (word.Length == 0)
        {
            return [];
        }

        var shown = shownTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return existingTags
            .Where(count => !shown.Contains(count.Tag) && count.Tag.Contains(word, StringComparison.OrdinalIgnoreCase))
            .OrderBy(count => count.Tag.StartsWith(word, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(count => count.Items)
            .ThenBy(count => count.Tag, StringComparer.OrdinalIgnoreCase)
            .Take(MostSuggestions)
            .ToList();
    }

    /// <summary><paramref name="text"/> with the word being typed, the one after its last space, replaced by <paramref name="suggestion"/>.</summary>
    public static string WithSuggestion(string text, string suggestion) => text[..(text.LastIndexOf(' ') + 1)] + suggestion;

    /// <summary>
    /// Shows <paramref name="tags"/>, each with its ×, takes the existing tags the suggestions and the list come from, and
    /// marks in the list <paramref name="carriedByAll"/>, the tags every item carries. A null catalogue is still being read.
    /// An open list follows at once.
    /// </summary>
    public void Show(IReadOnlyList<string> tags, IReadOnlyList<TagCount>? existingTags, IReadOnlyList<string> carriedByAll)
    {
        catalogueNeedsRetry = false;
        existing = existingTags;
        marked = carriedByAll;
        if (!tags.SequenceEqual(Tags))
        {
            ClearRemovalHighlight();
            // The refresh replaces the chip controls. Park focus inside the edit while they are rebuilt.
            var focusedTag = FocusedPart is Button { Tag: string tag } button && this.IsVisualAncestorOf(button) ? tag : null;
            if (focusedTag is not null)
            {
                FocusBox();
            }

            Tags = tags;
            tagsAndBox.Children.RemoveRange(0, tagsAndBox.Children.Count - 2);
            tagsAndBox.Children.InsertRange(0, tags.Select(TagWithRemove));
            if (focusedTag is not null)
            {
                UpdateLayout();
                this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => Equals(button.Tag, focusedTag))?.Focus(NavigationMethod.Tab);
            }
        }

        ShowSuggestions();
        ShowEveryTag();
    }

    /// <summary>Empties the box, so the next showing starts with nothing typed, and ends the edit.</summary>
    public void Clear()
    {
        edits++;
        openPickerAfterRead = null;
        ClearRemovalHighlight();
        box.Text = "";
        Choose(-1);
        ShowSuggestions();
    }

    internal void CatalogueReadFailed()
    {
        existing = null;
        catalogueNeedsRetry = true;
        openPickerAfterRead = null;
        ShowSuggestions();
        ShowEveryTag();
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
                if (!box.IsFocused && OwnsFocus(FocusedPart))
                {
                    // Pasting text into the field returns to its box, where Enter adds the pasted words.
                    FocusBox();
                }

                box.SelectedText = Regex.Replace(words, @"\s+", " ");
            }
        };
    }

    /// <summary>Gives the text box focus.</summary>
    public bool FocusBox() => box.Focus();

    /// <summary>Closes the list of every tag, as the field going away does.</summary>
    public void CloseList()
    {
        openPickerAfterRead = null;
        Picker.Close();
    }

    /// <summary>
    /// Enter's commit: <see cref="EnterCommits"/> raised as <see cref="Committed"/>; also a click on the preview's ‹ or ›,
    /// which takes no focus, so its commit is Enter's (G19).
    /// </summary>
    public void CommitAsEnter() => Committed?.Invoke(EnterCommits);

    internal void CancelAsEscape()
    {
        ClearRemovalHighlight();
        if (Picker.IsOpen)
        {
            CloseList();
        }
        else
        {
            Cancelled?.Invoke();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TagRenameBox)
        {
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            if (e.Source is Button && e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelAsEscape();
            e.Handled = true;
            return;
        }

        // A focused × is a button: Enter and Space activate that button, leaving any words in the box alone.
        if (!ReferenceEquals(e.Source, box))
        {
            return;
        }

        if (e.Key == Key.Back)
        {
            BackspaceInBox(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                CommitAsEnter();
                break;
            case Key.Down when Suggestions.Count > 0:
                Choose(Math.Min(chosen + 1, Suggestions.Count - 1));
                break;
            case Key.Down when Text.Length == 0 && existing is not { Count: 0 }:
                OpenListFromBox();
                break;
            case Key.Up when Suggestions.Count > 0:
                Choose(Math.Max(chosen - 1, -1));
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        backspace.Released(e.Key);
        // Avalonia's button handles Space on release even if the key went down on another control.
        if (e.Key == Key.Space && e.Source is Button { Tag: string, IsPressed: false } button && this.IsVisualAncestorOf(button))
        {
            e.Handled = true;
        }
    }

    private void BackspaceInBox(KeyEventArgs e)
    {
        var pressed = backspace.Pressed(Key.Back);
        if (Text.Length == 0)
        {
            if (pressed)
            {
                BackspaceOnEmptyBox();
            }

            e.Handled = true;
        }
    }

    private void BackspaceOnEmptyBox()
    {
        if (Tags.Count == 0)
        {
            return;
        }

        var last = (Border)tagsAndBox.Children[Tags.Count - 1];
        if (ReferenceEquals(armedRemoval, last))
        {
            RemoveTag(Tags[^1]);
        }
        else
        {
            armedRemoval = last;
            last.Classes.Add(LookStyles.Lit);
            last.BringIntoView();
        }
    }

    private void RemoveTag(string tag)
    {
        ClearRemovalHighlight();
        Removed?.Invoke(tag);
    }

    private void ClearRemovalHighlight()
    {
        armedRemoval?.Classes.Remove(LookStyles.Lit);
        armedRemoval = null;
    }

    private void ToggleList()
    {
        if (Picker.IsOpen)
        {
            CloseList();
        }
        else
        {
            OpenList(focusEntry: false);
        }
    }

    /// <summary>
    /// A pick on <paramref name="tag"/> in the list of every tag, for <paramref name="items"/>: toggled as Add tag ▸ toggles
    /// it, or given back when a pick in this open list took it off its last item (G17).
    /// </summary>
    public Task<Outcome> Pick(Organising organising, IReadOnlyList<StashItem> items, string tag) =>
        organising.PickTag(items, tag, whileAnyCarries: false, everyTagList);

    /// <summary>The open list's rows for <paramref name="counts"/>, held while it is open, marking the tags every item carries.</summary>
    private IReadOnlyList<TagPickerEntry> EveryTagEntries(IReadOnlyList<TagCount> counts) => TagPickerEntry.FromCounts(everyTagList.Rows(counts), marked);

    private void OpenListFromBox()
    {
        ClearRemovalHighlight();
        OpenList(focusEntry: true);
    }

    private void OpenList(bool focusEntry)
    {
        if (existing is null)
        {
            openPickerAfterRead = focusEntry;
            if (catalogueNeedsRetry)
            {
                catalogueNeedsRetry = false;
                CatalogueRequested?.Invoke();
            }

            return;
        }

        if (!Picker.IsOpen && existing.Count > 0)
        {
            everyTagList.Close();
            Picker.Open(everyTag, EveryTagEntries(existing), tag => Picked?.Invoke(tag));
        }

        if (focusEntry)
        {
            Picker.FocusFirstEntry(box);
        }
    }

    /// <summary>The button while nothing is typed and there is a tag to pick (G17); typing hides it and closes its list, and an open list shows what the field now shows.</summary>
    private void ShowEveryTag()
    {
        // An open list keeps its rows while nothing is typed, even once the last tag has lost its last item (G17).
        var available = Text.Length == 0 && existing is { } tags && (tags.Count > 0 || Picker.IsOpen);
        if (!available)
        {
            Picker.Close();
        }
        else if (Picker.IsOpen)
        {
            Picker.Show(EveryTagEntries(existing!));
        }

        pickerAnchor.IsVisible = everyTag.IsVisible = available || Text.Length == 0 && catalogueNeedsRetry;
        if (openPickerAfterRead is { } focusEntry && existing is not null)
        {
            openPickerAfterRead = null;
            if (available && (!focusEntry || box.IsFocused))
            {
                OpenList(focusEntry);
            }
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
        var counted = SuggestionsFor(Text, existing ?? [], Tags);
        if (counted.SequenceEqual(suggested))
        {
            return;
        }

        suggested = counted;
        Suggestions = counted.Select(count => count.Tag).ToList();
        chosen = -1;
        suggestionRow.Children.Clear();
        foreach (var (suggestion, count) in counted)
        {
            // Not focusable, so a click commits it without taking focus from the box.
            var button = new Button { Content = TagCountText.Beside(suggestion, count), Height = 28, Padding = new Thickness(10, 0), VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
            button.Classes.Add(LookStyles.TagChip);
            // Found by its words alone, its number aside.
            AutomationProperties.SetName(button, suggestion);
            button.Click += (_, _) => Committed?.Invoke(WithSuggestion(Text, suggestion));
            suggestionRow.Children.Add(button);
        }

        suggestionRow.IsVisible = counted.Count > 0;
    }

    private Control TagWithRemove(string tag)
    {
        var remove = new Button
        {
            Tag = tag,
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center },
            Padding = new Thickness(5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(remove, $"Remove tag {tag}");
        ToolTipRule.Set(remove, "Remove tag");
        remove.Click += (_, _) => RemoveTag(tag);
        var text = new TextBlock { Text = tag, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        ToolTipRule.ShowWhole(text, tag);
        var chip = new Border
        {
            // A long tag is trimmed, its whole word in its tip, so its × never leaves the field (G17).
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { text, remove } },
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.Classes.Add(LookStyles.TagFieldChip);
        chip.Bind(Border.CornerRadiusProperty, chip.GetResourceObservable(LookKeys.ControlRadius));
        return chip;
    }
}

/// <summary>
/// A tag field scrolling within a bounded height, however many tags it shows, so what is placed below it stays in the
/// window (G17, G19, E4). The focused tag control stays in view as the row grows.
/// </summary>
public sealed class TagFieldScroller : ScrollViewer
{
    private readonly TagField field;

    public TagFieldScroller(TagField field)
    {
        this.field = field;
        Content = field;
        MaxHeight = 148;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        field.SizeChanged += (_, _) => KeepFocusInView();
        field.GotFocus += (_, _) => KeepFocusInView();
    }

    /// <summary>Drawn as the theme draws any scroller.</summary>
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>Reveals the focused ×, or the box and its suggestions while the box holds focus.</summary>
    public void KeepFocusInView()
    {
        var focused = field.FocusedPart;
        if (field.BoxFocused || focused is not null && field.Picker.Contains(focused))
        {
            ScrollToEnd();
        }
        else if (focused is not null && field.IsVisualAncestorOf(focused))
        {
            focused.BringIntoView();
        }
    }
}

/// <summary>
/// "Tags of …" (G17), opened by Type a tag…, Add tags… and <c>Ctrl+T</c>: the tag field over the grid, inside the window's own tree as the drop card and the notices are,
/// never a window of its own (F4), so the flyout keeps focus and stays (G2). It acts on the items it was opened on: it
/// shows the tags they carry, a × taking that tag off every one of them, and scrolls within a bounded height however
/// many they are; its list of every tag marks the tags every one of them carries, and a pick there gives the tag to every
/// one, or takes it off all of them when all carry it, as Add tag ▸ does; Enter gives every one the tags typed, one per word, and leaves it open with the box empty for the
/// next, so several are added in a row; Esc closes it and gives focus back to where it was. Every change goes through
/// the window's one organising entry, which names each refusal (G35). What it shows is read from the store when it
/// opens, after each change and whenever the listing changes, never kept, so a tag no item carries is neither shown
/// nor suggested, and one carried is not suggested (G17). Focus moving elsewhere in the window, by a click, Tab or a
/// summon putting it in the search box, first gives every item the words typed, as Enter would, then closes it, focus
/// staying where it went (<see cref="FieldLeaving"/>, G17, G7). An import beginning to replace the stash closes it with
/// nothing added, so nothing typed into it lands on an item the imported store gives the same number (G36).
/// </summary>
public sealed class TagFieldOverlay : Border
{
    /// <summary>Opening the field over items, as an act for the notices its reading raises (G16).</summary>
    public const string OpenAct = "Type a tag";

    private readonly Listing listing;
    private readonly Organising organising;
    private readonly Notices notices;
    private readonly TextBlock heading;
    private readonly TagFieldScroller scroller;
    private IReadOnlyList<StashItem> items = [];
    private IInputElement? returnTo;
    private int reads;

    public TagFieldOverlay(Listing listing, Organising organising, Notices notices, PressOnce? heldKeys = null, ClipboardPaste? paste = null)
    {
        this.listing = listing;
        this.organising = organising;
        this.notices = notices;
        heading = new TextBlock { FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
        Field = new TagField();
        Field.Picker.ConfigureTagManagement(listing, organising, heldKeys, paste);
        scroller = new TagFieldScroller(Field);
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
        Field.CatalogueRequested += () => _ = Read();
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
        using var acting = UserAct.Begin(OpenAct);
        items = opened;
        returnTo = returnFocusTo;
        heading.Text = "Tags of " + Notices.Describe(opened);
        Field.Clear();
        Field.Show(Organising.CarriedTags(opened), null, CarriedByAll(opened));
        IsVisible = true;
        // Laid out first: the scroller places the field in the tree only once its template applies, and only a box in
        // the tree takes focus.
        UpdateLayout();
        Field.FocusBox();
        scroller.KeepFocusInView();
        _ = Read();
    }

    /// <summary>
    /// Enter: the box emptied, focus left in it, and every item given the tags typed, the field then showing what they
    /// carry; nothing typed, or only <c>#</c>s, changes nothing. The field stays open either way (G17).
    /// </summary>
    private async Task Commit(string text)
    {
        // One act with its reading after it, so what either names is the tag's (G16).
        using var acting = UserAct.Begin(ItemTags.AddAction);
        Field.Clear();
        var opened = items;
        await FieldLeaving.AddTags(organising, opened, text);
        if (IsVisible && ReferenceEquals(items, opened))
        {
            await Read();
        }
    }

    /// <summary>A pick in the list: the tag toggled for every item as Add tag ▸ toggles it, the field and the list then showing what they carry (G17).</summary>
    private async Task Toggle(string tag)
    {
        using var acting = UserAct.Begin(Organising.TagAct);
        var opened = items;
        await Field.Pick(organising, opened, tag);
        if (IsVisible && ReferenceEquals(items, opened))
        {
            await Read();
        }
    }

    /// <summary>The tags every one of <paramref name="carriers"/> carries, which the list marks (G17).</summary>
    private static IReadOnlyList<string> CarriedByAll(IReadOnlyList<StashItem> carriers) =>
        Organising.CarriedTags(carriers).Where(tag => ItemTags.AllCarry(carriers, tag)).ToList();

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
        var loaded = await store.AllWithTagCounts();
        if (read != reads)
        {
            return;
        }

        if (!loaded.Succeeded)
        {
            Field.CatalogueReadFailed();
            notices.Raise(loaded.Failure);
            return;
        }

        var (all, counts) = loaded.Value;
        var present = all.Where(item => ids.Contains(item.Id)).ToList();
        Field.Show(Organising.CarriedTags(present), counts, CarriedByAll(present));
    }

    private void FocusMoved(object? sender, FocusChangedEventArgs e)
    {
        var intoField = Field.OwnsFocus(e.Source) || e.Source is Visual focused && this.IsVisualAncestorOf(focused);
        if (FieldLeaving.Commits(IsVisible, intoField))
        {
            // Started on the items it was opened on before closing lets them go; the act's own reading is then skipped.
            _ = Commit(Field.EnterCommits);
            Close(giveFocusBack: false);
        }
    }
}
