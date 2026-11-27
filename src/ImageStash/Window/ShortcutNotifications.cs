namespace ImageStash.Window;

/// <summary>
/// The notification above the tray that a shortcut became unavailable (G33, G16), at start or later: the tooltip's
/// sentence for it (<see cref="ShortcutWords.Notification"/>), with Choose another, which asks for Settings on that
/// shortcut's own row, the ask the tray's and the window's Settings make, so an open page is brought forward on it
/// (G32). It goes through the tray's notifications like every other: shown without taking focus, as long as its words
/// need and longer while hovered, with its ×, and, naming a problem, staying until its × or the shortcut's next one. A
/// shortcut is said once while it stays unavailable for the same combination and the same kind of reason, however often
/// a registration judges it again (Pause's toggle back, a field's end, settings applied again, under Pause too); it is
/// said again when the combination or the kind differs, or once it has worked in between, which a change of the
/// shortcuts finding it acting, or bound to another combination, tells. The character a layout types is detail, never a
/// new reason. Two shortcuts unavailable at once are two notifications, each leading to its own row.
/// </summary>
public sealed class ShortcutNotifications
{
    public const string ChooseAnother = "Choose another";

    private readonly Notices notifications;
    private readonly Action<ShortcutAct> openSettingsOn;
    // What each act's notification said, by combination and kind, while the act has neither worked nor been rebound since.
    private readonly Dictionary<ShortcutAct, (string Combination, ShortcutReasonKind Kind)> told = [];

    /// <param name="notifications">The notifications above the tray.</param>
    /// <param name="openSettingsOn">Opens Settings on the act's row, or brings forward the page open, on that row (G32, G33).</param>
    public ShortcutNotifications(Notices notifications, Action<ShortcutAct> openSettingsOn)
    {
        this.notifications = notifications;
        this.openSettingsOn = openSettingsOn;
    }

    /// <summary>From now on, an act whose shortcut a change of the shortcuts finds acting, or bound to another combination, is forgotten.</summary>
    public void Follow(GlobalShortcuts shortcuts) => shortcuts.Changed += () => ForgetWorkingOrRebound(shortcuts);

    /// <summary>The policy telling that <paramref name="problem"/>'s act has no shortcut now, on the UI thread: said unless it is already.</summary>
    public void Tell(ShortcutProblem problem)
    {
        var act = problem.Act;
        if (told.TryGetValue(act, out var said) && said == (problem.Combination, problem.Reason.Kind))
        {
            return;
        }

        told[act] = (problem.Combination, problem.Reason.Kind);
        // This one says what holds now, in place of an earlier notification of the same shortcut, which stays until then
        // (G16); another shortcut's is another action and stays.
        using var acting = UserAct.BeginNew(ActionOf(act));
        notifications.Raise(ShortcutWords.Notification(problem), namesAProblem: true,
            button: new NoticeButton(ChooseAnother, () =>
            {
                openSettingsOn(act);
                return Task.CompletedTask;
            }));
    }

    /// <summary>The action of <paramref name="act"/>'s shortcut found unavailable, one for each of the four (G16).</summary>
    internal static string ActionOf(ShortcutAct act) => $"The {act} shortcut unavailable";

    /// <summary>
    /// Forgets each act whose shortcut works now, or is bound to another combination than the one said; one only held off
    /// by Pause, or bound again to the same combination and not yet judged, is not forgotten, since it has not worked.
    /// </summary>
    private void ForgetWorkingOrRebound(GlobalShortcuts shortcuts)
    {
        foreach (var (act, said) in told.ToList())
        {
            if (ActingShortcut.Of(shortcuts, act) is not null || shortcuts.CombinationOf(act) != said.Combination)
            {
                told.Remove(act);
            }
        }
    }
}
