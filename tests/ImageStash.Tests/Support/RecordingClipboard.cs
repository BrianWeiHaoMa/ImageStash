using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// The clipboard seam for the portable checks: keeps every content written, in order, with the time it was written on
/// the check's clock; can be told to refuse the next write, to wait before writing, and to run a check of its own at
/// the moment of the write; remembers the identities of its latest completed write, which a check may also set, and
/// keeps every write again asked for, answering each with a preset answer; holds what a paste would then get, which
/// another program's write a check makes replaces, and empties it, forgetting its latest write, as the seam's
/// <see cref="Empty"/> says, keeping every emptying asked for and able to hold or fail the next; answers every read, and every image-only
/// read, with a preset reading, counting each kind of read, after a wait the check may set; and answers every drag
/// with a preset ending, keeping the files each was given.
/// </summary>
public sealed class RecordingClipboard(TimeProvider clock) : IClipboardAndDrops
{
    public List<(ClipboardContent Content, DateTimeOffset At)> Written { get; } = [];

    /// <summary>How many writes have begun, whether or not they completed.</summary>
    public int Begun { get; private set; }

    /// <summary>The refusal of a write while another program holds the clipboard open, as the real seam names it.</summary>
    public static readonly Failure Held = new("Copy", "the clipboard", HeldClipboard.Reason);

    /// <summary>A refusal for the next write, which then leaves nothing written.</summary>
    public Failure? FailNext { get; set; }

    /// <summary>While set, a write waits for it before it does anything else.</summary>
    public Task? Hold { get; set; }

    /// <summary>Runs as each write completes, before it returns, so a check can look at the store at that moment.</summary>
    public Func<Task>? AtWrite { get; set; }

    /// <summary>The checks hand an import its drop's contents directly.</summary>
    public DropContents ReadDrop(IDataTransfer data) => throw new NotSupportedException("The checks build DropContents themselves.");

    public async Task<Outcome> Write(TopLevel owner, ClipboardContent content, TimeSpan heldClipboardRetry)
    {
        Begun++;
        if (Hold is { } hold)
        {
            await hold;
        }

        if (FailNext is { } failure)
        {
            FailNext = null;
            return failure;
        }

        Written.Add((content, clock.GetUtcNow()));
        LastWritten = content.Items;
        Holds = content;
        changedSince = false;
        if (AtWrite is { } atWrite)
        {
            await atWrite();
        }

        return Outcome.Done;
    }

    /// <summary>The text writes, in order.</summary>
    public List<string> TextsWritten { get; } = [];

    /// <summary>Run as each text write is made.</summary>
    public Action? AtWriteText { get; set; }

    public Task<Outcome> WriteText(TopLevel owner, string action, string text, TimeSpan heldClipboardRetry)
    {
        TextsWritten.Add(text);
        // As the real seam's text write empties the clipboard first and forgets the write before it.
        Holds = null;
        LastWritten = [];
        AtWriteText?.Invoke();
        return Task.FromResult(Outcome.Done);
    }

    /// <summary>The identities the latest completed write or write again put there, as the real seam remembers them.</summary>
    public IReadOnlyList<long> LastWritten { get; set; } = [];

    public void ForgetWritten() => LastWritten = [];

    /// <summary>Every write again asked for, in order, with how many writes had completed when it was asked.</summary>
    public List<(long SavedItem, ClipboardContent Content, int WrittenBefore)> WrittenAgain { get; } = [];

    /// <summary>What the next writes again answer: written, until a check says otherwise.</summary>
    public Outcome<bool> WritesAgain { get; set; } = Outcome<bool>.Of(true);

    /// <summary>A task the next writes again wait on once asked for, as a clipboard another program holds keeps them waiting; null for none.</summary>
    public Task? HoldWriteAgain { get; set; }

    public async Task<Outcome<bool>> WriteAgain(long savedItem, ClipboardContent content, TimeSpan heldClipboardRetry)
    {
        WrittenAgain.Add((savedItem, content, Written.Count));
        if (HoldWriteAgain is { } hold)
        {
            await hold;
        }

        if (changedSince)
        {
            // As the real seam writes nothing once another program has changed the clipboard since its latest write (G26).
            return Outcome<bool>.Of(false);
        }

        if (WritesAgain is { Succeeded: true, Value: true })
        {
            LastWritten = content.Items;
            Holds = content;
            changedSince = false;
        }

        return WritesAgain;
    }

    /// <summary>
    /// What a paste would now get of the files and pictures written: the latest content written, written again or put
    /// there by <see cref="AnotherProgramWrites"/>; null before any, once emptied, and after a text write.
    /// </summary>
    public ClipboardContent? Holds { get; private set; }

    // Another program has written since the latest write here, as the real seam's sequence number would tell.
    private bool changedSince;

    /// <summary>Another program replaces the clipboard's contents, which this seam's latest write no longer is, though it still remembers it.</summary>
    public void AnotherProgramWrites(ClipboardContent content)
    {
        Holds = content;
        changedSince = true;
    }

    /// <summary>Every emptying asked for, by the item given, in order.</summary>
    public List<long> EmptiesAsked { get; } = [];

    /// <summary>A task the next emptyings wait on once asked for, before judging, as a clipboard another program holds keeps them waiting; null for none.</summary>
    public Task? HoldEmpty { get; set; }

    /// <summary>A failure for the next emptying, which then empties nothing, as a clipboard held past its retries fails it.</summary>
    public Failure? FailNextEmpty { get; set; }

    public async Task<Outcome<bool>> Empty(long removedItem, TimeSpan heldClipboardRetry)
    {
        EmptiesAsked.Add(removedItem);
        if (HoldEmpty is { } hold)
        {
            await hold;
        }

        if (FailNextEmpty is { } failure)
        {
            FailNextEmpty = null;
            return failure;
        }

        if (changedSince || !LastWritten.Contains(removedItem))
        {
            return Outcome<bool>.Of(false);
        }

        Holds = null;
        LastWritten = [];
        return Outcome<bool>.Of(true);
    }

    /// <summary>What the next reads find: an empty clipboard until a check says otherwise.</summary>
    public Outcome<ClipboardReading> Reading { get; set; } = Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false));

    /// <summary>How many reads have been made.</summary>
    public int Reads { get; private set; }

    /// <summary>While set, a read waits for it before it answers, as one waits on a clipboard another program holds.</summary>
    public Task? HoldRead { get; set; }

    public async Task<Outcome<ClipboardReading>> Read(TimeSpan heldClipboardRetry)
    {
        Reads++;
        if (HoldRead is { } hold)
        {
            await hold;
        }

        return Reading;
    }

    /// <summary>What the next image-only reads find: no image until a check says otherwise.</summary>
    public Outcome<ClipboardImage?> ImageReading { get; set; } = Outcome<ClipboardImage?>.Of(null);

    /// <summary>How many image-only reads have been made, apart from <see cref="Reads"/>.</summary>
    public int ImageReads { get; private set; }

    public async Task<Outcome<ClipboardImage?>> ReadImage(TimeSpan heldClipboardRetry)
    {
        ImageReads++;
        if (HoldRead is { } hold)
        {
            await hold;
        }

        return ImageReading;
    }

    /// <summary>The files of every drag started, in order.</summary>
    public List<IReadOnlyList<string>> Dragged { get; } = [];

    /// <summary>How the next drags end: landed (true), cancelled or taken by nothing (false), or a failure.</summary>
    public Outcome<bool> DragEnds { get; set; } = Outcome<bool>.Of(true);

    /// <summary>Runs while a drag is in progress, before it ends, so a check can look at the window's state then.</summary>
    public Action? DuringDrag { get; set; }

    public Task<Outcome<bool>> StartDrag(TopLevel owner, PointerPressedEventArgs press, IReadOnlyList<string> filePaths)
    {
        Dragged.Add(filePaths);
        DuringDrag?.Invoke();
        return Task.FromResult(DragEnds);
    }
}
