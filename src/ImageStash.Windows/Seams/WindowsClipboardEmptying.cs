using System.Runtime.InteropServices;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// A capture card's Undo on Windows (G16, How G26 holds): the clipboard emptied under one open made with the remembered
/// write's owner, so the emptying is that window's and no other program's write can come between the judgement and the
/// emptying, judged as the refresh judges (<c>WindowsClipboardRefresh.cs</c>): the remembered write still the latest one,
/// the sequence number still the one read after its close, and the owner still its window. <c>EmptyClipboard</c> needs
/// the clipboard open and gives ownership to the window that opened it (EmptyClipboard, Microsoft Learn, updated
/// 2025-07-01); the emptying raises the sequence number, which needs no open (S14). Nothing here reads clipboard data
/// (N9, R5).
/// </summary>
public sealed partial class WindowsClipboardAndDrops
{
    private const string EmptyAction = "Clear the clipboard";

    public Task<Outcome<bool>> Empty(long removedItem, TimeSpan heldClipboardRetry)
    {
        var last = remembered;
        if (last is null || !last.Items.Contains(removedItem))
        {
            return Task.FromResult(Outcome<bool>.Of(false));
        }

        return Task.Run(() => EmptyNow(last, heldClipboardRetry));
    }

    private Outcome<bool> EmptyNow(RememberedWrite last, TimeSpan heldClipboardRetry)
    {
        var opened = Open(last.Window, heldClipboardRetry, EmptyAction);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        try
        {
            if (!StillHolds(last))
            {
                return Outcome<bool>.Of(false);
            }

            // Forgotten first, so a refresh after a refused emptying writes nothing back either (G26).
            Forget();
            return EmptyClipboard() ? Outcome<bool>.Of(true) : new Failure(EmptyAction, Subject, WindowsRefusal.ReasonFor(Marshal.GetLastPInvokeError()));
        }
        finally
        {
            Close();
        }
    }
}
