using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Look;

namespace ImageStash.Capture;

/// <summary>
/// The region selection on the real screen (G14): one borderless topmost window per display, placed on its bounds in
/// physical pixels, showing that display's frozen grab dimmed; a drag with the left button selects a rectangle within
/// the display it started on, shown undimmed with a thin light border and its size above it; Esc, or a click without a
/// drag, cancels. Each window is shown activated, the last one ending in front, so Esc reaches the overlay from the
/// moment it shows; the pointer over it is <see cref="RegionCursor"/>'s cross at its display's scaling; every window is
/// closed, and every grab's bitmap and every cross let go, before <see cref="Select"/> returns (F4).
/// The record shortcut opens the same overlay (G15); only its hint and its windows' title tell what the release leads to.
/// </summary>
public sealed class RegionOverlay : IRegionSelection
{
    public const string Title = "ImageStash screenshot";

    public const string RecordingTitle = "ImageStash record a clip";

    private readonly List<Avalonia.Controls.Window> shown = [];

    /// <summary>The overlay's windows showing now, one per display; none between selections.</summary>
    public IReadOnlyList<Avalonia.Controls.Window> Shown => shown.ToList();

    /// <summary>The size label showing now, while a rectangle is dragged; null otherwise.</summary>
    public string? Label => shown.Select(window => ((RegionView)window.Content!).Label).FirstOrDefault(label => label is not null);

    public async Task<SelectedRegion?> Select(IReadOnlyList<DisplayGrab> grabs, CaptureMode mode)
    {
        // Answered from inside a window's own event, it closes the windows only once that event has returned.
        var answer = new TaskCompletionSource<SelectedRegion?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            foreach (var grab in grabs)
            {
                var window = Overlay(grab, mode, answer);
                shown.Add(window);
                // Small, on its display, then full screen there, which takes that display's own rectangle in physical
                // pixels: a size in device-independent units comes back from Avalonia's rounding a pixel off at some
                // scalings, leaving an edge live and undimmed or spilling onto the next display (G14, E1).
                window.Position = grab.Display.Bounds.Position;
                window.WindowState = WindowState.FullScreen;
                window.Show();
            }

            return await answer.Task;
        }
        finally
        {
            foreach (var window in shown)
            {
                window.Close();
                ((RegionView)window.Content!).Dispose();
                window.Cursor?.Dispose();
            }

            shown.Clear();
        }
    }

    private static Avalonia.Controls.Window Overlay(DisplayGrab grab, CaptureMode mode, TaskCompletionSource<SelectedRegion?> answer)
    {
        // The picture first: a window made before it, whose native window Avalonia creates at once, would be left behind
        // unclosed if there were no room for the picture.
        var view = new RegionView(grab, mode, region => answer.TrySetResult(region));
        Cursor? cross = null;
        Avalonia.Controls.Window window;
        try
        {
            cross = RegionCursor.For(grab.Display.Scaling);
            window = new Avalonia.Controls.Window
            {
                Title = mode == CaptureMode.Recording ? RecordingTitle : Title,
                Content = view,
                WindowDecorations = WindowDecorations.None,
                ShowInTaskbar = false,
                Topmost = true,
                CanResize = false,
                CanMinimize = false,
                CanMaximize = false,
                Cursor = cross,
                // Wholly on its display before it goes full screen there: the nearest display to the window is the one taken.
                Width = 64,
                Height = 64,
            };
        }
        catch
        {
            view.Dispose();
            cross?.Dispose();
            throw;
        }

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                answer.TrySetResult(null);
            }
        };
        // Closed some other way, such as Alt+F4: cancelled, as Esc would.
        window.Closed += (_, _) => answer.TrySetResult(null);
        return window;
    }

    /// <summary>
    /// One display's frozen picture, dimmed but for the dragged rectangle, which carries a thin light border outside it
    /// and its size label above it, with the hint at the bottom. The rectangle is followed in physical
    /// pixels on the desktop and kept to this display by <see cref="RegionSelection"/>. The hint says what the release
    /// leads to.
    /// </summary>
    private sealed class RegionView : Control, IDisposable
    {
        private const string ScreenshotHint = "Drag over what you want · release to save it to your stash · Esc to cancel";
        private const string RecordingHint = "Drag over what you want · release, then Record to start · Esc to cancel";

        private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(133, 12, 14, 22));
        private static readonly IBrush PillBackground = new SolidColorBrush(Color.FromArgb(219, 20, 21, 27));
        private static readonly IBrush Outline = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
        private static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");

        private readonly DrawnLook look;
        private readonly Display display;
        private readonly string hint;
        private readonly Action<SelectedRegion?> released;
        private readonly Bitmap picture;
        private PixelPoint? press;
        private PixelRect? selected;

        public RegionView(DisplayGrab grab, CaptureMode mode, Action<SelectedRegion?> released)
        {
            look = new DrawnLook(this);
            display = grab.Display;
            hint = mode == CaptureMode.Recording ? RecordingHint : ScreenshotHint;
            this.released = released;
            picture = FrameBitmap.Of(grab.Frame);
            // The frozen picture pixel for pixel, never smoothed.
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        }

        /// <summary>The size label's words while a rectangle is dragged; null otherwise.</summary>
        public string? Label => selected is { } rectangle ? RegionSelection.Label(rectangle) : null;

        public void Dispose() => picture.Dispose();

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (press is null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                press = OnDesktop(e);
                // Followed onto another display too, where the rectangle stops at this one's edge.
                e.Pointer.Capture(this);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (press is { } from)
            {
                selected = RegionSelection.Released(display, from, OnDesktop(e))?.Rectangle;
                InvalidateVisual();
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (press is { } from && e.InitialPressMouseButton == MouseButton.Left)
            {
                released(RegionSelection.Released(display, from, OnDesktop(e)));
            }
        }

        public override void Render(DrawingContext context)
        {
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? display.Scaling;
            var whole = new Rect(Bounds.Size);
            context.DrawImage(picture, new Rect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height), whole);
            if (selected is not { } rectangle)
            {
                context.FillRectangle(Dim, whole);
            }
            else
            {
                var hole = new Rect((rectangle.X - display.Bounds.X) / scaling, (rectangle.Y - display.Bounds.Y) / scaling, rectangle.Width / scaling, rectangle.Height / scaling);
                context.FillRectangle(Dim, new Rect(0, 0, whole.Width, hole.Top));
                context.FillRectangle(Dim, new Rect(0, hole.Bottom, whole.Width, Math.Max(0, whole.Height - hole.Bottom)));
                context.FillRectangle(Dim, new Rect(0, hole.Top, hole.Left, hole.Height));
                context.FillRectangle(Dim, new Rect(hole.Right, hole.Top, Math.Max(0, whole.Width - hole.Right), hole.Height));
                // One physical pixel just outside the rectangle, so every pixel inside shows as it will be saved.
                var pixel = 1 / scaling;
                context.DrawRectangle(new Pen(Outline, pixel), hole.Inflate(pixel / 2));
                DrawLabel(context, RegionSelection.Label(rectangle), hole, whole);
            }

            DrawHint(context, hint, whole);
        }

        /// <summary>The size in a dark pill just above the rectangle's top-left corner, or just inside it at the display's top.</summary>
        private void DrawLabel(DrawingContext context, string words, Rect hole, Rect whole)
        {
            var text = Text(words, 11.5, FontWeight.SemiBold);
            var size = new Size(text.Width + 16, text.Height + 8);
            var top = hole.Top - 6 - size.Height >= 0 ? hole.Top - 6 - size.Height : hole.Top + 6;
            var left = Math.Clamp(hole.Left, 0, Math.Max(0, whole.Width - size.Width));
            context.DrawRectangle(PillBackground, null, new RoundedRect(new Rect(new Point(left, top), size), look.Radius(LookKeys.SmallRadius)));
            context.DrawText(text, new Point(left + 8, top + 4));
        }

        /// <summary>What a drag does and how to cancel, in a dark pill at the bottom centre.</summary>
        private void DrawHint(DrawingContext context, string hint, Rect whole)
        {
            var text = Text(hint, 12.5, FontWeight.Normal);
            var size = new Size(text.Width + 28, 32);
            var place = new Rect(new Point((whole.Width - size.Width) / 2, whole.Height - 28 - size.Height), size);
            context.DrawRectangle(PillBackground, null, new RoundedRect(place, Math.Min(look.Radius(LookKeys.PillRadius), size.Height / 2)));
            context.DrawText(text, new Point(place.X + 14, place.Y + (size.Height - text.Height) / 2));
        }

        private static FormattedText Text(string words, double size, FontWeight weight) =>
            new(words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Font, FontStyle.Normal, weight), size, Brushes.White);

        private PixelPoint OnDesktop(PointerEventArgs e)
        {
            var window = TopLevel.GetTopLevel(this)!;
            return RegionSelection.OnDesktop(window.PointToScreen(default), e.GetPosition(window), window.RenderScaling);
        }
    }
}
