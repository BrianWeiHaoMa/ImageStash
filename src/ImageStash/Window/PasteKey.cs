using ImageStash.Editor;

namespace ImageStash.Window;

/// <summary>
/// <c>Ctrl+V</c>'s act (G7, G13). With the caret in a text or caption-bar layer's open text in the editor it never
/// imports: the clipboard's words go in at the caret as typed (<see cref="LayerTextPaste"/>), and anything else gets the
/// notice. Being in the editor, or a layer's text being open, is not enough: the picker's hex box, while a layer's text
/// stays open, and the editor with no field focused paste as everywhere else, through <see cref="ClipboardPaste.Paste"/>.
/// The answer lands only in the opening of the text the key was pressed in, at its caret as it is when the read
/// returns; a text closed since, closed and opened again, another layer's text opened or the editor gone takes nothing.
/// </summary>
public sealed class PasteKey(ClipboardPaste paste, Editing editing)
{
    /// <param name="caretIn">The layer's open text the caret is in, or null: the caret is anywhere else.</param>
    /// <param name="intoField">The search box's or a tag field's taker of the words, else null (<see cref="ClipboardPaste.Paste"/>).</param>
    public Task Press(IOpenLayerText? caretIn, Action<string>? intoField)
    {
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
