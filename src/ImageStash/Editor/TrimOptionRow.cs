using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// Trim's option row (G20, G22, G25): play and pause, where the play is over the clip's length, what the
/// handles keep and for how long, and Reset, shown while there is a trim to reset (<see cref="ControlPresence"/>). The position and what is kept hold room for their widest readings.
/// The load and failure state stands in for the readings until every frame has been timed (G35).
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
    private readonly SteadyReadout position;
    private readonly TextBlock busy;
    private readonly SteadyReadout kept;
    private readonly Button reset;

    public TrimOptionRow(TrimTool tool, EditorSession session)
    {
        this.tool = tool;
        this.session = session;
        EditorOptions.SetSpacing(this);
        play = EditorButtons.Icon("", Pause, "Pause (Space)");
        play.Height = EditorButtons.ChoiceHeight;
        play.Click += (_, _) => session.TogglePlaying();
        position = new SteadyReadout { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        busy = new TextBlock { Text = Reading, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        // Keep Reset beside the range when the whole group wraps. Even a long imported clip can use two text lines here.
        kept = new SteadyReadout
        {
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
            FontSize = 13,
            LineHeight = 15,
            MaxWidth = 320,
            TextWrapping = TextWrapping.Wrap,
        };
        reset = EditorButtons.Labelled(null, Reset);
        reset.Height = EditorButtons.ChoiceHeight;
        reset.Click += (_, _) => tool.Reset();
        Children.AddRange([EditorOptions.Group([play, position, busy], spacing: 4), EditorOptions.Group([kept, reset], EditorOptions.GroupSpacing)]);
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
        position.Widest = tool.WidestPosition;
        position.IsVisible = at is not null;
        kept.Text = keeps ?? "";
        kept.Widest = tool.WidestKeptWords;
        kept.IsVisible = keeps is not null;
        busy.Text = session.FramesFailure is null ? Reading : Unread;
        busy.IsVisible = keeps is null && (session.FramesBusy is not null || session.FramesFailure is not null);
        ControlPresence.Show(reset, tool.CanReset, true);
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
