using System.Runtime.CompilerServices;
using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>
/// A Meme's size while it is typed (G21, L2): a Meme a click added in this edit, whose size no corner drag has changed
/// since, shrinks as its letters are laid out so that they stay on two lines, or on as many as its typed lines, down to
/// half the size it was added at, and grows back towards that size as letters are taken away, holding the edge the click
/// put it at, its top or its bottom, wherever the picture's edges or a crop's lie since. What the session knows of
/// each Meme follows the very layer object, which every history step keeps, so Undo and Redo bring it back with the
/// layer; it is no layer and nothing stores it (F7), and it ends with the session.
/// </summary>
public sealed partial class EditorSession
{
    // Keyed by the layer object: a layer equal in value to another is still another layer.
    private readonly ConditionalWeakTable<Layer, MemeSize> memeSizes = [];

    /// <summary>A Meme a click just added at the top or the bottom: its letters fit to two lines from <paramref name="meme"/>'s size down to half of it.</summary>
    internal void FitAsTyped(TextLayer meme, bool atBottom) => memeSizes.AddOrUpdate(meme, new MemeSize(meme.FontSize, atBottom));

    /// <summary>
    /// <paramref name="changed"/>, made from <paramref name="from"/> by typing or a font, with its box following its letters
    /// (<see cref="LayerGeometry.Measured"/>); for a Meme that fits as typed, at the largest whole size from the one it was
    /// added at down to half of it whose letters Media lays on two lines, or on its typed lines when those are more, else
    /// at that half, its box keeping <paramref name="from"/>'s top or bottom edge as the click put it.
    /// </summary>
    internal TextLayer Laid(TextLayer from, TextLayer changed)
    {
        if (changed.Preset != TextPreset.Meme || !memeSizes.TryGetValue(from, out var fit) || fit.AddedAt is not { } start)
        {
            return LayerGeometry.Measured(changed, Layers, OriginalSize);
        }

        var floor = Math.Max(1, start - start / 2);
        var goal = Math.Max(2, LayerComposer.TextLines(changed with { Box = changed.Box with { Width = int.MaxValue } }));
        var size = floor;
        if (Lines(start) <= goal)
        {
            size = start;
        }
        else
        {
            // Fewer lines at a smaller size: the largest size between the floor and the start that keeps to the goal.
            for (int low = floor, high = start - 1; low <= high;)
            {
                var middle = low + (high - low) / 2;
                if (Lines(middle) <= goal)
                {
                    size = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
        }

        // The box takes its height here, from the edge it holds, so Measured's nearer-edge rule has nothing to choose.
        var sized = changed with { FontSize = size };
        var height = LayerComposer.TextSize(sized).Height;
        var y = fit.AtBottom ? (long)from.Box.Y + from.Box.Height - height : from.Box.Y;
        return LayerGeometry.Measured(sized with { Box = sized.Box with { Y = (int)Math.Clamp(y, int.MinValue, int.MaxValue), Height = height } }, Layers, OriginalSize);

        int Lines(int fontSize) => LayerComposer.TextLines(changed with { FontSize = fontSize });
    }

    /// <summary>
    /// A corner drag on the selected layer let go, from <paramref name="before"/>: a Meme whose size it changed keeps that
    /// size from then on, typed letters wrapping as they do on any Meme (G21: "a corner handle resizes it"); one it left
    /// at its size is as it was.
    /// </summary>
    internal void SizedByCorner(Snapshot before)
    {
        if (SelectedLayer is TextLayer { Preset: TextPreset.Meme } after && before.Selected is { } index
            && before.Layers.Layers[index] is TextLayer was && was.FontSize != after.FontSize)
        {
            memeSizes.AddOrUpdate(after, new MemeSize(null, false));
        }
    }

    /// <summary>
    /// What the session knows of a Meme passed on to the layer that replaces it in place, as a change of its text, font,
    /// colour, place or turn does: the same index in a list of the same length, the old layer gone from it. Layers kept
    /// as they were need nothing, and a list of another length added or removed layers without replacing one.
    /// </summary>
    private void CarrySizes(LayerList before, LayerList after)
    {
        if (before.Layers.Count != after.Layers.Count)
        {
            return;
        }

        for (var i = 0; i < after.Layers.Count; i++)
        {
            var (was, now) = (before.Layers[i], after.Layers[i]);
            if (!ReferenceEquals(was, now) && now is TextLayer { Preset: TextPreset.Meme } && !memeSizes.TryGetValue(now, out _)
                && memeSizes.TryGetValue(was, out var fit) && !after.Layers.Any(layer => ReferenceEquals(layer, was)))
            {
                memeSizes.AddOrUpdate(now, fit);
            }
        }
    }

    /// <summary>A Meme's size as the session knows it: the size a click added it at, which it fits from, null once a corner set it; and whether the click put it at the bottom.</summary>
    private sealed record MemeSize(int? AddedAt, bool AtBottom);

}
