using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ImageStash.Files;
using ImageStash.Look;
using Avalonia.Reactive;

namespace ImageStash.Capture;

/// <summary>
/// The recording's bar (G15), laid out once for the wait and the recording alike: Cancel, worded, at its left end; the
/// readings, "Up to 0:30" while the region waits and, once it records, a red dot, the elapsed time against the cap
/// ("0:01.6 / 0:30") and the size written so far ("~614 KB"); the record shortcut while it would act ("Ctrl+Shift+PrtScn
/// starts and stops", then "… stops"); and at its right end Record, which becomes Stop in the same place, so the pointer
/// that clicked Record rests on Stop. Every part whose words change holds room for the widest of them in both looks,
/// so the bar is one size from the wait to Stop; where the shortcut's words do not fit whole they give way. Once the
/// sampling has ended, only the words naming the saving (G35).
/// Where it goes beside the region is <see cref="Place"/>. Its buttons open no tooltip, which would be a window of its
/// own in the recording, and take no focus. Light under the light theme and dark under the dark (F10), repainted when the
/// theme changes; hovered or pressed, Record and Stop stay red with white words and Cancel's word reads on its ground.
/// </summary>
public sealed class RecordingBar : Border
{
    public const string Title = "ImageStash recording bar";

    public const string SavingWords = "Saving the clip…";

    /// <summary>The automation name of the bar's Record, Stop and Cancel buttons, by which scripts find them.</summary>
    public const string RecordName = "Record";
    public const string StopName = "Stop";
    public const string CancelName = "Cancel recording";

    /// <summary>The word Cancel shows on the bar.</summary>
    public const string CancelWord = "Cancel";

    /// <summary>The space between the region and the bar, in device-independent pixels.</summary>
    public const double Gap = 14;

    /// <summary>A double-click's time where the platform names none, and the least the bar waits out: Windows' default.</summary>
    public static readonly TimeSpan DefaultDoubleClick = TimeSpan.FromMilliseconds(500);

    private static readonly IBrush RedHovered = new SolidColorBrush(Color.FromRgb(0xC9, 0x33, 0x38));
    private static readonly IBrush RedPressed = new SolidColorBrush(Color.FromRgb(0xAE, 0x28, 0x2D));
    private static readonly FontFamily Icons = new("Segoe Fluent Icons");

    private readonly SteadyReadout elapsed;
    private readonly SteadyReadout size;
    private readonly TextBlock against;
    private readonly TextBlock upTo;
    private readonly Control waitingReadings;
    private readonly Control recordingReadings;
    private readonly Control recordLook;
    private readonly Control stopLook;
    private readonly SteadyReadout shortcutWords;
    private readonly Button primary;
    private readonly Button cancel;
    private readonly Action record;
    private readonly Action stop;
    private readonly TimeProvider clock;
    private readonly TimeSpan doubleClick;
    private string? shortcut;
    private BarLook look = BarLook.Waiting;
    private long? recordClicked;

    /// <summary>
    /// The bar for a region waiting to be recorded for up to <paramref name="cap"/>, naming <paramref name="shortcut"/>,
    /// the record shortcut's words while it would act, or nothing when null; offering Record, which runs
    /// <paramref name="record"/>, and Cancel, which runs <paramref name="cancel"/> within its click; its Stop, from
    /// <see cref="Recording"/> on, runs <paramref name="stop"/>, except for a click within <paramref name="doubleClick"/>
    /// on <paramref name="clock"/> of the click on Record, the second click of a double-click there.
    /// </summary>
    public RecordingBar(TimeSpan cap, string? shortcut, Action record, Action stop, Action cancel, TimeProvider clock, TimeSpan doubleClick)
    {
        (this.record, this.stop, this.clock, this.doubleClick) = (record, stop, clock, doubleClick);
        var capWords = Time((long)cap.TotalMilliseconds, tenths: false);
        // Held as wide as the cap's reading and a clip of the bar's envelope, so no later reading moves what follows.
        elapsed = new SteadyReadout { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Text = Time(0), Widest = WidestElapsed(cap) };
        size = new SteadyReadout { VerticalAlignment = VerticalAlignment.Center, Text = Size(0), Widest = WidestSize };
        against = new TextBlock { Text = "/ " + capWords, VerticalAlignment = VerticalAlignment.Center };
        upTo = new TextBlock { Text = "Up to " + capWords, VerticalAlignment = VerticalAlignment.Center };
        waitingReadings = Row(upTo);
        recordingReadings = Row(
            new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(RecordingFrame.Red), VerticalAlignment = VerticalAlignment.Center },
            elapsed,
            against,
            size);
        shortcutWords = new SteadyReadout { VerticalAlignment = VerticalAlignment.Center };
        recordLook = Labelled(new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, VerticalAlignment = VerticalAlignment.Center }, "Record");
        stopLook = Labelled(Glyph(""), "Stop");
        primary = Button(RecordName, PrimaryClicked, new SolidColorBrush(RecordingFrame.Red), OneOf(recordLook, stopLook));
        Hover(primary, RedHovered, RedPressed, Brushes.White);
        this.cancel = Button(CancelName, cancel, Brushes.Transparent, new TextBlock { Text = CancelWord, VerticalAlignment = VerticalAlignment.Center });

        TextElement.SetFontSize(this, 12.5);
        TextElement.SetFontFamily(this, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        Height = 44;
        Padding = new Thickness(8, 0);
        Child = Laid(this.cancel, OneOf(waitingReadings, recordingReadings), new GiveWay { Child = shortcutWords, Margin = new Thickness(12, 0) }, primary);
        Show(waitingReadings, recordingReadings);
        Show(recordLook, stopLook);
        NameShortcut(shortcut);
        Paint();
    }

    private enum BarLook
    {
        Waiting,
        Recording,
        Saving,
    }

    /// <summary>Record, while the region waits; then null. The same button as <see cref="StopButton"/>, in the same place.</summary>
    public Button? RecordButton => look == BarLook.Waiting ? primary : null;

    /// <summary>Stop, from <see cref="Recording"/> until the bar names the saving; null otherwise.</summary>
    public Button? StopButton => look == BarLook.Recording ? primary : null;

    /// <summary>Cancel, until the bar names the saving; then null.</summary>
    public Button? CancelButton => look == BarLook.Saving ? null : cancel;

    /// <summary>The words the bar shows now outside its buttons, in order.</summary>
    public IReadOnlyList<string> Words => Child is Panel row ? Texts(row).ToList() : [((TextBlock)Child!).Text ?? ""];

    /// <summary>
    /// Where the bar goes for <paramref name="region"/> in physical pixels: centred beneath it when the working area has
    /// room below; else above it when there is room above; else over its bottom edge, inside it (G15); always within the
    /// working area, never wider than it, so never under the taskbar or past a display's edge.
    /// </summary>
    public static PixelRect Place(PixelRect region, PixelRect workingArea, PixelSize bar, int gap)
    {
        var width = Math.Min(bar.Width, workingArea.Width);
        var left = Math.Max(workingArea.X, Math.Min(region.X + (region.Width - width) / 2, workingArea.Right - width));
        var below = region.Bottom + gap;
        var above = region.Y - gap - bar.Height;
        var top = below + bar.Height <= workingArea.Bottom ? below
            : above >= workingArea.Y ? above
            : Math.Max(workingArea.Y, Math.Min(region.Bottom, workingArea.Bottom) - gap - bar.Height);
        return new PixelRect(left, top, width, bar.Height);
    }

    /// <summary>A time on the clip's timeline as the bar shows it: minutes, seconds and, unless told otherwise, tenths ("0:01.6").</summary>
    public static string Time(long milliseconds, bool tenths = true)
    {
        var whole = milliseconds / 1000;
        var words = string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
        return tenths ? string.Create(CultureInfo.InvariantCulture, $"{words}.{milliseconds / 100 % 10}") : words;
    }

    /// <summary>The bytes written so far, in the one wording of a byte count, as an estimate of the clip's size.</summary>
    public static string Size(long bytes) => "~" + FileSize.Words(bytes);

    /// <summary>
    /// The widest size reading the bar holds room for: a clip's of 10,000 MB ("~10000.0 MB"), the bar's envelope, which
    /// no recording at the caps Settings offers comes near; every reading up to it is no wider, its digits being of one
    /// width. A larger clip's reading may run under the words after it.
    /// </summary>
    public static string WidestSize => Size(10_000L * 1024 * 1024);

    /// <summary>The widest elapsed reading the bar holds room for under <paramref name="cap"/>: the cap's own, every earlier time no wider.</summary>
    public static string WidestElapsed(TimeSpan cap) => Time((long)cap.TotalMilliseconds);

    /// <summary>
    /// How long after the click on Record a click on Stop is still that double-click's second click: the platform's
    /// double-click time, <paramref name="platform"/>, but never less than <see cref="DefaultDoubleClick"/>, so a
    /// platform answering none, or a very short time, still leaves no double-click able to end the take at once.
    /// </summary>
    public static TimeSpan DoubleClickTime(TimeSpan? platform) =>
        platform is { } time && time > DefaultDoubleClick ? time : DefaultDoubleClick;

    /// <summary>The bar's words for the record shortcut while the region waits: it starts the recording and stops it.</summary>
    public static string StartsAndStops(string shortcut) => $"{shortcut} starts and stops";

    /// <summary>The bar's words for the record shortcut while it records.</summary>
    public static string Stops(string shortcut) => $"{shortcut} stops";

    /// <summary>
    /// The recording's look in place of the wait's, in the same parts and places: the readings in place of "Up to", Stop
    /// in Record's place, Cancel where it stood (G15).
    /// </summary>
    public void Recording()
    {
        if (look != BarLook.Waiting)
        {
            return;
        }

        look = BarLook.Recording;
        Show(recordingReadings, waitingReadings);
        Show(stopLook, recordLook);
        AutomationProperties.SetName(primary, StopName);
        NameShortcut(shortcut);
    }

    /// <summary>
    /// From now on the record shortcut named as <paramref name="words"/>, or none when null: in the place held for it at
    /// the start, so nothing else moves; words longer than that place give way.
    /// </summary>
    public void NameShortcut(string? words)
    {
        shortcut = words;
        // The wait's words are held room for in both looks, so the bar is one size from the wait to Stop.
        shortcutWords.Widest = words is null ? null : StartsAndStops(words);
        shortcutWords.Text = words is null ? "" : look == BarLook.Waiting ? StartsAndStops(words) : Stops(words);
    }

    /// <summary>Reads the time and the size afresh.</summary>
    public void Refresh(IRecordingReadings readings)
    {
        elapsed.Text = Time(readings.ElapsedMs);
        size.Text = Size(readings.BytesWritten);
    }

    /// <summary>The words naming the saving in place of everything else, Stop and Cancel gone (G35).</summary>
    public void Saving()
    {
        look = BarLook.Saving;
        Child = new TextBlock { Text = SavingWords, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }

    /// <summary>
    /// Record or Stop, as the bar shows now. A click on Stop within a double-click's time of the click on Record is that
    /// double-click's second click and does nothing, so it never ends the take it just started; the shortcut and the
    /// tray's Stop recording do not come here and end it at any time (G15).
    /// </summary>
    private void PrimaryClicked()
    {
        if (look == BarLook.Waiting)
        {
            recordClicked ??= clock.GetTimestamp();
            record();
        }
        else if (look == BarLook.Recording && (recordClicked is not { } at || clock.GetElapsedTime(at) > doubleClick))
        {
            stop();
        }
    }

    /// <summary>
    /// The bar's colours, the take's for the theme it shows under, following either's change while it shows (F10, G32);
    /// the red Record and Stop keep their white on the recording's red under every take.
    /// </summary>
    private void Paint()
    {
        this.Bind(BackgroundProperty, this.GetResourceObservable(LookKeys.BarGround));
        this.Bind(TextElement.ForegroundProperty, this.GetResourceObservable(LookKeys.BarWords));
        foreach (var faint in new TextBlock[] { against, size, upTo, shortcutWords })
        {
            faint.Bind(TextBlock.ForegroundProperty, faint.GetResourceObservable(LookKeys.BarFaintWords));
        }

        cancel.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, cancel.GetResourceObservable(LookKeys.BarWords));
        IBrush? hovered = null;
        IBrush? words = null;
        void HoverCancel()
        {
            if (hovered is not null && words is not null)
            {
                Hover(cancel, hovered, hovered, words);
            }
        }

        // Read from the bar, not from Cancel: Cancel's own resources, which these set, are below the bar, so setting them
        // does not wake these readers again.
        this.GetResourceObservable(LookKeys.BarHoverGround).Subscribe(new AnonymousObserver<object?>(value =>
        {
            hovered = value as IBrush;
            HoverCancel();
        }));
        this.GetResourceObservable(LookKeys.BarWords).Subscribe(new AnonymousObserver<object?>(value =>
        {
            words = value as IBrush;
            HoverCancel();
        }));
    }

    /// <summary>The grounds and words <paramref name="button"/> shows hovered and pressed, in place of the theme's, which suit neither a red button nor this bar.</summary>
    private static void Hover(Button button, IBrush hovered, IBrush pressed, IBrush words)
    {
        button.Resources["ButtonBackgroundPointerOver"] = hovered;
        button.Resources["ButtonBackgroundPressed"] = pressed;
        button.Resources["ButtonForegroundPointerOver"] = words;
        button.Resources["ButtonForegroundPressed"] = words;
    }

    /// <summary><paramref name="shown"/> showing in its place and <paramref name="hidden"/> not, though it still holds its room there.</summary>
    private static void Show(Control shown, Control hidden) => (shown.Opacity, hidden.Opacity) = (1, 0);

    /// <summary>
    /// The words of a part and of the parts inside it, in order, leaving out what does not show and anything empty; a
    /// button's words are its own.
    /// </summary>
    private static IEnumerable<string> Texts(Control part) => part.Opacity == 0 ? [] : part switch
    {
        TextBlock text => string.IsNullOrEmpty(text.Text) ? [] : [text.Text],
        Panel inner => inner.Children.SelectMany(Texts),
        Decorator { Child: { } inner } => Texts(inner),
        _ => [],
    };

    /// <summary>
    /// The bar's one layout: Cancel at its left end, then the readings, then the shortcut's words in the room left, and
    /// Record or Stop at its right end, so Cancel is never one slip from Stop.
    /// </summary>
    private static Grid Laid(Control cancel, Control readings, Control words, Control primary)
    {
        readings.Margin = new Thickness(12, 0, 0, 0);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        var parts = new[] { cancel, readings, words, primary };
        for (var column = 0; column < parts.Length; column++)
        {
            Grid.SetColumn(parts[column], column);
            row.Children.Add(parts[column]);
        }

        return row;
    }

    /// <summary>Two looks of one part in one place, the part as large as the larger of them whichever shows.</summary>
    private static Grid OneOf(Control first, Control second) => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Children = { first, second },
    };

    /// <summary>One row of the bar's readings, its parts in order, a person's reading order.</summary>
    private static StackPanel Row(params Control[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.AddRange(parts);
        return row;
    }

    /// <summary>A red button's look: its mark, then its word, centred in the room both looks hold.</summary>
    private static StackPanel Labelled(Control mark, string word) => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 6,
        Children = { mark, new TextBlock { Text = word, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
    };

    private static Button Button(string name, Action clicked, IBrush background, Control content)
    {
        var button = new Button
        {
            Content = content,
            Background = background,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        };
        button.Bind(CornerRadiusProperty, button.GetResourceObservable(LookKeys.PillRadius));
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => clicked();
        return button;
    }

    private static TextBlock Glyph(string glyph) =>
        new() { Text = glyph, FontFamily = Icons, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// Its child at its whole width where the room it is given holds that, and otherwise not shown at all, so words that
    /// do not fit give way whole rather than run under what stands beside them or show cut.
    /// </summary>
    internal sealed class GiveWay : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child is not { } child)
            {
                return default;
            }

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            return new Size(Math.Min(child.DesiredSize.Width, availableSize.Width), child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child is { } child)
            {
                // Half a unit for layout rounding, which may give the room a hair less than the words measured.
                var whole = child.DesiredSize.Width <= finalSize.Width + 0.5;
                child.Opacity = whole ? 1 : 0;
                child.Arrange(new Rect(0, 0, whole ? child.DesiredSize.Width : 0, finalSize.Height));
            }

            return finalSize;
        }
    }
}
