using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The one stash window (F4) in its two states: the flyout, with no title bar, which hides on Esc with nothing to clear
/// and on losing focus to another app; and the pinned window, with a title bar, resizing and a taskbar button, which
/// stays. Both show the same content and answer the same keys (G2): pinning changes the chrome, the hide rules and the
/// size, nothing else. It is created at start and kept hidden, never closed, and shown and placed only by its summon,
/// so a summon only shows it (G1); Close unpins and hides it and never exits ImageStash (G2, G3). The preview and the
/// editor are overlays inside it (F4).
/// </summary>
public sealed class StashWindow : Avalonia.Controls.Window
{
    private const long ThumbnailBytes = 96L * 1024 * 1024;

    private readonly Listing listing;
    private readonly Notices notices;
    private readonly SettingsKeeper settings;
    private readonly IQuietWindows quietWindows;
    private readonly IDialogsAndExplorer dialogs;
    private readonly IClipboardAndDrops drops;
    private readonly PasteTargetHolder pasteTarget = new();
    private readonly Import import;
    private readonly Handoff handoff;
    private readonly Deletion deletion;
    private readonly Renaming renaming;
    private readonly Organising organising;
    private readonly ClipboardPaste paste;
    private readonly PressOnce pasteKey = new();
    private readonly ItemMenu itemMenu;
    private readonly TagFieldOverlay tagField;
    private readonly Preview preview;
    private readonly Editing editing;
    private readonly Moving moving;
    private readonly EditorOverlay editor;
    private readonly Thumbnails<Bitmap> thumbnails;
    private readonly TextBox search;
    private readonly ToggleButton pin;
    private readonly Grid head;
    private readonly WindowChrome chrome;
    private readonly ChipRow scopes;
    private readonly ScrollViewer scroller;
    private readonly ThumbnailGrid grid;
    private readonly PendingQueryKeys keys;
    private readonly WindowStatus status;
    private readonly SizeWheel sizeWheel = new();

    /// <summary>The drag out of the window in progress, or null; a chip it is dropped on takes it (G17).</summary>
    private OwnDrag? ownDrag;
    private readonly WindowActivation activation;

    public StashWindow(Listing listing, Notices notices, SettingsKeeper settings, Import import, Handoff handoff, Editing editing, OpenWith openWith, Moving moving, PlatformSeams seams)
    {
        this.listing = listing;
        this.moving = moving;
        this.editing = editing;
        this.notices = notices;
        this.settings = settings;
        this.import = import;
        this.handoff = handoff;
        quietWindows = seams.QuietWindows;
        dialogs = seams.DialogsAndExplorer;
        drops = seams.ClipboardAndDrops;
        deletion = new Deletion(listing, notices);
        renaming = new Renaming(listing, notices, handoff);
        organising = new Organising(listing, notices);
        paste = new ClipboardPaste(listing, notices, import, drops, notices.Clock, HeldClipboard.Retry);
        itemMenu = new ItemMenu(listing, notices, handoff, organising, deletion, editing, openWith, dialogs, HeldFileRetry.Duration);
        // What a Move waits for in this window (State: Move); the wait itself is Moving's.
        moving.WaitForWindow(paste, import, handoff, deletion, renaming, organising, editing, itemMenu, notices);
        Title = "ImageStash";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        search = WindowLayout.SearchBox();
        pin = new ToggleButton { Content = IconGlyph.Of("", 16), Width = 36, Height = 36, Padding = default, Focusable = false };
        pin.Classes.Add(LookStyles.IconButton);
        chrome = new WindowChrome(this, settings, pin);
        seams.BorderDrag.StopAtMinimum(this);
        head = WindowLayout.Head(search, WindowLayout.SettingsButton(() => SettingsRequested?.Invoke()), AddMenu.Button(settings, paste, ImportFiles, () => ScreenshotRequested?.Invoke(), () => RecordRequested?.Invoke()), pin);

        // A chip's word goes into the box, which searches it, so the box, the grid and the lit chips agree (G4, G5); the
        // caret goes to its end, with nothing selected, whatever a summon left selected (G1).
        scopes = new ChipRow(listing, organising, () => ownDrag, text =>
        {
            search.Text = text;
            search.SelectionStart = search.SelectionEnd = search.CaretIndex = text.Length;
        })
        { Margin = new Thickness(10, 4, 10, 8) };

        thumbnails = new Thumbnails<Bitmap>(
            ThumbnailBytes,
            Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            HeldFileRetry.Duration,
            FrameBitmap.Of,
            action => Dispatcher.UIThread.Post(action),
            bitmap => bitmap.Dispose(),
            failure => notices.Raise(failure));
        grid = new ThumbnailGrid(listing, thumbnails, () => settings.Current.ThumbnailSize) { Margin = new Thickness(10, 4, 10, 10) };
        // The grid lays out its rows in a layout pass after the listing changes: an arrow moves among the rows of what is listed now.
        keys = new PendingQueryKeys(listing, () =>
        {
            grid.UpdateLayout();
            return grid.Rows;
        });
        scroller = WindowLayout.GridScroller(grid);
        var dropTarget = new DropTarget(this, drops, import, () => DragInProgress, () => listing.LitTags);
        tagField = new TagFieldOverlay(listing, organising, notices);
        preview = new Preview(listing, handoff, organising, deletion, renaming, itemMenu, notices, notices.Clock, pasteTarget, DialogShown, grid);
        editor = new EditorOverlay(failure => notices.Raise(failure), seams.Fonts);
        status = new WindowStatus(listing, notices, settings, pasteTarget, scroller, () => preview.IsOpen || editing.IsOpen, () => search.Text = "");
        var body = new Panel { Children = { scroller, status.StateView } };
        // Above the preview too, so a failure is still named and a drop still taken while it is open (G35, G11); the drop
        // card above the notices, so an earlier batch's notices never hide what the next drop will add.
        var overBody = new Panel { Children = { status.NoticeArea, tagField, dropTarget.Card } };
        Content = WindowLayout.Rows(head, scopes, body, status.Footer, preview, editor, overBody);
        preview.Changed += Cover;

        // Before Wire: its handlers of the window's activation, deactivation and Close come first.
        activation = new WindowActivation(this, chrome, editing, notices, pasteTarget, seams.Target, pasteKey, () => DialogUp, () => DragInProgress);
        CaptureHiding = new CaptureReturn(this, chrome, activation, () => DialogUp, () => DragInProgress, FocusContent).Hiding;
        chrome.Apply(pinnedNow: false);
        Wire();
        WireEditor(Cover);
        status.ShowHints();
        status.ShowListing();
    }

    /// <summary>Set while a dialog the window opened is up, so losing focus to it does not hide the flyout (G2).</summary>
    public bool DialogUp { get; set; }

    // Two can be up at once (Open with… asked again while its first copy is still being made): the flag stays until the last closes (G2).
    private int dialogsUp;

    /// <summary>A dialog the window opened up, or closed: once the last has closed, the flyout judges again where focus went, which may be the program it started (G2).</summary>
    private void DialogShown(bool up)
    {
        dialogsUp += up ? 1 : -1;
        DialogUp = dialogsUp > 0;
        if (!DialogUp)
        {
            activation.FocusMayHaveLeft();
        }
    }

    /// <summary>Set while a drag the window started is in progress, so losing focus meanwhile does not hide the flyout (G2).</summary>
    public bool DragInProgress => ownDrag is not null;

    /// <summary>The paste target the footer and the item menu name and Enter pastes into (G8).</summary>
    public PasteTargetHolder PasteTarget => pasteTarget;

    /// <summary>The window out of the way while a capture is in progress and back as it was afterwards (G3).</summary>
    public CaptureHiding CaptureHiding { get; }

    /// <summary>Take a screenshot chosen from the <c>+</c> menu, raised once the menu has closed; the act is the shortcut's (G14).</summary>
    public event Action? ScreenshotRequested;

    /// <summary>Record a clip chosen from the <c>+</c> menu, raised once the menu has closed; the act is the shortcut's (G15).</summary>
    public event Action? RecordRequested;

    /// <summary>The Settings button clicked; the page is the app's, one whichever way it is asked for (F4, G32).</summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// The summon from the tray, Open ImageStash and a second launch (G1, G3, G34): the paste target read as the window
    /// that had focus just before, which after a click on the tray icon is the taskbar, so none (G8); the content as it
    /// was left, the search box focused with its text selected; the flyout above the tray icon, or the pinned window
    /// brought forward where it is.
    /// </summary>
    public void SummonFromTray()
    {
        CaptureHiding.Forget();
        activation.ReadTargetAtSummon();
        SummonFlyoutAt(() => Summon.AboveTrayIcon(quietWindows.TrayIconRectangle(), chrome.Displays(), settings.Current.FlyoutSize.UsableOr(WindowSize.FlyoutDefault)));
    }

    /// <summary>
    /// Once the tray menu that ran Open ImageStash has closed, a window of its own whose going, or the shell's hidden icons
    /// closing under it, may have put another app in front: the window comes forward again, and since the window before it
    /// was that menu, there is no paste target, whichever app the summon read in front (G1, G8, G34, In use).
    /// </summary>
    public void KeepForward()
    {
        if (IsVisible && !IsActive)
        {
            activation.WithOwnActivation(() =>
            {
                Activate();
                FocusContent();
            });
        }

        activation.TrayMenuClosed();
    }

    /// <summary>
    /// The open shortcut, which opens or hides the window (F5), with <paramref name="inFront"/> what was in front at the
    /// press, the paste target unless that was ImageStash itself (G8): a showing flyout hides, unless a dialog it opened
    /// is up or a drag it started is in progress (G2); otherwise the summon as from the tray, the flyout placed at the
    /// anchor (G1), and the activation it causes leaves the press's target standing.
    /// </summary>
    public void SummonFromShortcut(ForegroundReading inFront, SummonAnchor anchor)
    {
        pasteTarget.FromPress(inFront);
        SummonFromShortcut(anchor);
    }

    /// <summary>The open shortcut's summon once its anchor is known, the press's reading having set the target at the press.</summary>
    public void SummonFromShortcut(SummonAnchor anchor)
    {
        CaptureHiding.Forget();
        if (IsVisible && !chrome.Pinned)
        {
            if (Summon.FlyoutHides(chrome.Pinned, DialogUp, DragInProgress, editing.IsOpen))
            {
                activation.HideWindow();
            }
            else if (editing.IsOpen)
            {
                // G2: during an edit the shortcut brings the flyout forward instead of hiding it.
                activation.WithOwnActivation(() =>
                {
                    Activate();
                    FocusContent();
                });
            }

            return;
        }

        activation.WithOwnActivation(() => SummonFlyoutAt(() => Summon.AtAnchor(anchor, chrome.Displays(), settings.Current.FlyoutSize.UsableOr(WindowSize.FlyoutDefault))));
    }

    /// <summary>The first run with an empty stash opens the window pinned (G2).</summary>
    public void SummonPinned()
    {
        CaptureHiding.Forget();
        activation.ReadTargetAtSummon();
        ReadForSummon();
        thumbnails.RetryFailed();
        chrome.Apply(pinnedNow: true);
        ShowAt(chrome.PinnedPlace());
    }

    /// <summary>
    /// Every summon's one path (G1): the content as it was left, the search box focused with its text selected; the
    /// pinned window brought forward where it is, else the flyout shown at <paramref name="flyoutPlace"/>.
    /// </summary>
    private void SummonFlyoutAt(Func<PixelRect> flyoutPlace)
    {
        ReadForSummon();
        thumbnails.RetryFailed();
        if (chrome.Pinned && IsVisible)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            FocusAtSummon();
            return;
        }

        chrome.Apply(pinnedNow: false);
        ShowAt(flyoutPlace());
    }

    /// <summary>
    /// What a summon reads again, the content being kept as it was left (G1): a load the store refused, and an open
    /// preview's item, so what it says is true now, however long ago it was left (G19).
    /// </summary>
    private void ReadForSummon()
    {
        _ = listing.RetryFailedLoad();
        if (preview.IsOpen)
        {
            _ = preview.Refresh();
        }
    }

    /// <summary>
    /// While the preview or the editor is open it is the window's content (F4, G19, G20): the head, the chips and the
    /// footer's hints are out of sight and reach, and the grid it covers plays nothing (G6), nor a preview the editor covers.
    /// </summary>
    private void Cover()
    {
        head.IsVisible = scopes.IsVisible = !preview.IsOpen && !editing.IsOpen;
        if (!scopes.IsVisible)
        {
            scopes.Picker.Close();
        }

        grid.Covered = preview.IsOpen || editing.IsOpen;
        preview.Covered = editing.IsOpen;
        status.ShowListing();
    }

    /// <summary>The window's handlers: what it shows as the listing, the notices, the busy states and the target change, and what its grid and preview ask for.</summary>
    private void Wire()
    {
        listing.Changed += status.ShowListing;
        // An import renames the stash folder, which a file this window holds open would stop (G36).
        listing.LetGoWhenWithdrawn(() => Task.WhenAll(grid.LetGo(), preview.LetGo()));
        notices.Changed += status.ShowNotices;
        import.Changed += ShowBusy;
        handoff.Changed += ShowBusy;
        editing.Changed += ShowBusy;
        deletion.Changed += ShowBusy;
        renaming.Changed += ShowBusy;
        paste.Changed += ShowBusy;
        moving.Changed += ShowBusy;
        pasteTarget.Changed += status.ShowHints;
        grid.TileDoubleClicked += HandOverSelection;
        grid.TilePreviewRequested += () => OpenPreview(editingName: false);
        grid.DragStarted += DragOut;
        // Type a tag… gives focus back to the grid, which the right-click focused; it, Preview and Rename open once the
        // menu has closed, whose own focus would otherwise end the field's and leave the preview's on the grid beneath it.
        grid.ItemMenuRequested += anchor => itemMenu.Show(grid, anchor, this, activation.TargetNow(), activation.HideForPaste, DialogShown,
            items => Dispatcher.UIThread.Post(() => tagField.Open(items, grid), DispatcherPriority.Background),
            item => Dispatcher.UIThread.Post(() => preview.Open(item), DispatcherPriority.Background),
            item => Dispatcher.UIThread.Post(() => preview.Open(item, editingName: true), DispatcherPriority.Background),
            item => Dispatcher.UIThread.Post(() => _ = editing.Open(item, EditOrigin.Grid), DispatcherPriority.Background));
        // An import's end shows All with the new items selected, which the preview would hide (G12); one an edit covers
        // stays, for the edit to return to (G26).
        import.Finishing += () =>
        {
            if (!editing.IsOpen)
            {
                preview.Close();
            }
        };
        // The preview's buttons are Enter's and Ctrl+C's own path on its item (G19, G8, G9).
        preview.HandOverRequested += () => _ = preview.HandOver(this, activation.TargetNow(), activation.HideForPaste);
        preview.CopyRequested += () => _ = preview.Copy(this);
        preview.EditRequested += () =>
        {
            if (preview.Item is { } item)
            {
                _ = editing.Open(item, EditOrigin.Preview);
            }
        };
        new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => notices.Sweep()).Start();
        WireSearchBox();
        WireKeys();
        WirePin();
    }

    /// <summary>
    /// The editor shown as the edit opens, closes or goes on (<paramref name="cover"/> hiding what it covers), its asks, and
    /// where the window shows the item when an edit closes or goes on with a new item (G20, G26, G2).
    /// </summary>
    private void WireEditor(Action cover)
    {
        editing.Changed += () =>
        {
            var opened = editing.Session is not null && !ReferenceEquals(editor.Session, editing.Session);
            editor.Show(editing.Session);
            cover();
            if (opened)
            {
                editor.FocusAsLeft();
            }
            else if (!editing.IsOpen)
            {
                activation.EditEnded();
            }
        };
        editor.SaveRequested += () => _ = editing.Save();
        editor.SaveAsNewItemRequested += () => _ = editing.SaveAsNewItem();
        editor.CancelRequested += () => editing.Cancel();
        editing.Returned += back =>
        {
            if (back is { Origin: EditOrigin.Preview, Item: { } item })
            {
                preview.Open(item);
            }
            else
            {
                // The grid, even when a preview of another item was opened since Edit was started (its Undo reopened it).
                preview.Close();
                grid.Focus(NavigationMethod.Tab);
            }
        };
        editing.Continued += item =>
        {
            if (preview.IsOpen)
            {
                preview.Open(item);
                editor.FocusAsLeft();
            }
        };
    }

    /// <summary>The search box: no paste of its own, the window's <c>Ctrl+V</c> alone reading the clipboard, and every change of its text searched at once (G5, G13, N9).</summary>
    private void WireSearchBox()
    {
        // The text box's own Paste (Ctrl+V, Shift+Insert, its menu) would read the clipboard a second time; ImageStash reads
        // the clipboard only for Paste image, Ctrl+V's one read and Stash clipboard image (N9, R5).
        search.PastingFromClipboard += (_, e) => e.Handled = true;
        // Its own menu would then offer a Paste that does nothing (G35); Cut and Copy of its text stay on their keys.
        search.ContextFlyout = null;
        new SearchBoxClipboard(search, drops, notices, HeldClipboard.Retry).Wire();
        // The box is where the query is typed; the listing clears it only by its own reset, which the box shows at once.
        listing.QueryCleared += () => search.Text = "";
        // TextChanging, raised as the text changes: TextChanged comes later, posted, when a load or an import may already
        // have changed what the window shows (G5, G12). The listing's own clear comes back here and is not searched again.
        search.TextChanging += (_, _) =>
        {
            if ((search.Text ?? "") != listing.Query)
            {
                _ = listing.SetQuery(search.Text ?? "");
            }
        };
    }

    /// <summary>The window's keys, the wheel's size steps and typing in the grid (G6, G7).</summary>
    private void WireKeys()
    {
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => pasteKey.Released(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyReleases.IgnoreUnpressed(this);
        AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            // Ctrl+wheel steps the thumbnail size instead of scrolling; the wheel alone scrolls (G6).
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                StepThumbnails(sizeWheel.Turn(e.Delta.Y));
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        grid.AddHandler(TextInputEvent, OnGridTextInput, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    /// <summary>The pin button: pins the flyout, or unpins the pinned window (G2).</summary>
    private void WirePin()
    {
        pin.Click += (_, _) =>
        {
            if (chrome.Pinned)
            {
                Unpin();
            }
            else
            {
                Pin();
            }
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Another text field's keys are its own (the tag field's); only those it leaves unused reach the window (G7).
        var focused = FocusManager?.GetFocusedElement();
        var place = StashKeys.PlaceOf(search.IsFocused, focused is TextBox && !ReferenceEquals(focused, search), preview.IsOpen, preview.ButtonFocused, editing.IsOpen);
        // Esc goes to an open tag list wherever focus is, a field's own Esc included: it closes the list alone (G2, G17);
        // the list a tag field's chevron opens leaves focus where it was, on the preview's picture as often as in the box.
        var action = OpenTagLists().Count > 0 && (e.Key, e.KeyModifiers) == (Key.Escape, KeyModifiers.None)
            ? KeyAction.Escape
            : StashKeys.Route(e.Key, e.KeyModifiers, place, !string.IsNullOrEmpty(search.SelectedText));
        if (StashKeys.Direction(action) is { } direction)
        {
            _ = keys.Move(direction);
        }
        else if (!RunKeyAction(action, e.Key, place))
        {
            return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Does what a routed key's action does (G7); false for a key routed to no action, which is left unhandled for the
    /// focused control, so the search box keeps every typed character and Del deletes one there. The acts on the selection
    /// go after any arrow still waiting for what was typed, in the order pressed (G5).
    /// </summary>
    private bool RunKeyAction(KeyAction action, Key key, KeyPlace place)
    {
        switch (action)
        {
            case KeyAction.FocusGrid:
                grid.Focus(NavigationMethod.Tab);
                break;
            case KeyAction.SelectAll:
                keys.Then(listing.SelectAll);
                break;
            case KeyAction.ImportFiles:
                ImportFiles();
                break;
            case KeyAction.Paste:
                if (pasteKey.Pressed(key))
                {
                    // The words replace the box's selection, or go in at its caret, as a text box's paste does: the search
                    // box's, or a tag field's as typed text; in any other field the notice says so (G13).
                    _ = paste.Paste(place == KeyPlace.SearchBox ? words => search.SelectedText = words
                        : (FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<TagField>() is { } field ? field.PasteNow()
                        : null);
                }

                break;
            case KeyAction.Handoff:
                // The target as Enter found it, whatever waits before the handoff (G8).
                var target = activation.TargetNow();
                // With the preview open, its item, whatever the grid beneath it selects (G8, G19).
                keys.Then(() => _ = preview.IsOpen ? preview.HandOver(this, target, activation.HideForPaste) : HandOverTypedSelection(target));
                break;
            case KeyAction.Copy:
                // In the editor, the item being edited as last saved: nothing of the live layers is rendered before Save (G9).
                keys.Then(() => _ = editing.Session is { } edited ? handoff.Copy(this, [edited.Item]) : preview.IsOpen ? preview.Copy(this) : CopyTypedSelection());
                break;
            case KeyAction.Edit:
                keys.Then(() => _ = editing.OpenOnSelection(preview.Item));
                break;
            case KeyAction.Delete:
                keys.Then(() => _ = preview.IsOpen ? preview.Delete() : deletion.DeleteSelection());
                break;
            case KeyAction.ToggleFavorite:
                keys.Then(() => _ = preview.IsOpen ? preview.ToggleFavorite() : organising.ToggleFavoriteOfSelection());
                break;
            case KeyAction.TogglePreview:
                keys.Then(TogglePreview);
                break;
            case KeyAction.Rename:
                keys.Then(Rename);
                break;
            case KeyAction.LargerThumbnails or KeyAction.SmallerThumbnails:
                StepThumbnails(action == KeyAction.LargerThumbnails ? 1 : -1);
                break;
            case KeyAction.Escape:
                // After a Space still waiting behind an arrow, so it closes the preview that Space opens (G2).
                keys.Then(Escape);
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>Space: the preview closed when it is open, else opened on the focused item (G19).</summary>
    private void TogglePreview()
    {
        if (preview.IsOpen)
        {
            preview.Dismiss();
        }
        else
        {
            OpenPreview(editingName: false);
        }
    }

    /// <summary>F2: the preview's name in edit, the preview opened on the focused item first when it is closed (G17).</summary>
    private void Rename()
    {
        if (preview.IsOpen)
        {
            preview.EditName();
        }
        else
        {
            OpenPreview(editingName: true);
        }
    }

    /// <summary>Esc: closes an open tag list, else the preview, else clears the search, else hides the flyout; pinned, it goes no further (G2).</summary>
    private void Escape()
    {
        var openLists = OpenTagLists();
        switch (Summon.OnEscape(search.Text ?? "", chrome.Pinned, preview.IsOpen, openLists.Count > 0))
        {
            case EscapeAction.ClosePicker:
                foreach (var list in openLists)
                {
                    list.Close();
                }

                break;
            case EscapeAction.ClosePreview:
                preview.Dismiss();
                break;
            case EscapeAction.ClearSearch:
                search.Text = "";
                break;
            case EscapeAction.Hide:
                activation.HideWindow();
                break;
        }
    }

    /// <summary>The window's tag lists open now: the chip row's, or a tag field's in the overlay or the preview (G4, G17).</summary>
    private List<TagPicker> OpenTagLists() => OpenedPopups.OfType<TagPicker>().Where(list => list.IsOpen).ToList();

    /// <summary>Typing while the grid has focus returns focus to the search box with what was typed (G7).</summary>
    private void OnGridTextInput(object? sender, TextInputEventArgs e)
    {
        if (!StashKeys.TypesIntoSearch(e.Text))
        {
            return;
        }

        search.Focus();
        search.Text = (search.Text ?? "") + e.Text;
        search.CaretIndex = search.Text.Length;
        e.Handled = true;
    }

    /// <summary><c>Ctrl+O</c> and Import files…: the window's own picker, which leaves the flyout up while it is open (G2, F4).</summary>
    private void ImportFiles() =>
        _ = import.Pick(() => dialogs.OpenFiles(this, "Import files", Import.PickerFilters), DialogShown);

    /// <summary>
    /// Enter: the handoff on the selection, once the matches of what was typed have arrived, so a key pressed right after
    /// typing hands over the first match, not what showed before it (G5); into the target the footer named when Enter
    /// was pressed, or onto the clipboard when it named none (G8).
    /// </summary>
    private async Task HandOverTypedSelection(PasteTarget? target)
    {
        await listing.Settle();
        await handoff.HandOver(this, listing.SelectedItems, target, activation.HideForPaste);
    }

    /// <summary><c>Ctrl+C</c>: the selection onto the clipboard once what was typed has arrived, and the window stays (G2, G9).</summary>
    private async Task CopyTypedSelection()
    {
        await listing.Settle();
        await handoff.Copy(this, listing.SelectedItems);
    }

    /// <summary>A double-click: the handoff on the tile it landed on, which its first click selected (G8).</summary>
    private void HandOverSelection() => _ = handoff.HandOver(this, listing.SelectedItems, activation.TargetNow(), activation.HideForPaste);

    /// <summary>
    /// Space in the grid and a <c>Ctrl</c>+double-click: the preview on the focused item, the one with the ring (G19);
    /// F2, with its name in edit (G17).
    /// </summary>
    private void OpenPreview(bool editingName)
    {
        if (listing.Items.FirstOrDefault(item => item.Id == listing.Focused) is { } item)
        {
            preview.Open(item, editingName);
        }
    }

    /// <summary>Focus at a summon: <see cref="FocusContent"/>, the box's text selected, so that typing replaces it (G1).</summary>
    private void FocusAtSummon()
    {
        FocusContent();
        if (!editing.IsOpen && !preview.IsOpen)
        {
            search.SelectAll();
        }
    }

    /// <summary>Focus where a summon puts it: the search box, or the editor or the preview as it was left while it covers the box (G7, G1).</summary>
    private void FocusContent()
    {
        if (editing.IsOpen)
        {
            editor.FocusAsLeft();
        }
        else if (preview.IsOpen)
        {
            preview.FocusAsLeft();
        }
        else
        {
            search.Focus();
        }
    }

    /// <summary>
    /// The selection dragged out as files from the grid's press, the flyout staying while the drag is in progress; a
    /// focus lost to another app meanwhile hides it once the drag has ended (G2, G9). Dropped on the window's own tag
    /// chip or Favorites, it landed nowhere outside, whatever the drag reports, so it records no use (G10).
    /// </summary>
    private void DragOut(PointerPressedEventArgs press)
    {
        var drag = new OwnDrag(listing.SelectedItems);
        _ = handoff.DragOut(drag.Items, async paths =>
            {
                var ended = await drops.StartDrag(this, press, paths);
                return ended.Succeeded && drag.TakenByChip ? Outcome<bool>.Of(false) : ended;
            },
            dragging => ownDrag = dragging ? drag : null,
            activation.FocusMayHaveLeft);
    }

    /// <summary>
    /// ImageStash's notifications above the tray, whose focus the window's hide rule and paste target follow: a click on
    /// one takes no focus, and a click into a card's field takes it as the user's act (G2, G8, G16); a card's Edit or
    /// Trim summons the window as the tray does, coming in front from that notification, while the click's activation is
    /// still held, then opens the editor on the card's item (G16, G1); and a click elsewhere on a card summons it the same
    /// way, then shows the card's item selected, an open preview closed first, and dismisses the card, while an open edit
    /// stays as it was left, and the card with it (G16: it goes since the window then shows the item).
    /// </summary>
    public void Follow(Notification notification, CaptureNotification captured)
    {
        notification.Deactivated += activation.NotificationLostFocus;
        notification.StayAbove(this);
        captured.FieldTookFocus += activation.NotificationTookFocus;
        captured.EditRequested += asked =>
        {
            SummonFromTray();
            _ = editing.OpenFromNotification(asked);
        };
        captured.ShowRequested += (item, dismiss) =>
        {
            SummonFromTray();
            if (!editing.IsOpen)
            {
                dismiss();
                _ = captured.ShowOnAll(item, preview.Close);
            }
        };
    }

    /// <summary>
    /// The Settings page, once opened: focus with it has not gone to another app, so the flyout stays, as it does for a
    /// notification's field, and judges again where focus went when the page loses it (G2).
    /// </summary>
    public void Follow(SettingsWindow page) => activation.SettingsPageOpened(page);

    /// <summary>G6's three sizes: the grid lays its rows out again at the new size, which settings remember.</summary>
    private void StepThumbnails(int steps)
    {
        var now = settings.Current.ThumbnailSize;
        var next = JustifiedRows.Step(now, steps);
        if (next != now)
        {
            settings.Change(s => s with { ThumbnailSize = next });
            grid.InvalidateMeasure();
            grid.InvalidateVisual();
        }
    }

    private void ShowBusy() => status.ShowBusy([paste.Busy, import.Busy, handoff.Busy, deletion.Busy, renaming.Busy, editing.Busy, moving.Busy]);

    private void Pin() => activation.WithOwnActivation(chrome.Pin);

    /// <summary>The pin button again: the window stays showing, as a flyout where it is, at the flyout's size, with focus.</summary>
    private void Unpin() => activation.WithOwnActivation(() =>
    {
        chrome.Unpin();
        Activate();
        FocusContent();
    });

    /// <summary>Shows the window placed on <paramref name="place"/>, activated, with focus where a summon puts it (G1).</summary>
    private void ShowAt(PixelRect place)
    {
        chrome.ShowAt(place);
        Activate();
        FocusAtSummon();
    }

    /// <summary>A shortcut remapped in Settings: the empty stash lists it from now on (G2).</summary>
    public void ShortcutsChanged() => status.ShortcutsChanged();
}
