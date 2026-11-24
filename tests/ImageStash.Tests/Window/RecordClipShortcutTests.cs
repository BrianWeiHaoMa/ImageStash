using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The record shortcut as Record a clip hands it to the recording's bar (G15, G33): its words, read as the recording
/// starts, while a press of it would act, and none while it is paused or left unregistered; a pause, its toggle back or
/// a rebind while the bar shows reaching the bar at once. The shortcut ends the recording at any time, the moment after
/// Record included: only a click on the bar's Stop waits out a double-click. Capture is told the words by the window,
/// which owns the shortcuts, and names nothing of it.
/// </summary>
[TestClass]
public sealed class RecordClipShortcutTests
{
    private static readonly PixelRect Region = new(100, 50, 32, 24);

    private static readonly string RecordText = Shortcut.Parse(new Shortcuts().Record).Value!.Text;

    [TestMethod]
    public async Task TheBarIsHandedTheRecordShortcutAsTheRecordingStarts()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();

        CollectionAssert.AreEqual(new[] { RecordText }, recording.Frame.ShortcutsNamed.ToList(), "the shortcut as every surface writes it (Shortcut.Text)");
        await CancelAndEnd(recording, acting);
    }

    [TestMethod]
    public async Task APausedShortcutIsNotNamedAndAPauseWhileTheBarShowsReachesIt()
    {
        using var recording = await Recording();
        recording.Shortcuts.TogglePause();

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        recording.Shortcuts.TogglePause();
        recording.Frame.PressRecord();
        await recording.WhileSampling();
        recording.Shortcuts.TogglePause();

        CollectionAssert.AreEqual(new[] { null, RecordText, null }, recording.Frame.ShortcutsNamed.ToList(),
            "none at the start, paused; named at the toggle back; none again at the next pause, while it records (G33)");
        await CancelAndEnd(recording, acting);
    }

    [TestMethod]
    public async Task ARebindWhileTheBarShowsNamesTheNewShortcut()
    {
        using var recording = await Recording();
        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();

        // As Settings remaps it: in a field recording the press.
        using (recording.Shortcuts.Suspend())
        {
            Assert.IsNull(recording.Shortcuts.Rebind(ShortcutAct.Record, Shortcut.Parse("Ctrl+Alt+R").Value!));
        }

        var named = recording.Frame.ShortcutsNamed;
        Assert.AreEqual(RecordText, named[0], "the old words while it waited");
        Assert.IsTrue(named.Count > 1 && named.Skip(1).All(words => words == "Ctrl+Alt+R"), $"never the old words after a remap: {string.Join(", ", named)}");
        await CancelAndEnd(recording, acting);
    }

    [TestMethod]
    public async Task AShortcutLeftUnregisteredIsNotNamed()
    {
        using var recording = await Recording();
        recording.ShortcutsSeam.Release(Shortcut.Parse(RecordText).Value!, ShortcutRefusal.TypesACharacter(null));

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();

        CollectionAssert.AreEqual(new string?[] { null }, recording.Frame.ShortcutsNamed.ToList(), "a shortcut that would type a character is unregistered and does nothing (G33)");
        await CancelAndEnd(recording, acting);
    }

    [TestMethod]
    public async Task TheShortcutStopsTheRecordingTheMomentAfterRecord()
    {
        using var recording = await Recording();
        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        recording.Frame.PressRecord();
        await recording.WhileSampling();
        recording.StepRecording(0);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(10));

        // The shortcut's press, 10 ms after Record: well within a double-click's time.
        await recording.RecordClip.StartOrStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.HasCount(1, await recording.Stash.All(), "Stop on the shortcut ended it and the clip was saved (G15)");
        CollectionAssert.AreEqual(new[] { "show", "record", "saving", "close" }, recording.Frame.Calls.ToList());
    }

    [TestMethod]
    public void CaptureNamesNothingOfTheWindowsShortcuts()
    {
        var capture = Path.Combine(RepositoryRoot.Above(AppContext.BaseDirectory)!, "src", "ImageStash", "Capture");
        foreach (var source in Directory.GetFiles(capture, "*.cs"))
        {
            var text = File.ReadAllText(source);
            Assert.IsFalse(text.Contains("ImageStash.Window", StringComparison.Ordinal) || text.Contains("GlobalShortcuts", StringComparison.Ordinal),
                $"{Path.GetFileName(source)}: Window calls Capture, never the other way (Window boundary)");
        }
    }

    private static async Task<ListedStash> Recording()
    {
        var recording = await ListedStash.OpenRecording();
        recording.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, Region);
        MediaFrame[] pictures = [recording.Screen.Paint(RecordingScreen.Primary, seed: 1), recording.Screen.Paint(RecordingScreen.Primary, seed: 2)];
        recording.Screen.EachGrab = index => Outcome<MediaFrame>.Of(pictures[index % 2]);
        return recording;
    }

    private static async Task CancelAndEnd(ListedStash recording, Task acting)
    {
        recording.Frame.PressCancel();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsEmpty(await recording.Stash.All(), "cancelled, nothing added");
    }
}
