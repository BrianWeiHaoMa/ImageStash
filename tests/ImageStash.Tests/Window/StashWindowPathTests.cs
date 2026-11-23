using Avalonia.Input;
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
    /// favorite, its tile alone carries the star and the preview's star reads filled for it (G17, G6, G19). A tile's pixels
    /// and the preview's control are beyond these parts: the walk reads the rules they draw from.
    /// </summary>
    [TestMethod]
    public async Task CtrlDOnTheMiddleOfThreePicturesStarsItsTileAloneAndThePreviewsStarReadsFilled()
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
