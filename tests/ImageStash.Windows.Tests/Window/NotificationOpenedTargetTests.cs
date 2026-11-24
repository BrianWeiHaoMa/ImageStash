using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.CaptureDesk;
using static ImageStash.Windows.Tests.Support.ShownWindow;
using static ImageStash.Windows.Tests.Support.TestMouse;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The paste target of the stash window a click on a capture card opens (G8, G16): none when ImageStash's own pinned
/// window was in front before the click; the chat, heard of late once the window is in front and the card has gone; and
/// the chat still after the edit's Save, nothing pasted (G26, N3).
/// </summary>
[TestClass]
public sealed class NotificationOpenedTargetTests
{
    [TestMethod]
    public async Task ACardClickedFromThePinnedStashClearsItsPasteTargetAndShowsTheCopyHint()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash pinned card.png"], platformTarget: targets);
        var shown = desk.Shown;
        var item = desk.Items().Single();
        var card = await desk.Raise(item);
        var at = DisplayPlaces.At(DisplayPlaces.Primary(), 40, 40);
        shown.Settings.Change(settings => settings with { PinnedPosition = new WindowPosition(at.X, at.Y), PinnedSize = new WindowSize(560, 560) });
        var chat = new PasteTarget(desk.Chat.Window, desk.Chat.AppName);
        TrayDesktop.Run(shown.Window.SummonPinned);
        await shown.ClickSearchInFront();
        var stash = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        await Eventually(() => shown.Window.PasteTarget.Current == chat);

        Click(TrayDesktop.Get(() => card.PointToScreen(new Point(5, card.Bounds.Height / 2))));
        await Eventually(() => FrontWindow.GetForegroundWindow() == stash && desk.Cards().Count == 0
            && TrayDesktop.Get(() => shown.Window.IsActive && shown.Listing.Selection.SequenceEqual([item.Id])));

        TrayDesktop.Run(() =>
        {
            // G8: “it is none when that window was ImageStash itself”.
            Assert.IsNull(shown.Window.PasteTarget.Current, "the pinned stash was in front before the card click (G8)");
            CollectionAssert.AreEqual(new[] { "Enter · Copy" }, shown.FooterHints());
        });
    }

    [TestMethod]
    public async Task EditTakesTheLateChatTargetAfterTheStashIsInFrontAndTheCardIsGone()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash late card.png"], platformTarget: targets);
        var item = desk.Items().Single();
        var card = await desk.Raise(item, CaptureKind.Screenshot);
        var shown = desk.Shown;
        var stash = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        var edit = Centre(TrayDesktop.Get(() => EditButton(card)));
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "the chat is in front before Edit");

        using (HeldThread.Suspended(targets.HookThread))
        {
            Click(edit);
            await Eventually(() => FrontWindow.GetForegroundWindow() == stash && desk.Cards().Count == 0
                && TrayDesktop.Get(() => shown.Window.IsActive && shown.Editing.Session?.Item.Id == item.Id));
            // G8: “while ImageStash has not yet heard from Windows which window had focus just before it, it is none until it hears, and then that window”.
            Assert.IsNull(shown.Window.PasteTarget.Current, "the stash is in front with the card gone and the activation report is still held (G8)");
        }

        var chat = new PasteTarget(desk.Chat.Window, desk.Chat.AppName);
        await Eventually(() => shown.Window.PasteTarget.Current == chat);
        Assert.AreEqual(stash, FrontWindow.GetForegroundWindow(), "the late report updates the target while the stash stays in front (G8)");
    }

    [TestMethod]
    public async Task SavingAHideBoxAfterEditFromTheChatKeepsTheItemSelectedAndTheChatHintWithoutPasting()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash beside the edit.png"], platformTarget: targets);
        var shown = desk.Shown;
        using var editor = new ShownEditor(shown);
        var item = await desk.Import(shown.Picture("stash hide box.png", 160, 120, (x, y) => new SKColor((byte)x, (byte)y, 90)));
        var card = await desk.Raise(item, CaptureKind.Screenshot);
        var stash = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        var chat = new PasteTarget(desk.Chat.Window, desk.Chat.AppName);
        desk.Chat.ClickTitleBar();
        var before = desk.Chat.Messages().Count;
        var textBefore = desk.Chat.Text();

        Click(Centre(TrayDesktop.Get(() => EditButton(card))));
        await Eventually(() => FrontWindow.GetForegroundWindow() == stash && TrayDesktop.Get(() =>
            editor.Session?.Item.Id == item.Id && editor.Canvas.Mapping() is not null));
        await Eventually(() => shown.Window.PasteTarget.Current == chat);
        var save = TrayDesktop.Get(() =>
        {
            editor.Session!.Tool = editor.Session.Tools.OfType<HideTool>().Single();
            editor.Drag((0.2, 0.2), (0.7, 0.7));
            Assert.HasCount(1, editor.Session.Layers.Layers, "the Hide box was added");
            return editor.Button("Save");
        });
        await Eventually(() => TrayDesktop.Get(() => save.IsEnabled
            && save.TranslatePoint(new Point(save.Bounds.Width / 2, save.Bounds.Height / 2), shown.Window) is { } centre
            && shown.Window.InputHitTest(centre) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(save)));
        Click(Centre(save));
        await Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Listing.Selection.SequenceEqual([item.Id])
            && shown.Listing.Items.Any(listed => listed.Id == item.Id && listed.RenderedRevision == 1)));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible && shown.Window.IsActive, "the window stays after Save (G26)");
            CollectionAssert.AreEqual(new[] { item.Id }, shown.Listing.Selection.ToList(), "the edited item stays selected (G26)");
            // G8: “keeps the paste target it had, and so does the stash window a click on a notification opens (G16)”.
            Assert.AreEqual(chat, shown.Window.PasteTarget.Current);
            CollectionAssert.AreEqual(new[] { $"Enter · Paste into {chat.AppName}", "Ctrl+C · Copy" }, shown.FooterHints());
        });
        Assert.AreEqual(textBefore, desk.Chat.Text(), "the chat text is unchanged (N3)");
        Assert.IsFalse(desk.Chat.Messages().Skip(before).Any(message => message.StartsWith("WM_PASTE") || message == "WM_KEYDOWN:0x56"),
            "neither the Edit click nor Save sends a paste to the chat (N3)");
    }
}
