namespace ImageStash.Media;

/// <summary>
/// How long a frame shows wherever ImageStash plays a picture: the grid, the preview and the editor (G6, G19, G22). A
/// frame's own duration still times what is saved and what the editor's clock reads; only playing on screen uses this.
/// </summary>
public static class ShownDuration
{
    /// <summary>
    /// Browsers show a frame that asks for 10 ms or less for 100 ms, and the chats a GIF is handed to are browsers, so
    /// ImageStash shows it as they will.
    /// </summary>
    public static TimeSpan Of(int durationMs) => TimeSpan.FromMilliseconds(durationMs <= 10 ? 100 : durationMs);
}
