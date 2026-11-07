using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Thickness = Avalonia.Thickness;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The line under the editor's header that says which fonts of the picture this PC lacks, so they show in a fallback
/// (G21, E3): the families the latest draw of the canvas reported, gone once a draw reports none. At most
/// two lines tall, however many families a list from another PC carries, so the editor's footer stays inside the window
/// at its minimum size (G20); what does not fit ends in an ellipsis, the whole sentence in its tooltip.
/// </summary>
internal sealed class FontFallbackNote : Border
{
    private readonly TextBlock words;

    public FontFallbackNote()
    {
        words = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var info = EditorButtons.Glyph("", 14);
        DockPanel.SetDock(info, Dock.Left);
        Child = new DockPanel { Children = { info, words } };
        Padding = new Thickness(12, 8);
        Margin = new Thickness(10, 0, 10, 8);
        this.Bind(CornerRadiusProperty, this.GetResourceObservable(LookKeys.ControlRadius));
        this.Bind(BackgroundProperty, this.GetResourceObservable(LookKeys.NoteGround));
        IsVisible = false;
    }

    /// <summary>Names <paramref name="families"/>, or hides with none.</summary>
    public void Show(IReadOnlyList<string> families)
    {
        IsVisible = families.Count > 0;
        // Several families come after what is said of them, so a list cut short at two lines still says it.
        words.Text = families.Count switch
        {
            0 => "",
            1 => $"{families[0]} is not installed on this PC, so its text shows in a fallback font.",
            _ => $"These fonts are not installed on this PC, so their text shows in a fallback font: {string.Join(", ", families.Take(families.Count - 1))} and {families[^1]}.",
        };
        ToolTipRule.ShowWhole(words, families.Count > 0 ? words.Text : null);
    }
}
