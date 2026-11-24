using Avalonia.Media;
using ImageStash.Look;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The empty stash as the window draws it over a real, empty store (G2): the drop line, then every way in as a row, in
/// order, each with its shortcut's key caps as the settings read it, or the tray menu's words while the setting is no
/// shortcut, and a remap read at once; the line saying how ImageStash opens again, naming the open shortcut, the tray and
/// that closing keeps it running (G3); each row running the one act the window hands it (G12 to G15); no clipboard read
/// until a row's act reads it (N9); no row taking the keyboard's focus (G7); each row led by an icon of its own in the
/// look's icon font, and the caps in the look's own keys (F10).
/// </summary>
[TestClass]
public sealed class EmptyStashWaysInTests
{
    private const string Line = "Open ImageStash from any app with Ctrl+Alt+Space, or from its icon in the tray. Closing this window keeps it running.";

    [TestMethod]
    public async Task TheDropLineThenEveryWayInInOrderEachWithItsShortcutsCapsThenHowImageStashOpensAgain()
    {
        using var listed = await ListedStash.Open();

        var shown = EmptyStashShown.Over(listed);

        Assert.IsTrue(shown.Showing, "the empty stash shows in the grid's place (G2)");
        CollectionAssert.AreEqual(new[] { "Your stash is empty", "Drop images and GIFs here, from a browser, a chat or File Explorer", Line }, shown.ReadLines().ToList(),
            "the heading, the drop line as words, and under the rows the line naming the open shortcut, the tray and that closing keeps it running");
        AssertRows(shown, ("Paste a copied image", "Ctrl+V"), ("Take a screenshot", "Ctrl+PrtScn"), ("Record a clip", "Ctrl+Shift+PrtScn"),
            ("Stash the clipboard image", listed.Settings.Current.Shortcuts.StashClipboardImage), ("Import files", "Ctrl+O"));
    }

    [TestMethod]
    public async Task EachRowLeadsWithAnIconOfItsOwnInTheLooksIconFontBeforeItsWordsWithOrWithoutAShortcut()
    {
        using var listed = await ListedStash.Open();
        var shown = EmptyStashShown.Over(listed);

        AssertIcons(shown.ReadRows(), "with every shortcut set");

        // Two settings that are no shortcut, so their rows read the tray menu's words in their caps' place.
        listed.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Record = "nonsense", StashClipboardImage = "" } });
        shown.ShortcutsChanged();

        AssertIcons(shown.ReadRows(), "with two settings that are no shortcut");

        static void AssertIcons(IReadOnlyList<ShownWay> rows, string when)
        {
            var read = string.Join(" | ", rows.Select(way => $"{(way.Icon is { } icon ? $"U+{(int)icon[0]:X4}" : "no icon")} {way.Words}"));
            Assert.HasCount(5, rows, read);
            Assert.IsTrue(rows.All(way => way.Icon is { Length: 1 } icon && icon[0] is >= '\uE000' and <= '\uF8FF'),
                $"{when}, each row leads with one glyph of the look's icon font, then its words and its caps (G2, F10): {read}");
            Assert.HasCount(5, rows.Select(way => way.Icon).Distinct(), $"{when}, each way in has an icon of its own: {read}");
        }
    }

    [TestMethod]
    public async Task ShortcutsRemappedWhileTheEmptyStashShowsReadInItsRowsAndItsLineAtOnceAndOneThatIsNoShortcutReadsTheTrayMenu()
    {
        using var listed = await ListedStash.Open();
        var shown = EmptyStashShown.Over(listed);

        // Rebound as a Settings field rebinds them, the window told as the app tells it.
        using (listed.Shortcuts.Suspend())
        {
            Assert.IsTrue(listed.Shortcuts.Rebind(ShortcutAct.Screenshot, Shortcut.Parse("shift+f8").Value!).Succeeded);
            Assert.IsTrue(listed.Shortcuts.Rebind(ShortcutAct.Open, Shortcut.Parse("Ctrl+Alt+F9").Value!).Succeeded);
        }

        AssertRows(shown, ("Paste a copied image", "Ctrl+V"), ("Take a screenshot", "Shift+F8"), ("Record a clip", "Ctrl+Shift+PrtScn"),
            ("Stash the clipboard image", listed.Settings.Current.Shortcuts.StashClipboardImage), ("Import files", "Ctrl+O"));
        Assert.AreEqual("Open ImageStash from any app with Ctrl+Alt+F9, or from its icon in the tray. Closing this window keeps it running.", shown.ReadLines()[^1],
            "the line names the open shortcut as the settings now read it");

        // Two settings that are no shortcut, as an edited settings file holds them.
        listed.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Record = "nonsense", StashClipboardImage = "" } });
        shown.ShortcutsChanged();

        AssertRows(shown, ("Paste a copied image", "Ctrl+V"), ("Take a screenshot", "Shift+F8"), ("Record a clip", null), ("Stash the clipboard image", null), ("Import files", "Ctrl+O"));

        listed.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Open = "nonsense" } });
        shown.ShortcutsChanged();

        Assert.AreEqual("Open ImageStash from its icon in the tray. Closing this window keeps it running.", shown.ReadLines()[^1],
            "an open setting that is no shortcut leaves the tray alone named");
    }

    [TestMethod]
    public async Task EachRowRunsTheOneActTheWindowHandsItAndNoOther()
    {
        using var listed = await ListedStash.Open();
        var ran = new List<string>();
        var shown = EmptyStashShown.Over(listed, new WaysInActs(() => ran.Add("paste"), () => ran.Add("screenshot"), () => ran.Add("record"), () => ran.Add("stash clipboard image"), () => ran.Add("import files")));

        foreach (var (words, act) in new[] { ("Paste a copied image", "paste"), ("Take a screenshot", "screenshot"), ("Record a clip", "record"), ("Stash the clipboard image", "stash clipboard image"), ("Import files", "import files") })
        {
            ran.Clear();
            shown.Click(words);
            CollectionAssert.AreEqual(new[] { act }, ran, $"a click on {words} runs {act}, the act the window was handed, once (G12 to G15)");
        }
    }

    [TestMethod]
    public async Task ShowingTheEmptyStashReadsNothingOfTheClipboardAndEachClipboardRowsActReadsItOnce()
    {
        using var listed = await ListedStash.Open();
        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false));
        listed.Clipboard.ImageReading = Outcome<ClipboardImage?>.Of(new ClipboardImage(MediaFixtures.Bytes("photo.jpg"), null));

        var shown = EmptyStashShown.Over(listed);
        listed.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = "Ctrl+Shift+F8" } });
        shown.ShortcutsChanged();
        shown.ShowListing();

        Assert.AreEqual((0, 0), (listed.Clipboard.Reads, listed.Clipboard.ImageReads), "showing the empty stash, and drawing it again, read nothing of the clipboard (N9)");
        CollectionAssert.AreEqual(new[] { "Paste a copied image", "Take a screenshot", "Record a clip", "Stash the clipboard image", "Import files" }, shown.ReadRows().Select(way => way.Words).ToList(),
            "the clipboard's rows read as they always do, whatever it holds");

        shown.Click("Paste a copied image");
        await listed.WayInRunning;

        Assert.AreEqual((1, 0), (listed.Clipboard.Reads, listed.Clipboard.ImageReads), "the Paste row's act reads it once, as Ctrl+V does (G13)");
        Assert.HasCount(1, await listed.Stash.All(), "and adds its picture");

        shown.Click("Stash the clipboard image");
        await listed.WayInRunning;

        Assert.AreEqual((1, 1), (listed.Clipboard.Reads, listed.Clipboard.ImageReads), "Stash the clipboard image's row reads the image alone, once, as the tray's entry does (G13)");
        Assert.HasCount(2, await listed.Stash.All(), "and stashes it");
    }

    [TestMethod]
    public async Task NoRowTakesTheFocusAndTheirCapsWearTheLooksKeyCapKeys()
    {
        using var listed = await ListedStash.Open();
        var shown = EmptyStashShown.Over(listed);
        var ground = new SolidColorBrush(Colors.Teal);
        var edge = new SolidColorBrush(Colors.Olive);

        Assert.IsFalse(shown.AnyRowFocusable, "a click on a row leaves the keyboard's focus where it was (G7)");
        ControlThread.Run(() =>
        {
            shown.Status.StateView.Resources[LookKeys.KeyCapGround] = ground;
            shown.Status.StateView.Resources[LookKeys.KeyCapBorder] = edge;
        });
        var caps = shown.Caps();
        Assert.IsGreaterThan(0, caps.Count);
        ControlThread.Run(() => Assert.IsTrue(caps.All(cap => ReferenceEquals(cap.Background, ground) && ReferenceEquals(cap.BorderBrush, edge)), "every cap takes the take's key-cap ground and edge (F10)"));
    }

    /// <summary>The rows read in order, each with its words and the caps of its shortcut's text, or the tray menu's words for a null one.</summary>
    private static void AssertRows(EmptyStashShown shown, params (string Words, string? Shortcut)[] expected)
    {
        var rows = shown.ReadRows();
        var read = string.Join(" | ", rows.Select(way => $"{way.Words} {(way.Instead ?? string.Join(" ", way.Caps))}"));
        Assert.HasCount(expected.Length, rows, read);
        for (var i = 0; i < expected.Length; i++)
        {
            var (words, shortcut) = expected[i];
            Assert.AreEqual(words, rows[i].Words, $"row {i + 1} (G2): {read}");
            CollectionAssert.AreEqual(shortcut?.Split('+') ?? [], rows[i].Caps.ToList(), $"{words}'s caps, one per key of its shortcut as the settings read it: {read}");
            Assert.AreEqual(shortcut is null ? EmptyStashWaysIn.FromTrayMenu : null, rows[i].Instead, $"{words}: {read}");
        }
    }
}
