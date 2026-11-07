using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>What an entry of the item menu does.</summary>
public enum ItemMenuAction
{
    Paste,
    Copy,

    /// <summary>The preview on the one item (G19).</summary>
    Preview,

    /// <summary>The editor on the one item (G20).</summary>
    Edit,

    /// <summary>Every layer of the one edited item removed at once, with a notice and Undo (G26).</summary>
    RevertToOriginal,

    /// <summary>A second item made of the one edited item, then edited (G26).</summary>
    SaveAsNewItem,
    AddToFavorites,
    RemoveFromFavorites,

    /// <summary>The preview on the one item with its name in edit (G17).</summary>
    Rename,

    /// <summary>Without a tag, the Add tag ▸ submenu; with one, that submenu's entry giving the tag.</summary>
    AddTag,

    /// <summary>Without a tag, the Remove tag ▸ submenu; with one, that submenu's entry taking the tag off.</summary>
    RemoveTag,

    /// <summary>Add tag ▸'s Type a tag…, which opens the tag field over the items.</summary>
    TypeATag,
    SaveACopy,

    /// <summary>The Open-with dialog on a copy of the one item's finished picture (G27).</summary>
    OpenWith,
    Reveal,
    Delete,
}

/// <summary>
/// An entry of the item menu as it shows: its words, its key, its icon, whether a separator comes before it, and on an
/// entry of a tag list, its tag and whether it is marked.
/// </summary>
public sealed record ItemMenuEntry(ItemMenuAction Action, string Label, KeyGesture? Gesture, string Glyph, bool AfterSeparator = false, string? Tag = null, bool Marked = false);

/// <summary>
/// The item menu a right-click on a tile opens (G8, G9, G17, G18, G19, G20, G26): the handoff first, which with a paste target is
/// Paste into that app, with Copy after it, and with none is Copy; for one item Preview and Edit, and on an edited item
/// Revert to original and Save as new item; then Add to Favorites, or Remove from Favorites while
/// every item is one; for one item Rename; Add tag ▸, every tag in the stash and Type a tag…; Remove tag ▸, the tags the
/// items carry, while they carry one (<see cref="TagLists"/>); then, for one item, Save a copy…, Open with… and Reveal
/// in File Explorer; last, Delete. An entry shows only once its action works, so the rest of the menu arrives with its
/// actions and nothing is inert. Save a copy…, Open with… and Reveal are this menu's own actions; the favorites and
/// tag entries are the window's one organising entry, Delete its one Delete, Preview and Rename open its preview,
/// Rename with the name in edit, and Edit, Revert to original and Save as new item are its one editing entry's.
/// </summary>
public sealed class ItemMenu(Listing listing, Notices notices, Handoff handoff, Organising organising, Deletion deletion, Editing editing, OpenWith openWith, IDialogsAndExplorer dialogs, TimeSpan heldFileRetry)
{
    public const string SaveACopyAction = "Save a copy";

    // How many Save a copy… writes run, from the last refusal they passed until the file is written.
    private int savingACopy;

    private static readonly ItemMenuEntry Copy = new(ItemMenuAction.Copy, "Copy", new KeyGesture(Key.Enter), "");
    private static readonly ItemMenuEntry CopyBesidePaste = Copy with { Gesture = new KeyGesture(Key.C, KeyModifiers.Control) };
    private static readonly ItemMenuEntry PreviewEntry = new(ItemMenuAction.Preview, "Preview", new KeyGesture(Key.Space), "");
    private static readonly ItemMenuEntry EditEntry = new(ItemMenuAction.Edit, "Edit", new KeyGesture(Key.E, KeyModifiers.Control), "");
    private static readonly ItemMenuEntry RevertEntry = new(ItemMenuAction.RevertToOriginal, "Revert to original", null, "");
    private static readonly ItemMenuEntry SaveAsNewItemEntry = new(ItemMenuAction.SaveAsNewItem, "Save as new item", null, "");
    private static readonly ItemMenuEntry RenameEntry = new(ItemMenuAction.Rename, "Rename", new KeyGesture(Key.F2), "");
    private static readonly ItemMenuEntry SaveACopyEntry = new(ItemMenuAction.SaveACopy, "Save a copy…", null, "", AfterSeparator: true);
    private static readonly ItemMenuEntry DeleteEntry = new(ItemMenuAction.Delete, "Delete", new KeyGesture(Key.Delete), "", AfterSeparator: true);
    private static readonly ItemMenuEntry OpenWithEntry = new(ItemMenuAction.OpenWith, "Open with…", null, "");
    private static readonly ItemMenuEntry Reveal = new(ItemMenuAction.Reveal, "Reveal in File Explorer", null, "");
    private static readonly ItemMenuEntry AddToFavorites = new(ItemMenuAction.AddToFavorites, "Add to Favorites", new KeyGesture(Key.D, KeyModifiers.Control), "", AfterSeparator: true);
    private static readonly ItemMenuEntry RemoveFromFavorites = new(ItemMenuAction.RemoveFromFavorites, "Remove from Favorites", new KeyGesture(Key.D, KeyModifiers.Control), "", AfterSeparator: true);

    private static readonly ItemMenuEntry AddTagMenu = new(ItemMenuAction.AddTag, "Add tag", null, "");
    private static readonly ItemMenuEntry RemoveTagMenu = new(ItemMenuAction.RemoveTag, "Remove tag", null, "");

    /// <summary>The entries for a selection of <paramref name="items"/>, in order, with <paramref name="target"/> the paste target or null.</summary>
    public static IReadOnlyList<ItemMenuEntry> Entries(IReadOnlyList<StashItem> items, PasteTarget? target)
    {
        IReadOnlyList<ItemMenuEntry> handoff = target is null
            ? [Copy]
            : [new ItemMenuEntry(ItemMenuAction.Paste, PasteTargetHolder.HandoffName(target), new KeyGesture(Key.Enter), ""), CopyBesidePaste];
        // G17's mixed rule, as Ctrl+D toggles it: Remove only while every item is a favorite.
        var favorites = Organising.AllFavorites(items) ? RemoveFromFavorites : AddToFavorites;
        // Remove tag ▸ only while there is a tag to take off, for one item as for several: nothing is inert.
        IReadOnlyList<ItemMenuEntry> tags = Organising.CarriedTags(items).Count > 0 ? [AddTagMenu, RemoveTagMenu] : [AddTagMenu];
        return items.Count == 1
            ? [.. handoff, PreviewEntry, EditEntry, .. Edited(items[0], afterSeparator: false), favorites, RenameEntry, .. tags, SaveACopyEntry, OpenWithEntry, Reveal, DeleteEntry]
            : [.. handoff, favorites, .. tags, DeleteEntry with { Label = $"Delete {items.Count} items" }];
    }

    /// <summary>
    /// The preview's ⋯ menu (G19): Edit, Rename and, on an edited item, after a separator, Revert to original and Save as
    /// new item; after a separator, Save a copy…, Open with… and Reveal in File Explorer; after a separator, Delete; on
    /// <paramref name="item"/>, the one item the preview shows. Its other entries arrive with their actions.
    /// </summary>
    public static IReadOnlyList<ItemMenuEntry> PreviewEntries(StashItem item) =>
        [EditEntry, RenameEntry, .. Edited(item, afterSeparator: true), SaveACopyEntry, OpenWithEntry, Reveal, DeleteEntry];

    /// <summary>Revert to original and Save as new item, on an edited item only, so neither is ever inert (G19).</summary>
    private static IReadOnlyList<ItemMenuEntry> Edited(StashItem item, bool afterSeparator) =>
        item.Edited ? [RevertEntry with { AfterSeparator = afterSeparator }, SaveAsNewItemEntry] : [];

    /// <summary>
    /// Opens <paramref name="entries"/>, drawn as this menu draws them, below <paramref name="button"/> with its right edge
    /// at the button's, each entry's click handing its action to <paramref name="chosen"/>: the preview's ⋯ menu, whose
    /// entries act on the item the preview shows. A menu inside the window, as the item menu is, not a window (F4).
    /// </summary>
    public static void ShowBelow(Control button, IReadOnlyList<ItemMenuEntry> entries, Action<ItemMenuAction> chosen)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var entry in entries)
        {
            Add(menu, entry).Click += (_, _) => chosen(entry.Action);
        }

        menu.Open(button);
    }

    /// <summary>
    /// Opens the menu for the selection as it is now, at <paramref name="anchor"/> in <paramref name="target"/>'s
    /// coordinates, naming <paramref name="pasteTarget"/> as the footer does now; its entries act on that selection and
    /// paste into that target. <paramref name="dialogUp"/> says when Save a copy…'s picker or Open with…'s dialog is up;
    /// <paramref name="hideFlyout"/> is the paste's hide (G2); <paramref name="typeATag"/> opens the tag field over the
    /// items, <paramref name="preview"/> the preview on the one item, <paramref name="rename"/> the preview on it with
    /// its name in edit, and <paramref name="edit"/> the editor on it.
    /// </summary>
    public void Show(Control target, Rect anchor, TopLevel owner, PasteTarget? pasteTarget, Action hideFlyout, Action<bool> dialogUp, Action<IReadOnlyList<StashItem>> typeATag,
        Action<StashItem> preview, Action<StashItem> rename, Action<StashItem> edit)
    {
        var items = listing.SelectedItems;
        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu(new TagListDrag()) { Placement = PlacementMode.BottomEdgeAlignedLeft, PlacementRect = anchor, Styles = { TagListHeight.ItemMenuLists(TagListHeight.For(target)) } };
        var lists = new ShownTagLists(listing, notices, organising, items, typeATag);
        foreach (var entry in Entries(items, pasteTarget))
        {
            var item = Add(menu, entry);
            if (entry.Action == ItemMenuAction.AddTag)
            {
                lists.AddTag = item;
            }
            else if (entry.Action == ItemMenuAction.RemoveTag)
            {
                lists.RemoveTag = item;
            }
            else if (entry.Action == ItemMenuAction.Preview)
            {
                item.Click += (_, _) => preview(items[0]);
            }
            else if (entry.Action == ItemMenuAction.Rename)
            {
                item.Click += (_, _) => rename(items[0]);
            }
            else if (entry.Action == ItemMenuAction.Edit)
            {
                item.Click += (_, _) => edit(items[0]);
            }
            else
            {
                item.Click += (_, _) => _ = Act(entry.Action, owner, items, pasteTarget, hideFlyout, dialogUp);
            }
        }

        // Remove tag ▸ is there, hidden, while the items carry nothing, to show once a click in Add tag ▸ gives them a tag.
        if (lists.RemoveTag is null && lists.AddTag is { } addTag)
        {
            lists.RemoveTag = MenuEntry(RemoveTagMenu);
            lists.RemoveTag.IsVisible = false;
            menu.Items.Insert(menu.Items.IndexOf(addTag) + 1, lists.RemoveTag);
        }

        lists.AddTag?.Classes.Add(TagListHeight.ItemMenuList);
        lists.RemoveTag?.Classes.Add(TagListHeight.ItemMenuList);
        lists.Fill(TagLists.AddTagEntries([], items), TagLists.RemoveTagEntries(items));
        _ = lists.Read();
        menu.Open(target);
    }

    /// <summary>What an entry does, on the items the menu was opened on, pasting into the target it named.</summary>
    public Task<Outcome> Act(ItemMenuAction action, TopLevel owner, IReadOnlyList<StashItem> items, PasteTarget? pasteTarget, Action hideFlyout, Action<bool> dialogUp) => action switch
    {
        ItemMenuAction.Paste => handoff.HandOver(owner, items, pasteTarget, hideFlyout),
        ItemMenuAction.Copy => handoff.Copy(owner, items),
        ItemMenuAction.AddToFavorites => organising.SetFavorite(items, true),
        ItemMenuAction.RemoveFromFavorites => organising.SetFavorite(items, false),
        ItemMenuAction.SaveACopy => SaveACopy(owner, items[0], dialogUp),
        ItemMenuAction.OpenWith => OpenWith(owner, items[0], dialogUp),
        ItemMenuAction.Delete => deletion.Delete(items),
        ItemMenuAction.RevertToOriginal or ItemMenuAction.SaveAsNewItem => OnEdited(action, items[0], EditOrigin.Grid),
        _ => Task.FromResult(RevealInExplorer(items[0])),
    };

    /// <summary>
    /// Revert to original or Save as new item on <paramref name="item"/>, from this menu or the preview's ⋯
    /// (<paramref name="from"/>), through the window's one editing entry (G26).
    /// </summary>
    public Task<Outcome> OnEdited(ItemMenuAction action, StashItem item, EditOrigin from) =>
        action == ItemMenuAction.RevertToOriginal ? editing.RevertToOriginal(item) : editing.SaveAsNewItem(item, from);

    /// <summary>
    /// Save a copy… (G9): the standard save picker, up while <paramref name="dialogUp"/> says so, so the flyout stays
    /// (G2, F4), suggesting the item's file name and offering its format alone, and asking before an existing file is
    /// replaced (S16); then the finished picture's bytes as they are, written there off the UI thread with a held file
    /// retried (G35). A path inside the stash folder is refused before anything is written, because only Store creates,
    /// renames or removes files there (G29). A cancelled picker changes and says nothing; a failure is named.
    /// </summary>
    /// <summary>Raised whenever <see cref="SavingACopy"/> changes.</summary>
    public event Action? Changed;

    /// <summary>A Save a copy… writes its file, which a Move waits for (State: Move).</summary>
    public bool SavingACopy => Volatile.Read(ref savingACopy) > 0;

    public async Task<Outcome> SaveACopy(TopLevel owner, StashItem item, Action<bool> dialogUp)
    {
        // From a Move's asking nothing but a handoff, a copy or a drag out starts: this would write beside a stash in
        // motion, perhaps into the folder it is copied to (State: Move); asked again once the picker has answered.
        if (listing.CannotStartReason is { } refused)
        {
            return Named(new Failure(SaveACopyAction, ItemQueries.Named(item.Name, item.FileName), refused));
        }

        string? chosen;
        bool confirmed;
        // An import while the picker is up gives this item's identity to another item, whose picture this would copy.
        var replacements = listing.Replacements;
        dialogUp(true);
        try
        {
            chosen = await dialogs.SaveFile(owner, SaveACopyAction, item.FileName, FileType(item.FileName));
            // Only a file the picker found there, and so asked about, is replaced: judged as it answers, before anything
            // else runs; one another program makes later is not.
            confirmed = chosen is not null && File.Exists(chosen);
        }
        catch (Exception e)
        {
            // The platform's dialog failing is a failure to name, not a crash (G35).
            return Named(new Failure(SaveACopyAction, "the file picker", e.Message));
        }
        finally
        {
            dialogUp(false);
        }

        if (chosen is null)
        {
            return Outcome.Done;
        }

        if (listing.Replacements != replacements)
        {
            return Named(new Failure(SaveACopyAction, ItemQueries.Named(item.Name, item.FileName), Listing.ReplacedSinceReason));
        }

        if (listing.Store is not { } store)
        {
            return Named(new Failure(SaveACopyAction, Path.GetFileName(chosen), listing.NotOpenReason!));
        }

        // Confirmed again by its identity: while the picker was up a Move may have carried the stash elsewhere, or a rename
        // moved the file, so the picture copied is the finished picture where it is now (G9, G30); its file name stays in
        // hand until the copy is written, so a rename never puts another item's picture where a held file's retry reads.
        using var inHand = await store.Confirm([item], SaveACopyAction);
        if (listing.CannotStartReason is { } refusedSince)
        {
            return Named(new Failure(SaveACopyAction, Path.GetFileName(chosen), refusedSince));
        }

        if (inHand.Items[0] is not { Succeeded: true, Value: { } current })
        {
            return Named(inHand.Items[0].Failure!);
        }

        if (InsideStash(chosen))
        {
            return Named(new Failure(SaveACopyAction, Path.GetFileName(chosen), "it would be inside the stash folder, which holds only ImageStash's own files"));
        }

        // In the turn of the refusal it passed, so a Move asked from now on waits until the file is written.
        Interlocked.Increment(ref savingACopy);
        Changed?.Invoke();
        Outcome copied;
        try
        {
            copied = await Task.Run(() => HeldFileRetry.Run(SaveACopyAction, chosen, heldFileRetry, () => File.Copy(current.FilePath, chosen, overwrite: confirmed)));
        }
        finally
        {
            Interlocked.Decrement(ref savingACopy);
            Changed?.Invoke();
        }

        return copied.Succeeded ? copied : Named(copied.Failure);
    }

    /// <summary>
    /// Open with… (G27, G9): the Open-with dialog on the copy of the finished picture Store keeps outside the stash folder,
    /// up while <paramref name="dialogUp"/> says so, so the flyout stays (G2, F4), the copy watched from before it opens;
    /// a refusal to begin, or the dialog's own failure, is named here, and what follows above the tray (G35).
    /// </summary>
    public async Task<Outcome> OpenWith(TopLevel owner, StashItem item, Action<bool> dialogUp)
    {
        var opened = await openWith.Begin(owner, item, dialogUp);
        return opened.Succeeded ? opened : Named(opened.Failure);
    }

    /// <summary>Reveal in File Explorer (G9): the stash folder with the finished picture's file selected; a failure is named.</summary>
    public Outcome RevealInExplorer(StashItem item)
    {
        var revealed = dialogs.Reveal(item.FilePath);
        return revealed.Succeeded ? revealed : Named(revealed.Failure);
    }

    /// <summary>The save picker's one file type: the finished picture's format by its file's name, with that format's extensions.</summary>
    public static PickerFilter FileType(string fileName)
    {
        var format = FormatByName.Judge(fileName).Format;
        var words = format switch
        {
            ImageFormat.Png => "PNG",
            ImageFormat.Jpeg => "JPG",
            ImageFormat.Gif => "GIF",
            ImageFormat.WebP => "WebP",
            _ => Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant(),
        };
        var patterns = format is { } known ? ImageFormatExtensions.Of(known).Select(extension => "*" + extension).ToList() : ["*" + Path.GetExtension(fileName)];
        return new PickerFilter($"{words} picture", patterns);
    }

    /// <summary>
    /// Whether <paramref name="path"/> lies in the stash folder or below it, compared as Windows compares paths, ignoring
    /// case, once every link along either (a junction or a symbolic link) is followed, so no other spelling reaches it.
    /// </summary>
    private bool InsideStash(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Resolved(listing.Store!.Folder.Root)) + Path.DirectorySeparatorChar;
        return Resolved(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The full path, each folder or file along it that is a link replaced by its final target; a link that cannot be followed is kept as spelled.</summary>
    private static string Resolved(string path)
    {
        var full = Path.GetFullPath(path);
        var resolved = Path.GetPathRoot(full)!;
        foreach (var part in full[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            try
            {
                FileSystemInfo entry = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
                if (entry.LinkTarget is not null && entry.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    resolved = target.FullName;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return resolved;
    }

    private static MenuItem MenuEntry(ItemMenuEntry entry) => new()
    {
        // A header's underscore marks an access key; doubled, it shows as typed, as a tag or an app's name may hold one.
        Header = entry.Label.Replace("_", "__"),
        InputGesture = entry.Gesture,
        Icon = new TextBlock { Text = entry.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center },
        // A tag list's entry shows its mark and leaves the menu open for the next (G17); every other entry closes it.
        ToggleType = entry.Tag is null ? MenuItemToggleType.None : MenuItemToggleType.CheckBox,
        IsChecked = entry.Marked,
        StaysOpenOnClick = entry.Tag is not null,
    };

    /// <summary>Adds <paramref name="entry"/> to <paramref name="menu"/>, after a separator when it comes after one; the entry it shows as.</summary>
    private static MenuItem Add(ItemsControl menu, ItemMenuEntry entry)
    {
        if (entry.AfterSeparator)
        {
            menu.Items.Add(new Separator());
        }

        var item = MenuEntry(entry);
        menu.Items.Add(item);
        return item;
    }

    private Outcome Named(Failure failure)
    {
        notices.Raise(failure);
        return failure;
    }

    /// <summary>
    /// One open menu's Add tag ▸ and Remove tag ▸ (G17), on the items the menu was opened on. Add tag ▸ shows Type a tag…
    /// alone until every tag is read from the store as the menu opens, so a tag no item carries any more is never offered
    /// and one just added is. After each click on a list's entry the lists show the items as the store now holds them:
    /// every mark follows its answer, never the menu's own flip of it; Remove tag ▸ shows while they carry a tag, listing
    /// what they carry, and hides with its list once they carry none, so nothing in the menu is inert. Add tag ▸ keeps
    /// its entries and their order while open, so none moves under the pointer and a tag it took off its last item stays,
    /// unmarked, for a second click to give back; the next opening reads them anew. A read that fails is named (G35).
    /// </summary>
    private sealed class ShownTagLists(Listing listing, Notices notices, Organising organising, IReadOnlyList<StashItem> items, Action<IReadOnlyList<StashItem>> typeATag)
    {
        private bool everyTag;
        private int reads;

        public MenuItem? AddTag { get; set; }

        public MenuItem? RemoveTag { get; set; }

        /// <summary>Gives Add tag ▸ and Remove tag ▸ these entries.</summary>
        public void Fill(IReadOnlyList<ItemMenuEntry> add, IReadOnlyList<ItemMenuEntry> remove)
        {
            Fill(AddTag, add);
            Fill(RemoveTag, remove);
        }

        /// <summary>Reads the stash's tags and the items as the store holds them now, and shows them; the latest read alone applies.</summary>
        public async Task Read()
        {
            if (listing.Store is not { } store)
            {
                return;
            }

            var read = ++reads;
            var counts = await store.TagCounts();
            var present = await organising.Present(items);
            if (read != reads)
            {
                return;
            }

            if (!counts.Succeeded)
            {
                notices.Raise(counts.Failure);
                return;
            }

            if (!everyTag)
            {
                everyTag = true;
                Fill(AddTag, TagLists.AddTagEntries(counts.Value!, present));
            }

            Mark(AddTag, present);
            if (RemoveTag is null)
            {
                return;
            }

            var remove = TagLists.RemoveTagEntries(present);
            if (remove.Count == 0)
            {
                RemoveTag.IsSubMenuOpen = false;
                RemoveTag.IsVisible = false;
                return;
            }

            RemoveTag.IsVisible = true;
            if (!remove.Select(entry => entry.Tag).SequenceEqual(Shown(RemoveTag).Select(entry => entry.Tag)))
            {
                Fill(RemoveTag, remove);
            }
            else
            {
                Mark(RemoveTag, present);
            }
        }

        private static IEnumerable<ItemMenuEntry> Shown(MenuItem list) => list.Items.OfType<MenuItem>().Select(item => item.Tag).OfType<ItemMenuEntry>();

        private static void Mark(MenuItem? list, IReadOnlyList<StashItem> present)
        {
            foreach (var item in list?.Items.OfType<MenuItem>() ?? [])
            {
                if (item.Tag is ItemMenuEntry { Tag: { } tag } entry)
                {
                    var marked = entry with { Marked = Organising.AllCarry(present, tag) };
                    item.Tag = marked;
                    item.IsChecked = marked.Marked;
                }
            }
        }

        private void Fill(MenuItem? list, IReadOnlyList<ItemMenuEntry> entries)
        {
            if (list is null)
            {
                return;
            }

            list.Items.Clear();
            foreach (var entry in entries)
            {
                var item = Add(list, entry);
                item.Tag = entry;
                item.Click += (_, _) =>
                {
                    if (entry.Tag is { } tag)
                    {
                        // The menu flips a checkable entry's mark itself before this; the mark waits for the store's answer instead.
                        item.IsChecked = ((ItemMenuEntry)item.Tag!).Marked;
                        _ = ClickThenRead(entry.Action, tag);
                    }
                    else
                    {
                        typeATag(items);
                    }
                };
            }
        }

        private async Task ClickThenRead(ItemMenuAction list, string tag)
        {
            await TagLists.Click(organising, list, items, tag);
            await Read();
        }
    }
}
