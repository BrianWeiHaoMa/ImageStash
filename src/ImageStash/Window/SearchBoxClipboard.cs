using Avalonia.Controls;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The search box's <c>Ctrl+C</c> and <c>Ctrl+X</c> of its selected text (G7), written through the seam, the one place
/// that reaches the clipboard. The text box's own write only logs a clipboard another program holds, so the words would
/// silently not arrive; here that is retried, then named, and text a cut could not write stays in the box (E3, G35).
/// </summary>
public sealed class SearchBoxClipboard(TextBox box, IClipboardAndDrops clipboard, Notices notices, TimeSpan heldClipboardRetry)
{
    public const string CopyAction = "Copy the search text";
    public const string CutAction = "Cut the search text";

    public void Wire()
    {
        box.CopyingToClipboard += (_, e) =>
        {
            e.Handled = true;
            _ = Write(CopyAction);
        };
        box.CuttingToClipboard += (_, e) =>
        {
            e.Handled = true;
            _ = Write(CutAction);
        };
    }

    private async Task Write(string action)
    {
        var text = box.SelectedText;
        if (TopLevel.GetTopLevel(box) is not { } owner || string.IsNullOrEmpty(text))
        {
            return;
        }

        var written = await clipboard.WriteText(owner, action, text, heldClipboardRetry);
        if (!written.Succeeded)
        {
            notices.Raise(written.Failure);
        }
        else if (action == CutAction && box.SelectedText == text)
        {
            box.SelectedText = "";
        }
    }
}
