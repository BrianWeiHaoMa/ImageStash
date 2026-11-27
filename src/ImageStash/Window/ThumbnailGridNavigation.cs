using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ImageStash.Window;

public sealed partial class ThumbnailGrid
{
    private bool focusRevealPending;
    private (double Width, double RowHeight)? arrangedGeometry;
    private double roomBelow;
    private int keyMovesSeen;
    private bool revealClearOfRoom;

    /// <summary>
    /// The room the grid leaves below its last row for what is drawn over its bottom (the window's notices,
    /// <see cref="GridNoticeRoom"/>): its extent reaches that far past the last row, so scrolled to the end the row stands
    /// above them, and a tile the keys reach is revealed that far clear of the viewport's bottom (G7); a focus that moved
    /// any other way (a refresh keeping the place after a Delete, a click) is revealed against the viewport alone, so the
    /// grid stays where it was (G4). Setting it changes the extent alone, never the offset or the rows (G6), so it moves
    /// nothing the person is looking at; a grid listing nothing leaves none.
    /// </summary>
    internal double RoomBelow
    {
        get => roomBelow;
        set
        {
            if (value == roomBelow)
            {
                return;
            }

            roomBelow = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The height of a row at the size step the grid shows now (G6).</summary>
    internal double RowHeight => JustifiedRows.RowHeight(size());

    private void RevealRangeAfterReflow()
    {
        var geometry = (laidOutWidth, laidOutRowHeight);
        if (geometry == arrangedGeometry)
        {
            return;
        }

        arrangedGeometry = geometry;
        if (listing.HasKeyboardRange)
        {
            // Measure may have laid out new rows already. Reveal against the new scroll extent after arrange.
            Dispatcher.UIThread.Post(BringFocusedIntoView, DispatcherPriority.Background);
        }
    }

    internal (IReadOnlyList<GridRow> Rows, double ViewportHeight) ReadNavigationLayout()
    {
        // Settle layout when the key takes effect so a pending read or resize supplies the current rows and viewport.
        UpdateLayout();
        if (!IsEffectivelyVisible && double.IsFinite(laidOutWidth))
        {
            // A key pressed before the flyout hid can still resolve against the new listing at the last arranged width.
            LayOut(laidOutWidth);
        }

        return (Rows, View().Height);
    }

    internal void BringFocusedIntoView()
    {
        if (listing.Focused is not { } focused)
        {
            focusRevealPending = false;
            return;
        }

        if (!IsEffectivelyVisible)
        {
            focusRevealPending = true;
            return;
        }

        focusRevealPending = false;
        // A key asked for this reveal when it moved nothing (End on the last item): clear of the room too.
        NoteKeyMoves(focusMoved: false);
        UpdateLayout();
        foreach (var row in rows)
        {
            if (row.Tiles.FirstOrDefault(tile => tile.Id == focused) is { } tile)
            {
                this.BringIntoView(FocusRevealBounds(tile, View().Height, revealClearOfRoom ? roomBelow : 0));
                return;
            }
        }
    }

    /// <summary>
    /// The listing changed, its focus moved when <paramref name="focusMoved"/>; whether to reveal it: when it moved, or when
    /// a key asked to move it since the last change, even one that left it where it was (Down on the last row), so the tile
    /// the keys are on is never left under the notices (G7). What a key asked for is revealed clear of the room below, any
    /// other move against the viewport alone.
    /// </summary>
    private bool NoteKeyMoves(bool focusMoved)
    {
        var byKeys = listing.KeyMoves != keyMovesSeen;
        keyMovesSeen = listing.KeyMoves;
        if (focusMoved || byKeys)
        {
            revealClearOfRoom = byKeys;
        }

        return focusMoved || byKeys;
    }

    private void RetryFocusReveal()
    {
        if (focusRevealPending && window is { IsVisible: true })
        {
            Dispatcher.UIThread.Post(BringFocusedIntoView, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// What the scroller is asked to bring into view for <paramref name="tile"/>: the tile with its ring and the room below
    /// it, so it stands clear of what is drawn over the grid's bottom; with the ring alone, or the tile alone, when the
    /// viewport cannot hold more.
    /// </summary>
    internal static Rect FocusRevealBounds(GridTile tile, double viewportHeight, double roomBelow = 0)
    {
        var withRing = tile.Bounds.Inflate(JustifiedRows.Gap);
        var clear = withRing.WithHeight(withRing.Height + roomBelow);
        return clear.Height <= viewportHeight ? clear : withRing.Height <= viewportHeight ? withRing : tile.Bounds;
    }
}
