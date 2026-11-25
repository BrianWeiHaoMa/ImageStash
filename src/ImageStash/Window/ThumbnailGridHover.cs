using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Store;

namespace ImageStash.Window;

public sealed partial class ThumbnailGrid
{
    private readonly TileHover hover = new(LocalClock.Instance);
    private readonly Popup hoverTip;
    private readonly DispatcherTimer hoverDelay = new(DispatcherPriority.Background);

    /// <summary>The last point on screen, so scrolling or moving the window can put another tile under a still pointer.</summary>
    private PixelPoint? pointer;
    private bool pointerButtonDown;

    private void WatchHover()
    {
        LogicalChildren.Add(hoverTip);
        hoverDelay.Tick += (_, _) =>
        {
            hoverDelay.Stop();
            UpdateHover();
        };
        LayoutUpdated += (_, _) => UpdateHover();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        // A scene re-hit can raise Enter without button information while a press is still captured.
        TrackPointer(e, mayRelease: false);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ForgetPointer();
    }

    private void OnHoverWindowMoved(object? sender, PixelPointEventArgs e) => UpdateHover();

    private void TrackPointer(PointerEventArgs e, bool mayRelease = true)
    {
        pointer = window is not null && e.Pointer.Type != PointerType.Touch
            ? PointerScreenPoint(e.GetPosition(window), window.PointToScreen(default), window.RenderScaling)
            : null;
        var buttons = e.GetCurrentPoint(this).Properties;
        var pressed = buttons.IsLeftButtonPressed || buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed;
        if (mayRelease || pressed)
        {
            pointerButtonDown = pressed;
        }

        UpdateHover();
        QueuePlay();
    }

    // The native coordinates are whole pixels. Truncating the conversion back from DIPs can lose a pixel at 175%.
    internal static PixelPoint PointerScreenPoint(Point at, PixelPoint origin, double scaling) =>
        origin + new PixelPoint((int)Math.Round(at.X * scaling), (int)Math.Round(at.Y * scaling));

    private void ForgetPointer()
    {
        pointer = null;
        EndHover();
        QueuePlay();
    }

    private void EndHover()
    {
        hoverDelay.Stop();
        hoverTip.IsOpen = false;
        if (hover.Track(null, listing.Replacements))
        {
            InvalidateVisual();
        }
    }

    private void UpdateHover()
    {
        var target = pointerButtonDown ? null : HoveredTile();
        if (hover.Track(target?.Item, listing.Replacements))
        {
            hoverDelay.Stop();
            hoverTip.IsOpen = false;
            InvalidateVisual();
            QueuePlay();
        }

        if (target is not { } tile || pointer is not { } at)
        {
            return;
        }

        var remaining = hover.Remaining;
        if (remaining > TimeSpan.Zero)
        {
            if (!hoverDelay.IsEnabled)
            {
                hoverDelay.Interval = remaining;
                hoverDelay.Start();
            }

            return;
        }

        ToolTips.ShowDrawnTarget(hoverTip, tile.VisibleBox, at, hover.Words);
    }

    private bool ShowsGrid => !covered && window is { IsVisible: true } && IsEffectivelyVisible
        && window is not Avalonia.Controls.Window { WindowState: WindowState.Minimized };

    private (StashItem Item, Rect VisibleBox)? HoveredTile()
    {
        // A press can capture and route movement here even when an in-window notice stands over the tile.
        if (!ShowsGrid || !IsPointerOver || pointer is not { } at)
        {
            return null;
        }

        var local = this.PointToClient(at);
        var grid = new Rect(Bounds.Size);
        var view = viewport?.TranslatePoint(default, this) is { } topLeft
            ? grid.Intersect(new Rect(topLeft, viewport.Bounds.Size))
            : grid;
        return HoverAt(local, view);
    }

    internal (StashItem Item, Rect VisibleBox)? HoverAt(Point point, Rect view)
    {
        if (LaidOutStale || !view.ContainsExclusive(point) || TileAt(point) is not { } tile)
        {
            return null;
        }

        return (laidOutItems[tile.Index], tile.Bounds.Intersect(view));
    }
}

/// <summary>A hover belongs to one item of one stash, independent of where that item is laid out.</summary>
internal sealed class TileHover(TimeProvider clock)
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(600);

    private long began;
    private int replacement;

    internal StashItem? Item { get; private set; }

    internal TimeSpan Remaining => Delay - clock.GetElapsedTime(began);

    internal bool Track(StashItem? item, int replacements)
    {
        var changed = Item?.Id != item?.Id || (item is not null && replacement != replacements);
        if (changed)
        {
            began = clock.GetTimestamp();
            replacement = replacements;
        }

        Item = item;
        return changed;
    }

    internal string? Words => Item is { } item && Remaining <= TimeSpan.Zero ? WordsFor(item) : null;

    private static string WordsFor(StashItem item)
    {
        var words = item.NameOrFileName;
        if (item.Tags.Count > 0)
        {
            words += "\n" + string.Join(" ", item.Tags.Take(3).Select(tag => $"#{tag}"));
        }

        return item.Favorite ? words + "\n★" : words;
    }
}
