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
/// Recycle Bin, at most once, a renamed item included (G29).
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
