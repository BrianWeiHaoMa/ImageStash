using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The capture notification's entry (G16): one notice of the notifications with Undo, its words for each capture (G13:
/// a stashed clipboard image in the stash with the still-only sentence and nothing of the clipboard; on the clipboard
/// and a length only when given), an item without a name named by its file; its card's words as one rule, titled by
/// kind in the stash, one line of a length, the clipboard and Ctrl+V only when on it, and the size measured from the
/// saved file, a changed picture's own length and size (G24); its Edit, Trim for a recording, a click
/// elsewhere on it and the name the clicks that take the activation; a click elsewhere showing its item selected on All,
/// the search cleared, and an item gone meanwhile changing nothing; and its Undo by identity through Store, to the
/// Recycle Bin, at most once, a renamed item included (G29). A card's Edit, Trim or click elsewhere as the window follows
/// it (G8, G16): when it brought the notification in front, the window it opens names the window that had focus just
/// before the notification, heard of late or at once, none when that was ImageStash's own; when the platform refused
/// that activation, the window it gained focus from.
/// </summary>
[TestClass]
public sealed class CaptureNotificationTests
{
    [TestMethod]
    public async Task EachCaptureSaysItIsInTheStashAndOnlyWhatItWasGiven()
    {
        using var raising = await ListedStash.Open();
        var cat = await raising.Stash.Import("cat");
        var nameless = await raising.Stash.Import("");
        var captured = raising.CaptureNotification;

        captured.Raise(cat, CaptureKind.StashedClipboardImage, onClipboard: false, lengthMs: null);
        captured.Raise(cat, CaptureKind.Screenshot, onClipboard: true, lengthMs: null);
        captured.Raise(cat, CaptureKind.Recording, onClipboard: false, lengthMs: 5000);
        captured.Raise(nameless, CaptureKind.Screenshot, onClipboard: false, lengthMs: null);

        // G16 "says it is in the stash (and on the clipboard when it is)", "for a recording, with its length": the notice's
        // own words, the notification's title, say what its card says, titled by what was captured.
        CollectionAssert.AreEqual(
            new[]
            {
                $"Clipboard image stashed: “cat”. {Import.StillOnly}",
                "Screenshot stashed: “cat”. On the clipboard · Ctrl+V pastes it.",
                "Recording stashed: “cat”. 5.0 s.",
                $"Screenshot stashed: {nameless.FileName}.",
            },
            raising.Notifications.Shown.Select(notice => notice.Text).ToList());
        Assert.IsTrue(raising.Notifications.Shown.All(notice => notice.UndoLabel == "Undo"));
        Assert.IsTrue(raising.Notifications.Shown.All(notice => notice.Card is not null), "each drawn as the capture card");
        Assert.IsNull(raising.Notifications.Raise("Copied · press Ctrl+V").Card, "a plain notice as the notice card");
        Assert.IsEmpty(raising.Notices.Shown, "above the tray, never in the window");
    }

    [TestMethod]
    public void OnlyTheCaptureCardsNameEditAndClickElsewhereTakeTheActivation()
    {
        var callers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType == typeof(IQuietWindows) && called.Name == nameof(IQuietWindows.TakeActivation)))
            .Select(method => $"{ProductCalls.Outermost(method.DeclaringType!).FullName}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(new[] { "ImageStash.Window.CaptureCard.AskInFront", "ImageStash.Window.CaptureCard.NamePressed" }, callers,
            "the clicks that activate a notification, the user's act (G16)");
    }

    [TestMethod]
    public async Task AClickElsewhereShowsTheItemSelectedOnAllWithTheSearchClearedThePreviewClosedFirst()
    {
        using var raising = await ListedStash.Open();
        var cat = await raising.Stash.Import("cat");
        await raising.Stash.Import("dog");
        await raising.Listing.ShowScope(Scope.Favorites);
        await raising.Listing.SetQuery("dog");
        var closed = 0;

        await raising.CaptureNotification.ShowOnAll(cat, () => closed++);

        Assert.AreEqual(1, closed, "the preview closed, so the grid shows the item");
        Assert.AreEqual(Scope.All, raising.Listing.Scope, "All, where every item is listed (G16, G12)");
        Assert.AreEqual("", raising.Listing.Query, "the search cleared");
        CollectionAssert.AreEqual(new[] { cat.Id }, raising.Listing.Selection.ToList(), "that item selected");
        Assert.AreEqual(cat.Id, raising.Listing.Focused, "and focused");
    }

    [TestMethod]
    public async Task AClickElsewhereOnTheCardOfAnItemGoneMeanwhileLeavesTheWindowAsItWasLeft()
    {
        using var raising = await ListedStash.Open();
        var cat = await raising.Stash.Import("cat");
        var dog = await raising.Stash.Import("dog");
        await raising.Listing.ShowScope(Scope.All);
        await raising.Listing.SetQuery("dog");
        await raising.Listing.Settle();
        Assert.IsTrue((await raising.Stash.Store!.Remove([cat.Id]))[0].Succeeded);
        var closed = 0;

        await raising.CaptureNotification.ShowOnAll(cat, () => closed++);

        Assert.AreEqual(0, closed, "nothing closes for an item that is gone");
        Assert.AreEqual("dog", raising.Listing.Query, "the window opens as the tray opens it, the search as it was left (G1)");
        CollectionAssert.AreEqual(new[] { dog.Id }, raising.Listing.Selection.ToList(), "and its selection");
        Assert.IsEmpty(raising.Notices.Shown, "nothing refused");
        Assert.IsEmpty(raising.Notifications.Shown);
    }

    [TestMethod]
    public async Task EditTrimOrAClickThatBroughtTheNotificationInFrontOpenTheWindowNamingTheAppBeforeTheNotification()
    {
        // G8: "so does the stash window a click on a notification opens (G16): that click sets the paste target as the
        // notification gains focus, to the window that had focus just before the notification".
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var chat = RecordingTarget.App("Chat");
        // The stash window comes in front from the notification, so the window before it is the notification.
        listed.Target.Before = null;
        listed.Target.BeforeHeld = chat;
        listed.Target.InFront = new ForegroundReading(true, null);

        foreach (var asked in new[] { new CaptureOpening(cat, new CaptureEdit(cat, Trim: false), true), new CaptureOpening(cat, new CaptureEdit(cat, Trim: true), true), new CaptureOpening(cat, null, true) })
        {
            listed.PasteTarget.None();
            listed.CaptureNotification.Open(asked);
            await Handing.Eventually(() => asked.Edit is null ? listed.Listing.Selection.SequenceEqual([cat.Id]) : listed.Editing.Session?.Item.Id == cat.Id);

            Assert.AreEqual(chat, listed.PasteTarget.Current, $"{asked.Edit?.Trim switch { true => "Trim", false => "Edit", null => "a click elsewhere" }}: the chat the person came from");
            CollectionAssert.AreEqual(new[] { "Enter · Paste into Chat", "Ctrl+C · Copy" }, listed.FooterHints.Texts.ToList());
            listed.Editing.Cancel();
        }

        Assert.AreEqual(3, listed.Target.Steps.Count(step => step == "hold before mine"), "each read as its notification gained focus");
        Assert.IsEmpty(listed.Target.Pasted, "nothing is pasted without Enter (N3)");
    }

    [TestMethod]
    public async Task AClickWhoseReportIsHeardOnlyOnceTheWindowIsInFrontIsNoneUntilThenTheChat()
    {
        // G8: "it is none until it hears, and then that window", heard once the window is in front and the card has gone.
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        listed.CaptureNotification.Raise(cat, CaptureKind.Screenshot, onClipboard: true, lengthMs: null);
        listed.Target.Before = null;
        listed.Target.BeforeHeld = null;
        listed.Target.InFront = new ForegroundReading(true, null);

        listed.CaptureNotification.Open(new CaptureOpening(cat, new CaptureEdit(cat, Trim: false), true));
        await Handing.Eventually(() => listed.Editing.Session?.Item.Id == cat.Id);
        listed.Notifications.Close(listed.Notifications.Shown.Single());
        Assert.IsNull(listed.PasteTarget.Current, "none until it hears");

        listed.Target.BeforeHeld = RecordingTarget.App("Chat");
        listed.Target.ReportLate();

        Assert.AreEqual(RecordingTarget.App("Chat"), listed.PasteTarget.Current, "then the window before the notification, not before the stash window");
    }

    [TestMethod]
    public async Task AClickFromImageStashsOwnWindowLeavesNoneAndOneAfterSwitchingAppsNamesTheSecond()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        listed.Target.InFront = new ForegroundReading(true, null);
        // Captured over one app, the earlier summon's target.
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.App("First")));

        // G8 "by the same rule": the pinned stash window or Settings in front before the notification.
        listed.Target.Before = RecordingTarget.App("First");
        listed.Target.BeforeHeld = null;
        listed.CaptureNotification.Open(new CaptureOpening(cat, null, true));
        await Handing.Eventually(() => listed.Listing.Selection.SequenceEqual([cat.Id]));

        Assert.IsNull(listed.PasteTarget.Current, "none: neither the earlier target nor the last other app");
        CollectionAssert.AreEqual(new[] { "Enter · Copy" }, listed.FooterHints.Texts.ToList());

        // G8 "the window that had focus just before": the person switched to a second app, then clicked Edit.
        listed.Target.BeforeHeld = RecordingTarget.App("Second");
        listed.CaptureNotification.Open(new CaptureOpening(cat, new CaptureEdit(cat, Trim: false), true));
        await Handing.Eventually(() => listed.Editing.IsOpen);

        Assert.AreEqual(RecordingTarget.App("Second"), listed.PasteTarget.Current);
    }

    [TestMethod]
    public async Task AClickWhoseActivationThePlatformRefusedStillOpensTheWindowNamingTheAppItGainedFocusFrom()
    {
        // G16: the window still opens; G8: its target is then the window it gained focus from, as the tray's summon reads it.
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        listed.Target.InFront = new ForegroundReading(true, null);
        listed.Target.Before = RecordingTarget.App("Chat");
        listed.Target.BeforeHeld = RecordingTarget.App("Not read");

        listed.CaptureNotification.Open(new CaptureOpening(cat, new CaptureEdit(cat, Trim: false), false));
        await Handing.Eventually(() => listed.Editing.IsOpen);

        Assert.AreEqual(RecordingTarget.App("Chat"), listed.PasteTarget.Current);
        Assert.DoesNotContain("hold before mine", listed.Target.Steps, "no notification gained focus");
    }

    [TestMethod]
    public void ARecordingsCardOffersTrimAndEveryOtherCaptureEdit()
    {
        Assert.AreEqual("Edit", CaptureNotification.EditLabel(CaptureKind.Screenshot));
        Assert.AreEqual("Edit", CaptureNotification.EditLabel(CaptureKind.StashedClipboardImage));
        Assert.AreEqual("Trim", CaptureNotification.EditLabel(CaptureKind.Recording), "a recording's, of one image too: the card names the act (G16)");
    }

    [TestMethod]
    public void EachKindsTitleNamesWhatWasCapturedAndThatItIsInTheStash()
    {
        // G16 "says it is in the stash": the title by kind says where, not only that it was saved.
        Assert.AreEqual("Screenshot stashed", CaptureNotification.Title(CaptureKind.Screenshot));
        Assert.AreEqual("Recording stashed", CaptureNotification.Title(CaptureKind.Recording));
        Assert.AreEqual("Clipboard image stashed", CaptureNotification.Title(CaptureKind.StashedClipboardImage));
    }

    [TestMethod]
    public void TheLineLeadsWithALengthSaysTheClipboardOnlyWhenOnItAndEndsWithTheSizeLeavingNoSeparatorHanging()
    {
        var size = FileSize.Words(1_153_434);
        // G16 "(and on the clipboard when it is)", "for a recording, with its length and size"; G24 the measured size.
        Assert.AreEqual($"2.4 s · On the clipboard · Ctrl+V pastes it · {size}", CaptureNotification.Line(2400, onClipboard: true, 1_153_434));
        Assert.AreEqual($"On the clipboard · Ctrl+V pastes it · {size}", CaptureNotification.Line(null, onClipboard: true, 1_153_434));
        Assert.AreEqual($"2.4 s · {size}", CaptureNotification.Line(2400, onClipboard: false, 1_153_434), "off the clipboard, no word of it or of Ctrl+V");
        Assert.AreEqual(size, CaptureNotification.Line(null, onClipboard: false, 1_153_434));
        Assert.AreEqual("2.4 s · On the clipboard · Ctrl+V pastes it", CaptureNotification.Line(2400, onClipboard: true, fileBytes: null), "before the size is measured");
        Assert.AreEqual("2.4 s", CaptureNotification.Line(2400, onClipboard: false, fileBytes: null));
        Assert.AreEqual("", CaptureNotification.Line(null, onClipboard: false, fileBytes: null));
        // G13 "the result says that an animated image is added by dragging its file in", on lines of its own.
        Assert.AreEqual(Import.StillOnly, CaptureNotification.Aside(CaptureKind.StashedClipboardImage));
        Assert.IsNull(CaptureNotification.Aside(CaptureKind.Screenshot));
        Assert.IsNull(CaptureNotification.Aside(CaptureKind.Recording));
    }

    [TestMethod]
    public async Task ACardsLineReadsWholeUntilTheSavedFilesSizeIsMeasuredAndThenEndsWithIt()
    {
        using var raising = await ListedStash.Open();
        var cat = await raising.Stash.ImportFixture("cat", "still.png");
        var notMeasured = cat with { FilePath = cat.FilePath + ".not there" };

        raising.CaptureNotification.Raise(notMeasured, CaptureKind.Screenshot, onClipboard: true, lengthMs: null);
        var waiting = raising.CaptureNotification.WordsOf(raising.Notifications.Shown.Single())!;
        await waiting.Measure(notMeasured.FilePath);
        Assert.AreEqual(("Screenshot stashed", "On the clipboard · Ctrl+V pastes it", (string?)null), (waiting.Title, waiting.Line, waiting.Size),
            "no size until one is measured, and no separator left hanging (G24)");
        Assert.AreEqual("Edit", waiting.EditLabel);

        raising.CaptureNotification.Raise(cat, CaptureKind.Screenshot, onClipboard: true, lengthMs: null);
        var measured = raising.CaptureNotification.WordsOf(raising.Notifications.Shown[^1])!;
        await Handing.Eventually(() => measured.Size is not null);
        Assert.AreEqual($"On the clipboard · Ctrl+V pastes it · {FileSize.Words(new FileInfo(cat.FilePath).Length)}", measured.Line, "the saved file's size, measured from it (G24)");
        Assert.IsNull(raising.CaptureNotification.WordsOf(raising.Notifications.Raise("Copied · press Ctrl+V")), "a plain notice has no card words");
    }

    [TestMethod]
    public async Task APictureChangedWhileTheCardShowsSaysItsOwnLengthAtOnceAndItsOwnSizeOnceMeasured()
    {
        using var raising = await ListedStash.Open();
        using var scratch = new ScratchFolder();
        var clip = await raising.Stash.ImportFixture("clip", "animated.gif");
        raising.CaptureNotification.Raise(clip, CaptureKind.Recording, onClipboard: true, lengthMs: 2400);
        var words = raising.CaptureNotification.WordsOf(raising.Notifications.Shown.Single())!;
        await Handing.Eventually(() => words.Size is not null);
        Assert.AreEqual($"2.4 s · On the clipboard · Ctrl+V pastes it · {FileSize.Words(new FileInfo(clip.FilePath).Length)}", words.Line);
        Assert.AreEqual("Trim", words.EditLabel);

        // A trim saved while the card shows: G16 "for a recording, with its length and size", both the picture's now shown.
        words.PictureChanged(new MediaFacts(ImageFormat.Gif, 32, 24, true, 3, 1200));
        Assert.AreEqual("1.2 s · On the clipboard · Ctrl+V pastes it", words.Line, "its own length at once, never beside the size before it");
        var trimmed = scratch.File("trimmed.gif");
        File.WriteAllBytes(trimmed, new byte[3000]);
        await words.Measure(trimmed);
        Assert.AreEqual($"1.2 s · On the clipboard · Ctrl+V pastes it · {FileSize.Words(3000)}", words.Line);

        // A measure of the picture before, answered after the next change, never shows.
        var before = words.Measure(clip.FilePath);
        words.PictureChanged(new MediaFacts(ImageFormat.Gif, 32, 24, false, 1, 0));
        await before;
        Assert.AreEqual(("On the clipboard · Ctrl+V pastes it", (string?)null), (words.Line, words.Size), "a still says no length, and no size until its own is measured");

        // A clip recorded as one image is a still to Media: an edit saved while its card shows keeps the length its
        // recorder closed it at (G16 "for a recording, with its length").
        var oneImage = await raising.Stash.ImportFixture("one image", "still.gif");
        Assert.IsFalse(oneImage.Facts.Animated);
        raising.CaptureNotification.Raise(oneImage, CaptureKind.Recording, onClipboard: false, lengthMs: 100);
        var oneImageWords = raising.CaptureNotification.WordsOf(raising.Notifications.Shown[^1])!;
        oneImageWords.PictureChanged(new MediaFacts(ImageFormat.Gif, 32, 24, false, 1, 0));
        Assert.AreEqual("0.1 s", oneImageWords.Line);
    }

    [TestMethod]
    public async Task UndoRemovesARenamedItemByIdentityToTheRecycleBinOnce()
    {
        using var raising = await ListedStash.Open();
        var cat = await raising.Stash.Import("cat");
        var kept = await raising.Stash.Import("kept");
        raising.CaptureNotification.Raise(cat, CaptureKind.StashedClipboardImage, false, null);
        var notice = raising.Notifications.Shown.Single();
        Assert.IsTrue((await raising.Renaming.Rename(cat, "dog")).Succeeded);
        var renamed = (await raising.Stash.All()).Single(item => item.Id == cat.Id);

        await raising.Notifications.Undo(notice);
        await raising.Notifications.Undo(notice);

        CollectionAssert.AreEqual(new[] { kept.Id }, (await raising.Stash.All()).Select(item => item.Id).ToList());
        CollectionAssert.AreEqual(new[] { renamed.FilePath }, raising.Stash.RecycleBin.Asked, "its file as renamed, once (G29)");
        Assert.IsFalse(raising.Listing.Items.Any(item => item.Id == cat.Id), "the listing refreshed");
        Assert.IsEmpty(raising.Notifications.Shown);
    }
}
