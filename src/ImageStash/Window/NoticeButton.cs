namespace ImageStash.Window;

/// <summary>
/// A notice's button beside its Undo, as an import's Show (G12): its label and what a press does. Unlike the Undo it
/// acts for as long as the notice shows, past its time too when the notice stays (G16), and it leaves the notice
/// showing; like the Undo its button takes no focus (G7).
/// </summary>
public sealed record NoticeButton(string Label, Func<Task> Act)
{
    /// <summary>A press on <paramref name="notice"/>'s button: its act while the notice shows, nothing once it has gone.</summary>
    public static Task Press(Notices notices, Notice notice) =>
        notice.Button is { } button && notices.Shown.Contains(notice) ? button.Act() : Task.CompletedTask;
}
