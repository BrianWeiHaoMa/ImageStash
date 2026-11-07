using Avalonia.Controls;
using ImageStash.Seams;

namespace ImageStash.Editor;

/// <summary>A tool's option row: the choices for its tool's next layer and selected layer, and what the mouse does (G20).</summary>
internal interface IOptionRow
{
    /// <summary>The tool whose options it shows.</summary>
    EditorTool Tool { get; }

    /// <summary>Shows the tool's values as they are now.</summary>
    void Refresh();
}

/// <summary>Each tool's option row, one entry per tool: a new tool's row is one file beside these and one line here.</summary>
internal static class OptionRows
{
    /// <param name="menu">Makes one of the editor's menus, which hands the editor its keys.</param>
    /// <param name="fonts">The installed fonts, for the font menu's All fonts… (F8, F13).</param>
    public static Control For(EditorTool tool, EditorSession session, EditorCanvas canvas, EditorPane pane, Func<PlacementMode, ContextMenu> menu, IFonts fonts) => tool switch
    {
        TextTool text => new TextOptionRow(text, session.Colors, canvas, pane, menu, fonts),
        CropTool crop => new CropOptionRow(crop),
        TrimTool trim => new TrimOptionRow(trim, session),
        HideTool hide => new HideOptionRow(hide),
        DrawTool draw => new DrawOptionRow(draw, session.Colors, canvas, pane),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool.Name, "a tool with no option row"),
    };
}
