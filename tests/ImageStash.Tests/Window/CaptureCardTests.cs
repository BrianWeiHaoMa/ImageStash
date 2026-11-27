using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The capture card's name kept as Enter keeps it on every way out of its field but Esc (G16, G17): another window
/// taking focus, the card's ×, and its Edit, Trim or a click elsewhere, which ask only once the item has its new name;
/// Esc keeping nothing, even as the focus it gives back leaves; a name left unchanged changing and saying nothing; an
/// emptied name doing what Enter does with it; Undo removing the item with nothing kept and no failure named; a refusal
/// on leaving named above the tray as after Enter (G35). Another window taking focus is the card's own deactivation, as
/// the window would raise it.
/// </summary>
[TestClass]
public sealed class CaptureCardTests
{
    [TestMethod]
    public async Task AnotherWindowTakingFocusKeepsTheTypedNameAsEnterWould()
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        OnControls(async () =>
        {
            var card = Raise(listed, shot);
            Type(card, "cat facepalm");

            card.WindowDeactivated();

            Assert.IsFalse(card.Field.IsEditing, "the edit ended");
            await Handing.Eventually(() => card.Field.Shown == "cat facepalm");
            Assert.AreEqual("cat facepalm", (await Stored(listed, shot)).Name, "renamed through the store, as Enter renames it");
            Assert.IsEmpty(listed.Notifications.Shown.Where(notice => notice != Notice(listed)).ToList(), "success says nothing more");
        });
    }

    [TestMethod]
    [DataRow("Edit")]
    [DataRow("Trim")]
    [DataRow("a click elsewhere")]
    public async Task EditTrimOrAClickElsewhereRenameFirstSoTheWindowOpensOnTheNewName(string click)
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        var opened = new List<CaptureOpening>();
        listed.CaptureNotification.OpenRequested += opened.Add;
        OnControls(async () =>
        {
            var card = Raise(listed, shot, click == "Trim" ? CaptureKind.Recording : CaptureKind.Screenshot);
            Type(card, "cat facepalm");

            if (click == "a click elsewhere")
            {
                card.RaiseEvent(CaptureCardPresses.Body(card));
            }
            else
            {
                Click(card, click);
            }

            await Handing.Eventually(() => opened.Count == 1);
            Assert.AreEqual("cat facepalm", opened[0].Item.Name, "asked on the item under its new name");
            Assert.AreEqual("cat facepalm", (await Stored(listed, shot)).Name);
            Assert.AreEqual(click == "a click elsewhere" ? null : click == "Trim", opened[0].Edit?.Trim);
            if (click != "a click elsewhere")
            {
                await Handing.Eventually(() => listed.Editing.Session is not null);
                Assert.AreEqual("cat facepalm", listed.Editing.Session!.Item.Name, "the editor's head names the item by its new name");
            }
            else
            {
                await Handing.Eventually(() => listed.Listing.Selection.SequenceEqual([shot.Id]));
                Assert.AreEqual("cat facepalm", listed.Listing.Items.Single(item => item.Id == shot.Id).Name, "the window selects it under its new name");
            }

            Assert.DoesNotContain(Notice(listed, required: false), listed.Notifications.Shown.ToList(), "the card goes");
            listed.Editing.Cancel();
        });
    }

    [TestMethod]
    public async Task TheXKeepsTheTypedNameAndTheCardGoes()
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        OnControls(async () =>
        {
            var card = Raise(listed, shot);
            var notice = Notice(listed);
            Type(card, "kept by x");

            Click(card, "Close");

            Assert.DoesNotContain(notice, listed.Notifications.Shown.ToList());
            await Handing.Eventually(() => listed.Listing.Items.Any(item => item.Name == "kept by x"));
            Assert.AreEqual("kept by x", (await Stored(listed, shot)).Name);
        });
    }

    [TestMethod]
    public async Task EscThenTheFocusItGivesBackLeavingKeepsNothing()
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        OnControls(async () =>
        {
            var card = Raise(listed, shot);
            Type(card, "abandoned");

            NameBox(card).RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
            card.WindowDeactivated();
            Click(card, "Close");
            await listed.Listing.Refresh();

            Assert.AreEqual("Screenshot 1", (await Stored(listed, shot)).Name, "the name as recorded");
            Assert.IsEmpty(listed.Notifications.Shown, "nothing renamed, nothing refused");
        });
    }

    [TestMethod]
    public async Task LeavingWithTheNameUnchangedChangesNothingAndSaysNothing()
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        var asked = 0;
        listed.CaptureNotification.Renaming.Changed += () => asked++;
        OnControls(async () =>
        {
            var card = Raise(listed, shot);
            card.Field.Edit();

            card.WindowDeactivated();
            Click(card, "Close");
            await listed.Listing.Refresh();

            Assert.IsFalse(card.Field.IsEditing);
            Assert.AreEqual(0, asked, "no rename asked of the store");
            Assert.IsEmpty(listed.Notifications.Shown, "nothing said");
            Assert.AreEqual("Screenshot 1", (await Stored(listed, shot)).Name);
        });
    }

    [TestMethod]
    public async Task AnEmptiedNameDoesOnLeavingWhatEnterDoesWithIt()
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        OnControls(async () =>
        {
            var card = Raise(listed, shot);
            Type(card, "");

            card.WindowDeactivated();

            await Handing.Eventually(() => card.Field.Shown == "");
            Assert.AreEqual("", (await Stored(listed, shot)).Name, "nothing requires a name (G17)");
        });
    }

    [TestMethod]
    public async Task UndoWithTextTypedRemovesTheItemKeepingNothingAndNamingNoFailure()
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        OnControls(async () =>
        {
            var card = Raise(listed, shot);
            Type(card, "going anyway");

            Click(card, "Undo");
            card.WindowDeactivated();
            await Handing.Eventually(() => listed.Listing.Items.Count == 0);
            await listed.Listing.Refresh();

            Assert.IsEmpty(await listed.Stash.All());
            Assert.IsEmpty(listed.Notifications.Shown, "the card went and no failure is named");
        });
    }

    [TestMethod]
    public async Task ARefusedRenameOnLeavingIsNamedAboveTheTrayAsAfterEnter()
    {
        using var listed = await ListedStash.Open();
        var shot = await listed.Stash.ImportFixture("Screenshot 1", "still.png");
        OnControls(async () =>
        {
            var card = Raise(listed, shot);
            Assert.IsTrue((await listed.Stash.Store!.Remove([shot.Id]))[0].Succeeded);
            Type(card, "too late");

            card.WindowDeactivated();

            await Handing.Eventually(() => listed.Notifications.Shown.Count == 2);
            var refusal = listed.Notifications.Shown.Last();
            StringAssert.Contains(refusal.Text, "is no longer in the stash", "named as Enter's refusal is (G35)");
        });
    }

    private static CaptureCard Raise(ListedStash listed, StashItem item, CaptureKind kind = CaptureKind.Screenshot)
    {
        listed.CaptureNotification.Raise(item, kind, onClipboard: false, lengthMs: kind == CaptureKind.Recording ? 1000 : null);
        var notice = listed.Notifications.Shown.Last();
        return (CaptureCard)notice.Card!(notice, listed.Notifications);
    }

    private static Notice Notice(ListedStash listed, bool required = true) =>
        required ? listed.Notifications.Shown.First() : listed.Notifications.Shown.FirstOrDefault()!;

    /// <summary>The name put in edit, as the name's click does, and <paramref name="text"/> typed over it.</summary>
    private static void Type(CaptureCard card, string text)
    {
        card.Field.Edit();
        NameBox(card).Text = text;
    }

    private static TextBox NameBox(CaptureCard card) => card.Field.GetLogicalDescendants().OfType<TextBox>().Single();

    private static void Click(CaptureCard card, string label) =>
        card.GetLogicalDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, label) || Equals(Avalonia.Automation.AutomationProperties.GetName(button), label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void OnControls(Func<Task> act) => ControlThread.Run(() => ControlThread.FinishOnDispatcher(act));

    private static async Task<StashItem> Stored(ListedStash listed, StashItem item) =>
        (await listed.Stash.All()).Single(row => row.Id == item.Id);
}
