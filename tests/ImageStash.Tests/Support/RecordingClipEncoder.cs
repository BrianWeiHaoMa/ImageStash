using ImageStash.Media;

namespace ImageStash.Tests.Support;

/// <summary>
/// The clip encoder for the recorder's checks: every call recorded with its time and thread, nothing written and no frame
/// kept past its call, so a check reads what reached the encoder and when. It can hold each Append until the check lets
/// it through, as a slow encoder would, and can answer a failure from Append or Stop. A frame's mark is its first pixel's
/// blue and green bytes, which a check paints with the grab's index.
/// </summary>
public sealed class RecordingClipEncoder : IClipEncoder
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly object gate = new();
    private readonly List<Appended> appends = [];
    private readonly SemaphoreSlim letThrough = new(0);
    private volatile bool holding;
    private int entered;
    private long bytes;

    /// <summary>One Append: the frame's time, its mark, and the thread it came on.</summary>
    public sealed record Appended(long AtMs, int Mark, int Thread);

    /// <summary>What the clip was opened with, once it was.</summary>
    public (string WorkPath, int Width, int Height, string Name)? Opened { get; private set; }

    /// <summary>Stop's time, once Stop came.</summary>
    public long? StoppedAt { get; private set; }

    public bool Cancelled { get; private set; }

    /// <summary>When set, Append number n (counted from zero) answers this instead of done.</summary>
    public Func<int, Outcome>? AppendAnswer { get; set; }

    /// <summary>When set, Stop answers this instead of the clip.</summary>
    public Outcome<GifClip?>? StopAnswer { get; set; }

    /// <summary>Run inside every Append, before it is held or answers, so a check can look at its caller then.</summary>
    public Action? WhileAppending { get; set; }

    /// <summary>Opens this encoder, recording the path, the size and the name.</summary>
    public ClipOpener Opener => (workPath, width, height, name) =>
    {
        Opened = (workPath, width, height, name);
        return Outcome<IClipEncoder>.Of(this);
    };

    /// <summary>Every Append that returned, in order.</summary>
    public IReadOnlyList<Appended> Appends
    {
        get
        {
            lock (gate)
            {
                return appends.ToList();
            }
        }
    }

    public long BytesWritten => Interlocked.Read(ref bytes);

    /// <summary>From now on every Append waits until <see cref="LetThrough"/> lets it go.</summary>
    public void Hold() => holding = true;

    /// <summary>Lets <paramref name="count"/> held Appends go.</summary>
    public void LetThrough(int count = 1) => letThrough.Release(count);

    /// <summary>Holds no more Appends and lets every waiting one go.</summary>
    public void LetAllThrough()
    {
        holding = false;
        letThrough.Release(1000);
    }

    /// <summary>Waits until <paramref name="count"/> Appends have begun.</summary>
    public void WaitForAppendsBegun(int count) => WaitUntil(() => Volatile.Read(ref entered) >= count, $"{count} Appends begun");

    /// <summary>Waits until <paramref name="count"/> Appends have returned.</summary>
    public void WaitForAppends(int count) => WaitUntil(() => Appends.Count >= count, $"{count} Appends returned");

    public Outcome Append(MediaFrame frame, long atMs)
    {
        var index = Interlocked.Increment(ref entered) - 1;
        WhileAppending?.Invoke();
        if (holding && !letThrough.Wait(Patience))
        {
            return new Failure("Append", "the check's encoder", "the check never let it through");
        }

        var answer = AppendAnswer?.Invoke(index) ?? Outcome.Done;
        lock (gate)
        {
            appends.Add(new Appended(atMs, frame.Pixels[0] | frame.Pixels[1] << 8, Environment.CurrentManagedThreadId));
        }

        Interlocked.Add(ref bytes, 1000);
        return answer;
    }

    public Outcome<GifClip?> Stop(long atMs)
    {
        StoppedAt = atMs;
        var appended = Appends;
        return StopAnswer ?? Outcome<GifClip?>.Of(appended.Count == 0 ? null : new GifClip(Opened!.Value.WorkPath, appended.Count, atMs - appended[0].AtMs));
    }

    public void Cancel() => Cancelled = true;

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, $"waited in vain for {what}");
            Thread.Sleep(1);
        }
    }
}
