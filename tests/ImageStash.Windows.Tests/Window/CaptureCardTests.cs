using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.CaptureDesk;
using static ImageStash.Windows.Tests.Support.ShownWindow;
using static ImageStash.Windows.Tests.Support.TestMouse;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The capture notification's card above the tray as Windows shows it (G16, G24, G17): its picture, small, its words,
/// the saved file's size and the name; a click on its Undo or × leaving the chat in front with the keystrokes, and Undo
/// leaving the stash window active when it is in front; a click elsewhere on it opening the window on its item, the card
/// staying; a real click on the name activating it, the name selected and replaced by real typing, Enter renaming through the store and Esc leaving the
/// name as recorded, either giving the chat back its keystrokes; the notice held while the field has focus; a rename in
/// the window shown in the field, the picture bounded and read where its file now is, read again with its size once
/// another program's rewrite replaced it and once that was undone, and the size shown without the picture while another
/// program holds the file; every refusal named (G35). These checks show the notification and a
/// chat window of their own on the primary display, click them, type into them and move the pointer.
/// </summary>
[TestClass]
public sealed class CaptureCardTests
{
    private const ushort Enter = 0x0D;
    private const ushort Esc = 0x1B;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task TheActsCardShowsThePictureTheWordsTheSizeAndTheNameAndItsXLeavesTheChatItsKeystrokes()
    {
        using var desk = await CaptureDesk.Open();
        var act = TrayDesktop.Get(() => new StashClipboardImage(desk.Shown.Listing, desk.Notifications, desk.Capture, new WindowsClipboardAndDrops(),
            new StoppedClock(), HeldClipboard.Retry, HeldFileRetry.Duration));
        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(desk.Shown.Picture("stash card.png"))));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await TrayDesktop.Get(act.Stash);
        // Timed to the card shown above the tray with its picture and size, before the settle CardShown waits out.
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is { IsVisible: true } shown
            && shown.GetVisualDescendants().OfType<CaptureCard>().LastOrDefault() is { Picture: not null, Size: not null }));
        var took = clock.ElapsedMilliseconds;
        var card = await desk.CardShown();

        var item = desk.Items().Single();
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(item.Name, card.Field.Shown, "the name as the field");
            CollectionAssert.IsSubsetOf(new[] { CaptureCard.Title, Import.StillOnly, FileSize.Words(new FileInfo(item.FilePath).Length), CaptureCard.NameHint }, Texts(card));
            CollectionAssert.AreEqual(new[] { "Edit", "Undo" }, card.GetVisualDescendants().OfType<Button>().Select(button => button.Content).OfType<string>().ToList(), "Edit, then Undo (G16)");
            Assert.AreEqual(new PixelSize(6, 4), ((Bitmap)card.Picture!).PixelSize, "a small picture as it is, never enlarged");
        });
        // No line binds the card's arrival to a time (G1's second is the stash window's): its content above is the pin.
        TestContext.WriteLine($"the card with its picture and size, after the act: {took} ms");
        desk.ChatKeepsTheKeystrokes("after the card appeared");

        TestMouse.Click(Centre(TrayDesktop.Get(() => CloseButton(card))));
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is null));
        desk.ChatKeepsTheKeystrokes("after a click on ×");
        Assert.HasCount(1, desk.Items(), "× only closes the card");
    }

    [TestMethod]
    public async Task APressOnTheCardAsksForItsItemUnlessItIsOnItsFieldOrButtonsAndOnlyTheLeftButtonAsks()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("stash pressed.png"))).Single();
        var notifications = new Notices(new StoppedClock());
        var capture = TrayDesktop.Get(() => new CaptureNotification(notifications, shown.Listing, shown.Handoff, new RefusedActivation(), HeldFileRetry.Duration));
        var asked = new List<long>();
        var host = TrayDesktop.Get(() =>
        {
            capture.ShowRequested += (shownItem, dismiss) =>
            {
                asked.Add(shownItem.Id);
                dismiss();
            };
            capture.Raise(item, CaptureKind.Screenshot, onClipboard: false, lengthMs: null);
            var notice = notifications.Shown.Single();
            var window = new Avalonia.Controls.Window { Width = 460, Height = 220, ShowActivated = false, Position = new PixelPoint(-8000, 300), Content = notice.Card!(notice, notifications) };
            window.Show();
            return window;
        });
        try
        {
            void Pressed(Control on, MouseButton button = MouseButton.Left)
            {
                var (modifier, update) = button == MouseButton.Left ? (RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed) : (RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed);
                on.RaiseEvent(new PointerPressedEventArgs(on, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), host, new Point(2, 2), 0, new PointerPointProperties(modifier, update), KeyModifiers.None, 1));
            }

            TrayDesktop.Run(() =>
            {
                var card = (CaptureCard)host.Content!;
                Pressed(card);
                CollectionAssert.AreEqual(new[] { item.Id }, asked, "on its blank area (G16)");
                Assert.IsEmpty(notifications.Shown, "and the notification goes once the window says it shows the item (G16)");
                Pressed(card.GetVisualDescendants().OfType<Image>().Single());
                Assert.HasCount(2, asked, "on its picture");
                Pressed(card.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == CaptureCard.Title));
                Assert.HasCount(3, asked, "on its words");
                Pressed(card, MouseButton.Right);
                Assert.HasCount(3, asked, "only the left button asks");
                Pressed(NameButton(card));
                Assert.HasCount(3, asked, "the field keeps its own click");
                Pressed(NameBox(card));
                Assert.HasCount(3, asked, "and so does its box, in edit or not");
                foreach (var button in new[] { EditButton(card), UndoButton(card), CloseButton(card) })
                {
                    Pressed(button);
                    Assert.HasCount(3, asked, $"{Avalonia.Automation.AutomationProperties.GetName(button)} keeps its own click");
                }
            });
        }
        finally
        {
            TrayDesktop.Run(host.Close);
        }
    }

    /// <summary>A place on the card's blank area, its left margin at mid height, on the desktop.</summary>
    private static PixelPoint Blank(CaptureCard card) => TrayDesktop.Get(() => card.PointToScreen(new Point(5, card.Bounds.Height / 2)));

    /// <summary>The window hidden with the item out of its view, a search typed in Favorites, then until a click on the card has brought it in front on that item.</summary>
    private static async Task ClickShowsTheItem(CaptureDesk desk, StashItem item, PixelPoint on)
    {
        var window = desk.Shown.Window;
        TrayDesktop.Run(() =>
        {
            window.Hide();
            window.ShowActivated = true;
        });
        await TrayDesktop.Get(() => desk.Shown.Listing.ShowScope(Scope.Favorites));
        await TrayDesktop.Get(() => desk.Shown.Listing.SetQuery("nothing"));
        desk.Chat.ClickTitleBar();

        TestMouse.Click(on);

        var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => desk.Shown.Listing.Selection.SequenceEqual([item.Id])));
    }

    [TestMethod]
    public async Task AClickOnTheCardsBlankAreaOrPictureOpensTheFlyoutInFrontOnItsItemWithNoEditorAndTheCardGoesWhileEditKeepsIt()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash shown.png", "stash beside.png"], platformTarget: targets);
        var shown = desk.Items()[0];
        var window = desk.Shown.Window;

        foreach (var what in new[] { "its blank area", "its picture" })
        {
            var clicked = await desk.Raise(shown);
            var on = what == "its picture" ? Centre(TrayDesktop.Get(() => clicked.GetVisualDescendants().OfType<Image>().Single())) : Blank(clicked);
            await ClickShowsTheItem(desk, shown, on);
            Thread.Sleep(500);

            Assert.AreEqual(TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle), FrontWindow.GetForegroundWindow(), $"after a click on {what} the stash window stays in front, not the notification (G16)");
            TrayDesktop.Run(() =>
            {
                var expected = Summon.AboveTrayIcon(null, Summon.DisplaysOf(window.Screens), WindowSize.FlyoutDefault);
                Assert.IsTrue(window.IsVisible && window.IsActive);
                Assert.IsFalse(window.ShowInTaskbar, "a flyout, the window not having been pinned (G16)");
                Assert.AreEqual(expected.Position, window.Position, "placed as from the tray, here with no icon to stand above (G1)");
                Assert.AreEqual(Scope.All, desk.Shown.Listing.Scope, "All, where the item is listed");
                Assert.AreEqual("", desk.Shown.Search.Text, "the search cleared");
                Assert.AreEqual(shown.Id, desk.Shown.Listing.Focused);
                Assert.IsFalse(desk.Shown.Editing.IsOpen, "no editor (G16)");
                Assert.IsNull(window.PasteTarget.Current, "the window before it was ImageStash's own notification, so no paste target (G8)");
                CollectionAssert.AreEqual(new[] { "Enter · Copy" }, desk.Shown.FooterHints(), "Enter copies");
                Assert.IsFalse(desk.Notification.Shown?.GetVisualDescendants().OfType<CaptureCard>().Contains(clicked) ?? false, $"after a click on {what} the card goes, since the window shows the item (G16)");
                Assert.IsEmpty(desk.Notifications.Shown);
            });
        }

        var card = await desk.Raise(shown);
        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        TestHotKeys.Tap(Esc);
        await Eventually(() => TrayDesktop.Get(() => !card.Field.IsEditing));
        TestMouse.Click(Centre(TrayDesktop.Get(() => EditButton(card))));
        await Eventually(() => TrayDesktop.Get(() => desk.Shown.Editing.Session?.Item.Id == shown.Id));
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(card, desk.Notification.Shown?.GetVisualDescendants().OfType<CaptureCard>().SingleOrDefault(), "after Edit the card stays, its Undo closing the edit (G16)");
            desk.Shown.Editing.Cancel();
        });
    }

    [TestMethod]
    public async Task AClickOnTheCardClosesAnOpenPreviewButLeavesAnOpenEditOfAnotherItemAsItWasLeft()
    {
        using var desk = await CaptureDesk.Open(["stash asked.png", "stash open.png"]);
        var (asked, open) = (desk.Items()[0], desk.Items()[1]);
        var card = await desk.Raise(asked);
        var window = desk.Shown.Window;
        var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
        TrayDesktop.Run(() => window.ShowActivated = true);

        TrayDesktop.Run(() => desk.Shown.Preview.Open(open));
        TestMouse.Click(Blank(card));
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => !desk.Shown.Preview.IsOpen && desk.Shown.Listing.Selection.SequenceEqual([asked.Id])));

        await TrayDesktop.Get(() => desk.Shown.Editing.Open(open, EditOrigin.Grid));
        TrayDesktop.Run(() => desk.Shown.Listing.Focus(open.Id));
        TrayDesktop.Run(() => Assert.IsEmpty(desk.Notifications.Shown, "the first click's card went (G16)"));
        card = await desk.Raise(asked);
        desk.Chat.ClickTitleBar();
        TestMouse.Click(Blank(card));
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle);
        Thread.Sleep(300);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(open.Id, desk.Shown.Editing.Session?.Item.Id, "the edit stays as it was left");
            Assert.AreSame(card, desk.Notification.Shown?.GetVisualDescendants().OfType<CaptureCard>().SingleOrDefault(), "and the card with it, since the window does not show its item (G16)");
            CollectionAssert.AreEqual(new[] { open.Id }, desk.Shown.Listing.Selection.ToList(), "the asked item not selected beneath it");
            desk.Shown.Editing.Cancel();
        });
    }

    [TestMethod]
    public async Task ARecordingsCardOffersTrimAndAScreenshotsEditBesideUndoClearOfTheX()
    {
        using var desk = await CaptureDesk.Open(["stash recorded.png", "stash shot.png"]);
        var recorded = await desk.Raise(desk.Items()[0], CaptureKind.Recording);
        var shot = await desk.Raise(desk.Items()[1], CaptureKind.Screenshot);

        TrayDesktop.Run(() =>
        {
            foreach (var (card, words) in new[] { (recorded, "Trim"), (shot, "Edit") })
            {
                CollectionAssert.AreEqual(new[] { words, "Undo" }, card.GetVisualDescendants().OfType<Button>().Select(button => button.Content).OfType<string>().ToList(), $"{words}, then Undo (G16)");
                var hint = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == CaptureCard.NameHint);
                var close = CloseButton(card).TranslatePoint(default, card)!.Value.X;
                foreach (var control in new Control[] { EditButton(card), UndoButton(card), hint })
                {
                    Assert.IsLessThanOrEqualTo(close + 1e-9, control.TranslatePoint(new Point(control.Bounds.Width, 0), card)!.Value.X, $"{words}'s card: nothing of its row under the ×");
                }
            }
        });
    }

    [TestMethod]
    public async Task AClickOnTheNameActivatesItSelectedAndEnterRenamesTheItemAndItsFileAndGivesTheChatItsKeystrokesBack()
    {
        using var desk = await CaptureDesk.Open(["dog.png", "stash named.png"]);
        var (taken, named) = (desk.Items()[0], desk.Items()[1]);
        var card = await desk.Raise(named);

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => FrontWindow.GetForegroundWindow() == desk.NotificationHandle);
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        TrayDesktop.Run(() => Assert.AreEqual("stash named", NameBox(card).SelectedText, "the name selected, so typing replaces it"));
        var chatBefore = desk.Chat.Messages().Count;
        Type("dog");
        await Eventually(() => TrayDesktop.Get(() => NameBox(card).Text) == "dog");
        Assert.IsEmpty(desk.Chat.Messages().Skip(chatBefore).Where(message => message.StartsWith("WM_KEYDOWN")).ToList(), "the letters went to the field, not the chat");

        TestHotKeys.Tap(Enter);
        await Eventually(() => FrontWindow.GetForegroundWindow() == desk.Chat.Window);
        await Eventually(() => TrayDesktop.Get(() => card.Field.Shown) == "dog");

        var renamed = desk.Items().Single(item => item.Id == named.Id);
        Assert.AreEqual("dog", renamed.Name, "the name as typed (G17)");
        Assert.AreEqual("dog 2.png", renamed.FileName, "its file's stem with \" 2\" while another item's file has the name (P1)");
        Assert.IsTrue(File.Exists(renamed.FilePath), "the file renamed on disk");
        Assert.AreEqual("dog.png", desk.Items().Single(item => item.Id == taken.Id).FileName);
        desk.ChatKeepsTheKeystrokes("after Enter");
        TrayDesktop.Run(() => Assert.IsFalse(card.Field.IsEditing));
    }

    [TestMethod]
    public async Task EscLeavesTheNameAsRecordedAndAClickOnTheChatEndsTheEditMovingNothing()
    {
        using var desk = await CaptureDesk.Open(["stash kept.png"]);
        var kept = desk.Items().Single();
        var card = await desk.Raise(kept);

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        Type("zz");
        await Eventually(() => TrayDesktop.Get(() => NameBox(card).Text) == "zz");
        TestHotKeys.Tap(Esc);
        await Eventually(() => FrontWindow.GetForegroundWindow() == desk.Chat.Window);
        TrayDesktop.Run(() => Assert.AreEqual("stash kept", card.Field.Shown, "the recorded name (G19)"));
        desk.ChatKeepsTheKeystrokes("after Esc");

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        Type("zz");
        await Eventually(() => TrayDesktop.Get(() => NameBox(card).Text) == "zz");
        desk.Chat.ClickTitleBar();
        await Eventually(() => TrayDesktop.Get(() => !card.Field.IsEditing));
        Thread.Sleep(500);
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "the user's click wins: nothing moves the foreground after it");
        desk.ChatKeepsTheKeystrokes("after the click on the chat");
        Assert.AreEqual("stash kept", desk.Items().Single().Name, "nothing committed");
    }

    [TestMethod]
    public async Task WhileTheFieldHasFocusTheNoticeOutlivesItsSixSecondsAndItsTimeRunsOnOnceFocusHasGone()
    {
        using var desk = await CaptureDesk.Open(["stash held.png"]);
        var card = await desk.Raise(desk.Items().Single());
        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        TestMouse.MoveTo(TestMouse.Away);
        Thread.Sleep(300);

        desk.Clock.Advance(TimeSpan.FromSeconds(7));
        Thread.Sleep(600);
        TrayDesktop.Run(() => Assert.HasCount(1, desk.Notifications.Shown, "held past its six seconds while its field has focus (G16)"));

        TestHotKeys.Tap(Esc);
        await Eventually(() => FrontWindow.GetForegroundWindow() == desk.Chat.Window);
        desk.Clock.Advance(TimeSpan.FromSeconds(5.5));
        Thread.Sleep(600);
        TrayDesktop.Run(() => Assert.HasCount(1, desk.Notifications.Shown, "its clock stopped while the field had focus"));
        desk.Clock.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is null));
    }

    [TestMethod]
    public async Task UndoWithTheFieldFocusedRemovesTheItemToTheRecycleBinAndGivesTheChatTheForegroundBack()
    {
        using var desk = await CaptureDesk.Open(["stash undone.png", "stash other.png"]);
        var undone = desk.Items()[0];
        var card = await desk.Raise(undone);
        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));

        TestMouse.Click(Centre(TrayDesktop.Get(() => UndoButton(card))));
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is null));
        await Eventually(() => desk.Items().Count == 1);

        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "the window in front before the field's click has it again");
        desk.ChatKeepsTheKeystrokes("after Undo");
        Assert.IsFalse(File.Exists(undone.FilePath));
        var entries = RecycleBinEntries.Of(undone.FilePath);
        Assert.HasCount(1, entries, "its file in the Recycle Bin (G29)");
        foreach (var entry in entries)
        {
            File.Delete(entry.Recycled);
            File.Delete(entry.Record);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UndoClickedWithoutTheFieldRemovesTheItemToTheRecycleBinAndTheWindowInFrontKeepsItsKeystrokes(bool flyoutInFront)
    {
        using var desk = await CaptureDesk.Open(["stash undone at once.png", "stash other.png"]);
        var undone = desk.Items()[0];
        var card = await desk.Raise(undone);
        var flyout = desk.Shown.Window;
        if (flyoutInFront)
        {
            await desk.FlyoutInFront();
        }

        // The card's usual Undo: a click that takes no activation (G16) still reaches the button.
        TestMouse.Click(Centre(TrayDesktop.Get(() => UndoButton(card))));
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is null));
        await Eventually(() => desk.Items().Count == 1);

        if (flyoutInFront)
        {
            Thread.Sleep(300);
            Assert.AreEqual(TrayDesktop.Get(() => flyout.TryGetPlatformHandle()!.Handle), FrontWindow.GetForegroundWindow(), "the flyout in front after Undo");
            TrayDesktop.Run(() => Assert.IsTrue(flyout.IsVisible && flyout.IsActive, "and it keeps the activation"));
            var typed = TrayDesktop.Get(() => desk.Shown.Search.Text ?? "") + "k";
            TestHotKeys.Tap('K');
            await Eventually(() => TrayDesktop.Get(() => desk.Shown.Search.Text) == typed);
        }
        else
        {
            desk.ChatKeepsTheKeystrokes("after Undo");
        }

        Assert.IsFalse(File.Exists(undone.FilePath));
        var entries = RecycleBinEntries.Of(undone.FilePath);
        Assert.HasCount(1, entries, "its file in the Recycle Bin (G29)");
        foreach (var entry in entries)
        {
            File.Delete(entry.Recycled);
            File.Delete(entry.Record);
        }
    }

    [TestMethod]
    public async Task EnterWhileTheStoreIsHeldGivesTheChatItsKeystrokesAtOnceKeepsTheNameShutUntilTheStoreAnswersAndNamesTheFailure()
    {
        using var desk = await CaptureDesk.Open(["stash waited.png"]);
        var waited = desk.Items().Single();
        var card = await desk.Raise(waited);
        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        Type("new");
        await Eventually(() => TrayDesktop.Get(() => NameBox(card).Text) == "new");

        using (desk.Shown.HoldStore())
        {
            TestHotKeys.Tap(Enter);
            await Eventually(() => FrontWindow.GetForegroundWindow() == desk.Chat.Window);
            TrayDesktop.Run(() => Assert.AreEqual("stash waited", card.Field.Shown, "the recorded name while the store works (R9)"));
            // Drawn again, so the click lands on the name and not on what stood under the box.
            Thread.Sleep(200);
            TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
            Thread.Sleep(400);
            Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), $"a click on the name does nothing until the store has answered; \"{FrontWindow.NativeText(FrontWindow.GetForegroundWindow())}\" is in front");
            TrayDesktop.Run(() => Assert.IsFalse(card.Field.IsEditing));
            // The rename's retry, then the listing's refresh retried the same way, before the refusal is named.
            var waiting = System.Diagnostics.Stopwatch.StartNew();
            while (TrayDesktop.Get(() => desk.Notifications.Shown.Count) < 2)
            {
                Assert.IsLessThan(3 * HeldFileRetry.Duration.TotalMilliseconds + 5000, waiting.ElapsedMilliseconds, "the refusal was not named");
                await Task.Delay(100);
            }
        }

        TrayDesktop.Run(() =>
        {
            Assert.StartsWith("Rename failed: ", desk.Notifications.Shown[^1].Text, "the refusal named above the tray (G35)");
            Assert.AreEqual("stash waited", card.Field.Shown);
        });
        Assert.AreEqual("stash waited", desk.Items().Single().Name, "still listed as it was");
    }

    [TestMethod]
    public async Task AnItemDeletedWhileTheCardShowsIsRefusedByNameAndTheCardKeepsItsName()
    {
        using var desk = await CaptureDesk.Open(["stash deleted.png"]);
        var deleted = desk.Items().Single();
        var card = await desk.Raise(deleted);
        var held = await TrayDesktop.Get(() => desk.Shown.Listing.Store!.Delete([deleted.Id]));

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        Type("cat");
        await Eventually(() => TrayDesktop.Get(() => NameBox(card).Text) == "cat");
        TestHotKeys.Tap(Enter);
        await Eventually(() => TrayDesktop.Get(() => desk.Notifications.Shown.Count) == 2);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Rename failed: stash deleted, it is no longer in the stash", desk.Notifications.Shown[^1].Text);
            Assert.AreEqual("stash deleted", card.Field.Shown, "the card's name unchanged");
        });
        await TrayDesktop.Get(() => desk.Shown.Listing.Store!.UndoDelete([held[0].Value!]));
    }

    [TestMethod]
    public async Task AnActivationThePlatformRefusesLeavesTheNameAsItIsAndIsNamed()
    {
        using var desk = await CaptureDesk.Open(["stash refused.png"], activations: new RefusedActivation());
        var card = await desk.Raise(desk.Items().Single());

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => desk.Notifications.Shown.Count) == 2);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual($"Rename failed: “stash refused”, {CaptureCard.NotActivated}", desk.Notifications.Shown[^1].Text, "named (G35)");
            Assert.IsFalse(card.Field.IsEditing, "nothing enters edit");
            Assert.AreEqual("stash refused", card.Field.Shown);
        });
        desk.ChatKeepsTheKeystrokes("after the refused click");
    }

    [TestMethod]
    public async Task TwoCardsTheFirstFieldThenTheSecondThenEscGiveTheChatBackItsKeystrokes()
    {
        using var desk = await CaptureDesk.Open(["stash first.png", "stash second.png"]);
        var first = await desk.Raise(desk.Items()[0]);
        var second = await desk.Raise(desk.Items()[1]);

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(first))));
        await Eventually(() => TrayDesktop.Get(() => first.Field.IsEditing && NameBox(first).IsFocused));
        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(second))));
        await Eventually(() => TrayDesktop.Get(() => second.Field.IsEditing && NameBox(second).IsFocused && !first.Field.IsEditing));
        Assert.AreEqual(desk.NotificationHandle, FrontWindow.GetForegroundWindow(), "the second field keeps the activation the first took");

        TestHotKeys.Tap(Esc);
        await Eventually(() => FrontWindow.GetForegroundWindow() == desk.Chat.Window);
        desk.ChatKeepsTheKeystrokes("after Esc in the second field");
    }

    [TestMethod]
    public async Task ThePlainNotificationBesideACaptureCardIsQuietAndTheNewestNamesTheWindow()
    {
        using var desk = await CaptureDesk.Open(["stash beside.png"]);
        await desk.Raise(desk.Items().Single());
        TrayDesktop.Run(() => desk.Notifications.Raise("Copied · press Ctrl+V"));
        await Eventually(() => TrayDesktop.Get(() => ((StackPanel)((ScrollViewer)desk.Notification.Shown!.Content!).Content!).Children.Count == 2));
        Thread.Sleep(500);

        TrayDesktop.Run(() => Assert.AreEqual("Copied · press Ctrl+V", desk.Notification.Shown!.Title));
        var copiedWords = TrayDesktop.Get(() => desk.Notification.Shown!.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Copied · press Ctrl+V"));
        TestMouse.Click(Centre(copiedWords));
        desk.ChatKeepsTheKeystrokes("after a click on the plain card");
    }

    [TestMethod]
    public async Task ARenameInTheWindowShowsInTheFieldAndAPictureWhoseFileItMovedIsReadWhereItNowIs()
    {
        using var desk = await CaptureDesk.Open(["stash moved.png"], heldFileRetry: TimeSpan.FromSeconds(3));
        var moved = desk.Items().Single();
        var windowsRename = TrayDesktop.Get(() => new Renaming(desk.Shown.Listing, desk.Shown.Notices, desk.Shown.Handoff));
        CaptureCard card;
        // Read refused, rename let through: the card's picture is still being read when the window's rename moves its file.
        using (new FileStream(moved.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            card = await desk.Raise(moved);
            Assert.IsNull(TrayDesktop.Get(() => card.Picture), "the picture still being read");
            Assert.IsTrue((await TrayDesktop.Get(() => windowsRename.Rename(moved, "renamed in the window"))).Succeeded);
            await Eventually(() => TrayDesktop.Get(() => card.Field.Shown) == "renamed in the window");
        }

        await Eventually(() => TrayDesktop.Get(() => card.Picture is not null));
        TrayDesktop.Run(() => Assert.HasCount(1, desk.Notifications.Shown, "nothing named: the picture was read where the file now is (G16)"));
        Assert.AreEqual("renamed in the window.png", desk.Items().Single().FileName);
    }

    [TestMethod]
    [DataRow(SKEncodedImageFormat.Png)]
    [DataRow(SKEncodedImageFormat.Jpeg)]
    public async Task APictureOpenWithReplacedShowsOnceTheStashIsListedAgainAndItsUndoShowsThePreviousOneAgain(SKEncodedImageFormat saved)
    {
        using var desk = await CaptureDesk.Open(["stash adopted.png"]);
        var item = desk.Items().Single();
        var card = await desk.Raise(item);
        var captured = Due(item, new PixelSize(6, 4));
        Assert.AreEqual(captured, await ShownWithin(card, captured), "the captured picture and its size");

        // The item's Open with… copy rewritten as another program saving it would: a PNG keeps the item's file name, a
        // JPEG gives it another; noise, so the saved file's size in words differs from the capture's too (G24).
        var store = desk.Shown.Listing.Store!;
        var copy = (await TrayDesktop.Get(() => store.BeginOpenWith(item))).Value!;
        using (var bitmap = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
        {
            var noise = new Random(7);
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    bitmap.SetPixel(x, y, new SKColor((uint)noise.Next() | 0xFF000000));
                }
            }

            File.WriteAllBytes(copy, bitmap.Encode(saved, 100).ToArray());
        }

        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, (await TrayDesktop.Get(() => store.ClassifyOpenWith(item.Id))).Value, "the rewrite seen");
        var adopted = await TrayDesktop.Get(() => store.AdoptRewrite(item.Id));
        Assert.IsTrue(adopted.Succeeded, "the rewrite adopted");
        try
        {
            // The stash listed again, as the window lists it once an adoption has answered.
            await TrayDesktop.Get(() => desk.Shown.Listing.Refresh());
            var replaced = Due(desk.Items().Single(), new PixelSize(200, 100));
            Assert.AreNotEqual(captured, replaced);
            Assert.AreEqual(replaced, await ShownWithin(card, replaced), "the card shows the picture and size the item's file held when the stash was last listed (G6, G24)");

            Assert.IsNotNull((await TrayDesktop.Get(() => store.UndoAdoption(item.Id, adopted.Value!.RenderedRevision))).Value, "the adoption undone");
            await TrayDesktop.Get(() => desk.Shown.Listing.Refresh());
            var restored = Due(desk.Items().Single(), new PixelSize(6, 4));
            Assert.AreEqual(restored, await ShownWithin(card, restored), "after its Undo, the previous picture and its size again (G27, G6)");
        }
        finally
        {
            // The outside version the Undo moved to the Recycle Bin (G27), the only entry this check leaves there.
            foreach (var entry in RecycleBinEntries.Within(store.Folder.Root))
            {
                File.Delete(entry.Recycled);
                File.Delete(entry.Record);
            }
        }
    }

    [TestMethod]
    public async Task ALargePictureIsShownBoundedWithoutItsFullFrameKept()
    {
        using var desk = await CaptureDesk.Open();
        var large = Path.Combine(Path.GetDirectoryName(desk.Shown.Picture("stash small.png"))!, "stash large.png");
        using (var bitmap = new SKBitmap(new SKImageInfo(4000, 3000, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
        {
            bitmap.Erase(new SKColor(20, 120, 220));
            File.WriteAllBytes(large, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        var item = (await TrayDesktop.Get(() => desk.Shown.Import.Drop(new DropContents([new DroppedItem("stash large.png", large, null, IsFolder: false)], false, false, [])))).Single().Item!;
        var card = await desk.Raise(item);
        await Eventually(() => TrayDesktop.Get(() => card.Picture is not null));

        TrayDesktop.Run(() => Assert.AreEqual(new PixelSize(CaptureCard.PictureBound, CaptureCard.PictureBound * 3 / 4), ((Bitmap)card.Picture!).PixelSize, "its longer edge at the bound (R7)"));
    }

    [TestMethod]
    public async Task AFileHeldPastTheRetryShowsTheWordsTheSizeAndTheFieldWithoutThePictureAndNamesIt()
    {
        using var desk = await CaptureDesk.Open(["stash locked.png"], heldFileRetry: TimeSpan.FromMilliseconds(300));
        var locked = desk.Items().Single();
        using (new FileStream(locked.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var card = await desk.Raise(locked);
            await Eventually(() => TrayDesktop.Get(() => desk.Notifications.Shown.Count) == 2);

            TrayDesktop.Run(() =>
            {
                Assert.IsNull(card.Picture);
                Assert.AreEqual(FileSize.Words(new FileInfo(locked.FilePath).Length), card.Size, "the size, which a program holding the file does not hide (G24)");
                Assert.StartsWith($"Read the picture failed: {locked.FileName}, ", desk.Notifications.Shown[^1].Text, "named with the file and why (G35)");
            });
            TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
            await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
            TestHotKeys.Tap(Esc);
            await Eventually(() => FrontWindow.GetForegroundWindow() == desk.Chat.Window);
        }
    }

    private static void Type(string letters)
    {
        foreach (var letter in letters.ToUpperInvariant())
        {
            TestHotKeys.Tap(letter);
        }
    }

    private static List<string> Texts(Control control) => control.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToList();

    /// <summary>The card's picture in pixels and its size as shown, once they are <paramref name="due"/> or ten seconds have passed.</summary>
    private static async Task<string> ShownWithin(CaptureCard card, string due)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Shown(card) != due && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        return Shown(card);
    }

    private static string Shown(CaptureCard card) => TrayDesktop.Get(() => $"{(card.Picture as Bitmap)?.PixelSize} {card.Size}");

    /// <summary>A picture of <paramref name="pixels"/> and the measured size of the item's file as it is now (G24).</summary>
    private static string Due(StashItem item, PixelSize pixels) => $"{pixels} {FileSize.Words(new FileInfo(item.FilePath).Length)}";
}
