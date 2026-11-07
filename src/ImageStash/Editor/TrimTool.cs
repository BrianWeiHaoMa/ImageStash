using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>
/// Trim (G22), listed for an animated item only (G20): two handles on the filmstrip whose drags set the frames the
/// finished clip keeps, each one step Undo reverses (G25), and Reset, which keeps every frame again; the option row states
/// the position over the clip's length and what is kept and for how long. The trim is the list's one trim layer, its
/// range in the original's frame indices, so the frames outside the handles stay in the original and a trim can be
/// widened later; one covering the whole clip is no trim, and the list then holds none. The trim never moves a layer's
/// range, nor a range the trim. It is never a selected layer, so no click selects it and Del never removes it, and a
/// press on the picture under Trim selects the layer under it as under any tool.
/// </summary>
public sealed class TrimTool : EditorTool
{
    internal TrimTool(EditorSession session)
        : base(session)
    {
    }

    public override string Name => "Trim";

    public override string Hint => "Drag the handles under the picture";

    /// <summary>The frames the handles keep: the trim's range clamped into the frames the editor can show, or every frame without a trim.</summary>
    public FrameRange Kept
    {
        get
        {
            var last = Session.FrameCount - 1;
            return Session.Layers.Trim is { Range: var range } ? new FrameRange(Math.Clamp(range.First, 0, last), Math.Clamp(range.Last, 0, last)) : new FrameRange(0, last);
        }
    }

    /// <summary>
    /// Where the play is over the clip's length, "0:01.3 / 0:02.4": the frame showing's start, from the frames' own
    /// durations, whatever the trim; null until every frame of the original has been timed, so never while the frames are
    /// read, nor once a frame could not be read (G35).
    /// </summary>
    public string? Position => Timeline() is { } times ? $"{ClipLength.Clock(times[Math.Clamp(Session.FrameIndex, 0, times.Count - 1)].StartMs)} / {ClipLength.Clock(End(times[^1]))}" : null;

    /// <summary>
    /// What the handles keep and for how long, "Keeps 0:00.4 – 0:02.1 · 1.7 s of 2.4 s", from the kept frames' own
    /// durations, so the words and the saved clip agree (F3); null, as <see cref="Position"/> is, until every frame of
    /// the original has been timed (G35).
    /// </summary>
    public string? KeptWords
    {
        get
        {
            if (Timeline() is not { } times)
            {
                return null;
            }

            var kept = Kept;
            var (start, end) = (times[Math.Min(kept.First, times.Count - 1)].StartMs, End(times[Math.Min(kept.Last, times.Count - 1)]));
            return $"Keeps {ClipLength.Clock(start)} – {ClipLength.Clock(end)} · {ClipLength.Words(end - start)} of {ClipLength.Words(End(times[^1]))}";
        }
    }

    /// <summary>
    /// A press on the left or right handle: its drag moves that end of the kept frames, never past the other (G22), which
    /// stays as the trim has it, every frame of the original without one, even beyond the frames the editor could read.
    /// </summary>
    public void PressHandle(RangeEnd end)
    {
        if (Session.Accepts)
        {
            Session.EndTyping();
            var before = Session.Layers;
            PressEnd(before.Trim?.Range ?? new FrameRange(0, Session.ClipFrames - 1), end, kept => Trimmed(before, kept));
        }
    }

    /// <summary>The handle pressed moved to frame <paramref name="frame"/>.</summary>
    public void DragHandle(int frame)
    {
        if (Session.Accepts)
        {
            DragEnd(frame);
        }
    }

    /// <summary>The handle pressed let go where its drag left it: one step, when the kept frames changed (G25).</summary>
    public void ReleaseHandle()
    {
        if (Session.Accepts)
        {
            ReleaseEnd();
        }
    }

    /// <summary>Keeps every frame again: no trim, as one step.</summary>
    public void Reset()
    {
        if (Session.Accepts && Session.Layers.Trim is not null)
        {
            Session.ChangeKeepingSelected(Trimmed(Session.Layers, new FrameRange(0, Session.ClipFrames - 1)));
        }
        else
        {
            Session.RaiseChanged();
        }
    }

    /// <summary>The trim is never selected, so no layer is this tool's to select.</summary>
    public override bool Owns(Layer layer) => false;

    private static long End(FrameTime time) => time.StartMs + time.DurationMs;

    /// <summary>
    /// <paramref name="layers"/> with <paramref name="kept"/> as their trim, in the trim's place in the list or after the
    /// rest; with no trim when it keeps the whole clip, which a trim never needs to.
    /// </summary>
    private LayerList Trimmed(LayerList layers, FrameRange kept)
    {
        var rest = layers.Layers.Where(layer => layer is not TrimLayer).ToList();
        if (!Session.WholeClip(kept))
        {
            rest.Insert(layers.Layers.ToList().FindIndex(layer => layer is TrimLayer) is var at and >= 0 ? at : rest.Count, new TrimLayer(kept));
        }

        return new LayerList(rest);
    }

    /// <summary>Every frame's time, once each frame of the original has been read; else null.</summary>
    private IReadOnlyList<FrameTime>? Timeline()
    {
        // Complete alone also means the read stopped at a frame it could not read: the frames before it are not the clip.
        var times = Session.FrameTimes;
        return Session.TimelineComplete && times.Count > 0 && times.Count >= Session.ClipFrames ? times : null;
    }
}
