namespace ImageStash.Editor;

/// <summary>
/// The editor's tools in tool-bar order (G20): one plain list, a new tool one file beside these and one entry here. Trim
/// is listed for an animated item only, so a tool's place in the list is never relied on.
/// </summary>
internal static class EditorTools
{
    public static IReadOnlyList<EditorTool> For(EditorSession session) =>
        [new TextTool(session), new CropTool(session), .. session.Animated ? [new TrimTool(session)] : Array.Empty<EditorTool>(), new HideTool(session), new DrawTool(session)];
}
