using Avalonia;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// From the open shortcut's press to the window's summon (F5, G1, G8): runs on the Shortcuts seam's thread and reads
/// there, first, the window in front at the press, which is the paste target unless it is ImageStash itself, then where
/// the pointer is, so a UI thread busy loading thumbnails neither delays the readings nor lets another window or the
/// pointer move on; then asks the Target seam for the paste target's caret (the window in front, or while ImageStash
/// is, the target already held), which it waits for off every thread of the window's, at most
/// <see cref="CaretBound"/>. The reading goes to the UI thread at once, so an activation after the press sets the
/// target after it (G8); the summon follows with both anchors once the caret has answered.
/// </summary>
/// <param name="heldTarget">The paste target the window holds now; read when ImageStash itself is in front at the press.</param>
/// <param name="pressedOnUiThread">Posts <see cref="PasteTargetHolder.FromPress"/> with the reading to the UI thread.</param>
/// <param name="summonOnUiThread">Posts <see cref="StashWindow.SummonFromShortcut(SummonAnchor)"/> with the anchor to the UI thread.</param>
public sealed class ShortcutSummon(ITarget target, IQuietWindows quietWindows, Func<PasteTarget?> heldTarget, Action<ForegroundReading> pressedOnUiThread, Action<SummonAnchor> summonOnUiThread)
{
    /// <summary>How long the caret lookup may take before the pointer stands in for the caret (How the guarantees hold, G1).</summary>
    public static readonly TimeSpan CaretBound = TimeSpan.FromMilliseconds(250);

    // The previous press's summon, so presses reach the window in the order they were made even when a later press's
    // caret is found sooner. Touched only on the Shortcuts seam's thread.
    private Task previous = Task.CompletedTask;

    public void Pressed()
    {
        var inFront = target.Foreground();
        var pointer = quietWindows.PointerPosition();
        // G1's last resort, centred on the active display: the display of the window in front at the press.
        var front = pointer is null ? quietWindows.FrontWindowRectangle() : null;
        pressedOnUiThread(inFront);
        // How the guarantees hold, G1: the target read at the press, "or the one it already holds when ImageStash is foreground".
        var cameFrom = inFront.IsMine ? heldTarget() : inFront.Target;
        var caret = cameFrom is { } app ? target.CaretOf(app, CaretBound) : Task.FromResult<PixelRect?>(null);
        // Inline when nothing is pending, as a press with no target always is; otherwise once the lookup answers.
        previous = Task.WhenAll(previous, caret).ContinueWith(
            _ => summonOnUiThread(new SummonAnchor(pointer, caret.IsCompletedSuccessfully ? caret.Result : null, front)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
