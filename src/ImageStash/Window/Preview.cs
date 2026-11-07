using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using LayerList = ImageStash.Layers.LayerList;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The preview (G19, F4): a layer inside the stash window over the whole of its content, never a window of its own,
/// showing one item at its own size with its animation playing (<see cref="PreviewPicture"/>) and the facts about it
/// (<see cref="PreviewFacts"/>), with a back arrow, the pointer's way out (G7), its name edited in place
/// (<see cref="NameField"/>) by a click on it, F2 or the menus' Rename, its tags with their × and the tag field
/// (<see cref="PreviewTags"/>), the handoff button named as the footer names Enter's handoff, Copy beside it while there
/// is a target to paste into, Edit, and a ⋯ menu of the item menu's entries for its item, Revert to original and Save as
/// new item among them on an edited item (G19, G17, G20, G26). While an edit
/// covers it, it stays open on its item and plays nothing (<see cref="Covered"/>). While it is open its item
/// is what the window acts on: the handoff, the copy, Delete, the favorite flag, Rename and the tags take
/// <see cref="Item"/> as last read, never the grid's selection, which a refresh may move (G8, G17, G18); its buttons ask
/// the window for the same handoff and copy Enter and <c>Ctrl+C</c> make. It reads its item
/// again by identity from the store when it opens, after every change it makes and whenever the listing changes,
/// whether or not the listing still lists it, the latest read alone applied; it closes when the item is no longer in the
/// stash and stays open on one that merely left the scope or the query. Opening selects the item alone and gives the preview focus; closing gives focus back to the
/// grid, whose hovered-or-selected tile then plays again (G6).
/// </summary>
public sealed class Preview : Border
{
    private readonly Listing listing;
    private readonly Handoff handoff;
    private readonly Organising organising;
    private readonly Deletion deletion;
    private readonly Renaming renaming;
    private readonly ItemMenu itemMenu;
    private readonly Notices notices;
    private readonly TimeProvider clock;
    private readonly PasteTargetHolder pasteTarget;
    private readonly Action<bool> dialogUp;
    private readonly IInputElement returnFocusTo;
    private readonly NameField name;
    private readonly Button handOff;
    private readonly Button copy;
    private readonly Button edit;
    private readonly PreviewPicture picture;
    private readonly TextBlock pictureFacts;
    private readonly TextBlock useFacts;
    private readonly TextBlock editFacts;
    private readonly PreviewTags tags;
    private readonly DispatcherTimer restating;
    private int reads;
    private int openings;

    /// <summary>What the facts were last stated from, which their times are stated again from as time passes.</summary>
    private (StashItem Item, long? FileBytes, UseHistory Uses, LayerList Layers)? stated;

    /// <summary>The rename in flight with the read of its answer; done while none runs.</summary>
    private Task renamed = Task.CompletedTask;

    /// <param name="pasteTarget">The paste target the footer names, which names the handoff button.</param>
    /// <param name="dialogUp">Told while Save a copy…'s picker or Open with…'s dialog is up, so the flyout stays (G2).</param>
    /// <param name="returnFocusTo">Where focus goes when the preview closes: the grid.</param>
    public Preview(Listing listing, Handoff handoff, Organising organising, Deletion deletion, Renaming renaming, ItemMenu itemMenu, Notices notices, TimeProvider clock,
        PasteTargetHolder pasteTarget, Action<bool> dialogUp, IInputElement returnFocusTo)
    {
        this.listing = listing;
        this.handoff = handoff;
        this.organising = organising;
        this.deletion = deletion;
        this.renaming = renaming;
        this.itemMenu = itemMenu;
        this.notices = notices;
        this.clock = clock;
        this.pasteTarget = pasteTarget;
        this.dialogUp = dialogUp;
        this.returnFocusTo = returnFocusTo;

        var back = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 },
            Width = 36,
            Height = 36,
            Padding = default,
            BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        back.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(back, "Back");
        ToolTipRule.Set(back, "Back (Esc)");
        back.Click += (_, _) => Dismiss();
        name = new NameField { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
        var more = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 },
            Width = 36,
            Height = 36,
            Padding = default,
            BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        more.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(more, "More options");
        ToolTipRule.Set(more, "More options");
        more.Click += (_, _) =>
        {
            if (Item is { } shown)
            {
                ItemMenu.ShowBelow(more, ItemMenu.PreviewEntries(shown), action => _ = FromMenu(action));
            }
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(10, 10, 10, 8) };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(more, 2);
        head.Children.Add(back);
        head.Children.Add(name);
        head.Children.Add(more);

        picture = new PreviewPicture(failure => notices.Raise(failure)) { Margin = new Thickness(10, 0) };
        // Either key gives focus back to the preview, whose Enter then hands off again (G7, G19).
        name.Committed += text =>
        {
            picture.Focus(NavigationMethod.Tab);
            _ = Rename(text);
        };
        name.Cancelled += () => picture.Focus(NavigationMethod.Tab);
        pictureFacts = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        useFacts = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.6 };
        editFacts = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.6 };
        var facts = new StackPanel { Spacing = 2, Margin = new Thickness(12, 10, 12, 14), Children = { pictureFacts, useFacts, editFacts } };
        tags = new PreviewTags { Margin = new Thickness(12, 0, 12, 12) };
        // Enter in the tag field leaves focus in its box, so several tags are added in a row (G17); Esc, as the name's
        // Esc does, gives focus back to the preview, whose Enter then hands off again (G19, G7).
        tags.Committed += text => _ = AddTags(text);
        tags.Cancelled += () => picture.Focus(NavigationMethod.Tab);
        tags.Removed += tag => _ = Act(item => organising.RemoveTag([item], tag));
        // A pick in the list of every tag gives the tag or takes it off, as Add tag ▸ does (G17).
        tags.Picked += tag => _ = Act(item => organising.ToggleTag([item], tag));

        handOff = new Button { Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(handOff, "Handoff");
        handOff.Click += (_, _) => HandOverRequested?.Invoke();
        copy = new Button { Content = Labelled("", "Copy", null) };
        AutomationProperties.SetAutomationId(copy, "Copy");
        AutomationProperties.SetName(copy, "Copy");
        copy.Click += (_, _) => CopyRequested?.Invoke();
        copy.Margin = new Thickness(8, 0, 0, 0);
        edit = new Button { Content = Labelled("", "Edit", "Ctrl+E"), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(edit, "Edit");
        AutomationProperties.SetName(edit, "Edit");
        edit.Click += (_, _) => EditRequested?.Invoke();
        // The handoff button takes what Copy and Edit leave, its words trimmed, so a long app name never pushes them out
        // of the window (G19, G7); left-aligned, the row is no wider than its buttons, Copy and Edit right beside it.
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 12, 12) };
        Grid.SetColumn(copy, 1);
        Grid.SetColumn(edit, 2);
        actions.Children.Add(handOff);
        actions.Children.Add(copy);
        actions.Children.Add(edit);
        pasteTarget.Changed += NameHandoff;
        NameHandoff();

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto") };
        Grid.SetRow(picture, 1);
        Grid.SetRow(facts, 2);
        Grid.SetRow(tags, 3);
        Grid.SetRow(actions, 4);
        layout.Children.Add(head);
        layout.Children.Add(picture);
        layout.Children.Add(facts);
        layout.Children.Add(tags);
        layout.Children.Add(actions);
        Child = layout;
        // Tab goes round the preview's own controls and never reaches the box or the grid it covers (G7).
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        this.Bind(BackgroundProperty, this.GetResourceObservable(LookKeys.Surface));
        IsVisible = false;

        listing.Changed += () =>
        {
            if (IsOpen)
            {
                _ = Refresh();
            }
        };
        // "today", "just now" and "4 min ago" stay true while it stays open (G19).
        restating = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        restating.Tick += (_, _) => RestateTimes();
    }

    /// <summary>Raised when the preview opens or closes and after each read of its item is applied.</summary>
    public event Action? Changed;

    /// <summary>Raised by a click on the handoff button: the window hands over the item as Enter does (G19, G8).</summary>
    public event Action? HandOverRequested;

    /// <summary>Raised by a click on Copy: the window copies the item as <c>Ctrl+C</c> does (G19, G9).</summary>
    public event Action? CopyRequested;

    /// <summary>Raised by Edit and the ⋯ menu's Edit: the window opens the editor on the item (G19, G20).</summary>
    public event Action? EditRequested;

    public bool IsOpen { get; private set; }

    /// <summary>Set while an open edit covers the preview: its animation is held, as while the window is hidden (G6).</summary>
    public bool Covered
    {
        get => picture.Covered;
        set => picture.Covered = value;
    }

    /// <summary>The item as the preview last read it; null while closed.</summary>
    public StashItem? Item { get; private set; }

    /// <summary>The facts it states, as last read; none until the first read after opening has arrived.</summary>
    public IReadOnlyList<string> Facts { get; private set; } = [];

    /// <summary>The preview's own element that takes focus when it opens, and where its fields give focus back.</summary>
    public IInputElement FocusTarget => picture;

    /// <summary>Whether a button of the preview has focus, which then takes Space as its own press (G7).</summary>
    public bool ButtonFocused => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Button button && this.IsVisualAncestorOf(button);

    /// <summary>
    /// Opens on <paramref name="item"/>, or moves to it from the item it showed: the item alone selected in the grid,
    /// focus inside the preview, its picture and name at once and its facts once read (G19, G7); with
    /// <paramref name="editingName"/>, as F2 and the item menu's Rename open it, its name in edit, selected (G17).
    /// </summary>
    public void Open(StashItem item, bool editingName = false)
    {
        reads++;
        openings++;
        Item = item;
        IsOpen = true;
        IsVisible = true;
        restating.Start();
        name.StopEditing();
        name.Show(item);
        ShowFacts([]);
        tags.Clear();
        tags.Show(Organising.CarriedTags([item]), []);
        picture.Show(item);
        listing.Focus(item.Id);
        Changed?.Invoke();
        picture.Focus(NavigationMethod.Tab);
        if (editingName)
        {
            name.Edit();
        }

        _ = Refresh();
    }

    /// <summary>F2 in the preview and the ⋯ menu's Rename: its name in edit, selected, with focus in it (G17, G19).</summary>
    public void EditName()
    {
        if (IsOpen)
        {
            name.Edit();
        }
    }

    /// <summary>Whether its name is being edited.</summary>
    public bool EditingName => name.IsEditing;

    /// <summary>
    /// Focus as it was left, as a summon gives it back (G1): in the name while it is being edited, else in the tag field
    /// when focus was last there, else on <see cref="FocusTarget"/>.
    /// </summary>
    public void FocusAsLeft()
    {
        if (name.IsEditing)
        {
            name.FocusBox();
        }
        else if (tags.HadFocus)
        {
            tags.FocusBox();
        }
        else
        {
            FocusTarget.Focus();
        }
    }

    /// <summary>
    /// Esc, Space and its back arrow: closes it once a rename in flight has been answered and read, so the grid it
    /// uncovers lists the item where the store put its file, and a handoff from there hands that file over (G8, P1).
    /// </summary>
    public void Dismiss()
    {
        if (renamed.IsCompleted)
        {
            Close();
            return;
        }

        _ = CloseAfterRename(openings);
    }

    /// <summary>Closes it, its play stopped and every read in flight dropped, and gives focus back to the grid (G6).</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        reads++;
        IsOpen = false;
        IsVisible = false;
        restating.Stop();
        Item = null;
        name.StopEditing();
        tags.Clear();
        picture.Clear();
        ShowFacts([]);
        Changed?.Invoke();
        returnFocusTo.Focus(NavigationMethod.Tab);
    }

    /// <summary>
    /// An import is replacing the stash (G36, G19): closed, so it shows nothing of the replaced item nor of an imported
    /// one its identity is given to; completes once its play has closed the item's file.
    /// </summary>
    public Task LetGo()
    {
        Close();
        return picture.Idle();
    }

    /// <summary>
    /// Reads the item again by its identity, with its file's size, its use history, every tag that exists, which the
    /// tag field suggests from, and on an edited item its layers; only the latest read applies, and opening or closing
    /// drops every read before it. An item the stash no longer lists closes the preview; a read the store refuses keeps
    /// what shows and is named (G35).
    /// </summary>
    public async Task Refresh()
    {
        if (Item is not { } item || listing.Store is not { } store)
        {
            return;
        }

        var read = ++reads;
        var found = await store.Item(item.Id);
        var uses = found.Value is null ? null : await store.UseHistory(item.Id);
        var counts = found.Value is null ? null : await store.TagCounts();
        var fileBytes = found.Value is { } current ? await Task.Run(() => PreviewFacts.FileLength(current.FilePath)) : null;
        var layers = found.Value is { } edited ? await PreviewFacts.EditedLayers(store, edited) : null;
        if (read != reads)
        {
            return;
        }

        if (!found.Succeeded || uses is { Succeeded: false } || counts is { Succeeded: false } || layers is { Succeeded: false })
        {
            notices.Raise(found.Failure ?? uses?.Failure ?? counts?.Failure ?? layers!.Failure!);
            return;
        }

        if (found.Value is not { } now)
        {
            Close();
            return;
        }

        Item = now;
        name.Show(now);
        tags.Show(Organising.CarriedTags([now]), counts!.Value!.Select(count => count.Tag).ToList());
        picture.Show(now);
        stated = (now, fileBytes, uses!.Value!, layers!.Value!);
        RestateTimes();
        Changed?.Invoke();
    }

    /// <summary>Enter: the handoff on the item, into <paramref name="target"/> or onto the clipboard (G8); the preview stays open, in the hidden flyout after a paste.</summary>
    public Task<Outcome> HandOver(TopLevel owner, PasteTarget? target, Action hideFlyout) =>
        Act(item => handoff.HandOver(owner, [item], target, hideFlyout));

    /// <summary><c>Ctrl+C</c>: the item onto the clipboard, the window staying (G9).</summary>
    public Task<Outcome> Copy(TopLevel owner) => Act(item => handoff.Copy(owner, [item]));

    /// <summary>Del: the item deleted through the window's one Delete, with its notice and Undo; the preview closes once it is gone (G18).</summary>
    public Task<Outcome> Delete() => Act(item => deletion.Delete([item]));

    /// <summary><c>Ctrl+D</c>: the item's favorite flag toggled through the window's one organising entry (G17).</summary>
    public Task<Outcome> ToggleFavorite() => Act(item => organising.ToggleFavorite([item]));

    /// <summary>
    /// The name field's Enter: the item renamed through the window's one rename path; the head shows the name it showed
    /// until the store's answer is read again, so a new name never shows before the store has it (G17, G28, R9).
    /// </summary>
    private Task<Outcome> Rename(string text)
    {
        var running = Act(item => renaming.Rename(item, text));
        renamed = running;
        return running;
    }

    /// <summary>
    /// The tag field's Enter: every tag typed, one per word, onto the item through the window's one organising entry;
    /// nothing typed, or only <c>#</c>s, changes nothing (G17: nothing requires a tag).
    /// </summary>
    private Task<Outcome> AddTags(string text) =>
        ItemTags.Words(text).Count > 0 ? Act(item => organising.AddTag([item], text)) : Task.FromResult(Outcome.Done);

    /// <summary>
    /// The ⋯ menu's entry on the item as last read: Edit opens the editor on it; Rename puts its name in edit; Revert to
    /// original and Save as new item, Save a copy…, Open with… and Reveal in File Explorer as the item menu does them,
    /// recording no use, a new item going on in the editor with the preview beneath moved to it; and Delete as Del does
    /// it (G17, G9, G10, G18, G26, G27).
    /// </summary>
    private Task<Outcome> FromMenu(ItemMenuAction action) => action switch
    {
        ItemMenuAction.Edit => EditOnceTheMenuHasClosed(),
        ItemMenuAction.Rename => EditNameOnceTheMenuHasClosed(),
        ItemMenuAction.RevertToOriginal => Act(item => itemMenu.OnEdited(action, item, EditOrigin.Preview)),
        ItemMenuAction.SaveAsNewItem => OnItem(item => itemMenu.OnEdited(action, item, EditOrigin.Preview)),
        ItemMenuAction.SaveACopy => OnItem(item => TopLevel.GetTopLevel(this) is { } owner ? itemMenu.SaveACopy(owner, item, dialogUp) : Task.FromResult(Outcome.Done)),
        ItemMenuAction.OpenWith => OnItem(item => TopLevel.GetTopLevel(this) is { } owner ? itemMenu.OpenWith(owner, item, dialogUp) : Task.FromResult(Outcome.Done)),
        ItemMenuAction.Reveal => OnItem(item => Task.FromResult(itemMenu.RevealInExplorer(item))),
        ItemMenuAction.Delete => Delete(),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not an entry of the preview's menu"),
    };

    /// <summary>The ⋯ menu's Edit, posted until the menu has closed, whose own focus going back would otherwise land beneath the editor.</summary>
    private Task<Outcome> EditOnceTheMenuHasClosed()
    {
        Dispatcher.UIThread.Post(() => EditRequested?.Invoke(), DispatcherPriority.Background);
        return Task.FromResult(Outcome.Done);
    }

    /// <summary>The ⋯ menu's Rename, posted until the menu has closed, whose own focus going back would otherwise end the edit at once.</summary>
    private Task<Outcome> EditNameOnceTheMenuHasClosed()
    {
        Dispatcher.UIThread.Post(EditName, DispatcherPriority.Background);
        return Task.FromResult(Outcome.Done);
    }

    private async Task CloseAfterRename(int opening)
    {
        await renamed;
        if (opening == openings)
        {
            Close();
        }
    }

    /// <summary>
    /// The handoff button reads as the footer's Enter does, Paste into the target's app or Copy, and Copy shows beside it
    /// only while there is a target, as the footer's <c>Ctrl+C</c> does: without one the handoff is the copy (G19, G8).
    /// </summary>
    private void NameHandoff()
    {
        var target = pasteTarget.Current;
        var words = PasteTargetHolder.HandoffName(target);
        var content = Labelled(target is null ? "" : "", words, "Enter");
        handOff.Content = content;
        AutomationProperties.SetName(handOff, words);
        // A long app name trims the words: the tip then holds them whole.
        ToolTipRule.ShowWhenTrimmed(handOff, (TextBlock)content.Children[1], words);
        copy.IsVisible = target is not null;
    }

    /// <summary>
    /// A button's content: its icon, its words and, faint after them, the key that does the same; the words trimmed when
    /// the button has less room than they need.
    /// </summary>
    private static Grid Labelled(string glyph, string words, string? key)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var text = new TextBlock { Text = words, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        if (key is not null)
        {
            var hint = new TextBlock { Text = key, FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(hint, 2);
            content.Children.Add(hint);
        }

        return content;
    }

    /// <summary>One change on the item as last read, then the item read again, so what shows is the store's answer.</summary>
    private async Task<Outcome> Act(Func<StashItem, Task<Outcome>> change)
    {
        var outcome = await OnItem(change);
        await Refresh();
        return outcome;
    }

    /// <summary>
    /// <paramref name="act"/> on the item as last read once a rename in flight has been answered and read, so a handoff,
    /// a copy or File Explorer gets its file where the store put it, never where it was (G8, G9, P1). A key pressed while
    /// the preview showed the item still acts on it when a close pressed before it, waiting for the same rename, has
    /// closed the preview meanwhile, or it has opened on another item since: on the item as the store now has it, and on
    /// nothing once it is gone from the stash, which the preview's closing showed (G8, G2, G7).
    /// </summary>
    private async Task<Outcome> OnItem(Func<StashItem, Task<Outcome>> act)
    {
        var asked = Item;
        await renamed;
        if (Item is { } item && item.Id == asked?.Id)
        {
            return await act(item);
        }

        if (asked is null || listing.Store is not { } store)
        {
            return Outcome.Done;
        }

        var now = await store.Item(asked.Id);
        if (!now.Succeeded)
        {
            notices.Raise(now.Failure);
            return now.Failure;
        }

        return now.Value is { } stored ? await act(stored) : Outcome.Done;
    }

    /// <summary>The facts as last read, their times stated as they are now.</summary>
    private void RestateTimes()
    {
        if (stated is { } read)
        {
            ShowFacts(PreviewFacts.Lines(read.Item, read.FileBytes, read.Uses, read.Layers, clock.GetUtcNow(), clock.LocalTimeZone));
        }
    }

    private void ShowFacts(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            stated = null;
        }

        Facts = lines;
        pictureFacts.Text = lines.Count > 0 ? lines[0] : "";
        useFacts.Text = lines.Count > 1 ? lines[1] : "";
        editFacts.Text = lines.Count > 2 ? lines[2] : "";
        editFacts.IsVisible = lines.Count > 2;
    }
}
