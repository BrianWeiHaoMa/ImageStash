using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Threading;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// F5, G33, S5, E3 on the real desktop: the open shortcut registered as a system hot key fires on the seam's own
/// thread at a real press, and not after it is released; a combination another program holds is refused as such and
/// leaves nothing registered; the layout judgement, under the layout in use, refuses a combination that types a
/// character and passes F5's.
/// </summary>
[TestClass]
public sealed class WindowsShortcutsTests
{
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut ShiftA = Shortcut.Parse("Shift+A").Value!;

    [TestMethod]
    public void TheOpenShortcutFiresOnceOnTheSeamsOwnThreadWithinATenthOfASecond()
    {
        using var shortcuts = new WindowsShortcuts();
        using var pressed = new ManualResetEventSlim();
        var presses = 0;
        var onUiThread = true;
        var thread = Environment.CurrentManagedThreadId;

        var registered = shortcuts.Register(Open, () =>
        {
            Interlocked.Increment(ref presses);
            onUiThread = Dispatcher.UIThread.CheckAccess();
            thread = Environment.CurrentManagedThreadId;
            pressed.Set();
        });
        Assert.IsNull(registered, $"refused: {registered?.Kind}");
        var clock = Stopwatch.StartNew();
        TestHotKeys.Press(Open);

        Assert.IsTrue(pressed.Wait(TimeSpan.FromSeconds(5)), "the press did not fire");
        Assert.IsLessThan(100, clock.ElapsedMilliseconds, "fired within 100 ms of the press");
        Thread.Sleep(300);
        Assert.AreEqual(1, presses, "one press, one call");
        Assert.IsFalse(onUiThread);
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, thread);
    }

    [TestMethod]
    public void AReleasedShortcutNoLongerFiresAndIsFreeForAnotherProgram()
    {
        using var shortcuts = new WindowsShortcuts();
        var presses = 0;
        Assert.IsNull(shortcuts.Register(Open, () => Interlocked.Increment(ref presses)));

        shortcuts.Release(Open);

        // The other program's hold proves the combination free, and it takes the press, which reaches no window.
        using var other = OtherProgramHoldingAHotKey.Start(Open);
        Assert.IsNotNull(other, "the released combination is free");
        TestHotKeys.Press(Open);
        Thread.Sleep(300);
        Assert.AreEqual(0, presses);
    }

    [TestMethod]
    public void ACombinationAnotherProgramHoldsIsRefusedAsHeldAndLeavesNothingRegistered()
    {
        using var shortcuts = new WindowsShortcuts();
        var presses = 0;
        var other = OtherProgramHoldingAHotKey.Start(Open);
        Assert.IsNotNull(other, "the check's holder took the combination");
        ShortcutRefusal? refused;
        try
        {
            refused = shortcuts.Register(Open, () => Interlocked.Increment(ref presses));
            TestHotKeys.Press(Open);
            Thread.Sleep(300);
        }
        finally
        {
            other.Dispose();
        }

        Assert.IsNotNull(refused);
        Assert.AreEqual(ShortcutRefusal.HeldByAnotherProgram, refused);
        Assert.AreEqual(0, presses);
        // Nothing of the check's process holds it: another holder can take it, and once that one ends the seam can.
        using (var again = OtherProgramHoldingAHotKey.Start(Open))
        {
            Assert.IsNotNull(again, "the refused registration left nothing registered");
        }

        Assert.IsNull(shortcuts.Register(Open, () => { }));
    }

    [TestMethod]
    public void AShortcutTheLayoutInUseComesToTypeIsReleasedWithinASecondAndReported()
    {
        var types = false;
        using var reports = new BlockingCollection<(Shortcut Shortcut, ShortcutRefusal Reason)>();
        using var shortcuts = new WindowsShortcuts((_, _) => Volatile.Read(ref types) ? ShortcutRefusal.TypesACharacter("@") : null);
        shortcuts.ListenForReleases((shortcut, reason) => reports.Add((shortcut, reason)));
        Assert.IsNull(shortcuts.Register(Open, () => { }));

        // As a switch of the foreground app's layout to one where the combination types a character.
        Volatile.Write(ref types, true);
        var clock = Stopwatch.StartNew();

        Assert.IsTrue(reports.TryTake(out var report, TimeSpan.FromSeconds(3)), "the seam reported no release");
        Assert.IsLessThan(1500, clock.ElapsedMilliseconds, "judged at least once a second (G33)");
        Assert.AreEqual((Open, ShortcutRefusal.TypesACharacter("@")), report, "reported with the character the judgment found");
        using var other = OtherProgramHoldingAHotKey.Start(Open);
        Assert.IsNotNull(other, "the hot key itself was released");
    }

    [TestMethod]
    public void APressTheLayoutInUseWouldTypeIsSwallowedAndTheShortcutReleased()
    {
        var types = false;
        using var judged = new AutoResetEvent(false);
        using var reports = new BlockingCollection<(Shortcut Shortcut, ShortcutRefusal Reason)>();
        using var shortcuts = new WindowsShortcuts((_, _) =>
        {
            var answer = Volatile.Read(ref types);
            judged.Set();
            return answer ? ShortcutRefusal.TypesACharacter("@") : null;
        });
        shortcuts.ListenForReleases((shortcut, reason) => reports.Add((shortcut, reason)));
        var presses = 0;
        Assert.IsNull(shortcuts.Register(Open, () => Interlocked.Increment(ref presses)));
        judged.WaitOne(0);

        // Just after a once-a-second sample, so the press, not the next sample, is what finds it.
        Assert.IsTrue(judged.WaitOne(TimeSpan.FromSeconds(3)), "no sample came");
        Volatile.Write(ref types, true);
        TestHotKeys.Press(Open);

        Assert.IsTrue(reports.TryTake(out var report, TimeSpan.FromSeconds(3)), "the seam reported no release");
        Assert.AreEqual((Open, ShortcutRefusal.TypesACharacter("@")), report, "reported with the character the judgment found");
        Thread.Sleep(300);
        Assert.AreEqual(0, presses, "the press is swallowed, not acted on (How G33 holds)");
    }

    [TestMethod]
    public void UnderTheLayoutInUseF5sShortcutsTypeNothingAndShiftAOrATypes()
    {
        var layout = WindowsShortcuts.ForegroundLayout();

        Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Open), layout));
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse("Ctrl+PrtScn").Value!), layout));
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse("Ctrl+Shift+PrtScn").Value!), layout));
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse("Win+Alt+V").Value!), layout));
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse("Ctrl+A").Value!), layout), "a control character types nothing");
        Assert.IsTrue(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(ShiftA), layout));
        Assert.IsTrue(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse("A").Value!), layout));
    }

    [TestMethod]
    public void UnderTheLayoutInUseEnterTabBackspaceAndEscTypeTheirOwnCharacterWhileCtrlsControlCodesTypeNothing()
    {
        var layout = WindowsShortcuts.ForegroundLayout();

        foreach (var text in new[] { "Enter", "Shift+Enter", "Back", "Shift+Back", "Tab", "Shift+Esc" })
        {
            Assert.IsTrue(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse(text).Value!), layout), $"{text} types its character in every program (G32)");
        }

        foreach (var text in new[] { "Ctrl+A", "Ctrl+Shift+A", "Ctrl+V" })
        {
            Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse(text).Value!), layout), $"{text} yields the code Ctrl makes of its key");
        }
    }

    [TestMethod]
    public void UnderTheLayoutInUseTheWindowsKeyWithAltTypesNothingWhileAltOrTheWindowsKeyAloneTypes()
    {
        var layout = WindowsShortcuts.ForegroundLayout();

        foreach (var text in new[] { "Win+Alt+V", "Win+Alt+Shift+V", "Win+Alt+Enter" })
        {
            Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse(text).Value!), layout), $"{text} is a system keystroke, typing no text (S5)");
        }

        foreach (var text in new[] { "Alt+V", "Alt+Enter", "Win+V" })
        {
            Assert.IsTrue(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse(text).Value!), layout), $"{text} types its character");
        }
    }

    [TestMethod]
    public void TheCharacterACombinationTypesCrossesTheSeamWithItsReasonAndADeadKeysNever()
    {
        // As TheWindowsKeyWithAltAndCtrlIsJudgedByItsTranslation, the translations an AltGr layout gives are supplied.
        var withCtrl = WindowsShortcuts.CombinationOf(Shortcut.Parse("Ctrl+Alt+Q").Value!).Modifiers;

        Assert.AreEqual(ShortcutRefusal.TypesACharacter("@"), WindowsShortcuts.Typing(withCtrl, 1, "@"), "the character the translation yields");
        Assert.AreEqual(ShortcutRefusal.TypesACharacter(null), WindowsShortcuts.Typing(withCtrl, -1, "^"), "a dead key types, and names no character");
        Assert.IsNull(WindowsShortcuts.Typing(withCtrl, 1, "\u0011"), "the code Ctrl makes of its key types nothing");
        var shiftA = WindowsShortcuts.Typing(WindowsShortcuts.CombinationOf(ShiftA), WindowsShortcuts.ForegroundLayout());
        Assert.AreEqual(ShortcutRefusalKind.TypesACharacter, shiftA?.Kind, "under the layout in use");
        Assert.IsFalse(string.IsNullOrWhiteSpace(shiftA!.Character), "with the letter it types");
    }

    [TestMethod]
    public void TheWindowsKeyWithAltAndCtrlIsJudgedByItsTranslation()
    {
        // A check loads no keyboard layout of its own, the layouts being the signed-in user's: so the translations an
        // AltGr layout gives, where Ctrl with Alt types, are supplied, as the German layout's '@' for Ctrl+Alt+Q.
        var withCtrl = WindowsShortcuts.CombinationOf(Shortcut.Parse("Ctrl+Win+Alt+Q").Value!).Modifiers;
        var withoutCtrl = WindowsShortcuts.CombinationOf(Shortcut.Parse("Win+Alt+Q").Value!).Modifiers;

        Assert.IsTrue(WindowsShortcuts.TypesACharacter(withCtrl, 1, "@"), "a character");
        Assert.IsTrue(WindowsShortcuts.TypesACharacter(withCtrl, -1, "^"), "a dead key");
        Assert.IsTrue(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse("Ctrl+Win+Alt+Shift+Q").Value!).Modifiers, 1, "@"), "a character, Shift held too");
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(withCtrl, 0, ""), "no translation");
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(withCtrl, 1, "\u0011"), "the code Ctrl makes of its key");
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(withoutCtrl, 1, "@"), "without Ctrl, a character");
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(withoutCtrl, -1, "^"), "without Ctrl, a dead key");
        Assert.IsFalse(WindowsShortcuts.TypesACharacter(WindowsShortcuts.CombinationOf(Shortcut.Parse("Shift+F9").Value!).Modifiers, 0, "q"), "only the characters the translation counts");
    }

    [TestMethod]
    public void EnterOrBackspaceAloneIsRefusedAtRegistrationAndLeftFree()
    {
        using var shortcuts = new WindowsShortcuts();

        foreach (var shortcut in new[] { Shortcut.Parse("Enter").Value!, Shortcut.Parse("Back").Value! })
        {
            var refused = shortcuts.Register(shortcut, () => { });

            Assert.IsNotNull(refused, $"{shortcut.Text} registered as a global hot key");
            Assert.AreEqual(ShortcutRefusalKind.TypesACharacter, refused!.Kind);
            using var other = OtherProgramHoldingAHotKey.Start(shortcut);
            Assert.IsNotNull(other, $"{shortcut.Text} was left free");
        }
    }

    [TestMethod]
    public void TheScreenshotShortcutIsRegisteredAsPrintScreenWithControl()
    {
        var combination = WindowsShortcuts.CombinationOf(Shortcut.Parse("Ctrl+PrtScn").Value!);

        // VK_SNAPSHOT is the key Avalonia.Win32 maps Key.PrintScreen to.
        Assert.AreEqual((0x2u, 0x2Cu), (combination.Modifiers, combination.Key), "MOD_CONTROL and VK_SNAPSHOT (F5)");
    }

    [TestMethod]
    public void ACombinationThatTypesACharacterIsRefusedAtRegistrationAndLeftUnregistered()
    {
        using var shortcuts = new WindowsShortcuts();

        var refused = shortcuts.Register(ShiftA, () => { });

        Assert.IsNotNull(refused);
        Assert.AreEqual(ShortcutRefusalKind.TypesACharacter, refused!.Kind);
        using var other = OtherProgramHoldingAHotKey.Start(ShiftA);
        Assert.IsNotNull(other, "Shift+A was left free");
    }

    [TestMethod]
    public void AKeyWindowsHasNoCodeForIsRefusedNamingTheKey()
    {
        using var shortcuts = new WindowsShortcuts();

        var refused = shortcuts.Register(Shortcut.Parse("Ctrl+FnLeftArrow").Value!, () => { });

        Assert.IsNotNull(refused);
        Assert.AreEqual(ShortcutRefusal.NotAKey(Avalonia.Input.Key.FnLeftArrow), refused);
    }
}
