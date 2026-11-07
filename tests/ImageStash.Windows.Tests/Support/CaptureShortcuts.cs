using Avalonia.Threading;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// F5's four shortcuts registered over the real seam as ImageStash binds them, from the default settings: the
/// screenshot's and the record shortcut's presses start the desk's acts on the UI thread, as the policy posts them; the
/// other two act on nothing here. Every shortcut it could not register is named, as the tooltip would name it.
/// </summary>
public sealed class CaptureShortcuts : IDisposable
{
    private readonly WindowsShortcuts seam = new();
    private readonly List<string> named = [];
    private readonly List<Task> taken = [];
    private readonly List<Task> recorded = [];

    public static CaptureShortcuts Register(CaptureDesk desk)
    {
        var pressed = new CaptureShortcuts();
        TrayDesktop.Run(() => new GlobalShortcuts(
            SettingsInMemory.Defaults(),
            GlobalShortcuts.Bindings(() => { }, () => pressed.taken.Add(desk.Screenshot.Take()), () => pressed.recorded.Add(desk.RecordClip.StartOrStop()), () => { }),
            pressed.seam,
            failure => pressed.named.Add(failure.Sentence),
            action => Dispatcher.UIThread.Post(action)).Start());
        return pressed;
    }

    /// <summary>How many screenshots the shortcut has started.</summary>
    public int Taken => TrayDesktop.Get(() => taken.Count);

    /// <summary>How many presses of the record shortcut have reached the act, whether each started a recording or stopped one.</summary>
    public int Recorded => TrayDesktop.Get(() => recorded.Count);

    public List<string> Named() => TrayDesktop.Get(() => named.ToList());

    /// <summary>Until the act the <paramref name="count"/>th press of the screenshot shortcut started has ended.</summary>
    public async Task Ended(int count)
    {
        await ShownWindow.Eventually(() => Taken >= count);
        await TrayDesktop.Get(() => taken[count - 1]);
    }

    /// <summary>Until the act the <paramref name="count"/>th press of the record shortcut ran has ended, a recording it started included.</summary>
    public async Task RecordEnded(int count)
    {
        await ShownWindow.Eventually(() => Recorded >= count);
        await TrayDesktop.Get(() => recorded[count - 1]).WaitAsync(TimeSpan.FromSeconds(30));
    }

    public void Dispose() => seam.Dispose();
}
