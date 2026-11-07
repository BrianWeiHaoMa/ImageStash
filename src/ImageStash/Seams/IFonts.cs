namespace ImageStash.Seams;

/// <summary>The fonts installed on this PC, for the Text tool's font menu (F8, F13).</summary>
public interface IFonts
{
    /// <summary>Every installed family name, each once, sorted ignoring case, read afresh at each call.</summary>
    IReadOnlyList<string> Families();

    /// <summary>Whether a text layer with this family draws in that family on this PC, rather than in a fallback (G21, E3).</summary>
    bool IsInstalled(string family);
}
