using System.Diagnostics;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// Other programs' windows at the display's scale (G1, E1, E6): a chat window per-monitor aware and one DPI-unaware, each
/// asked for at its place on the reference display, land at that place magnified, take a click on their title bar in
/// their caption, and have the caret of their edit box found where the checks' own reading of it says, its logical
/// pixels scaled by the window's display DPI over its own. The unaware one is in
/// <see cref="WindowsTargetTests.TheCaretOfAnotherProgramsEditBoxIsItsThreadsWindowsCaretOnTheScreen"/>. These checks
/// start PowerShell windows of their own at (100, 100) on the primary display and click their title bars.
/// </summary>
[TestClass]
public sealed class WindowsCaretScalingTests
{
    [TestMethod]
    public async Task APerMonitorAwareChatsCaretIsFoundInItsOwnPixelsWhichAreItsDisplays()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        var at = DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100);
        using var other = TargetApp.Start("ImageStash check aware caret target", at);
        other.ClickTitleBar();
        var target = seam.Foreground().Target!;
        var expected = Until(() => ScreenCaret.Of(other.Window));

        var found = await WindowsCaret.Find(other.Window, ShortcutSummon.CaretBound);
        var throughTheSeam = await seam.CaretOf(target, ShortcutSummon.CaretBound);
        var accessible = WindowsCaret.AccessibleCaretOf(ScreenCaret.FocusOf(other.Window));

        Assert.AreEqual(1, ScreenCaret.DisplayPerOwn(other.Window), "an aware window's pixels are its display's");
        Assert.AreEqual(new CaretFound(expected, CaretStep.WindowsCaret), found, "the first step of G1's chain");
        Assert.AreEqual(expected, throughTheSeam);
        Assert.AreEqual(expected, accessible?.Rectangle, "the accessibility caret object, the chain's second step, locates the same caret to the pixel");
    }

    [TestMethod]
    [DataRow(TargetAwareness.PerMonitor)]
    [DataRow(TargetAwareness.Unaware)]
    public void AChatAskedForAtItsReferencePlaceLandsThereMagnifiedAndTakesATitleBarClickInItsCaption(TargetAwareness awareness)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        var primary = DisplayPlaces.Primary();
        var at = DisplayPlaces.At(primary, 100, 100);
        using var chat = TargetApp.Start($"ImageStash check {awareness} chat", at, awareness: awareness);
        var expected = DisplayPlaces.Rect(primary, 100, 100, 400, 260);
        var bounds = chat.Bounds;

        var slack = awareness == TargetAwareness.PerMonitor ? 0 : 1;
        Assert.IsTrue(Math.Abs(bounds.X - expected.X) <= slack && Math.Abs(bounds.Y - expected.Y) <= slack && Math.Abs(bounds.Width - expected.Width) <= slack && Math.Abs(bounds.Height - expected.Height) <= slack,
            $"asked for {expected}, landed at {bounds} ({awareness})");
        Assert.AreEqual(awareness == TargetAwareness.PerMonitor ? 1 : primary.Scaling, ScreenCaret.DisplayPerOwn(chat.Window), 1e-9, "its own DPI against its display's");
        chat.ClickTitleBar();
        Assert.IsFalse(chat.Messages().Any(message => message.StartsWith("WM_LBUTTONDOWN", StringComparison.Ordinal)), "the click landed in the caption, not the client area");
    }

    /// <summary><paramref name="read"/>'s answer once it has one, within five seconds.</summary>
    private static T Until<T>(Func<T?> read) where T : struct
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (read() is { } answer)
            {
                return answer;
            }

            Thread.Sleep(20);
        }

        Assert.Fail("no answer within five seconds");
        return default;
    }
}
