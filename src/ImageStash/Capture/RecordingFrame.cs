using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using ImageStash.Seams;

namespace ImageStash.Capture;

/// <summary>What shows a recording on the screen while it waits and runs (G15, F4): the frame around its region and the bar; the live one, or a check's stand-in.</summary>
public interface IRecordingFrame
{
    /// <summary>
    /// Shows <paramref name="region"/> waiting to be recorded: the thin red frame around it, labelled with its size and
    /// no REC, and the bar beside it offering Record, which runs <paramref name="record"/>, and Cancel, which runs
    /// <paramref name="cancel"/>, on the UI thread; the bar's Stop, once <see cref="Recording"/> offers it, runs
    /// <paramref name="stop"/>, and <paramref name="cap"/> is the length it reads the time against. Neither takes the
    /// activation, and neither is in any grab (G15). A failure, with nothing left shown, when the platform will not show
    /// the frame as G15 needs it.
    /// </summary>
    Outcome Show(SelectedRegion region, TimeSpan cap, Action record, Action stop, Action cancel);

    /// <summary>
    /// The recording has started: the frame's label carries REC, and the same bar, placed again for its new size, reads
    /// the time against the cap and the size so far from <paramref name="readings"/> and offers Stop and Cancel instead
    /// of Record (G15). A failure, with nothing left shown, when the platform will not show the frame so.
    /// </summary>
    Outcome Recording(IRecordingReadings readings);

    /// <summary>The bar names the saving of the clip instead, offering no Stop or Cancel (G35).</summary>
    void Saving();

    /// <summary>The frame and the bar gone (F4); nothing once they are.</summary>
    void Close();
}

/// <summary>Where the frame's window lies on the desktop, and the parts of it that show, from its top-left: the band's strips and the label, in physical pixels.</summary>
public sealed record FrameLayout(PixelRect Window, IReadOnlyList<PixelRect> Parts, PixelRect Label);

/// <summary>
/// The recording's frame and bar on the real screen (G15): the frame is one borderless topmost window over the region
/// and its label, shown only in a red band around the region and the label, "W × H" while the region waits and
/// "● REC · W × H" once it records, through the Quiet-windows seam's <see cref="IQuietWindows.ShowOnly"/>, so the region
/// beneath stays live and clickable (Windows makes any borderless window at least 32 × 39 pixels, so four thin windows
/// cannot be the band); the bar is a second window placed by <see cref="RecordingBar.Place"/>, the same window for both
/// of its looks, placed again when it changes. Both are left out of every capture before they are first shown, which
/// holds for the window's life, a refusal named through <paramref name="named"/> and the window shown anyway, and both
/// are shown without activation, so Esc and every key reach the app being recorded (G15, S4, G35); neither opens a
/// popup. A frame Windows refuses its shape is that showing's failure, both windows closed, since a frame shown whole
/// would cover the region and none would leave it unmarked.
/// </summary>
public sealed class RecordingFrame(IQuietWindows quietWindows, IScreen screen, Action<Failure> named) : IRecordingFrame
{
    public const string Title = "ImageStash recording";

    /// <summary>The band's and the label's red.</summary>
    public static readonly Color Red = Color.FromRgb(0xE5, 0x48, 0x4D);

    /// <summary>How often the bar reads the time and the size (L2).</summary>
    private static readonly TimeSpan Refresh = TimeSpan.FromMilliseconds(100);

    private static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");

    private DispatcherTimer? timer;

    /// <summary>The region and the frame's drawing while the frame shows; null otherwise.</summary>
    private (SelectedRegion Region, FrameView View)? shown;

    /// <summary>The frame's window while it shows; null otherwise.</summary>
    public Avalonia.Controls.Window? FrameWindow { get; private set; }

    /// <summary>The bar's window while it shows; null otherwise.</summary>
    public Avalonia.Controls.Window? BarWindow { get; private set; }

    /// <summary>The bar's content while it shows; null otherwise.</summary>
    public RecordingBar? Bar { get; private set; }

    /// <summary>The words the frame's label shows now; null while the frame does not show.</summary>
    public string? LabelWords { get; private set; }

    /// <summary>The band's width in physical pixels: two at scaling 1, the nearest whole number at another (L2).</summary>
    public static int Band(double scaling) => Math.Max(1, (int)Math.Round(2 * scaling, MidpointRounding.AwayFromZero));

    /// <summary>The label's words for a region being recorded, REC and its size in physical pixels.</summary>
    public static string Label(PixelRect region) => "● REC · " + RegionSelection.Label(region);

    /// <summary>The label's words for a region waiting to be recorded: its size alone, no REC (G15).</summary>
    public static string WaitingLabel(PixelRect region) => RegionSelection.Label(region);

    /// <summary>
    /// The frame's layout for <paramref name="region"/> on its display: the band just outside the region where the display
    /// has room, else over the region's own edge, so it always shows; the label on the band above the region at its left,
    /// or inside the region's top-left corner when the display has no room above, kept on the display; the window the
    /// band and the label together.
    /// </summary>
    public static FrameLayout Layout(PixelRect region, PixelRect display, int band, PixelSize label)
    {
        var (left, top) = (Math.Max(region.X - band, display.X), Math.Max(region.Y - band, display.Y));
        var (right, bottom) = (Math.Min(region.Right + band, display.Right), Math.Min(region.Bottom + band, display.Bottom));
        var labelTop = top - label.Height >= display.Y ? top - label.Height : top + band;
        var labelLeft = Math.Max(display.X, Math.Min(left, display.Right - label.Width));
        var labelPlace = new PixelRect(labelLeft, labelTop, label.Width, label.Height);
        var (windowLeft, windowTop) = (Math.Min(left, labelPlace.X), Math.Min(top, labelPlace.Y));
        var window = new PixelRect(windowLeft, windowTop, Math.Max(right, labelPlace.Right) - windowLeft, Math.Max(bottom, labelPlace.Bottom) - windowTop);
        var strips = new[]
        {
            new PixelRect(left, top, right - left, band),
            new PixelRect(left, bottom - band, right - left, band),
            new PixelRect(left, top + band, band, bottom - top - 2 * band),
            new PixelRect(right - band, top + band, band, bottom - top - 2 * band),
        };
        var origin = new PixelVector(-window.X, -window.Y);
        var parts = strips.Where(strip => strip.Width > 0 && strip.Height > 0).Append(labelPlace).Select(part => part.Translate(origin)).ToList();
        return new FrameLayout(window, parts, labelPlace.Translate(origin));
    }

    /// <summary>
    /// The layout size of a window at <paramref name="place"/>, in physical pixels, at <paramref name="scaling"/>, never
    /// larger than the place, so a window at a place touching a display's edge never runs past it (G15). Avalonia snaps a
    /// window's size up to whole pixels, then truncates that size over the scaling times the scaling: the place over the
    /// scaling lands on it to the pixel, except one pixel short where that product falls a hair below the whole number (at
    /// 1.75, 230 pixels; no size between 80 and 1,500 at 1, 1.25, 1.5, 2 or 2.25). Asking for more lands a pixel past the
    /// place on most sizes, which put the bar past the working area's edge.
    /// </summary>
    public static Size WindowSize(PixelSize place, double scaling) => new(place.Width / scaling, place.Height / scaling);

    public Outcome Show(SelectedRegion region, TimeSpan cap, Action record, Action stop, Action cancel)
    {
        Close();
        // A click on Record or Stop is answered once its own event is over, so the bar is not changed under it. Cancel
        // changes nothing on the bar at once and acts at the click, so a clip the cap ends while the click is handled is
        // still discarded, never saved by the cap's step queued ahead of a posted Cancel (G15).
        var bar = new RecordingBar(cap, () => Dispatcher.UIThread.Post(record), () => Dispatcher.UIThread.Post(stop), cancel);
        BarWindow = QuietWindow(RecordingBar.Title, bar, bar.Background!);
        // Rounded as the notifications are; the frame, drawn in parts around the region, keeps its square corners.
        quietWindows.RoundCorners(BarWindow);
        // The window's ground follows the bar's, which follows the theme (F10), so no sliver of another colour shows.
        BarWindow.Bind(TemplatedControl.BackgroundProperty, bar.GetObservable(Border.BackgroundProperty));
        var barPlace = PlaceBar(region, bar);
        Bar = bar;
        ShowQuiet(BarWindow, barPlace);
        var view = new FrameView();
        shown = (region, view);
        var layout = Mark(region, view, WaitingLabel(region.Rectangle));
        FrameWindow = QuietWindow(Title, view, new SolidColorBrush(Red));
        ShowQuiet(FrameWindow, layout.Window);
        return Shaped(layout);
    }

    public Outcome Recording(IRecordingReadings readings)
    {
        if (shown is not { } current || Bar is not { } bar || BarWindow is not { } barWindow || FrameWindow is not { } frameWindow)
        {
            return new Failure(Recorder.Action, Title, "the frame is not on the screen");
        }

        var (region, view) = current;
        bar.Recording();
        // Wider now: placed again for its new size, so it stays beside the region and within the working area (G15).
        Fit(barWindow, PlaceBar(region, bar), barWindow.RenderScaling);
        bar.Refresh(readings);
        var layout = Mark(region, view, Label(region.Rectangle));
        Fit(frameWindow, layout.Window, frameWindow.RenderScaling);
        if (Shaped(layout).Failure is { } refused)
        {
            return refused;
        }

        timer = new DispatcherTimer(Refresh, DispatcherPriority.Background, (_, _) => bar.Refresh(readings));
        timer.Start();
        return Outcome.Done;
    }

    public void Saving()
    {
        timer?.Stop();
        Bar?.Saving();
    }

    public void Close()
    {
        timer?.Stop();
        timer = null;
        FrameWindow?.Close();
        BarWindow?.Close();
        (FrameWindow, BarWindow, Bar, shown, LabelWords) = (null, null, null, null, null);
    }

    /// <summary>
    /// The frame's layout around <paramref name="region"/> for a label of <paramref name="words"/> at the display's
    /// scaling, handed to <paramref name="view"/> to draw and kept as <see cref="LabelWords"/>.
    /// </summary>
    private FrameLayout Mark(SelectedRegion region, FrameView view, string words)
    {
        var scaling = region.Display.Scaling;
        var text = LabelText(words);
        var labelSize = new PixelSize((int)Math.Ceiling((text.Width + 16) * scaling), (int)Math.Ceiling((text.Height + 8) * scaling));
        var layout = Layout(region.Rectangle, region.Display.Bounds, Band(scaling), labelSize);
        view.Draw(layout, text);
        LabelWords = words;
        return layout;
    }

    /// <summary>The frame's window shown only in the band and the label of <paramref name="layout"/>; or, when Windows refuses that shape, both windows closed and the failure.</summary>
    private Outcome Shaped(FrameLayout layout)
    {
        if (!quietWindows.ShowOnly(FrameWindow!, layout.Parts).Succeeded)
        {
            Close();
            return new Failure(Recorder.Action, Title, "Windows would not show the frame around the region");
        }

        return Outcome.Done;
    }

    /// <summary>
    /// The bar's place for its content's size at the display's scaling, beside the region in the display's working area
    /// (G15); measured inside its window, where the theme gives its buttons their size.
    /// </summary>
    private static PixelRect PlaceBar(SelectedRegion region, RecordingBar bar)
    {
        var scaling = region.Display.Scaling;
        bar.Measure(Size.Infinity);
        var size = new PixelSize((int)Math.Ceiling(bar.DesiredSize.Width * scaling), (int)Math.Ceiling(bar.DesiredSize.Height * scaling));
        return RecordingBar.Place(region.Rectangle, region.Display.WorkingArea, size, (int)Math.Round(RecordingBar.Gap * scaling));
    }

    /// <summary>A borderless, topmost window without a taskbar button holding <paramref name="content"/>, not yet shown.</summary>
    private static Avalonia.Controls.Window QuietWindow(string title, Control content, IBrush background) => new()
    {
        Title = title,
        Content = content,
        Background = background,
        WindowDecorations = WindowDecorations.None,
        ShowInTaskbar = false,
        Topmost = true,
        CanResize = false,
        CanMinimize = false,
        CanMaximize = false,
    };

    /// <summary>
    /// <paramref name="window"/> at <paramref name="place"/>, marked out of every capture before its first frame, then shown
    /// without activation and fitted to its place once its scaling is its display's.
    /// </summary>
    private void ShowQuiet(Avalonia.Controls.Window window, PixelRect place)
    {
        Fit(window, place, window.DesktopScaling);
        if (screen.ExcludeFromCapture(window).Failure is { } refused)
        {
            named(refused);
        }

        quietWindows.ShowWithoutActivating(window);
        Fit(window, place, window.RenderScaling);
    }

    /// <summary>The window at <paramref name="place"/> in physical pixels, covering it exactly (<see cref="WindowSize"/>).</summary>
    private static void Fit(Avalonia.Controls.Window window, PixelRect place, double scaling)
    {
        window.Position = place.Position;
        (window.Width, window.Height) = WindowSize(place.Size, scaling);
    }

    private static FormattedText LabelText(string words) =>
        new(words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Font, FontStyle.Normal, FontWeight.SemiBold), 11.5, Brushes.White);

    /// <summary>The label's words in white on its part of the frame, which the window's red background fills.</summary>
    private sealed class FrameView : Control
    {
        private (FrameLayout Layout, FormattedText Text)? drawn;

        /// <summary>From now on, the label of <paramref name="layout"/> reading <paramref name="text"/>.</summary>
        public void Draw(FrameLayout layout, FormattedText text)
        {
            drawn = (layout, text);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (drawn is { } label)
            {
                var (layout, text) = label;
                var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                context.DrawText(text, new Point(layout.Label.X / scaling + 8, layout.Label.Y / scaling + 4));
            }
        }
    }
}
