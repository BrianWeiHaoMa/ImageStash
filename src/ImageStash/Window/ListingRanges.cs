namespace ImageStash.Window;

public sealed partial class Listing
{
    // Focused is the moving end of a keyboard range. The other end follows the item across refreshes by identity.
    private long? keyboardRangeAnchor;

    internal bool HasKeyboardRange => keyboardRangeAnchor is not null;

    private long RangeAnchorOr(long target) => keyboardRangeAnchor ?? Focused ?? target;

    private void ApplyRange(long anchor, long end, long focus, long? input = null)
    {
        var from = indexOf[anchor];
        var to = indexOf[end];
        var range = Items.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).Select(item => item.Id).ToHashSet();
        ApplySelection(range, focus, byImport: false, byPerson: true, input);
    }

    private void KeepRangeAfterRefresh(bool keepSelection, long? previousFocus)
    {
        if (keyboardRangeAnchor is { } anchor && (!keepSelection || Focused != previousFocus || !selection.Contains(anchor)))
        {
            keyboardRangeAnchor = null;
        }
    }
}
