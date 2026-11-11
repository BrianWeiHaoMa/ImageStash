using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The grid's thumbnail size in effect (G6, G32): the one place that changes it, by a step of the window's keys and
/// wheel, held at the smallest and the largest, or by the Settings page's choice, through the one settings keeper, which
/// remembers it (State: Settings); and that tells whoever follows it, the grid and a Settings page while it is open, once
/// it has changed, so a size chosen on the page is the grid's at once and a size stepped in the grid is the page's. UI
/// thread only.
/// </summary>
public sealed class ThumbnailSizing(SettingsKeeper settings)
{
    /// <summary>Raised after the size in effect changed, on the thread that changed it, the UI thread.</summary>
    public event Action? Changed;

    public ThumbnailSize Current => settings.Current.ThumbnailSize;

    /// <summary>The size <paramref name="steps"/> steps larger, or smaller when negative, held at the ends (G6).</summary>
    public void Step(int steps) => Choose(JustifiedRows.Step(Current, steps));

    /// <summary><paramref name="size"/> in effect and remembered; nothing changes, and nobody is told, when it is in effect already.</summary>
    public void Choose(ThumbnailSize size)
    {
        if (!Enum.IsDefined(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The grid has no such thumbnail size.");
        }

        if (size == Current)
        {
            return;
        }

        settings.Change(s => s with { ThumbnailSize = size });
        Changed?.Invoke();
    }
}
