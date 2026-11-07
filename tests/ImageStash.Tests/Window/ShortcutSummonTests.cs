using Avalonia;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G1, G8: the open shortcut's press reads, at the press and in this order, the window in front, which is the paste
/// target unless it is ImageStash itself, and the pointer, or no pointer when the platform cannot say; hands the
/// reading on at once; asks for that target's caret within 250 ms, or with ImageStash in front for the held target's,
/// and for no caret without a target; and hands the anchors to the summon, press by press in the order they were made.
/// </summary>
[TestClass]
public sealed class ShortcutSummonTests
{
    private static readonly PixelRect Caret = new(220, 243, 1, 16);

    [TestMethod]
    public void ThePressReadsTheWindowInFrontAndThePointerThenTheTargetsCaretAndHandsThemOn()
    {
        var steps = new List<string>();
        var target = new RecordingTarget(steps) { InFront = new ForegroundReading(false, RecordingTarget.App("Relay")), Caret = Task.FromResult<PixelRect?>(Caret) };
        var desktop = new Pointer(steps) { At = new PixelPoint(2000, 100) };
        var pressed = new List<ForegroundReading>();
        var summons = new List<SummonAnchor>();
        var summon = new ShortcutSummon(target, desktop, () => RecordingTarget.App("Held"), pressed.Add, summons.Add);

        summon.Pressed();
        target.InFront = new ForegroundReading(false, RecordingTarget.App("Other"));
        desktop.At = new PixelPoint(5, 5);

        CollectionAssert.AreEqual(new[] { new ForegroundReading(false, RecordingTarget.App("Relay")) }, pressed);
        CollectionAssert.AreEqual(new[] { new SummonAnchor(new PixelPoint(2000, 100), Caret) }, summons);
        CollectionAssert.AreEqual(new[] { "foreground", "pointer", "caret Relay" }, steps, "the window in front is read first, at the press (G8), and its caret looked up, not the window's in front later");
        CollectionAssert.AreEqual(new[] { (RecordingTarget.App("Relay"), TimeSpan.FromMilliseconds(250)) }, target.CaretsAsked, "abandoned after 250 ms (How the guarantees hold, G1)");
    }

    [TestMethod]
    public void WithNoPointerTheWindowInFrontIsReadForTheActiveDisplay()
    {
        var steps = new List<string>();
        var target = new RecordingTarget(steps) { InFront = new ForegroundReading(false, null) };
        var front = new PixelRect(2200, 200, 800, 600);
        var summons = new List<SummonAnchor>();
        var summon = new ShortcutSummon(target, new Pointer(steps) { At = null, Front = front }, () => null, _ => { }, summons.Add);

        summon.Pressed();

        CollectionAssert.AreEqual(new[] { new SummonAnchor(null, null, front) }, summons, "G1: else centred on the active display");
        CollectionAssert.AreEqual(new[] { "foreground", "pointer", "front window" }, steps, "read at the press, and only when the pointer cannot be");
    }

    [TestMethod]
    public void WithImageStashInFrontTheHeldTargetsCaretIsLookedUp()
    {
        var target = new RecordingTarget { InFront = new ForegroundReading(true, null), Caret = Task.FromResult<PixelRect?>(Caret) };
        var summons = new List<SummonAnchor>();
        var summon = new ShortcutSummon(target, new Pointer([]) { At = new PixelPoint(300, 200) }, () => RecordingTarget.App("Relay"), _ => { }, summons.Add);

        summon.Pressed();

        CollectionAssert.AreEqual(new[] { (RecordingTarget.App("Relay"), TimeSpan.FromMilliseconds(250)) }, target.CaretsAsked, "the paste target is the one already held while ImageStash is in front (How the guarantees hold, G1)");
        CollectionAssert.AreEqual(new[] { new SummonAnchor(new PixelPoint(300, 200), Caret) }, summons);
    }

    [TestMethod]
    public void WithNoTargetInFrontOrHeldNoCaretIsLookedUp()
    {
        var target = new RecordingTarget { InFront = new ForegroundReading(true, null) };
        var pressed = new List<ForegroundReading>();
        var summons = new List<SummonAnchor>();
        var summon = new ShortcutSummon(target, new Pointer([]) { At = new PixelPoint(300, 200) }, () => null, pressed.Add, summons.Add);

        summon.Pressed();
        target.InFront = new ForegroundReading(false, null);
        summon.Pressed();

        Assert.IsEmpty(target.CaretsAsked);
        CollectionAssert.AreEqual(new[] { new ForegroundReading(true, null), new ForegroundReading(false, null) }, pressed);
        CollectionAssert.AreEqual(new[] { new SummonAnchor(new PixelPoint(300, 200)), new SummonAnchor(new PixelPoint(300, 200)) }, summons, "ImageStash's own with no target held, or the desktop's press, has only the pointer");
    }

    [TestMethod]
    public void ACaretLookupThatFindsNoneOrFailsLeavesThePointerToStandIn()
    {
        var target = new RecordingTarget { InFront = new ForegroundReading(false, RecordingTarget.App("Relay")) };
        var summons = new List<SummonAnchor>();
        var summon = new ShortcutSummon(target, new Pointer([]) { At = new PixelPoint(300, 200) }, () => null, _ => { }, summons.Add);

        summon.Pressed();
        target.Caret = Task.FromException<PixelRect?>(new InvalidOperationException("the lookup broke"));
        summon.Pressed();

        CollectionAssert.AreEqual(new[] { new SummonAnchor(new PixelPoint(300, 200)), new SummonAnchor(new PixelPoint(300, 200)) }, summons);
    }

    [TestMethod]
    public void EachPressesReadingIsHandedOnAtThePressAndItsSummonInOrderOnceItsCaretIsFound()
    {
        var slow = new TaskCompletionSource<PixelRect?>();
        var target = new RecordingTarget { InFront = new ForegroundReading(false, RecordingTarget.App("Relay")), Caret = slow.Task };
        var pressed = new List<string>();
        var summons = new List<PixelRect?>();
        var summon = new ShortcutSummon(target, new Pointer([]), () => null, inFront => pressed.Add(inFront.Target!.AppName), anchor =>
        {
            lock (summons)
            {
                summons.Add(anchor.Caret);
            }
        });

        summon.Pressed();
        target.InFront = new ForegroundReading(false, RecordingTarget.App("Other"));
        target.Caret = Task.FromResult<PixelRect?>(new PixelRect(5, 5, 1, 16));
        summon.Pressed();
        var summonedWhileTheFirstWaits = Count(summons);
        slow.SetResult(Caret);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (Count(summons) < 2 && clock.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(10);
        }

        CollectionAssert.AreEqual(new[] { "Relay", "Other" }, pressed, "each reading reaches the window at its press, so an activation after it sets the target after it (G8)");
        Assert.AreEqual(0, summonedWhileTheFirstWaits, "the second press, its caret found at once, waits for the first's summon");
        CollectionAssert.AreEqual(new PixelRect?[] { Caret, new PixelRect(5, 5, 1, 16) }, summons, "presses reach the window in the order they were made (F5)");
    }

    [TestMethod]
    public void APointerThePlatformCannotFindLeavesTheAnchorEmpty()
    {
        var summons = new List<SummonAnchor>();

        new ShortcutSummon(new RecordingTarget(), new Pointer([]), () => null, _ => { }, summons.Add).Pressed();

        CollectionAssert.AreEqual(new[] { new SummonAnchor(null) }, summons);
    }

    private static int Count<T>(List<T> list)
    {
        lock (list)
        {
            return list.Count;
        }
    }

    private sealed class Pointer(List<string> steps) : IQuietWindows
    {
        public PixelPoint? At { get; set; }

        public void ShowWithoutActivating(Avalonia.Controls.Window window) => throw new NotSupportedException("The press shows nothing itself.");

        public Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<PixelRect> parts) => throw new NotSupportedException("The press shows nothing itself.");

        public Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window) => throw new NotSupportedException("The press activates nothing.");

        public void RoundCorners(Avalonia.Controls.Window window) => throw new NotSupportedException("The press shows no notification or bar.");

        public PixelRect? TrayIconRectangle() => null;

        public PixelPoint? PointerPosition()
        {
            steps.Add("pointer");
            return At;
        }

        public PixelRect? Front { get; set; }

        public PixelRect? FrontWindowRectangle()
        {
            steps.Add("front window");
            return Front;
        }
    }
}
