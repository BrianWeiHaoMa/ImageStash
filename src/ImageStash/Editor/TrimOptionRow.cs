using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ImageStash.Look;
using Thickness = Avalonia.Thickness;

namespace ImageStash.Editor;

/// <summary>
/// Trim's option row (G20, G22, G25): play and pause, where the play is over the clip's length, what the
/// handles keep and for how long, Reset, and what the mouse does; it wraps onto a second line when the window is narrower
/// than it. While the frames are still being read the times are not yet known, and the row says so in their place, in
/// words short enough to keep the row's two lines at the window's minimum width; the filmstrip names the item (G35). Once
/// a frame could not be read the clip's length is not known either, and the row says that instead. It follows the play
/// itself, as the session tells it apart from the rest of the editor.
/// </summary>
internal sealed class TrimOptionRow : WrapPanel, IOptionRow
{
    public const string Play = "Play";
    public const string Pause = "Pause";
    public const string Reset = "Reset";
    public const string Reading = "Reading the frames…";
    public const string Unread = "Not every frame could be read";

    private readonly TrimTool tool;
    private readonly EditorSession session;
    private readonly Button play;
    private readonly TextBlock position;
    private readonly TextBlock busy;
    private readonly TextBlock kept;

    public TrimOptionRow(TrimTool tool, EditorSession session)
    {
        this.tool = tool;
        this.session = session;
        Orientation = Orientation.Horizontal;
        ItemSpacing = 12;
        LineSpacing = 6;
        play = EditorButtons.Icon("", Pause, "Pause (Space)");
        play.Height = 32;
        play.Click += (_, _) => session.TogglePlaying();
        position = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        busy = new TextBlock { Text = Reading, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        kept = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        var reset = EditorButtons.Labelled(null, Reset);
        reset.Click += (_, _) => tool.Reset();
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { play, position, busy } });
        Children.Add(kept);
        Children.Add(reset);
        Children.Add(new TextBlock { Text = tool.Hint, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh()
    {
        var playing = session.Playing;
        ((TextBlock)play.Content!).Text = playing ? "" : "";
        AutomationProperties.SetName(play, playing ? Pause : Play);
        ToolTipRule.Set(play, playing ? "Pause (Space)" : "Play (Space)");
        // G35: no time is stated as a measurement before every frame's duration is known.
        var (at, keeps) = (tool.Position, tool.KeptWords);
        position.Text = at ?? "";
        position.IsVisible = at is not null;
        kept.Text = keeps ?? "";
        kept.IsVisible = keeps is not null;
        busy.Text = session.FramesFailure is null ? Reading : Unread;
        busy.IsVisible = keeps is null && (session.FramesBusy is not null || session.FramesFailure is not null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        session.FrameChanged += OnFrameChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        session.FrameChanged -= OnFrameChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFrameChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }
}
