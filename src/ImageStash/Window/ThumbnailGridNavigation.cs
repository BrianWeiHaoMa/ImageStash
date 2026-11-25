using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ImageStash.Window;

public sealed partial class ThumbnailGrid
{
    private bool focusRevealPending;

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
        UpdateLayout();
        foreach (var row in rows)
        {
            if (row.Tiles.FirstOrDefault(tile => tile.Id == focused) is { } tile)
            {
                this.BringIntoView(FocusRevealBounds(tile, View().Height));
                return;
            }
        }
    }

    private void RetryFocusReveal()
    {
        if (focusRevealPending && window is { IsVisible: true })
        {
            Dispatcher.UIThread.Post(BringFocusedIntoView, DispatcherPriority.Background);
        }
    }

    internal static Rect FocusRevealBounds(GridTile tile, double viewportHeight)
    {
        var withRing = tile.Bounds.Inflate(JustifiedRows.Gap);
        return withRing.Height <= viewportHeight ? withRing : tile.Bounds;
    }
}
