using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Tests.Support;

/// <summary>
/// The open field of a session's selected text or caption-bar layer as the editor's field holds it, in place of the
/// control: its text taken from the layer as the field opens (a Meme's in uppercase), the caret at its end; a selection
/// a check makes; and what it is given put back as the field puts typing, its text to the session at once.
/// </summary>
public sealed class TestLayerText : IOpenLayerText
{
    private readonly EditorSession session;

    private TestLayerText(EditorSession session, string text)
    {
        this.session = session;
        Text = text;
        SelectionStart = SelectionEnd = text.Length;
    }

    public string Text { get; private set; }

    public int SelectionStart { get; set; }

    public int SelectionEnd { get; set; }

    /// <summary>How many times text was put.</summary>
    public int Puts { get; private set; }

    /// <summary>The field as it opens on <paramref name="session"/>'s selected layer, which must be in edit.</summary>
    public static TestLayerText Over(EditorSession session)
    {
        Assert.IsTrue(session.Typing, "a layer's text is in edit");
        return new TestLayerText(session, session.SelectedLayer switch
        {
            TextLayer { Preset: TextPreset.Meme } meme => Uppercase.Of(meme.Text),
            TextLayer text => text.Text,
            CaptionBarLayer bar => bar.Text,
            _ => throw new AssertFailedException("the selected layer is no text"),
        });
    }

    /// <summary>The caret at <paramref name="at"/>, nothing selected.</summary>
    public void CaretAt(int at) => SelectionStart = SelectionEnd = at;

    public void Put(string text, int caret)
    {
        Puts++;
        Text = text;
        SelectionStart = SelectionEnd = caret;
        session.SetTypedText(text);
    }
}
