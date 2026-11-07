using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// The clipboard seam for the portable checks: keeps every content written, in order, with the time it was written on
/// the check's clock; can be told to refuse the next write, to wait before writing, and to run a check of its own at
/// the moment of the write; remembers the identities of its latest completed write, which a check may also set, and
/// keeps every write again asked for, answering each with a preset answer; answers every read, and every image-only
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
        if (AtWrite is { } atWrite)
        {
            await atWrite();
        }

        return Outcome.Done;
    }

    public Task<Outcome> WriteText(TopLevel owner, string action, string text, TimeSpan heldClipboardRetry) => Task.FromResult(Outcome.Done);

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

        if (WritesAgain is { Succeeded: true, Value: true })
        {
            LastWritten = content.Items;
        }

        return WritesAgain;
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
