using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using ImageStash.Look;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The pointer's hold on the stash window's notices, through the window's own wiring (<see cref="WindowStatus"/>), as
/// above the tray (G16): the pointer anywhere on the notices holds every notice's clock, one raised meanwhile's too; a
/// notice that leaves meanwhile keeps its room, so those above it do not drop under the hand; once the pointer has gone,
/// or the window has stopped showing, the clocks run on and the room is given up.
/// </summary>
[TestClass]
public sealed class NoticeStackHoldTests
{
    private static readonly TimeSpan Entrance = (TimeSpan)Look()[LookKeys.CardEntrance]!;
    private static readonly TimeSpan Exit = (TimeSpan)Look()[LookKeys.CardExit]!;

    [TestMethod]
    public async Task ThePointerOnTheUpperNoticeHoldsTheLowerAndOneRaisedMeanwhileUntilItLeaves()
    {
        using var listed = await ListedStash.Open();
        ControlThread.Run(() =>
        {
            var (status, _) = Shown(listed);
            var upper = listed.Notices.Raise("3 added");
            var lower = listed.Notices.Raise("2 added");
            status.ShowNotices();

            // Onto the upper card: the pass enters it, its room, the stack and the notices' area.
            Enter(status, upper);
            var meanwhile = listed.Notices.Raise("1 added");
            status.ShowNotices();
            listed.Stash.Clock.Now += Notices.LastingAtMost * 2;
            listed.Notices.Sweep();
            CollectionAssert.AreEqual(new[] { upper, lower, meanwhile }, listed.Notices.Shown.ToList(),
                "the lower one's clock and the one raised under the hand are held with the upper's");

            Leave(status, upper);
            listed.Stash.Clock.Now += Notices.Lasting - TimeSpan.FromMilliseconds(1);
            listed.Notices.Sweep();
            Assert.HasCount(3, listed.Notices.Shown, "the clocks run on from where they stopped, none charged for the hold");
            listed.Stash.Clock.Now += TimeSpan.FromMilliseconds(1);
            listed.Notices.Sweep();
            Assert.IsEmpty(listed.Notices.Shown, "each leaves at its time once the pointer has gone");
        });
    }

    [TestMethod]
    public async Task ALowerNoticeClosedUnderThePointerKeepsItsRoomUntilThePointerLeaves()
    {
        using var listed = await ListedStash.Open();
        ControlThread.Run(() =>
        {
            var (status, stack) = Shown(listed);
            var upper = listed.Notices.Raise("Deleted “one”.", "Undo", () => Task.FromResult(Outcome.Done));
            var lower = listed.Notices.Raise("2 added");
            status.ShowNotices();
            Step(listed, status, Entrance);

            Enter(status, upper);
            listed.Notices.Close(lower);
            status.ShowNotices();
            Step(listed, status, Exit / 2);
            Assert.IsTrue(double.IsNaN(status.NoticeCards.RoomOf(lower)!.Height), "its room does not close while the pointer is on the stack");
            Step(listed, status, Exit);
            Assert.HasCount(2, stack.Children, "faded out, it keeps its room, so the upper notice stays under the hand");
            Assert.AreEqual(0, status.NoticeCards.RoomOf(lower)!.Opacity);

            Leave(status, upper);
            Assert.IsNull(status.NoticeCards.RoomOf(lower), "its room given up once the pointer has gone");
            Assert.HasCount(1, stack.Children);
        });
    }

    [TestMethod]
    public async Task WithTheEffectsOffANoticeTakingTheHeldOnesPlaceIsHeldToo()
    {
        using var listed = await ListedStash.Open();
        ControlThread.Run(() =>
        {
            var (status, _) = Shown(listed, effectsShown: false);
            Notice refused;
            using (UserAct.Begin("Paste"))
            {
                refused = listed.Notices.Raise(new Failure("Paste", "the clipboard", "it holds no picture or file"));
            }

            status.ShowNotices();
            Enter(status, refused);
            Notice added;
            using (UserAct.Begin("Paste"))
            {
                added = listed.Notices.Raise("Added 1 to your stash.");
            }

            status.ShowNotices();
            Assert.AreSame(added, listed.Notices.Shown.Single(), "the later paste's notice took the refusal's place");
            listed.Stash.Clock.Now += Notices.LastingAtMost * 2;
            listed.Notices.Sweep();
            Assert.IsNotNull(status.NoticeCards.RoomOf(refused), "the left card keeps its room under the pointer, no motion playing");
            Assert.AreSame(added, listed.Notices.Shown.SingleOrDefault(), "the pointer still on the stack holds the new notice's clock");
        });
    }

    [TestMethod]
    public async Task TheHoldEndsWithTheWindowsShowing()
    {
        using var listed = await ListedStash.Open();
        ControlThread.Run(() =>
        {
            var (status, _) = Shown(listed);
            var showing = new WindowShowing(listed.Notices, status.NoticesShowing);
            showing.Seen(visible: true, minimised: false);
            var first = listed.Notices.Raise("3 added");
            status.ShowNotices();
            Enter(status, first);

            // The flyout hides with the pointer on its notices, and is summoned with the pointer elsewhere: no pointer
            // left the notices as far as the window was told.
            showing.Seen(visible: false, minimised: false);
            showing.Seen(visible: true, minimised: false);
            var next = listed.Notices.Raise("2 added");
            status.ShowNotices();
            listed.Stash.Clock.Now += Notices.Lasting;
            listed.Notices.Sweep();
            Assert.IsEmpty(listed.Notices.Shown, "every clock runs once shown again, one raised since too");
            Assert.IsNotNull(next);
        });
    }

    /// <summary>The window's notices as it draws them, dressed in the look, the window showing, the effects on unless said.</summary>
    private static (WindowStatus Status, StackPanel Stack) Shown(ListedStash listed, bool effectsShown = true)
    {
        var status = new WindowStatus(listed.Listing, listed.Notices, listed.Settings, listed.FooterHints, new ScrollViewer(), () => false, () => { }, listed.WaysIn, () => effectsShown);
        status.NoticeArea.Resources.MergedDictionaries.Add(Look());
        status.NoticesShowing(true);
        return (status, (StackPanel)status.NoticeArea.Content!);
    }

    /// <summary>The pointer comes onto <paramref name="notice"/>'s card: entered from the card out to the notices' area.</summary>
    private static void Enter(WindowStatus status, Notice notice) => Raise(InputElement.PointerEnteredEvent, status, notice);

    /// <summary>The pointer leaves the notices altogether, from <paramref name="notice"/>'s card out to the notices' area.</summary>
    private static void Leave(WindowStatus status, Notice notice) => Raise(InputElement.PointerExitedEvent, status, notice);

    private static void Raise(RoutedEvent<PointerEventArgs> routedEvent, WindowStatus status, Notice notice)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var room = status.NoticeCards.RoomOf(notice)!;
        foreach (var element in new InputElement[] { room.Child!, room, status.NoticeCards.View, status.NoticeArea })
        {
            element.RaiseEvent(new PointerEventArgs(routedEvent, element, pointer, null, default, 0, PointerPointProperties.None, KeyModifiers.None));
        }
    }

    private static void Step(ListedStash listed, WindowStatus status, TimeSpan passing)
    {
        listed.Stash.Clock.Now += passing;
        status.NoticeCards.Step();
    }

    private static ResourceDictionary Look() => Looks.All[0].Resources(ThemeVariant.Light, Looks.DefaultAccent);
}
