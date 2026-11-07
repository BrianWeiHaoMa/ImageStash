using System.Runtime.InteropServices;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// G26's refresh on Windows (How G26 holds, S14): the latest completed write remembered as the clipboard's sequence
/// number read once the clipboard was closed, since Windows raises the number again at the close, with the owner then
/// and the identities written; and the same write body run again only while, with the clipboard held open, the number
/// is still that one and the owner is still the window that wrote. The owner closes the instant between the close and
/// the read, in which another program's write would otherwise be remembered as this one's. The number is a counter and
/// the owner a window, neither of them clipboard data, so nothing here reads the clipboard (N9, R5); another program's
/// read of what was written, a format Windows makes from it included, changes neither (probed on Windows 11 build 26300).
/// </summary>
public sealed partial class WindowsClipboardAndDrops
{
    private volatile RememberedWrite? remembered;

    public IReadOnlyList<long> LastWritten => remembered?.Items ?? [];

    public Task<Outcome<bool>> WriteAgain(long savedItem, ClipboardContent content, TimeSpan heldClipboardRetry)
    {
        var last = remembered;
        if (last is null || !last.Items.Contains(savedItem))
        {
            return Task.FromResult(Outcome<bool>.Of(false));
        }

        // Judged under the write's own open, so no other program's write can come between the check and the write.
        return Task.Run(() => WriteNow(last.Window, content, heldClipboardRetry,
            onlyWhile: () => ReferenceEquals(remembered, last) && GetClipboardSequenceNumber() == last.Number && GetClipboardOwner() == last.Window));
    }

    /// <summary>
    /// After a write's close, with no other open of ImageStash's possible yet: what it wrote, while it is still the
    /// clipboard's. A write with no owner window is not remembered, since another program's could not be told from it.
    /// </summary>
    private void Remember(nint window, IReadOnlyList<long> items)
    {
        var number = GetClipboardSequenceNumber();
        remembered = window != 0 && GetClipboardOwner() == window ? new RememberedWrite(window, number, [.. items]) : null;
    }

    public void ForgetWritten() => Forget();

    /// <summary>As a write empties the clipboard, the write before it is no longer what it holds.</summary>
    private void Forget() => remembered = null;

    /// <summary>A completed write: its owner window, the sequence number after its close, and the identities it put there.</summary>
    private sealed record RememberedWrite(nint Window, uint Number, IReadOnlyList<long> Items);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardOwner();
}
