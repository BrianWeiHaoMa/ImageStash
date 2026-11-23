using System.Text;
using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>
/// The open field of a text or caption-bar layer as its box holds it: its text, its selection's two ends (equal for a
/// bare caret, either order), and its text replaced, the caret put at <c>caret</c> with nothing selected.
/// </summary>
public interface IOpenLayerText
{
    string Text { get; }

    int SelectionStart { get; }

    int SelectionEnd { get; }

    void Put(string text, int caret);
}

/// <summary>
/// What a layer's open text makes of the clipboard's words (G13, G25): they go in at the caret, in place of the
/// selection, as typed text: uppercased on a Meme as a typed character is (G21: "Meme is uppercase"), each line of the
/// clipboard a line of the layer as Enter stores it (<c>\n</c>, Windows' <c>\r\n</c> and a lone <c>\r</c> alike), a tab
/// a space and any other control character left out, since none of them can be typed there and Media would draw each
/// as a box; the caret goes after the last letter the words became. Words of which nothing could be typed change
/// nothing, as such a key changes nothing: the selection stays.
/// </summary>
public static class LayerTextPaste
{
    /// <summary>The text and the caret once <paramref name="words"/> land, or null when nothing of them could be typed.</summary>
    public static (string Text, int Caret)? Insert(string text, int selectionStart, int selectionEnd, string words, bool meme)
    {
        var typed = AsTyped(words, meme);
        if (typed.Length == 0)
        {
            return null;
        }

        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        return (text[..start] + typed + text[end..], start + typed.Length);
    }

    private static string AsTyped(string words, bool meme)
    {
        var typed = new StringBuilder(words.Length);
        for (var at = 0; at < words.Length; at++)
        {
            switch (words[at])
            {
                case '\r' when at + 1 < words.Length && words[at + 1] == '\n':
                    break;
                case '\r' or '\n':
                    typed.Append('\n');
                    break;
                case '\t':
                    typed.Append(' ');
                    break;
                case var other when char.IsControl(other):
                    break;
                case var letter:
                    typed.Append(letter);
                    break;
            }
        }

        return meme ? Uppercase.Of(typed.ToString()) : typed.ToString();
    }
}
