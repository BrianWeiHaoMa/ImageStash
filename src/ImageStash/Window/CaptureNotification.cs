using System.Runtime.CompilerServices;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>What a capture was, for the words its notification says (G13, G16).</summary>
public enum CaptureKind
{
    StashedClipboardImage,
    Screenshot,
    Recording,
}

/// <summary>A card's Edit, or its Trim, clicked: the editor asked for on the card's own item, with the Trim tool for Trim (G16).</summary>
public sealed record CaptureEdit(StashItem Item, bool Trim);

/// <summary>
/// A card's click that opens the stash window on its item (G16): its Edit or Trim, the editor asked for, or a click
/// elsewhere on it, <see cref="Edit"/> null; and whether that click brought the notification in front, which the
/// platform may refuse.
/// </summary>
public sealed record CaptureOpening(StashItem Item, CaptureEdit? Edit, bool NotificationInFront)
{
    /// <summary>
    /// The display the card stood on when it was another than the primary, kept with the card from its raising, so an
    /// opening asked once the card has gone still names it; null for a card above the tray (G16).
    /// </summary>
    public Display? StoodOn { get; init; }
}

/// <summary>
/// The capture notification (G16), every capture's one entry, serving the cards of every stack: a notice of the
/// notifications above the tray, or, for a screenshot or a recording whose region was on another display, of that
/// display's own stack (<see cref="NotificationStacks"/>), so its time, its hover, its × and its Undo at most once are the
/// notices' own. It is raised only for an item Store has
/// committed and placed (R9); a capture that failed before or after its commit is named by its caller as a plain
/// failure instead. Its words are one rule (<see cref="Title"/>, <see cref="Line"/>, <see cref="Aside"/>): titled by
/// what was captured, in the stash; one line saying a recording's length, that it is on the clipboard and that Ctrl+V
/// pastes it only when ImageStash put it there, and the saved file's size once measured; for a stashed clipboard image
/// G13's sentence. Its Undo removes the item through Store, its file to the Recycle Bin (G29), and records nothing
/// else. It never reads the clipboard (N9, R5), and a rename in its field rewrites it only through the handoff's guarded
/// refresh, as a rename in the window does (G17, G26). It is drawn as the capture card, with the picture, those words
/// and the name as a field, renamed through a rename path of its own
/// whose refusals are named above the tray, never only in the window, which may be hidden (G35); its Edit, or Trim for
/// a recording, asks the window that follows it for the editor on its item, a click elsewhere on it for the window on
/// that item, and the card goes at either click.
/// </summary>
public sealed class CaptureNotification(NotificationStacks stacks, Listing listing, Handoff handoff, IQuietWindows quietWindows, TimeSpan heldFileRetry)
{
    // The clipboard follows a card's rename as it follows the window's, through the one handoff (G17, G26); its refusals
    // stand above the tray, as every failure does.
    private readonly Renaming renaming = new(listing, stacks.Tray, handoff);

    private readonly ConditionalWeakTable<Notice, CaptureCardWords> shownWords = [];

    /// <summary>Said on the line only for a capture ImageStash put on the clipboard (G16).</summary>
    public const string OnClipboard = "On the clipboard";

    /// <summary>Said beside <see cref="OnClipboard"/>: the paste keystroke that takes it from there.</summary>
    public const string PastesIt = "Ctrl+V pastes it";

    /// <summary>The cards' one rename path, its refusals named above the tray; a Move waits for it (State: Move).</summary>
    public Renaming Renaming => renaming;

    /// <summary>A click into a card's name field brought the notification in front: ImageStash gained focus (G8).</summary>
    public event Action? FieldTookFocus;

    /// <summary>A card's Edit or Trim was clicked, or the card outside its field and buttons (G16).</summary>
    public event Action<CaptureOpening>? OpenRequested;

    /// <summary>A card's click that opens the stash window, as the card asks it (G16).</summary>
    public void Open(CaptureOpening asked) => OpenRequested?.Invoke(asked);

    /// <summary>
    /// The card's button for the editor (G16): Trim for a recording, a recording of one image included, since the card
    /// names the act and not the frames; Edit for the rest.
    /// </summary>
    public static string EditLabel(CaptureKind kind) => kind == CaptureKind.Recording ? "Trim" : "Edit";

    /// <summary>The card's title (G16): what was captured, and that it is in the stash, in one line of the card's column.</summary>
    public static string Title(CaptureKind kind) => kind switch
    {
        CaptureKind.Screenshot => "Screenshot stashed",
        CaptureKind.Recording => "Recording stashed",
        _ => "Clipboard image stashed",
    };

    /// <summary>
    /// The card's one line of facts (G16, G24), each part only when known, joined so no separator is left hanging: a
    /// recording's length first; that it is on the clipboard and that Ctrl+V pastes it, only when ImageStash put it
    /// there; then the saved file's measured size. Empty when none is known.
    /// </summary>
    public static string Line(long? lengthMs, bool onClipboard, long? fileBytes)
    {
        var parts = new List<string>();
        if (lengthMs is { } length)
        {
            parts.Add(ClipLength.Words(length));
        }

        if (onClipboard)
        {
            parts.Add(OnClipboard);
            parts.Add(PastesIt);
        }

        if (fileBytes is { } bytes)
        {
            parts.Add(FileSize.Words(bytes));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>What a stashed clipboard image's card says whole, on lines of its own: G13's sentence; null for the rest.</summary>
    public static string? Aside(CaptureKind kind) => kind == CaptureKind.StashedClipboardImage ? Import.StillOnly : null;

    /// <summary>
    /// The notice's own words, which the notification's window takes as its title and UI Automation reads: the card's
    /// title naming the item, then its line as known when it is raised (the size, measured later, aside), then
    /// <see cref="Aside"/>.
    /// </summary>
    public static string NoticeText(StashItem item, CaptureKind kind, bool onClipboard, long? lengthMs)
    {
        var said = new List<string> { $"{Title(kind)}: {Notices.Describe([item])}." };
        if (Line(lengthMs, onClipboard, fileBytes: null) is { Length: > 0 } line)
        {
            said.Add(line + ".");
        }

        if (Aside(kind) is { } aside)
        {
            said.Add(aside);
        }

        return string.Join(" ", said);
    }

    /// <summary>
    /// The card of <paramref name="item"/>, raised in the stack of <paramref name="on"/>, the display a screenshot's or a
    /// recording's region was on: above the tray for the primary display or none, as for Stash clipboard image, which is
    /// taken on no display (G13, G16).
    /// </summary>
    public void Raise(StashItem item, CaptureKind kind, bool onClipboard, long? lengthMs, Display? on = null)
    {
        var words = new CaptureCardWords(kind, onClipboard, lengthMs, raisedAsStill: !item.Facts.Animated);
        var trim = kind == CaptureKind.Recording;
        var stack = stacks.On(on);
        var stoodOn = ReferenceEquals(stack, stacks.Tray) ? null : on;
        // The notice's words name the item; its card shows the name in its field instead, so a rename shows there.
        var raised = stack.Raise(NoticeText(item, kind, onClipboard, lengthMs), "Undo", () => Undo(item.Id),
            card: (notice, notices) => new CaptureCard(notice, notices, item, words, listing, renaming, quietWindows, heldFileRetry,
                () => FieldTookFocus?.Invoke(),
                (shown, inFront) => Open(new CaptureOpening(shown, new CaptureEdit(shown, trim), inFront) { StoodOn = stoodOn }),
                (shown, inFront) => Open(new CaptureOpening(shown, null, inFront) { StoodOn = stoodOn })));
        shownWords.AddOrUpdate(raised, words);
        _ = words.Measure(item.FilePath);
    }

    /// <summary>What the card of <paramref name="notice"/> says while it shows; null for a notice that is not a capture's.</summary>
    public CaptureCardWords? WordsOf(Notice notice) => shownWords.TryGetValue(notice, out var words) ? words : null;

    /// <summary>
    /// The card's item shown selected, with no editor (G16): All, where every item is listed, the search cleared, as a
    /// notification's Edit and an import's end show it, <paramref name="closePreview"/> run first so the grid shows it (G12).
    /// An item gone from the stash meanwhile leaves the window as it was left, as the tray opens it.
    /// </summary>
    public async Task ShowOnAll(StashItem item, Action closePreview)
    {
        if (listing.Store is not { } store || await store.Item(item.Id) is { Succeeded: true, Value: null })
        {
            return;
        }

        closePreview();
        await listing.ShowNewItems([item.Id], byPerson: true);
    }

    /// <summary>
    /// The capture leaves by identity through Store's removal, as an import's Undo removes its batch, whatever it was
    /// renamed or marked meanwhile; the listing then shows the stash without it, and a refusal is the undo's outcome,
    /// which the notices name (G29, G35).
    /// </summary>
    private async Task<Outcome> Undo(long id)
    {
        var removed = await listing.Store!.Remove([id]);
        await listing.Refresh();
        return removed[0];
    }
}

/// <summary>
/// What a capture's card says while it shows (G13, G16, G24), the name in its field aside: its title, its one line of
/// facts and a stashed clipboard image's sentence, by <see cref="CaptureNotification"/>'s rule. The size joins the line
/// once measured from the saved file on disk, never taken from the item's facts. A picture changed while the card shows
/// (an edit saved or reverted, an Open with… rewrite adopted or undone) says its own length at once, an animation's, or
/// for a clip recorded as one image the length its recorder closed it at, and none for another still; and it leaves the
/// size out until its own is measured, so the line never says one picture's length beside another's size.
/// </summary>
public sealed class CaptureCardWords
{
    private readonly CaptureKind kind;
    private readonly bool onClipboard;
    // A clip of one image is a still to Media, so only its recorder knows how long it lasted; an edit keeps that (G16).
    private readonly long? stillLengthMs;
    private readonly Lock gate = new();
    private long? lengthMs;
    private long? bytes;
    private int asked;

    /// <param name="lengthMs">A recording's length as its recorder closed it; null for any other capture.</param>
    /// <param name="raisedAsStill">Whether the item was a still when raised, as a clip of one image is.</param>
    internal CaptureCardWords(CaptureKind kind, bool onClipboard, long? lengthMs, bool raisedAsStill)
    {
        this.kind = kind;
        this.onClipboard = onClipboard;
        this.lengthMs = lengthMs;
        stillLengthMs = raisedAsStill ? lengthMs : null;
    }

    /// <summary>Raised when the line changes: a size measured, or a picture changed.</summary>
    public event Action? Changed;

    public string Title => CaptureNotification.Title(kind);

    /// <summary>The editor's button: Edit, or Trim for a recording (<see cref="CaptureNotification.EditLabel"/>).</summary>
    public string EditLabel => CaptureNotification.EditLabel(kind);

    public string Line
    {
        get
        {
            lock (gate)
            {
                return CaptureNotification.Line(lengthMs, onClipboard, bytes);
            }
        }
    }

    public string? Aside => CaptureNotification.Aside(kind);

    /// <summary>The saved file's measured size in words, or null until it has been measured.</summary>
    public string? Size
    {
        get
        {
            lock (gate)
            {
                return bytes is { } length ? FileSize.Words(length) : null;
            }
        }
    }

    /// <summary>
    /// The saved file's size, measured from its length on disk, which a program holding the file does not hide (G24). A
    /// measure asked again, or a change of picture, meanwhile wins over it; a file no longer where it was leaves what is
    /// known.
    /// </summary>
    public async Task Measure(string path)
    {
        int mine;
        lock (gate)
        {
            mine = ++asked;
        }

        var measured = await Task.Run(() => PreviewFacts.FileLength(path));
        lock (gate)
        {
            if (mine != asked || measured is null)
            {
                return;
            }

            bytes = measured;
        }

        Changed?.Invoke();
    }

    /// <summary>The picture shown is another: its own length, and no size until <see cref="Measure"/> reads its file.</summary>
    public void PictureChanged(MediaFacts facts)
    {
        lock (gate)
        {
            asked++;
            lengthMs = facts.Animated ? facts.DurationMs : stillLengthMs;
            bytes = null;
        }

        Changed?.Invoke();
    }
}
