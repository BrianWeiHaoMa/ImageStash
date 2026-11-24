using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The capture notification's entry (G16): one notice of the notifications with Undo, its words for each capture (G13:
/// a stashed clipboard image in the stash with the still-only sentence and nothing of the clipboard; on the clipboard
/// and a length only when given), an item without a name named by its file; its Edit, Trim for a recording, a click
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

        CollectionAssert.AreEqual(
            new[]
            {
                $"Saved “cat” to your stash. {Import.StillOnly}",
                "Saved “cat” to your stash. It is on the clipboard too.",
                "Saved “cat” to your stash. It is 5.0 s long.",
                $"Saved {nameless.FileName} to your stash.",
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
