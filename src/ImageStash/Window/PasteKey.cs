using ImageStash.Editor;

namespace ImageStash.Window;

/// <summary>
/// <c>Ctrl+V</c>'s act (G7, G13). In a layer's open text or the color picker's hex box, words go into the opening the key
/// was pressed in, at its current caret. Other clipboard contents get a notice. Everywhere else uses the ordinary window paste.
/// </summary>
public sealed class PasteKey(ClipboardPaste paste, Editing editing)
{
    /// <param name="caretIn">The layer's open text the caret is in, or null: the caret is anywhere else.</param>
    /// <param name="intoField">The search box's or a tag field's taker of the words, else null (<see cref="ClipboardPaste.Paste"/>).</param>
    /// <param name="intoHex">The focused picker opening's text input, else null.</param>
    public Task Press(IOpenLayerText? caretIn, Action<string>? intoField, Action<string>? intoHex = null)
    {
        if (intoHex is not null)
        {
            return paste.PasteText(intoHex);
        }

        if (caretIn is null)
        {
            return paste.Paste(intoField);
        }

        // Taken at the key, before the read waits.
        var session = editing.Session;
        var opening = session?.TypingOpening;
        return paste.PasteText(words =>
        {
            if (opening is not null && ReferenceEquals(editing.Session, session) && session!.TypingOpening == opening
                && LayerTextPaste.Insert(caretIn.Text, caretIn.SelectionStart, caretIn.SelectionEnd, words, session.TypesUppercase) is var (text, caret))
            {
                caretIn.Put(text, caret);
            }
        });
    }
}
