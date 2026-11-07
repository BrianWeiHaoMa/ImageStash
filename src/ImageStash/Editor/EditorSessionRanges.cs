using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// The time ranges on an animated item (G22, What crosses every boundary): a text, hide or draw layer has a start and an
/// end on the original's timeline, the whole clip while its range is null, which the selected one shows as a bar over the
/// filmstrip whose ends drag; a caption bar spans the whole clip and has none. A range is kept as written: the trim
/// moving never rewrites it, and a bar end dragged moves that end alone. A range dragged back over the whole clip is
/// stored as none again, so it draws on every frame however the clip is later read.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// The selected layer's start and end as its bar shows them: its range, or the whole clip while it has none, clamped
    /// into the frames the editor can show for drawing only; null on a still, with nothing selected, or with a layer that
    /// has no range.
    /// </summary>
    public FrameRange? SelectedRange
    {
        get
        {
            if (!Animated || !Ranged(SelectedLayer, out var range))
            {
                return null;
            }

            var last = FrameCount - 1;
            return range is { } written ? new FrameRange(Math.Clamp(written.First, 0, last), Math.Clamp(written.Last, 0, last)) : new FrameRange(0, last);
        }
    }

    /// <summary>A press on <paramref name="end"/> of the selected layer's bar: its drag moves that end over the frames, one step at release (G22, G25).</summary>
    public void PressRangeEnd(RangeEnd end)
    {
        if (!Accepts || SelectedRange is null)
        {
            return;
        }

        // A field closing may take back a layer just added, and the selection with it.
        EndTyping();
        if (Selected is { } index && Ranged(Layers.Layers[index], out var range))
        {
            var before = Layers;
            tool.PressEnd(range ?? new FrameRange(0, ClipFrames - 1), end, moved => new LayerList(before.Layers.Select((layer, i) => i == index ? WithRange(layer, WholeClip(moved) ? null : moved) : layer)));
        }
    }

    /// <summary>The bar's end pressed moved over frame <paramref name="frame"/>.</summary>
    public void DragRangeEnd(int frame)
    {
        if (Accepts)
        {
            tool.DragEnd(frame);
        }
    }

    /// <summary>The bar's end pressed let go where its drag left it: one step, when the range changed.</summary>
    public void ReleaseRangeEnd()
    {
        if (Accepts)
        {
            tool.ReleaseEnd();
        }
    }

    /// <summary>Whether <paramref name="range"/> covers every frame of the original: no range, and no trim, is needed for it.</summary>
    internal bool WholeClip(FrameRange range) => range.First <= 0 && range.Last >= ClipFrames - 1;

    private static bool Ranged(Layer? layer, out FrameRange? range)
    {
        (var ranged, range) = layer switch
        {
            TextLayer text => (true, text.Range),
            HideLayer hide => (true, hide.Range),
            DrawMarkLayer mark => (true, mark.Range),
            _ => (false, (FrameRange?)null),
        };
        return ranged;
    }

    private static Layer WithRange(Layer layer, FrameRange? range) => layer switch
    {
        TextLayer text => text with { Range = range },
        HideLayer hide => hide with { Range = range },
        DrawMarkLayer mark => mark with { Range = range },
        _ => layer,
    };
}
