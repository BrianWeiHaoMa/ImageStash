using Avalonia.Input;
using ImageStash.Capture;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// One path through the window's parts as the window wires them over a real store, each key through the window's routing
/// to the act its case calls, the clock set forward: an import ending while the window is hidden leaves its notice for
/// when it shows, counting what was added and naming the file that was not, kept past its time without its Undo (G12,
/// G16); Del then <c>Ctrl+Z</c> from the grid brings the item back, and a second <c>Ctrl+Z</c> finds nothing (G7, G18);
/// <c>Ctrl+T</c> opens the tag field on the selection, and focus moving on adds what was typed there (G7, G17). Moving
/// focus between real controls is beyond these parts: the walk drives the rule a field calls as focus leaves it.
/// Collecting into a stash that holds pictures: with a tag chip lit, a drop tags its new picture and the one the stash
/// already held, its notice says so, and its Undo takes back both (G11, G12); a drop whose picture comes in after the
/// person typed a search and moved the selection leaves both, and its notice's Show brings the new picture up (G12); a
/// JPEG named <c>.jfif</c> and a PNG whose name has no extension come in under <c>.jpg</c> and <c>.png</c>, the card
/// counting the nameless files as to check, and a text file with no extension is skipped as such (F3, G11, G12).
/// Capture to paste, over a stash that holds used pictures, with a chat in front as the paste target's seam reports it:
/// a screenshot's notification's Edit opens the window naming the chat, Save sends nothing to any window, and Enter
/// pastes the saved finished picture into the chat, recording one use (G8, G16, G26, N3, G10); the screenshot's card,
/// read before that Edit, is titled as a screenshot and says on one line that it is on the clipboard and its measured
/// size, and a clip's card leads that line with its length and offers Trim (G16, G24).
/// </summary>
[TestClass]
public sealed class StashWindowPathTests
{
    private static readonly string[] Four = ["photo.jpg", "transparent.png", "animated.gif", "animated.webp"];

    [TestMethod]
    public async Task AHiddenImportsNoticeThenDelAndCtrlZThenCtrlTAndTypingTagsTheRestoredItem()
    {
        using var listed = await ListedStash.Open();

        // (1) Imported while the window is hidden; an hour later it shows.
        listed.WindowShowing.Seen(visible: false, minimised: false);
        var imported = await listed.Import.Drop(ListedStash.Contents([.. Four.Select(MediaFixtures.Path), listed.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
        Pass(listed, TimeSpan.FromHours(1));
        listed.WindowShowing.Seen(visible: true, minimised: false);

        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Added 4 · 1 skipped", notice.Heading, "its first words count four added and one not taken (G12)");
        StringAssert.Contains(notice.Text, "clip.mp4 (a video)");
        foreach (var name in Four.Select(Path.GetFileNameWithoutExtension))
        {
            Assert.DoesNotContain(name!, notice.Text, "none of the four is named");
        }

        Pass(listed, TimeSpan.FromMinutes(1));
        Assert.AreSame(notice, listed.Notices.Shown.Single(), "after a minute of shown time it still shows (G16)");
        Assert.IsFalse(listed.Notices.OffersUndo(notice), "and no longer offers its Undo");

        // (2) One of the four deleted by Del, then Ctrl+Z from the grid.
        var four = imported.Where(file => file.Item is not null).Select(file => file.Item!).ToList();
        Assert.HasCount(4, four);
        var chosen = four[1];
        listed.Listing.Focus(chosen.Id);
        Assert.AreEqual(KeyAction.Delete, await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid));
        Assert.IsFalse((await listed.Stash.All()).Any(item => item.Id == chosen.Id), "deleted");
        var deleted = listed.Notices.Shown[^1];

        await PressCtrlZ(listed);

        var back = (await listed.Stash.All()).Single(item => item.Id == chosen.Id);
        Assert.AreEqual((chosen.Name, chosen.FileName), (back.Name, back.FileName), "back with its data");
        Assert.IsTrue(File.Exists(back.FilePath));
        Assert.DoesNotContain(deleted, listed.Notices.Shown.ToList(), "the Delete's notice is gone");
        CollectionAssert.AreEquivalent(four.Select(item => item.Id).ToList(), (await listed.Stash.All()).Select(item => item.Id).ToList(), "the four still there");

        await PressCtrlZ(listed);

        CollectionAssert.AreEquivalent(four.Select(item => item.Id).ToList(), (await listed.Stash.All()).Select(item => item.Id).ToList(), "a second Ctrl+Z changes nothing");
        Assert.AreSame(notice, listed.Notices.Shown.Single());

        // (3) Ctrl+T on the restored item, "cat" typed, focus moved to the grid.
        await listed.Listing.Refresh();
        listed.Listing.Focus(chosen.Id);
        Assert.AreEqual(KeyAction.AddTags, await listed.Press(Key.T, KeyModifiers.Control, KeyPlace.Grid));
        var opened = listed.TagFieldItems;
        CollectionAssert.AreEqual(new[] { chosen.Id }, opened.Select(item => item.Id).ToList(), "the field opens on the restored item, as the act reports");

        Assert.IsTrue(FieldLeaving.Commits(holdsEdit: true, intoField: false), "focus moving to the grid commits");
        await FieldLeaving.AddTags(listed.Organising, opened, "cat");
        listed.TagFieldItems = [];

        CollectionAssert.AreEqual(new[] { "cat" }, (await listed.Stash.All()).Single(item => item.Id == chosen.Id).Tags.ToList(), "it carries cat (G17)");
        Assert.IsTrue((await listed.Stash.All()).Where(item => item.Id != chosen.Id).All(item => item.Tags.Count == 0), "and nothing else does");
    }

    /// <summary>
    /// The tidying path through the window's parts over a real store, each key through the window's routing to the act
    /// its case calls: three pictures listed in All, the middle one selected; <c>Ctrl+D</c> from the grid makes it a
    /// favorite, its tile alone carries the star and the preview's star reads filled for it (G17, G6, G19); with the
    /// preview open on it, Right steps to the item after it, the grid selecting that one alone and its star reading it no
    /// favorite, Right again is the list's end, and Left twice shows the first (G19, G8); <c>Ctrl+E</c> from the preview
    /// opens the editor on the item shown, a Meme text layer is added with its text in edit, and <c>Ctrl+V</c> there puts
    /// the clipboard's words in uppercased, adding no item and saying nothing, while a picture on the clipboard adds no
    /// item and gets the notice (G20, G13, G21). A tile's pixels, the preview's control and real focus in the layer's text
    /// are beyond these parts: the walk reads the rules they draw from, and tells the paste the caret is in the text.
    /// </summary>
    [TestMethod]
    public async Task CtrlDOnTheMiddleOfThreePicturesStarsItsTileAloneThenTheArrowsStepThePreviewAndCtrlVPutsWordsInAMeme()
    {
        using var listed = await ListedStash.Open();
        foreach (var fixture in new[] { "photo.jpg", "transparent.png", "still.webp" })
        {
            await listed.Stash.ImportFixture(Path.GetFileNameWithoutExtension(fixture), fixture);
        }

        // (1) The middle of the three selected; Ctrl+D from the grid.
        await listed.Listing.ShowScope(Scope.All);
        Assert.HasCount(3, listed.Listing.Items);
        var middle = listed.Listing.Items[1];
        listed.Listing.Focus(middle.Id);
        Assert.AreEqual(KeyAction.ToggleFavorite, await listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid));
        listed.Keys.Released(Key.D);

        var listedNow = listed.Listing.Items.Single(item => item.Id == middle.Id);
        Assert.IsTrue(listedNow.Favorite, "its listed item is a favorite (G17)");
        CollectionAssert.AreEqual(new[] { middle.Id }, listed.Listing.Items.Where(TileStar.Of).Select(item => item.Id).ToList(), "its tile alone carries the star (G6)");
        ControlThread.Run(() =>
        {
            var star = new PreviewStar(() => { });
            star.Show(listedNow);
            Assert.IsTrue(star.Filled, "the preview opened on it reads its star filled (G19)");
        });

        // (2) The preview told open on the middle one; Right, Right, Left, Left from the preview.
        listed.PreviewOpen = true;
        listed.PreviewItem = listedNow;
        var (first, last) = (listed.Listing.Items[0], listed.Listing.Items[2]);
        await PressInThePreview(listed, Key.Right, last);
        ControlThread.Run(() =>
        {
            var star = new PreviewStar(() => { });
            star.Show(listed.PreviewItem!);
            Assert.IsFalse(star.Filled, "the item stepped to reads no favorite (G19)");
        });
        await PressInThePreview(listed, Key.Right, last);
        await PressInThePreview(listed, Key.Left, listedNow);
        await PressInThePreview(listed, Key.Left, first);

        // (3) Ctrl+E from the preview; a Meme added, its text in edit; Ctrl+V there with words, then with a picture.
        Assert.AreEqual(KeyAction.Edit, await listed.Press(Key.E, KeyModifiers.Control, KeyPlace.Preview));
        Assert.AreEqual(first.Id, listed.Editing.Session?.Item.Id, "the editor opens on the item shown (G20)");
        var session = listed.Editing.Session!;
        session.Text().Choose(TextKind.Meme);
        session.Click(session.OriginalSize.Width / 2f, session.OriginalSize.Height / 4f);
        Assert.IsTrue(session is { Typing: true, SelectedLayer: TextLayer { Preset: TextPreset.Meme } }, "a Meme added, its text in edit");
        listed.LayerTextCaret = TestLayerText.Over(session);
        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false, "so true"));

        Assert.AreEqual(KeyAction.Paste, await listed.Press(Key.V, KeyModifiers.Control, KeyPlace.Field));
        listed.Keys.Released(Key.V);

        Assert.AreEqual("SO TRUE", ((TextLayer)session.SelectedLayer!).Text, "the words in the layer as typed on a Meme (G13, G21)");
        Assert.HasCount(3, await listed.Stash.All(), "no item added");
        Assert.IsEmpty(listed.Notices.Shown, "and nothing said");

        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false));
        Assert.AreEqual(KeyAction.Paste, await listed.Press(Key.V, KeyModifiers.Control, KeyPlace.Field));
        listed.Keys.Released(Key.V);

        Assert.HasCount(3, await listed.Stash.All(), "a picture there adds no item (G13)");
        Assert.AreEqual("Paste failed: the clipboard, it holds no text to paste", listed.Notices.Shown.Single().Text, "the notice says so");
        Assert.AreEqual("SO TRUE", ((TextLayer)session.SelectedLayer!).Text);
    }

    /// <summary>
    /// A first run through the window's parts over a real, empty store with default settings, the empty stash read from
    /// its view as the window draws it: (1) the listing is the empty stash, whose view lists the ways in, in order, each
    /// with its shortcut as the settings read it, then the line naming the open shortcut, the tray and that closing the
    /// window keeps ImageStash running, and nothing has read the clipboard (G2, G3, N9); (2) the Stash clipboard image row
    /// reads F5's default, Win+Alt+V, the tray's tooltip names the open shortcut, and once the open shortcut is remapped
    /// as a Settings field remaps it, the tooltip and the empty stash's line, drawn again, both read the new one (F5,
    /// G33, G34); (3) the Run entry the start writes carries an argument, and through the start's own dispatch over this
    /// stash a start given that entry's arguments asks for the pinned window on this first run and for none on a later
    /// one, while a start given none asks for the pinned window once on this first run and for the tray's summon on a
    /// later one (G2, G3, State: Settings); (4) with a picture on the clipboard, a click on the Paste row adds one item,
    /// and the grid takes the empty stash's place (G13, G2). The paste stays the walk's last act.
    /// </summary>
    [TestMethod]
    public async Task AFirstRunsEmptyStashNamesTheOpenShortcutAndItsPasteRowAddsTheClipboardsPicture()
    {
        using var listed = await ListedStash.Open();
        var shown = EmptyStashShown.Over(listed);
        var shortcuts = listed.Settings.Current.Shortcuts;

        // (1) The first run's empty stash.
        Assert.IsTrue(listed.Listing.IsEmptyStash, "a first run lists the empty stash");
        Assert.IsTrue(shown.Showing, "and the window shows it in the grid's place (G2)");
        var rows = shown.ReadRows();
        CollectionAssert.AreEqual(new[] { "Paste a copied image", "Take a screenshot", "Record a clip", "Stash the clipboard image", "Import files" }, rows.Select(way => way.Words).ToList(), "every way in, in order");
        CollectionAssert.AreEqual(new[] { "Ctrl+V", shortcuts.Screenshot, shortcuts.Record, shortcuts.StashClipboardImage, "Ctrl+O" }, rows.Select(way => string.Join("+", way.Caps)).ToList(),
            "each with its shortcut as the settings read it, in key caps");
        var line = shown.ReadLines()[^1];
        StringAssert.Contains(line, $"from any app with {shortcuts.Open}", "the line names the open shortcut (G1)");
        StringAssert.Contains(line, "from its icon in the tray", "and the tray (G34)");
        StringAssert.Contains(line, "Closing this window keeps it running", "and that closing keeps ImageStash running (G3)");
        Assert.AreEqual((0, 0), (listed.Clipboard.Reads, listed.Clipboard.ImageReads), "nothing has read the clipboard (N9)");

        // (2) The new default on its row; the tray's tooltip, over the shortcuts started, names the open shortcut; a remap
        // written through the settings' keeper reaches the tooltip and the line at once.
        Assert.AreEqual("Win+Alt+V", string.Join("+", rows[3].Caps), "Stash the clipboard image reads F5's default");
        var tray = new RecordingTray();
        new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }).Show(listed.Shortcuts);
        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", tray.ToolTip, "pointing at the icon names the open shortcut");
        using (listed.Shortcuts.Suspend())
        {
            Assert.IsTrue(listed.Shortcuts.Rebind(ShortcutAct.Open, Shortcut.Parse("Ctrl+Alt+F9").Value!).Succeeded);
        }

        Assert.AreEqual("Ctrl+Alt+F9", listed.Settings.Current.Shortcuts.Open, "remapped through the settings' keeper");
        Assert.AreEqual("ImageStash · Ctrl+Alt+F9", tray.ToolTip, "the tooltip reads the new one at once");
        StringAssert.Contains(shown.ReadLines()[^1], "from any app with Ctrl+Alt+F9", "and so does the empty stash's line, drawn again as the window draws it");
        Assert.AreEqual((0, 0), (listed.Clipboard.Reads, listed.Clipboard.ImageReads), "still nothing has read the clipboard (N9)");

        // (3) The Run entry as the start writes it; that entry's arguments, and none, given to the start's dispatch.
        var run = new RecordingStartWithWindows();
        Assert.IsTrue(App.ApplyStartWithWindows(new SettingsLoad(listed.Settings.Current, null, AsSaved: true), run, @"C:\Program Files\ImageStash\ImageStash.Windows.exe").Succeeded);
        Assert.IsNotNull(run.Argument, "the Run entry carries the argument that marks the start at sign-in (State: Settings)");
        string[] atSignIn = [run.Argument];
        var firstRun = new SettingsLoad(listed.Settings.Current, null, AsSaved: true, FirstRun: true);
        var later = firstRun with { FirstRun = false };
        Func<string, Task<StoreOpening>> thisStash = _ => Task.FromResult(new StoreOpening(listed.Listing.Store, []));
        CollectionAssert.AreEqual(new[] { StartShows.Pinned }, (await StartShows.Run(atSignIn, firstRun, listed.Settings, listed.Stash, thisStash)).Shows,
            "signed in on this first run: pinned, over its empty stash (G2, G3)");
        Assert.IsEmpty((await StartShows.Run(atSignIn, later, listed.Settings, listed.Stash, thisStash)).Shows, "signed in on a later run: no window (G3)");
        CollectionAssert.AreEqual(new[] { StartShows.Pinned }, (await StartShows.Run([], firstRun, listed.Settings, listed.Stash, thisStash)).Shows,
            "launched by hand on this first run: pinned, once (G2, G3)");
        CollectionAssert.AreEqual(new[] { StartShows.Tray }, (await StartShows.Run([], later, listed.Settings, listed.Stash, thisStash)).Shows,
            "launched by hand on a later run: as a tray click shows it (G3)");
        Assert.IsTrue(listed.Listing.IsEmptyStash, "still the empty stash");

        // (4) A picture on the clipboard; a click on the Paste row.
        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false));
        shown.Click("Paste a copied image");
        await listed.WayInRunning;

        Assert.AreEqual(1, listed.Clipboard.Reads, "the row's act read the clipboard once (G13, N9)");
        Assert.HasCount(1, await listed.Stash.All(), "one item added");
        Assert.IsFalse(listed.Listing.IsEmptyStash, "the listing is no longer the empty stash");
        shown.ShowListing();
        Assert.IsFalse(shown.Showing, "the grid takes the empty stash's place (G2)");
    }

    [TestMethod]
    public async Task CollectingIntoAStashThatHoldsPicturesTagsEveryDroppedPictureAndItsNoticeSaysWhatItDid()
    {
        using var listed = await ListedStash.Open();
        var held = await listed.Stash.ImportFixture("held", "still.png");
        var carrying = await listed.Stash.ImportFixture("carrying", "photo.jpg");
        listed.Stash.Tag(carrying.Id, "cat");

        // (1) With the chip #cat lit, as its click lights it, a drop of one new picture and one the stash already holds.
        await listed.Listing.SetQuery(ItemSearch.WithWord(listed.Listing.Query, "cat"));
        CollectionAssert.AreEqual(new[] { "cat" }, listed.Listing.LitTags.ToList(), "the chip is lit");
        var drop = ListedStash.Contents([MediaFixtures.Path("transparent.png"), MediaFixtures.Path("still.png")]);
        Assert.AreEqual("Each picture gets the tag #cat, those already in your stash too.", DropCard.Of(drop, listed.Listing.LitTags).Lines[1], "the drop card says so before release (G11)");
        var dropped = await listed.Import.Drop(drop);

        var added = dropped.Single(file => file.Result == ImportResultKind.Added).Item!;
        var stash = await listed.Stash.All();
        Assert.HasCount(3, stash, "one item added, the other already in the stash (G12)");
        CollectionAssert.AreEqual(new[] { "cat" }, stash.Single(item => item.Id == added.Id).Tags.ToList(), "the new item carries the tag");
        CollectionAssert.AreEqual(new[] { "cat" }, stash.Single(item => item.Id == held.Id).Tags.ToList(), "and so does the one the stash already held (G11)");
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual(
            ("Added 1, tagged #cat · 1 already in your stash", "still.png was already in your stash as “held”, now tagged #cat. Added “transparent”."),
            (notice.Heading, notice.Text),
            "it counts one added and one already in the stash, names that one, and says each got the tag (G12)");

        await listed.Notices.Undo(notice);

        stash = await listed.Stash.All();
        Assert.IsFalse(stash.Any(item => item.Id == added.Id), "its Undo removes the added item");
        Assert.IsEmpty(stash.Single(item => item.Id == held.Id).Tags, "and takes the tag off the other (G12)");
        CollectionAssert.AreEqual(new[] { "cat" }, stash.Single(item => item.Id == carrying.Id).Tags.ToList(), "leaving what the batch did not give");

        // (2) A drop whose new picture the walk holds back while it types a search the picture's name matches too and
        // moves the selection.
        listed.Stash.Tag(held.Id, "pet");
        listed.Stash.Tag(carrying.Id, "pet");
        var arriving = new HeldBackFile(MediaFixtures.Path("transparent.png"), "pet three.png");
        var running = listed.Import.Drop(arriving.Drop);
        await listed.Listing.SetQuery("pet");
        Assert.HasCount(2, listed.Listing.Items);
        listed.Listing.MoveFocus(Direction.Right, []);
        var moved = listed.Listing.Selection.Single();
        Assert.AreEqual(listed.Listing.Items[1].Id, moved, "the selection moved off the first match");
        arriving.Release();
        var third = (await running).Single().Item!;

        Assert.AreEqual("pet", listed.Listing.Query, "the search as the walk left it");
        CollectionAssert.AreEqual(new[] { moved }, listed.Listing.Selection.ToList(), "and the selection");
        CollectionAssert.Contains(listed.Listing.Items.Select(item => item.Id).ToList(), third.Id, "the new picture listed among the matches (G5)");
        var kept = listed.Notices.Shown[^1];
        Assert.AreEqual(ImportEnd.Show, kept.Button?.Label, "the notice offers Show (G12)");

        await NoticeButton.Press(listed.Notices, kept);

        Assert.AreEqual(("", Scope.All), (listed.Listing.Query, listed.Listing.LitChip), "Show clears the search and shows All");
        Assert.AreEqual(third.Id, listed.Listing.Items[0].Id, "the new item first");
        CollectionAssert.AreEqual(new[] { third.Id }, listed.Listing.Selection.ToList(), "and selected");

        // (3) A drop of a JPEG named .jfif, a PNG whose name has no extension and a text file whose name has none.
        var named = ListedStash.Contents(
        [
            listed.Stash.Source("from a browser.jfif", MediaFixtures.Bytes("progressive.jpg")),
            listed.Stash.Source("nameless", MediaFixtures.Bytes("translucent.png")),
            listed.Stash.Source("LICENSE", "MIT License"u8.ToArray()),
        ]);
        var card = DropCard.Of(named, listed.Listing.LitTags);
        Assert.AreEqual((1, 2, 0), (card.Supported, card.ToCheck, card.Skipped), "the drop card counts one supported and two to check (G11)");
        var brought = await listed.Import.Drop(named);

        CollectionAssert.AreEqual(new[] { ImportResultKind.Added, ImportResultKind.Added, ImportResultKind.Skipped }, brought.Select(file => file.Result).ToList());
        CollectionAssert.AreEqual(new[] { "from a browser.jpg", "nameless.png" }, brought.Take(2).Select(file => file.Item!.FileName).ToList(), "two items added, stored ending .jpg and .png (F3)");
        Assert.AreEqual("a file without an extension", brought[2].Type, "the text file skipped and named as a file without an extension (G12)");
        var namedNotice = listed.Notices.Shown[^1];
        Assert.AreEqual(("Added 2 · 1 skipped", "Skipped LICENSE (a file without an extension): ImageStash keeps GIF, PNG, JPG and WebP. Added “from a browser” and “nameless”."),
            (namedNotice.Heading, namedNotice.Text));
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task CaptureToPasteAScreenshotEditedFromItsNotificationGoesIntoTheChatItWasMadeOverWithOneEnter()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var used = await listed.Stash.ImportFixture("used", "still.png");
        Assert.IsTrue((await listed.Handoff.Copy(null!, [used])).Succeeded);
        var usesBefore = listed.Uses();
        listed.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new Avalonia.PixelRect(100, 50, 30, 20));
        listed.Screen.Paint(RecordingScreen.Primary, seed: 5);
        var chat = RecordingTarget.App("Chat");
        listed.Target.InFront = new ForegroundReading(false, chat);
        listed.Target.Before = chat;

        // (1) A screenshot over the chat, then its notification's Edit, whose click brings the notification in front: the
        // window it opens comes in front from the notification, so the window before the stash window is the notification.
        await listed.Screenshot.Take();
        var shot = (await listed.Stash.All()).Single(item => item.Id != used.Id);
        var original = File.ReadAllBytes(shot.FilePath);
        var notice = listed.Notifications.Shown.Single();
        Assert.IsNotNull(notice.Card, "drawn as the capture card, with its Edit");

        // (2) That card, read while it shows, before the Edit's click dismisses it: titled as a screenshot in the stash, and
        // on one line that it is on the clipboard, that Ctrl+V pastes it, and the saved file's measured size (G16, G24).
        var card = listed.CaptureNotification.WordsOf(notice)!;
        await Handing.Eventually(() => card.Size is not null);
        Assert.AreEqual(("Screenshot stashed", $"On the clipboard · Ctrl+V pastes it · {FileSize.Words(new FileInfo(shot.FilePath).Length)}", "Edit"),
            (card.Title, card.Line, card.EditLabel));

        listed.Target.InFront = new ForegroundReading(true, null);
        listed.Target.BeforeHeld = chat;
        listed.Target.Before = null;
        listed.CaptureNotification.Open(new CaptureOpening(shot, new CaptureEdit(shot, Trim: false), NotificationInFront: true));
        listed.Notifications.Close(notice);
        await Handing.Eventually(() => listed.Editing.Session?.Item.Id == shot.Id);

        Assert.AreEqual(chat, listed.PasteTarget.Current, "the window the notification opened names the chat (G8)");
        CollectionAssert.AreEqual(new[] { "Enter · Paste into Chat", "Ctrl+C · Copy" }, listed.FooterHints.Texts.ToList());

        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((2, 2), (20, 14));
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);

        Assert.IsEmpty(listed.Target.Pasted, "Save sends nothing to any window (N3)");
        Assert.IsFalse(listed.Target.Steps.Any(step => step.StartsWith("activate", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(new[] { shot.Id }, listed.Listing.Selection.ToList(), "the window stays open with the item selected (G26)");
        Assert.AreEqual(chat, listed.PasteTarget.Current, "still the chat after Save");
        var saved = listed.Listing.Items.Single(item => item.Id == shot.Id);
        Assert.AreEqual(1, saved.RenderedRevision, "listed as saved");

        var enter = listed.Press(Key.Enter, KeyModifiers.None, KeyPlace.SearchBox);
        listed.Keys.Released(Key.Enter);
        Assert.AreEqual(KeyAction.Handoff, await enter);

        Assert.AreEqual(chat, listed.Target.Pasted.Single().Target, "Enter pastes into the chat (G8)");
        var handed = listed.Clipboard.Written[^1].Content;
        CollectionAssert.AreEqual(new[] { saved.FilePath }, handed.FilePaths.ToList(), "the saved finished picture");
        CollectionAssert.AreEqual(File.ReadAllBytes(saved.FilePath), handed.Png);
        CollectionAssert.AreNotEqual(original, handed.Png, "rendered with its Hide box, not the screenshot as taken");
        Assert.AreEqual(usesBefore + 1, listed.Uses(), "one use recorded (G10)");
        Assert.IsEmpty(listed.Failures);

        // (2) A clip's card, the clip recorded with Also copy on, in a stash of its own on the recorder's stepped clock until
        // the walk records its clip in this stash: its line leads with the clip's length, and it offers Trim (G16).
        using var recording = await ListedStash.OpenRecording();
        recording.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new Avalonia.PixelRect(100, 50, 32, 24));
        MediaFrame[] pictures = [recording.Screen.Paint(RecordingScreen.Primary, seed: 1), recording.Screen.Paint(RecordingScreen.Primary, seed: 2)];
        recording.Screen.EachGrab = index => Outcome<MediaFrame>.Of(pictures[index % 2]);
        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        // Four samples, at 0, 70, 140 and 210 ms, then Stop at 280 ms.
        recording.StepRecording(210);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));
        var clip = (await recording.Stash.All()).Single();
        var clipCard = recording.CaptureNotification.WordsOf(recording.Notifications.Shown.Single())!;
        await Handing.Eventually(() => clipCard.Size is not null);
        Assert.AreEqual(("Recording stashed", $"0.3 s · On the clipboard · Ctrl+V pastes it · {FileSize.Words(new FileInfo(clip.FilePath).Length)}", "Trim"),
            (clipCard.Title, clipCard.Line, clipCard.EditLabel));
        Assert.IsEmpty(recording.Failures);
    }

    /// <summary>An arrow pressed in the preview as the window takes it; then the item shown, and the grid's one selected item, read.</summary>
    private static async Task PressInThePreview(ListedStash listed, Key key, StashItem expected)
    {
        Assert.AreEqual(key == Key.Right ? KeyAction.StepOn : KeyAction.StepBack, await listed.Press(key, KeyModifiers.None, KeyPlace.Preview));
        Assert.AreEqual(expected.Id, listed.PreviewItem?.Id, $"{key} leaves {expected.Name} shown");
        CollectionAssert.AreEqual(new[] { expected.Id }, listed.Listing.Selection.ToList(), "the grid selecting it alone (G8)");
    }

    /// <summary><c>Ctrl+Z</c> pressed in the grid and let go, as the window takes it.</summary>
    private static async Task PressCtrlZ(ListedStash listed)
    {
        Assert.AreEqual(KeyAction.UndoNotice, await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid));
        listed.Keys.Released(Key.Z);
    }

    /// <summary>Time passing, the sweep run as the window's timer runs it.</summary>
    private static void Pass(ListedStash listed, TimeSpan time)
    {
        listed.Stash.Clock.Now += time;
        listed.Notices.Sweep();
    }
}
