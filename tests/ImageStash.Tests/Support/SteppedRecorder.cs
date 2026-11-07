using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Seams;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Support;

/// <summary>
/// A recorder for the recording loop's checks: over the Screen seam's double showing one small display, a clock the check
/// steps, and the encoder's double unless a check gives another opener. Each grab's picture is one flat colour carrying
/// the grab's index in its blue and green bytes, so every frame tells which sample it is.
/// </summary>
public sealed class SteppedRecorder
{
    public const string Clip = "Recording 2026-10-20 12.00.00";

    public static readonly Display Display = new(new PixelRect(0, 0, 64, 48), new PixelRect(0, 0, 64, 48), 1, IsPrimary: true);

    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(30);

    public SteppedRecorder(ClipOpener? opener = null)
    {
        Screen.EachGrab = index =>
        {
            WhileGrabbing?.Invoke(index);
            return RefusedGrab is { } refused && index == refused.At
                ? refused.Refusal
                : Outcome<MediaFrame>.Of(Frame(Display.Bounds.Width, Display.Bounds.Height, (_, _) => ((byte)index, (byte)(index >> 8), 200)));
        };
        Recorder = new Recorder(Screen, opener ?? Encoder.Opener, Clock);
    }

    public RecordingScreen Screen { get; } = new() { Shown = [Display] };

    public SteppedClock Clock { get; } = new();

    public RecordingClipEncoder Encoder { get; } = new();

    public Recorder Recorder { get; }

    /// <summary>Runs inside each grab, on the sampler's thread, with the grab's index.</summary>
    public Action<int>? WhileGrabbing { get; set; }

    /// <summary>The grab, counted from zero, that the screen refuses, and its refusal.</summary>
    public (int At, Failure Refusal)? RefusedGrab { get; set; }

    /// <summary>The colour of grab <paramref name="index"/>'s picture, as <see cref="SyntheticFrames.Pixel"/> reads it.</summary>
    public static uint Colour(int index) => Color((byte)index, (byte)(index >> 8), 200);

    /// <summary>Starts recording the whole display: the cap 30 s unless given, with the pointer unless told otherwise.</summary>
    public Outcome Start(string? path = null, TimeSpan? cap = null, bool withPointer = true) =>
        Recorder.Start(Display.Bounds, withPointer, cap ?? Cap, path ?? Path.Combine(Path.GetTempPath(), "never-written.gif"), Clip);

    public void Step(long ms) => Clock.Advance(TimeSpan.FromMilliseconds(ms));

    /// <summary>
    /// Steps to each tick up to <paramref name="untilMs"/>, the first at once, waiting after each until the recorder
    /// holds no frame: an encoder slower than the stepped clock would otherwise see samples skipped.
    /// </summary>
    public void StepKeepingUp(long untilMs)
    {
        var period = (long)Recorder.SamplePeriod.TotalMilliseconds;
        for (var at = 0L; at <= untilMs; at += period)
        {
            Step(at == 0 ? 0 : period);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Recorder.FramesHeld > 0)
            {
                Assert.IsLessThan(deadline, DateTime.UtcNow, $"the encoder never caught up at {at} ms");
                Thread.Sleep(1);
            }
        }
    }

    public Task<Outcome<GifClip?>> Ended() => Recorder.Ended.WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>
    /// Steps <paramref name="clock"/> to each tick of <paramref name="recorder"/> up to <paramref name="untilMs"/> while it
    /// samples, the first at once, waiting after each until it holds no frame, for a recording a session started.
    /// </summary>
    public static void StepKeepingUp(SteppedClock clock, Recorder recorder, long untilMs)
    {
        var period = (long)Recorder.SamplePeriod.TotalMilliseconds;
        for (var at = 0L; at <= untilMs && recorder.Sampling; at += period)
        {
            clock.Advance(TimeSpan.FromMilliseconds(at == 0 ? 0 : period));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (recorder.FramesHeld > 0)
            {
                Assert.IsLessThan(deadline, DateTime.UtcNow, $"the encoder never caught up at {at} ms");
                Thread.Sleep(1);
            }
        }
    }
}
