using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class PreviewStepTests
{
    [TestMethod]
    public async Task TheFourArrowsStepFromThePictureEditButtonAndStarAndStopAtBothEnds()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("arrow one.png"), shown.Picture("arrow two.png"), shown.Picture("arrow three.png"));

        TrayDesktop.Run(() =>
        {
            var items = shown.Listing.Items;
            shown.OpenPreview(items[1]);
            Assert.AreSame(shown.PreviewPicture, shown.Focused);

            (Control Target, string Name)[] targets =
                [(shown.PreviewPicture, "picture"), (shown.EditButton, "Edit button"), (PreviewStarTests.StarButton(shown), "star")];
            (Key Key, int Item)[] steps =
            [
                (Key.Left, 0), (Key.Left, 0), (Key.Up, 0), (Key.Right, 1),
                (Key.Down, 2), (Key.Right, 2), (Key.Down, 2), (Key.Up, 1),
            ];
            foreach (var (target, name) in targets)
            {
                foreach (var (key, index) in steps)
                {
                    // A successful step gives focus back to the picture.
                    target.Focus(NavigationMethod.Tab);
                    Assert.AreSame(target, shown.Focused, $"before {key} on the {name}");
                    var before = shown.Preview.Item?.Id;
                    Tap(target, key);
                    AssertPreview(shown, items[index], $"after {key} on the {name} (G19)");
                    if (before == items[index].Id)
                    {
                        Assert.AreSame(target, shown.Focused, $"{key} at the end of the list leaves focus on the {name}");
                    }
                }
            }
        });
    }

    [TestMethod]
    public async Task SteppingToAFavoriteFillsItsStarAndSteppingBackToAPlainItemOutlinesIt()
    {
        using var shown = ShownWindow.Open();
        var added = await shown.OpenStash(shown.Picture("step favorite.png"), shown.Picture("step plain.png"));
        await MarkFavorite(shown, added.Single(item => item.Name == "step favorite"));

        TrayDesktop.Run(() =>
        {
            var items = shown.Listing.Items;
            shown.OpenPreview(items[0]);
            AssertStar(shown, FavoriteStar.Outlined);

            Tap(shown.PreviewPicture, Key.Right);
            AssertPreview(shown, items[1], "Right reaches the favorite (G19)");
            AssertStar(shown, FavoriteStar.Filled);

            Tap(shown.PreviewPicture, Key.Left);
            AssertPreview(shown, items[0], "Left returns to the plain item (G19)");
            AssertStar(shown, FavoriteStar.Outlined);
        });
    }

    [TestMethod]
    public async Task LeftAndRightInTheNameMoveItsCaretAndKeepThePreviewAndSelectionOnTheSameItem()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("caret one.png"), shown.Picture("caret two.png"), shown.Picture("caret three.png"));

        TrayDesktop.Run(() =>
        {
            var item = shown.Listing.Items[1];
            shown.OpenPreview(item);
            Tap(shown.PreviewPicture, Key.F2);
            shown.Window.UpdateLayout();
            var box = shown.NameBox;
            Assert.IsTrue(shown.Preview.EditingName);
            Assert.AreSame(box, shown.Focused);
            Tap(box, Key.End);
            Assert.AreEqual(item.Name.Length, box.CaretIndex);

            Tap(box, Key.Left);
            Assert.AreEqual(item.Name.Length - 1, box.CaretIndex, "Left moves the name's caret (G7)");
            AssertPreview(shown, item, "after Left inside the name");

            Tap(box, Key.Right);
            Assert.AreEqual(item.Name.Length, box.CaretIndex, "Right moves the name's caret (G7)");
            AssertPreview(shown, item, "after Right inside the name");
            Assert.AreSame(box, shown.Focused);
            Assert.IsTrue(shown.Preview.EditingName);
            Assert.AreEqual(item.Name, box.Text);
        });
    }

    [TestMethod]
    public async Task FiveQuickRightPressesShowTheLastItemsNameFactsStarAndAnimationPlaying()
    {
        using var shown = ShownWindow.Open();
        var added = await shown.OpenStash(
            shown.Fixture("animated.gif", "burst destination.gif"),
            shown.Fixture("restore-previous.gif", "burst penultimate.gif"),
            shown.Picture("burst three.png"), shown.Picture("burst four.png"), shown.Picture("burst five.png"),
            shown.Fixture("long.gif", "burst start.gif"));
        var destination = added.Single(item => item.Name == "burst destination");
        await MarkFavorite(shown, destination);

        var items = TrayDesktop.Get(() => shown.Listing.Items.ToArray());
        Assert.HasCount(6, items);
        Assert.AreEqual(destination.Id, items[5].Id, "the destination is five steps from the first item");
        TrayDesktop.Run(() => shown.OpenPreview(items[0]));
        await PausePreview(shown);

        TrayDesktop.Run(() =>
        {
            Assert.AreSame(shown.PreviewPicture, shown.Focused);
            for (var press = 0; press < 5; press++)
            {
                Tap(shown.PreviewPicture, Key.Right);
            }

            AssertPreview(shown, destination, "after five quick Right presses (G19)");
            Assert.AreEqual(destination.Name, shown.NameField.Shown);
            AssertStar(shown, FavoriteStar.Filled);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts is ["GIF · 8 × 6 · 1 KB · 0.6 s", _]));
        await AssertDestinationPlaying(shown);

        // Also pause the animation immediately before the destination, then step directly onto it.
        TrayDesktop.Run(() =>
        {
            Tap(shown.PreviewPicture, Key.Left);
            AssertPreview(shown, items[4], "Left reaches the animation before the destination");
        });
        await PausePreview(shown);
        TrayDesktop.Run(() =>
        {
            Tap(shown.PreviewPicture, Key.Right);
            AssertPreview(shown, destination, "Right from the paused neighbor");
        });
        await AssertDestinationPlaying(shown);
    }

    [TestMethod]
    public async Task EscAfterAStepClosesThePreviewAndFocusesTheGridWithTheSteppedToItemSelectedAlone()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("esc first.png"), shown.Picture("esc second.png"));

        TrayDesktop.Run(() =>
        {
            var items = shown.Listing.Items;
            shown.OpenPreview(items[0]);
            Tap(shown.PreviewPicture, Key.Right);
            AssertPreview(shown, items[1], "after Right (G19)");

            Tap(shown.PreviewPicture, Key.Escape);
            Assert.IsFalse(shown.Preview.IsOpen);
            Assert.AreSame(shown.Grid, shown.Focused);
            Assert.AreEqual(items[1].Id, shown.Listing.Focused);
            CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Selection.ToList());
        });
    }

    [TestMethod]
    public async Task WithTheListOfEveryTagOpenAndFocusOnThePictureTheArrowsStepNothing()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("list lion.png"), shown.Picture("list other.png"));
        await shown.Tag([items.Single(item => item.Name == "list other")], "stare");
        var lion = items.Single(item => item.Name == "list lion");
        TrayDesktop.Run(() => shown.OpenPreview(lion));
        await Eventually(() => TrayDesktop.Get(() => EveryTagButton(shown.PreviewTags.Field).IsEffectivelyVisible));

        // Clicked as the pointer clicks it, leaving focus on the picture.
        TrayDesktop.Run(() => RaiseClick(EveryTagButton(shown.PreviewTags.Field)));
        await Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Picker.IsOpen));
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(shown.PreviewPicture, shown.Focused);
            var selected = shown.Listing.Selection.ToList();
            foreach (var key in new[] { Key.Left, Key.Up, Key.Right, Key.Down })
            {
                Tap(shown.PreviewPicture, key);
                Assert.AreEqual(lion.Id, shown.Preview.Item?.Id, $"{key} steps nothing from under the list (G19)");
                Assert.IsTrue(shown.PreviewTags.Field.Picker.IsOpen, $"{key} leaves the list open");
            }

            CollectionAssert.AreEqual(selected, shown.Listing.Selection.ToList());
        });
    }

    [TestMethod]
    public async Task RightPressedWhileARenameIsRetriedStepsAtOnceAndTheRenameLandsOnTheItemItWasTypedFor()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("held first.png"), shown.Picture("held second.png"));
        var items = TrayDesktop.Get(() => shown.Listing.Items.ToArray());
        var (typedFor, next) = (items[0], items[1]);
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(typedFor);
            Press(shown.PreviewPicture, Key.F2);
            Type(shown.NameBox, "renamed");
        });

        // A scanner holding the file, as E3 admits: the rename Enter commits is retried until it lets go.
        using (new FileStream(typedFor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TrayDesktop.Run(() =>
            {
                Tap(shown.NameBox, Key.Enter);
                Assert.AreSame(shown.PreviewPicture, shown.Focused);
                Tap(shown.PreviewPicture, Key.Right);
                AssertPreview(shown, next, "Right steps at once, the rename still retried (G19)");
            });
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Any(item => item.Id == typedFor.Id && item.Name == "renamed")));
        TrayDesktop.Run(() =>
        {
            AssertPreview(shown, next, "the preview stays on the item stepped to");
            Assert.AreEqual(next.Name, shown.NameField.Shown, "which keeps its own name");
        });
    }

    private static void AssertPreview(ShownWindow shown, StashItem item, string when)
    {
        Assert.IsTrue(shown.Preview.IsOpen, when);
        Assert.AreEqual(item.Id, shown.Preview.Item?.Id, when);
        CollectionAssert.AreEqual(new[] { item.Id }, shown.Listing.Selection.ToList(), when);
    }

    private static void AssertStar(ShownWindow shown, string glyph)
    {
        var star = PreviewStarTests.StarButton(shown);
        Assert.IsTrue(star.IsEffectivelyVisible);
        Assert.AreEqual(glyph, ((TextBlock)star.Content!).Text, "the preview shows the current item's star (G19)");
    }

    private static async Task MarkFavorite(ShownWindow shown, StashItem item)
    {
        var result = (await shown.Listing.Store!.SetFavorite([item], true)).Single();
        Assert.IsTrue(result.Succeeded, result.Failure?.Sentence);
        await TrayDesktop.Get(shown.Listing.Refresh);
    }

    private static async Task PausePreview(ShownWindow shown)
    {
        await Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is not null));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            Assert.IsTrue(shown.PreviewPicture.Animated);
            shown.Click(shown.PreviewPicture);
            Assert.IsTrue(shown.PreviewPicture.Paused);
        });
    }

    private static async Task AssertDestinationPlaying(ShownWindow shown)
    {
        await Eventually(() => TrayDesktop.Get(() =>
        {
            if (shown.PreviewPicture.Image is not WriteableBitmap { PixelSize: { Width: 8, Height: 6 } } image)
            {
                return false;
            }

            // Only the destination GIF has a solid blue frame. Read its center to distinguish the other animations.
            using var frame = image.Lock();
            return unchecked((uint)Marshal.ReadInt32(frame.Address, 3 * frame.RowBytes + 4 * sizeof(uint))) == 0xFF0000FF;
        }));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.PreviewPicture.Animated);
            Assert.IsFalse(shown.PreviewPicture.Paused, "the destination's play starts even after a pause on another item (G19)");
        });
    }
}
