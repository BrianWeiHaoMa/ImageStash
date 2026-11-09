using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The preview's picture (G19, G6, R7): the item's finished picture at its own size, one of its pixels to one of the
/// display's, scaled down whole when the area cannot hold it and never enlarged, over a checkerboard laid under its own
/// pixels so transparency shows (G6). A still is decoded once at its own size; an animation plays through a playback of
/// its own, one frame at a time at its own size, and a click pauses it on the frame showing and another resumes it from
/// there. While its window is hidden or minimised it plays nothing, and shown again it goes on as it was left, playing or
/// paused on its frame (G6, G1). It takes focus, with a ring while it has it, so a preview opened by keyboard shows
/// where focus is (G7).
/// </summary>
public sealed class PreviewPicture : Control
{
    private static readonly Typeface IconFace = new("Segoe Fluent Icons", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    private readonly DrawnLook look;
    private readonly Playback<Bitmap> playback;
    private readonly Action<Failure> failed;
    private ThumbnailRequest? showing;
    private bool animated;
    private Bitmap? still;
    private bool couldNotRead;
    private bool covered;
    private int reads;
    private TopLevel? window;

    // The still's latest read off the UI thread, which holds its file open until it completes.
    private Task stillRead = Task.CompletedTask;

    /// <summary>What showing the window again does to the play its hiding held: resume it, or start it; null when it holds none.</summary>
    private Action? whenShown;

    /// <param name="failed">Told of a failure to read the picture other than its being unreadable, which is only marked (G35).</param>
    public PreviewPicture(Action<Failure> failed)
    {
        look = new DrawnLook(this);
        this.failed = failed;
        playback = new Playback<Bitmap>(LocalClock.Instance, HeldFileRetry.Duration, FrameBitmap.Of,
            action => Dispatcher.UIThread.Post(action), bitmap => bitmap.Dispose(), (_, failure) => CouldNotRead(failure));
        playback.Changed += InvalidateVisual;
        Focusable = true;
        // The ring drawn round the picture is the visible focus.
        FocusAdorner = null;
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    /// <summary>Whether an animation shows, which a click pauses and resumes.</summary>
    public bool Animated => showing is not null && animated;

    /// <summary>Whether the animation is paused on the frame showing.</summary>
    public bool Paused => playback.Paused;

    /// <summary>The image showing now: the still, or the animation's frame; null while none has arrived or none can be read.</summary>
    public Bitmap? Image => showing is null ? null : animated ? playback.Frame(showing.Key) : still;

    /// <summary>Shows <paramref name="item"/>'s finished picture; the same picture again changes nothing, so a play goes on, or stays paused, where it is.</summary>
    public void Show(StashItem item)
    {
        var bound = Math.Max(1, Math.Max(item.Facts.Width, item.Facts.Height));
        var request = new ThumbnailRequest(ThumbnailKey.Of(item, ThumbnailSize.Large), item.FilePath, item.Name, bound);
        if (showing?.Key == request.Key)
        {
            return;
        }

        Clear();
        showing = request;
        animated = item.Facts.Animated && item.Facts.FrameCount > 1;
        if (animated && WindowHidden)
        {
            whenShown = () => playback.Play(request);
        }
        else if (animated)
        {
            playback.Play(request);
        }
        else
        {
            _ = Decode(request, reads);
        }
    }

    /// <summary>Shows nothing, its play stopped and its file closed.</summary>
    public void Clear()
    {
        reads++;
        showing = null;
        couldNotRead = false;
        whenShown = null;
        playback.Play(null);
        still?.Dispose();
        still = null;
        InvalidateVisual();
    }

    /// <summary>Completes once its play and its still's read have closed their files, after <see cref="Clear"/>.</summary>
    public Task Idle() => Task.WhenAll(playback.Idle(), stillRead);

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var corner = look.Radius(LookKeys.SurroundRadius);
        var rounded = new RoundedRect(area, corner);
        context.DrawRectangle(look.Brush(LookKeys.PictureSurround), null, rounded);
        using (context.PushClip(rounded))
        {
            if (Image is { } picture)
            {
                var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                var box = ShownPicture.Fit(picture.PixelSize, area.Size, scaling);
                context.DrawRectangle(ShownPicture.Checkerboard(dark, scaling), null, box);
                context.DrawImage(picture, new Rect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height), box);
            }
            else if (couldNotRead)
            {
                var mark = Glyph("", 20, look.Brush(LookKeys.Words));
                context.DrawText(mark, area.Center - new Point(mark.Width / 2, mark.Height / 2));
            }

            if (Animated)
            {
                // Pause while it plays, play while it is paused: what a click does (G19).
                var glyph = Glyph(Paused ? "" : "", 12, Brushes.White);
                var pill = new Rect(10, area.Height - 12 - 26, 26 + glyph.Width - 12, 26);
                context.DrawRectangle(look.Brush(LookKeys.PicturePill), null, new RoundedRect(pill, Math.Min(look.Radius(LookKeys.PillRadius), pill.Height / 2)));
                context.DrawText(glyph, pill.Center - new Point(glyph.Width / 2, glyph.Height / 2));
            }
        }

        if (IsFocused)
        {
            context.DrawRectangle(null, new Pen(look.Brush(LookKeys.FocusRing), 2), new RoundedRect(area.Deflate(1), corner));
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this);
        if (window is not null)
        {
            window.PropertyChanged += OnWindowChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (window is not null)
        {
            window.PropertyChanged -= OnWindowChanged;
        }

        window = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        if (Animated)
        {
            if (Paused)
            {
                playback.Resume();
            }
            else
            {
                playback.Pause();
            }
        }

        e.Handled = true;
    }

    /// <summary>Set while an open edit covers the picture, which then plays nothing, as while its window is hidden (G6).</summary>
    public bool Covered
    {
        get => covered;
        set
        {
            covered = value;
            HoldOrLetGo();
        }
    }

    /// <summary>Whether its window is hidden or minimised, or an edit covers it, when no one sees the picture.</summary>
    private bool WindowHidden => covered || window is null || !window.IsVisible || window is Avalonia.Controls.Window { WindowState: WindowState.Minimized };

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Avalonia.Controls.Window.WindowStateProperty)
        {
            HoldOrLetGo();
        }
    }

    /// <summary>
    /// Hidden, minimised or covered, the picture holds a play that is going on, as a pause does, so no frame is read
    /// meanwhile; seen again, it lets it go on. A play the user paused stays paused through each (G6, G1).
    /// </summary>
    private void HoldOrLetGo()
    {
        if (!WindowHidden)
        {
            var held = whenShown;
            whenShown = null;
            held?.Invoke();
        }
        else if (Animated && whenShown is null && !playback.Paused)
        {
            playback.Pause();
            whenShown = playback.Resume;
        }
    }

    /// <summary>A still, decoded once at its own size on a pool thread; only the latest asked for is shown.</summary>
    private async Task Decode(ThumbnailRequest request, int read)
    {
        var reading = Task.Run(() =>
        {
            var opened = MediaDecoder.Open(request.Path, request.Name, HeldFileRetry.Duration);
            if (!opened.Succeeded)
            {
                return Outcome<MediaFrame?>.Fail(opened.Failure);
            }

            using var decoder = opened.Value!;
            return decoder.NextFrame(request.Bound);
        });
        stillRead = reading;
        var decoded = await reading;
        if (read != reads)
        {
            return;
        }

        if (!decoded.Succeeded)
        {
            CouldNotRead(decoded.Failure);
            return;
        }

        if (decoded.Value is { } frame)
        {
            still = FrameBitmap.Of(frame);
            InvalidateVisual();
        }
    }

    private void CouldNotRead(Failure failure)
    {
        couldNotRead = true;
        if (!MediaDecoder.IsUnreadable(failure))
        {
            failed(failure);
        }

        InvalidateVisual();
    }

    private static FormattedText Glyph(string glyph, double size, IBrush? brush) =>
        new(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, IconFace, size, brush);
}
