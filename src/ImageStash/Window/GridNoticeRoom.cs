using Avalonia.Controls;

namespace ImageStash.Window;

/// <summary>
/// The room the grid leaves below its last row for the stash window's notices, which are drawn over the body's bottom
/// (G16): while a card holds room in the stack (<see cref="NoticeStack.HoldsRoom"/>), as tall as the cards stand as laid
/// out, a card fading out or kept faded under the pointer included, with the notices' margin, so the last row scrolls
/// clear of them and the keys reveal a tile clear of them (G7); none once no card holds room. A problem notice staying past
/// its time keeps it, and its × gives it back, as the stack's rooms do. Only the grid's extent follows, never its offset,
/// so a notice arriving or leaving moves nothing the person is looking at (G4). While the preview or the editor covers the
/// grid the room holds as it was, the notices then standing over them, not over the grid. The notices' area is held low
/// enough that a row of the grid stays in view above it, though never lower than <see cref="LeastShown"/> of its newest
/// card, which it scrolls to, so in a window too short for both a notice and its × stay in reach; notices taller together
/// scroll within it (G16).
/// </summary>
public sealed class GridNoticeRoom
{
    /// <summary>How much of the newest card the notices' area always shows, a line of words beside the × (L2).</summary>
    public const double LeastShown = 48;

    private readonly ScrollViewer scroller;
    private readonly NoticeStack stack;
    private readonly Control area;
    private readonly Func<bool> covered;

    /// <param name="scroller">The grid's scroller, whose height is the body's.</param>
    /// <param name="area">The notices' area, over the body's bottom, holding <paramref name="stack"/>'s cards.</param>
    /// <param name="covered">Whether the preview or the editor covers the grid.</param>
    public GridNoticeRoom(ScrollViewer scroller, NoticeStack stack, Control area, Func<bool> covered)
    {
        this.scroller = scroller;
        this.stack = stack;
        this.area = area;
        this.covered = covered;
        // The cards' height changes as they are laid out, the body's as the window is sized, the rows' as the size steps.
        stack.View.SizeChanged += (_, _) => Follow();
        scroller.SizeChanged += (_, _) => Follow();
        if (scroller.Content is Control grid)
        {
            grid.SizeChanged += (_, _) => Follow();
        }
    }

    /// <summary>The room follows the notices' cards, the body and the grid's rows as they stand now; also told of a size step.</summary>
    public void Follow()
    {
        var grid = scroller.Content as ThumbnailGrid;
        var body = scroller.Bounds.Height;
        var rowsShown = grid is { Rows.Count: > 0 } && !covered();
        var clearAbove = rowsShown ? grid!.RowHeight + 2 * JustifiedRows.Gap : 0;
        var whole = Math.Max(0, body - WindowLayout.NoticeMargin().Bottom);
        var newest = stack.View.Children.Count > 0 ? Math.Min(stack.View.Children[^1].Bounds.Height, LeastShown) : 0;
        area.MaxHeight = body > 0 ? Math.Max(Math.Min(newest, whole), whole - clearAbove) : double.PositiveInfinity;
        if (grid is null || covered())
        {
            return;
        }

        var cards = stack.View.Bounds.Height;
        // The notices' own margin over the grid, not the editor's: under the editor the grid is covered and the room held.
        grid.RoomBelow = stack.HoldsRoom && cards > 0 ? Math.Min(cards, area.MaxHeight) + WindowLayout.NoticeMargin().Bottom : 0;
    }
}
