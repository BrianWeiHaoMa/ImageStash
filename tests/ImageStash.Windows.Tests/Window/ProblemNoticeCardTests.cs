using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.CaptureDesk;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// A notice inside the window that names a problem, on the real window (G16, G18): a failure's card is still there
/// after its time and a real click on its × takes it down; the card of an import's notice naming a skipped file, once
/// its Undo has ended at its time, shows its words and its × and no Undo button. These checks show the stash window.
/// </summary>
[TestClass]
public sealed class ProblemNoticeCardTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task AFailuresNoticeIsStillThereAfterItsTimeAndItsXTakesItDown()
    {
        var clock = new StoppedClock();
        using var shown = ShownWindow.Open(noticeClock: clock);
        await shown.OpenStash(shown.Picture("kept.png"));
        TrayDesktop.Run(() =>
        {
            shown.Notices.Close(shown.Notices.Shown.Single());
            // Finish the setup card's fade over the look's exit before raising the failure.
            NoticeMotion.Advance(shown, NoticeMotion.Exit);
            shown.Notices.Raise(new Failure("Paste", "the clipboard", "it holds no picture or file"));
        });
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));

        TrayDesktop.Run(() =>
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            shown.Notices.Sweep();
        });

        var close = TrayDesktop.Get(() =>
        {
            Assert.HasCount(1, shown.Notices.Shown, "a failure stays past its time");
            return CloseButton(NoticeCards(shown).Single());
        });
        using var pointer = TestMouse.PutBackAfterwards();
        var at = TestMouse.Centre(close);
        await Eventually(() => TrayDesktop.Get(() => NoticeMotion.Hits(shown.Window, at, close)));
        var card = TrayDesktop.Get(() => NoticeCards(shown).Single());
        TestMouse.Click(at);
        // The failure's card stays in the window while it fades over the look's exit.
        await NoticeMotion.FinishExit(shown.Notices, NoticeMotion.StashStack(shown.Window), card);
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count == 0 && NoticeCards(shown).Count == 0));
    }

    [TestMethod]
    public async Task TheCardOfAnImportsNoticeWhoseUndoHasEndedShowsItsWordsAndXAndNoUndoButton()
    {
        var clock = new StoppedClock();
        using var shown = ShownWindow.Open(noticeClock: clock);
        await shown.OpenStash();
        var picture = shown.Picture("kept.png");
        var clip = Path.Combine(Path.GetDirectoryName(picture)!, "clip.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);
        await TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
            [new DroppedItem("kept.png", picture, null, IsFolder: false), new DroppedItem("clip.mp4", clip, null, IsFolder: false)], false, false, [])));
        var notice = TrayDesktop.Get(() => shown.Notices.Shown.Single());
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));
        TrayDesktop.Run(() => Assert.IsTrue(UndoButton(NoticeCards(shown).Single()).IsVisible, "offered while its time runs"));

        TrayDesktop.Run(() =>
        {
            clock.Advance(Notices.LastingFor(notice.Words));
            shown.Notices.Sweep();
        });

        await Eventually(() => TrayDesktop.Get(() => !UndoButton(NoticeCards(shown).Single()).IsVisible));
        TrayDesktop.Run(() =>
        {
            var card = NoticeCards(shown).Single();
            Assert.Contains(notice.Text, card.GetSelfAndVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList());
            Assert.IsTrue(CloseButton(card).IsEffectivelyVisible, "its × remains");
            Assert.IsFalse(shown.Notices.OffersUndo(notice));
        });
    }

    /// <summary>The cards of the notices showing in the stash window.</summary>
    private static List<Border> NoticeCards(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<Border>().Where(border => border.Child is StackPanel row
            && row.Children.OfType<Button>().Any(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Close")).ToList();

    private static Button UndoButton(Control card) => card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));
}
