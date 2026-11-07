namespace ImageStash.Seams;

/// <summary>A drag of a window's border by the user.</summary>
public interface IBorderDrag
{
    /// <summary>
    /// A drag of <paramref name="window"/>'s border stops at the window's minimum as it stands at each step, the opposite
    /// edge staying where it is, whichever edge or corner is dragged (G20, G21). Called once, on the UI thread.
    /// </summary>
    void StopAtMinimum(Avalonia.Controls.Window window);
}
