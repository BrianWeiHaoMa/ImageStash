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
/// The recording's bar (G15). While the region waits: Record, red with its dot, the cap it may run to
/// ("Up to 0:30") and Cancel. Once it records: a red dot, the elapsed time against the cap ("0:01.6 / 0:30") and the size
/// written so far ("~614 KB"), and at its right end Stop and Cancel, which stand still as the readings change; once the
/// sampling has ended, only the words naming the saving (G35).
/// Where it goes beside the region is <see cref="Place"/>. Its buttons open no tooltip, which would be a window of its
/// own in the recording, and take no focus. Light under the light theme and dark under the dark (F10), repainted when the
/// theme changes; hovered or pressed, Record and Stop stay red with white words and Cancel's glyph reads on its ground.
/// </summary>
public sealed class RecordingBar : Border
{
    public const string Title = "ImageStash recording bar";

    public const string SavingWords = "Saving the clip…";

    /// <summary>The automation name of the bar's Record, Stop and Cancel buttons.</summary>
    public const string RecordName = "Record";
    public const string StopName = "Stop";
    public const string CancelName = "Cancel recording";

    /// <summary>The space between the region and the bar, in device-independent pixels.</summary>
    public const double Gap = 14;

    private static readonly IBrush RedHovered = new SolidColorBrush(Color.FromRgb(0xC9, 0x33, 0x38));
    private static readonly IBrush RedPressed = new SolidColorBrush(Color.FromRgb(0xAE, 0x28, 0x2D));
    private static readonly FontFamily Icons = new("Segoe Fluent Icons");

    private readonly TextBlock elapsed;
    private readonly TextBlock size;
    private readonly TextBlock against;
    private readonly TextBlock upTo;
    private readonly Button stop;
    private readonly Button cancel;

    /// <summary>
    /// The bar for a region waiting to be recorded for up to <paramref name="cap"/>, offering Record, which runs
    /// <paramref name="record"/>, and Cancel, which runs <paramref name="cancel"/>; its Stop, from <see cref="Recording"/>
    /// on, runs <paramref name="stop"/>. Its readings stay at their widest until the first <see cref="Refresh"/>, so a bar
    /// measured as it starts recording holds every later reading below 10,000 MB, the fourth digit of the size taking the
    /// gap before Stop; a larger one would run under Stop.
    /// </summary>
    public RecordingBar(TimeSpan cap, Action record, Action stop, Action cancel)
    {
        elapsed = new TextBlock { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Text = Time((long)cap.TotalMilliseconds) };
        size = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = Size(999L * 1024 * 1024) };
        against = new TextBlock { Text = "/ " + Time((long)cap.TotalMilliseconds, tenths: false), VerticalAlignment = VerticalAlignment.Center };
        upTo = new TextBlock { Text = "Up to " + Time((long)cap.TotalMilliseconds, tenths: false), VerticalAlignment = VerticalAlignment.Center };
        RecordButton = Button(RecordName, record, new SolidColorBrush(RecordingFrame.Red), Labelled(
            new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, VerticalAlignment = VerticalAlignment.Center }, "Record"));
        this.stop = Button(StopName, stop, new SolidColorBrush(RecordingFrame.Red), Labelled(Glyph(""), "Stop"));
        CancelButton = this.cancel = Button(CancelName, cancel, Brushes.Transparent, Glyph(""));
        foreach (var red in new[] { RecordButton, this.stop })
        {
            Hover(red, RedHovered, RedPressed, Brushes.White);
        }

        TextElement.SetFontSize(this, 12.5);
        TextElement.SetFontFamily(this, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        Height = 44;
        Padding = new Thickness(16, 0, 10, 0);
        Child = Row(RecordButton, upTo, CancelButton);
        Paint();
    }

    /// <summary>Record, while the region waits; then null.</summary>
    public Button? RecordButton { get; private set; }

    /// <summary>Stop, from <see cref="Recording"/> until the bar names the saving; null otherwise.</summary>
    public Button? StopButton { get; private set; }

    /// <summary>Cancel, until the bar names the saving; then null.</summary>
    public Button? CancelButton { get; private set; }

    /// <summary>The words the bar shows now, in order.</summary>
    public IReadOnlyList<string> Words => Child is Panel row ? Texts(row).ToList() : [((TextBlock)Child!).Text ?? ""];

    /// <summary>
    /// Where the bar goes for <paramref name="region"/> in physical pixels: centred beneath it when the working area has
    /// room below; else above it when there is room above; else over its bottom edge, inside it (G15); always within the
    /// working area across, so never under the taskbar.
    /// </summary>
    public static PixelRect Place(PixelRect region, PixelRect workingArea, PixelSize bar, int gap)
    {
        var left = Math.Max(workingArea.X, Math.Min(region.X + (region.Width - bar.Width) / 2, workingArea.Right - bar.Width));
        var below = region.Bottom + gap;
        var above = region.Y - gap - bar.Height;
        var top = below + bar.Height <= workingArea.Bottom ? below
            : above >= workingArea.Y ? above
            : Math.Max(workingArea.Y, Math.Min(region.Bottom, workingArea.Bottom) - gap - bar.Height);
        return new PixelRect(left, top, bar.Width, bar.Height);
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
    /// The recording's look in place of the wait's: the dot, the time against the cap and the size so far, then Stop and
    /// Cancel at the bar's right end, Record gone (G15).
    /// </summary>
    public void Recording()
    {
        (RecordButton, StopButton) = (null, stop);
        // Cancel moves into the new row, so it leaves the wait's row first: a control has one parent.
        (Child as Panel)?.Children.Clear();
        // The buttons keep to the right end of a bar sized for the widest readings, so a narrower reading moves neither.
        var buttons = Row(stop, cancel);
        buttons.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(buttons, 1);
        Child = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Row(
                    new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(RecordingFrame.Red), VerticalAlignment = VerticalAlignment.Center },
                    elapsed,
                    against,
                    size),
                buttons,
            },
        };
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
        (RecordButton, StopButton, CancelButton) = (null, null, null);
        Child = new TextBlock { Text = SavingWords, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }

    /// <summary>
    /// The bar's colours, the take's for the theme it shows under, following either's change while it shows (F10, G32);
    /// the red Record and Stop keep their white on the recording's red under every take.
    /// </summary>
    private void Paint()
    {
        this.Bind(BackgroundProperty, this.GetResourceObservable(LookKeys.BarGround));
        this.Bind(TextElement.ForegroundProperty, this.GetResourceObservable(LookKeys.BarWords));
        foreach (var faint in new[] { against, size, upTo })
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

    /// <summary>The words of a row's text and of the rows inside it, in order; a button's words are its own.</summary>
    private static IEnumerable<string> Texts(Panel row) => row.Children.SelectMany(IEnumerable<string> (part) => part switch
    {
        TextBlock text => [text.Text ?? ""],
        Panel inner => Texts(inner),
        _ => [],
    });

    /// <summary>One row of the bar, its parts in order, a person's reading order.</summary>
    private static StackPanel Row(params Control[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.AddRange(parts);
        return row;
    }

    /// <summary>A red button's content: its mark, then its word.</summary>
    private static StackPanel Labelled(Control mark, string word) => new()
    {
        Orientation = Orientation.Horizontal,
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
}
