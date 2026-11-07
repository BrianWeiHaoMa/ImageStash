using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>
/// The session's frames (G22, G25, G6, R7, G35): on an animated original, its own player (<see cref="FramePlayer"/>),
/// playing from the opening, and the clip's timeline the filmstrip reads (<see cref="ClipTimeline"/>), both ended with
/// the session; on a still, the one frame, and every play key does nothing. A frame neither can read is named once a
/// session (<see cref="FramesFailure"/>), and the frames the editor shows end before it.
/// </summary>
public sealed partial class EditorSession
{
    private readonly object framesGate = new();
    private readonly MediaFrame first;
    private FramePlayer? player;
    private ClipTimeline? timeline;
    private Failure? framesFailure;

    /// <summary>Raised when the frame showing or the play's state changes, so the canvas and the filmstrip follow the play without the rest of the editor.</summary>
    public event Action? FrameChanged;

    /// <summary>The frame of the original the session shows and draws, at full size, with its index on the original's timeline.</summary>
    public ShownFrame Showing => player?.Shown ?? new ShownFrame(first, 0);

    /// <summary>The frame showing, at full size, in the boundary's pixels: with the one on its way, the only full-size frames the session holds (R7).</summary>
    public MediaFrame Frame => Showing.Frame;

    /// <summary>The index of <see cref="Frame"/> on the original's timeline, the first frame 0.</summary>
    public int FrameIndex => Showing.Index;

    /// <summary>Whether the original is animated, so it plays and has a filmstrip.</summary>
    public bool Animated => player is not null;

    /// <summary>The frames the editor can show: the original's, or, once a frame could not be read, those before it.</summary>
    public int FrameCount => player?.Count ?? 1;

    /// <summary>Each frame's duration and start on the clip's timeline, for the frames read so far, in order.</summary>
    public IReadOnlyList<FrameTime> FrameTimes => timeline?.Times ?? [];

    /// <summary>Whether <see cref="FrameTimes"/> holds every frame the editor can show: the read has ended, or there is none to make.</summary>
    public bool TimelineComplete => timeline?.Complete ?? true;

    /// <summary>The filmstrip's thumbnails read so far, in frame order, each smaller than its frame and no larger than <see cref="ClipTimeline.ThumbnailBound"/>.</summary>
    public IReadOnlyList<MediaFrame> Thumbnails => timeline?.Thumbnails ?? [];

    /// <summary>The filmstrip's busy state in words naming the item while its frames are being read, else null (G35).</summary>
    public string? FramesBusy => timeline is { Complete: false } && !closed ? $"Reading the frames of “{Subject}”…" : null;

    /// <summary>A frame of the original the editor could not read, named once by the view; null while every one read.</summary>
    public Failure? FramesFailure
    {
        get
        {
            lock (framesGate)
            {
                return framesFailure;
            }
        }
    }

    public bool Playing => player?.Playing ?? false;

    /// <summary>The original's frames, whether or not the editor could read them all: what a range covering the whole clip covers.</summary>
    internal int ClipFrames { get; private set; } = 1;

    /// <summary>The full-size frames the session holds now (R7).</summary>
    internal int FullSizeFramesHeld => player?.FullSizeFramesHeld ?? 1;

    /// <summary>Plays on from the frame showing (G25).</summary>
    public void Play() => player?.Play();

    /// <summary>Pauses on the frame showing (G25).</summary>
    public void Pause() => player?.Pause();

    /// <summary>Space (G25): plays or pauses.</summary>
    public void TogglePlaying()
    {
        if (Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Left and Right (G25): pauses on the frame one before or after, stopping at the clip's ends.</summary>
    public void Step(int frames) => player?.Step(frames);

    /// <summary>A click on the filmstrip: pauses on frame <paramref name="index"/>, or on the nearest the editor can show.</summary>
    public void Seek(int index) => player?.Seek(index);

    /// <summary>On an animated original, starts its player over the open decoder and the timeline's read; else closes the decoder.</summary>
    private void StartFrames(MediaDecoder decoder, TimeProvider time, SynchronizationContext? ui)
    {
        ClipFrames = decoder.FrameCount;
        if (decoder.FrameCount < 2)
        {
            decoder.Dispose();
            return;
        }

        Action<Action> onUiThread = ui is null ? action => action() : action => ui.Post(_ => action(), null);
        player = new FramePlayer(decoder, first, Subject, time, onUiThread, () => FrameChanged?.Invoke(), FramesFailed);
        timeline = ClipTimeline.Read(Edit.OriginalPath, Edit.OriginalSha256, Subject, heldFileRetry, RaiseChanged, (read, failure) =>
        {
            player.Limit(read);
            onUiThread(() =>
            {
                FramesFailed(failure);
                FrameChanged?.Invoke();
            });
        });
        player.Play();
    }

    /// <summary>Keeps the first frame failure of the session, for the view to name once (G35).</summary>
    private void FramesFailed(Failure failure)
    {
        lock (framesGate)
        {
            framesFailure ??= failure with { Action = "Read the picture's frames" };
        }

        RaiseChanged();
    }

    /// <summary>Ends the player and the timeline's read; the frame showing stays for a draw still to come.</summary>
    private void EndFrames()
    {
        player?.Dispose();
        timeline?.Dispose();
    }
}
