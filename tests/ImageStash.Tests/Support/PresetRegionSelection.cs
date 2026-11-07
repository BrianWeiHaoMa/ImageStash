using ImageStash.Capture;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// The overlay's stand-in for the portable checks: answers a preset region, or null for a cancel, once its gate opens,
/// running what the check gives it at the release first; it records the displays and picture sizes it was handed, never
/// the pictures, so a check can tell whether the capture let them go.
/// </summary>
public sealed class PresetRegionSelection : IRegionSelection
{
    /// <summary>The region the user selects; null cancels.</summary>
    public SelectedRegion? Answer { get; set; }

    /// <summary>Held until this completes, as a person takes their time over the drag; open unless a check sets it.</summary>
    public Task Gate { get; set; } = Task.CompletedTask;

    /// <summary>Run at the release, just before the answer, such as moving the clock on, or throwing what the overlay could throw.</summary>
    public Action AtTheRelease { get; set; } = () => { };

    /// <summary>For each selection, every display handed with its picture's size, in order.</summary>
    public List<IReadOnlyList<(Display Display, int Width, int Height)>> Handed { get; } = [];

    /// <summary>For each selection, what its release starts, in order.</summary>
    public List<CaptureMode> Modes { get; } = [];

    public async Task<SelectedRegion?> Select(IReadOnlyList<DisplayGrab> grabs, CaptureMode mode)
    {
        Handed.Add(grabs.Select(grab => (grab.Display, grab.Frame.Width, grab.Frame.Height)).ToList());
        Modes.Add(mode);
        await Gate;
        AtTheRelease();
        return Answer;
    }
}
