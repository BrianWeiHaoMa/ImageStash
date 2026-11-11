using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The capture notification's card above the tray (G16): the item's picture, small, that it is in the stash with what
/// else its capture says, the saved file's measured size (G24), its name as a field, Edit (Trim for a recording), Undo
/// and ×. A click on Undo or × leaves the app in front with the keystrokes; a click on the name, on Edit or Trim, or
/// anywhere else on the card outside its field and buttons activates the notification, the user's act. The name's puts the
/// name in edit, selected so typing replaces it, the notice staying while it has focus. Enter renames the item through the
/// store and the field shows the item as the store then answers it, Esc leaves the name as recorded, and either, focus
/// going to another window, or the card leaving, gives the activation back to the window that had it, unless the user
/// has put another one in front. Edit or Trim asks for the editor on the item, a click elsewhere for the stash window on
/// it, and the card goes, whatever the ask comes to, so it never lies over the window the click brought forward; other
/// cards stay. A refusal is named above the tray. The item is read again by identity after every change to the stash,
/// so a rename made in the window shows here too, and a picture changed since, with its size (G6).
/// </summary>
public sealed class CaptureCard : Border
{
    /// <summary>The picture's longer edge in pixels: decoded at this size, never kept whole (R7).</summary>
    public const int PictureBound = 256;

    public const string Title = "Saved to your stash";

    public const string NameHint = "Click the name to change it";

    /// <summary>Why the name could not be put in edit, when the platform did not let the notification take the keystrokes.</summary>
    public const string NotActivated = "the notification could not be brought to the front to type in";

    private readonly Notice notice;
    private readonly Notices notices;
    private readonly Listing listing;
    private readonly Renaming renaming;
    private readonly IQuietWindows quietWindows;
    private readonly Action tookFocus;
    private readonly Action<StashItem> edit;
    private readonly Action<StashItem> show;
    private readonly TimeSpan heldFileRetry;
    private readonly Image picture = new() { Width = 112, Height = 84, Stretch = Stretch.Uniform };
    private readonly TextBlock size = new() { Opacity = 0.7, IsVisible = false };
    private StashItem item;
    private Avalonia.Controls.Window? shownIn;
    private IDisposable? activation;
    private bool renamingNow;
    private bool readingPicture;
    private bool pictureChanged;
    private bool left;

    /// <param name="tookFocus">Run once the name's click has brought the notification in front: ImageStash gained focus.</param>
    /// <param name="editLabel">The editor's button: Edit, or Trim for a recording.</param>
    /// <param name="edit">Run with the item as the card has it now when that button is clicked, the notification in front.</param>
    /// <param name="show">Run with the item as the card has it now when the card is clicked outside its field and buttons, the notification in front.</param>
    public CaptureCard(Notice notice, Notices notices, StashItem item, string said, Listing listing, Renaming renaming, IQuietWindows quietWindows, TimeSpan heldFileRetry, Action tookFocus,
        string editLabel, Action<StashItem> edit, Action<StashItem> show)
    {
        this.notice = notice;
        this.notices = notices;
        this.item = item;
        this.listing = listing;
        this.renaming = renaming;
        this.quietWindows = quietWindows;
        this.tookFocus = tookFocus;
        this.edit = edit;
        this.show = show;
        this.heldFileRetry = heldFileRetry;
        Field.Show(item);
        // Tunnelling, so the click on the name takes the activation before the name's own click would edit it.
        Field.AddHandler(PointerPressedEvent, NamePressed, RoutingStrategies.Tunnel);
        Field.Stopped += EditEnded;
        Field.Committed += typed => _ = Rename(typed);

        var words = new StackPanel { Spacing = 2, Width = 250 };
        words.Children.Add(new TextBlock { Text = Title, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (said.Length > 0)
        {
            words.Children.Add(new TextBlock { Text = said, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        }

        words.Children.Add(size);
        words.Children.Add(new Border { Child = Field, Margin = new Thickness(-6, 4, 0, 0) });
        // The buttons first, the hint beside them wrapping where the column has no room.
        var actions = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var editButton = new Button { Content = editLabel, Focusable = false, Margin = new Thickness(0, 0, 10, 0) };
        editButton.Click += (_, _) => AskInFront(edit);
        actions.Children.Add(editButton);
        if (notice.UndoLabel is { } label)
        {
            var undo = new Button { Content = label, Focusable = false, Margin = new Thickness(0, 0, 10, 0) };
            undo.Click += (_, _) => _ = notices.Undo(notice);
            actions.Children.Add(undo);
        }

        foreach (var button in actions.Children)
        {
            DockPanel.SetDock(button, Dock.Left);
        }

        actions.Children.Add(new TextBlock { Text = NameHint, FontSize = 11.5, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(actions);

        var close = NoticeCard.CloseButton(() => notices.Close(notice));
        close.VerticalAlignment = VerticalAlignment.Top;
        var frame = new Border { Child = picture, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
        frame.Bind(Border.CornerRadiusProperty, frame.GetResourceObservable(LookKeys.ControlRadius));
        Grid.SetColumn(words, 1);
        Grid.SetColumn(close, 2);
        Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { frame, words, close } };
        Padding = new Thickness(12, 10, 6, 10);
        CardLook.Dress(this);
        // Bubbling: the name's tunnelled press and the buttons' own have already handled theirs.
        AddHandler(PointerPressedEvent, ElsewherePressed);
        PointerEntered += (_, _) => notices.Hover(notice, over: true);
        PointerExited += (_, _) => notices.Hover(notice, over: false);
        AttachedToVisualTree += (_, _) =>
        {
            shownIn = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
            if (shownIn is not null)
            {
                shownIn.Deactivated += FocusWentElsewhere;
            }

            listing.Changed += StashChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            left = true;
            if (shownIn is not null)
            {
                shownIn.Deactivated -= FocusWentElsewhere;
            }

            shownIn = null;
            listing.Changed -= StashChanged;
            Field.StopEditing();
            EditEnded();
        };
        _ = ShowPictureAndSize();
    }

    /// <summary>The item's name, edited in place; the field itself, so it has no paste of its own (N9, R5).</summary>
    public NameField Field { get; } = new();

    /// <summary>The picture as shown: decoded once, at most <see cref="PictureBound"/> pixels on its longer edge; null until then or when it could not be read.</summary>
    public IImage? Picture => picture.Source;

    /// <summary>The saved file's size as shown, or null until it has been measured.</summary>
    public string? Size => size.IsVisible ? size.Text : null;

    /// <summary>
    /// The name's click, the one click on the card that takes the activation (G16): the notification comes in front with
    /// the keystrokes and the name goes into edit, the notice held while it has focus; while a rename runs it does
    /// nothing, and an activation the platform refused leaves the name as it is and is named (G35).
    /// </summary>
    private void NamePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Field.IsEditing || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Handled, so the name's own click never edits without the activation, and the text box's caret placement for
        // this press cannot undo the selection.
        e.Handled = true;
        if (renamingNow || shownIn is null)
        {
            return;
        }

        var taken = quietWindows.TakeActivation(shownIn);
        if (!taken.Succeeded)
        {
            notices.Raise(new Failure(ItemRename.Action, Notices.Describe([item]), NotActivated));
            return;
        }

        activation = taken.Value;
        // Read now, while the notification is in front and the window it came from is the one before it (G8).
        tookFocus();
        notices.FieldFocus(notice, focused: true);
        Field.Edit();
    }

    /// <summary>A left press outside the field and the buttons: the stash window on the item (G16).</summary>
    private void ElsewherePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !(e.Source is Visual source && (ReferenceEquals(source, Field) || Field.IsVisualAncestorOf(source))))
        {
            e.Handled = true;
            AskInFront(show);
        }
    }

    /// <summary>
    /// Edit, Trim, or a click elsewhere on the card (G16): the card's other clicks that take the activation, the user's
    /// act, so the stash window asked for comes in front from ImageStash's own notification, which leaves no paste target
    /// (G8). The hold ends once the ask has been handled, with the window then in front, which keeps the activation; an
    /// activation the platform refuses still asks, since the window's summon brings it forward itself. Then the card goes,
    /// whether the editor opened, the Edit was refused by name, or the window came forward with another edit as it was
    /// left (G16, G1).
    /// </summary>
    private void AskInFront(Action<StashItem> ask)
    {
        var taken = shownIn is null ? null : quietWindows.TakeActivation(shownIn);
        using (taken is { Succeeded: true } ? taken.Value : null)
        {
            ask(item);
        }

        // Only now: were it the last card, the notifications' window closing before the stash window was in front would
        // hand the foreground to the app beneath, which would then become the paste target, or the flyout would hide (G8, G2).
        notices.Close(notice);
    }

    /// <summary>Focus went to another window: the edit ends as Esc would, with nothing committed.</summary>
    private void FocusWentElsewhere(object? sender, EventArgs e) => Field.StopEditing();

    /// <summary>The edit ended, by Enter, Esc, focus moving on or the card leaving: the notice's time runs on and the activation goes back.</summary>
    private void EditEnded()
    {
        notices.FieldFocus(notice, focused: false);
        activation?.Dispose();
        activation = null;
    }

    /// <summary>
    /// Enter: the name exactly as typed, through the store's Rename, which names a refusal above the tray; the field then
    /// shows the item as the store answers it, read again by identity, never the typed text (R9). The name cannot be
    /// edited again until the store has answered.
    /// </summary>
    private async Task Rename(string typed)
    {
        renamingNow = true;
        try
        {
            var renamed = await renaming.Rename(item, typed);
            await ShowAsStored(nameReadFailure: renamed.Succeeded);
        }
        finally
        {
            renamingNow = false;
        }
    }

    /// <summary>A change to the stash, made anywhere, a rename in the window's preview included: the item read again.</summary>
    private void StashChanged() => _ = ShowAsStored(nameReadFailure: false);

    /// <summary>
    /// The item read again by identity and shown as the store answers it, so the field holds its recorded name whoever
    /// renamed it (G16, G17); a picture or size not yet read from its file is read again from where the file now is,
    /// and another picture than the one read, with its size (G6, G24). An item no longer listed keeps what the card
    /// showed, and its rename or Undo is refused by name.
    /// </summary>
    private async Task ShowAsStored(bool nameReadFailure)
    {
        if (listing.Store is not { } store)
        {
            return;
        }

        var read = await store.Item(item.Id);
        if (left)
        {
            return;
        }

        if (read is { Succeeded: true, Value: { } now })
        {
            var moved = now.FilePath != item.FilePath;
            Take(now);
            if (!readingPicture && (pictureChanged || moved && (picture.Source is null || !size.IsVisible)))
            {
                _ = ShowPictureAndSize();
            }
        }
        else if (!read.Succeeded && nameReadFailure)
        {
            notices.Raise(read.Failure);
        }
    }

    /// <summary>
    /// The item as the store now answers it, its name in the field. A new rendered revision is another picture (an Open
    /// with… rewrite adopted or undone, an edit saved or reverted), to be read again; a rename alone moves the same one.
    /// </summary>
    private void Take(StashItem now)
    {
        pictureChanged |= now.RenderedRevision != item.RenderedRevision;
        item = now;
        Field.Show(now);
    }

    /// <summary>
    /// The saved file's size, measured from its length on disk, which a program holding the file does not hide (G24);
    /// then its picture, decoded off the UI thread at a bounded size through Media, which retries a held file. A file
    /// renamed meanwhile is read again where it now is, and a picture changed meanwhile is read again with its size; a
    /// picture that still cannot be read leaves the card without it and is named (G35); the item stands.
    /// </summary>
    private async Task ShowPictureAndSize()
    {
        readingPicture = true;
        try
        {
            while (true)
            {
                var (path, name) = (item.FilePath, item.FileName);
                var changed = pictureChanged;
                pictureChanged = false;
                if (!size.IsVisible || changed)
                {
                    var bytes = await Task.Run(() => PreviewFacts.FileLength(path));
                    if (bytes is { } length)
                    {
                        size.Text = FileSize.Words(length);
                        size.IsVisible = true;
                    }
                    else if (changed)
                    {
                        // Never the size of the picture before it.
                        size.IsVisible = false;
                    }
                }

                var decoded = await Task.Run(() =>
                {
                    var opened = MediaDecoder.Open(path, name, heldFileRetry);
                    if (!opened.Succeeded)
                    {
                        return Outcome<MediaFrame?>.Fail(opened.Failure);
                    }

                    using var decoder = opened.Value!;
                    return decoder.NextFrame(PictureBound);
                });
                if (left)
                {
                    return;
                }

                if (decoded is { Succeeded: true, Value: { } frame })
                {
                    picture.Source = FrameBitmap.Of(frame);
                    picture.IsVisible = true;
                    // A picture that changed while this one was read is read in turn.
                    if (!pictureChanged)
                    {
                        return;
                    }

                    continue;
                }

                // A rename may have moved the file before its answer reached the card: Store's one thread answers this
                // read after that rename's commit, so a path that moved is known here.
                if (item.FilePath == path && listing.Store is { } store && await store.Item(item.Id) is { Succeeded: true, Value: { } now } && !left)
                {
                    Take(now);
                }

                if (item.FilePath == path && !pictureChanged || left)
                {
                    picture.Source = null;
                    picture.IsVisible = false;
                    if (!decoded.Succeeded && !left)
                    {
                        notices.Raise(decoded.Failure);
                    }

                    return;
                }
            }
        }
        finally
        {
            readingPicture = false;
        }
    }
}
