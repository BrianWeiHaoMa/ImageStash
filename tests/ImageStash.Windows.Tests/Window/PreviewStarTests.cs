using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class PreviewStarTests
{
    [TestMethod]
    public async Task ARealClickOnTheStarInTheHeadFavoritesThePreviewedItem()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("click favorite.png"))).Single();
        TransferredStash.CloseNotices(shown);
        TrayDesktop.Run(() => shown.OpenPreview(item));
        var star = TrayDesktop.Get(() =>
        {
            var button = StarButton(shown);
            var head = button.GetVisualParent();
            Assert.IsInstanceOfType<Grid>(head);
            Assert.AreSame(head, shown.NameField.GetVisualParent());
            Assert.AreSame(head, shown.MoreButton.GetVisualParent());
            Assert.IsGreaterThan(Grid.GetColumn(shown.NameField), Grid.GetColumn(button));
            Assert.IsGreaterThan(Grid.GetColumn(button), Grid.GetColumn(shown.MoreButton));
            AssertStar(button, favorite: false);
            return button;
        });
        await Eventually(() => TrayDesktop.Get(() =>
        {
            var centre = star.TranslatePoint(new Point(star.Bounds.Width / 2, star.Bounds.Height / 2), shown.Window);
            return centre is { } point && shown.Window.InputHitTest(point) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(star);
        }));

        TestMouse.Click(TestMouse.Centre(star));

        await AssertCommittedStar(shown, item, favorite: true);
    }

    [TestMethod]
    public async Task CtrlDOnThePictureFillsTheStarAfterTheStoreWrite()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("shortcut favorite.png"))).Single();
        TrayDesktop.Run(() => shown.OpenPreview(item));

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                Assert.AreSame(shown.PreviewPicture, shown.Focused);
                AssertStar(StarButton(shown), favorite: false);
                Tap(shown.PreviewPicture, Key.D, KeyModifiers.Control);
                AssertStar(StarButton(shown), favorite: false);
            });
            // Give the window a rendered frame while the held store still cannot commit the toggle (R9, G28).
            await RenderedWindow.Of(shown.Window);
            TrayDesktop.Run(() => AssertStar(StarButton(shown), favorite: false));
        }

        await AssertCommittedStar(shown, item, favorite: true);
    }

    [TestMethod]
    public async Task ACtrlDTheStoreRefusesLeavesTheStarOutlinedAndNamesTheFailure()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("refused favorite.png"))).Single();
        TransferredStash.CloseNotices(shown);
        TrayDesktop.Run(() => shown.OpenPreview(item));

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() => Tap(shown.PreviewPicture, Key.D, KeyModifiers.Control));
            // Held past the store's retry, the change is refused and named (G35).
            await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Any(notice => notice.Text.StartsWith("Couldn't add to Favorites: ", StringComparison.Ordinal))), TimeSpan.FromSeconds(30));
            TrayDesktop.Run(() => AssertStar(StarButton(shown), favorite: false));
        }

        var stored = await shown.Listing.Store!.Item(item.Id);
        Assert.IsFalse(stored.Value!.Favorite, "the flag was never written");
        TrayDesktop.Run(() => AssertStar(StarButton(shown), favorite: false));
    }

    [TestMethod]
    public async Task SpaceOnTheStarTogglesTheStoredFlagAndKeepsFocusOnTheStar()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("space favorite.png"))).Single();
        var star = TrayDesktop.Get(() =>
        {
            shown.OpenPreview(item);
            var button = StarButton(shown);
            AssertStar(button, favorite: false);
            button.Focus(NavigationMethod.Tab);
            Assert.AreSame(button, shown.Focused);
            Tap(button, Key.Space);
            return button;
        });

        await AssertCommittedStar(shown, item, favorite: true);
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(star, shown.Focused, "focus stays on the star after it fills (G7)");
            Tap(star, Key.Space);
        });
        await AssertCommittedStar(shown, item, favorite: false);
        TrayDesktop.Run(() => Assert.AreSame(star, shown.Focused, "focus stays on the star after it empties (G7)"));
    }

    [TestMethod]
    public async Task OpeningAnotherItemInTheOpenPreviewShowsItsStarInTheSameTurn()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("preview plain.png"), shown.Picture("preview starred.png"));
        var (plain, favorite) = (items[0], items[1]);
        var store = shown.Listing.Store!;
        Assert.IsTrue((await store.SetFavorite([favorite], true)).Single().Succeeded);
        var stored = (await store.Item(favorite.Id)).Value!;
        Assert.IsTrue(stored.Favorite);

        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(plain);
            AssertStar(StarButton(shown), favorite: false);

            shown.Preview.Open(stored);
            Assert.AreEqual(stored.Id, shown.Preview.Item?.Id);
            AssertStar(StarButton(shown), favorite: true);

            shown.Preview.Open(plain);
            Assert.AreEqual(plain.Id, shown.Preview.Item?.Id);
            AssertStar(StarButton(shown), favorite: false);
        });
    }

    internal static Button StarButton(ShownWindow shown) => shown.Preview.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetAutomationId(button) == "Favorite");

    private static void AssertStar(Button star, bool favorite)
    {
        Assert.IsTrue(star.IsEffectivelyVisible);
        Assert.AreEqual(favorite ? FavoriteStar.Filled : FavoriteStar.Outlined, ((TextBlock)star.Content!).Text, "the star's glyph");
        Assert.AreEqual(favorite ? "In Favorites" : "Not in Favorites", AutomationProperties.GetName(star));
        Assert.AreEqual(favorite ? "Remove from Favorites (Ctrl+D)" : "Add to Favorites (Ctrl+D)", ToolTip.GetTip(star));
    }

    private static async Task AssertCommittedStar(ShownWindow shown, StashItem item, bool favorite)
    {
        await Eventually(() => TrayDesktop.Get(() =>
            shown.Preview.Item?.Id == item.Id && ((TextBlock)StarButton(shown).Content!).Text == FavoriteStar.Glyph(favorite)));
        var stored = await shown.Listing.Store!.Item(item.Id);
        Assert.IsTrue(stored.Succeeded, stored.Failure?.Sentence);
        Assert.AreEqual(favorite, stored.Value?.Favorite, "the store holds the flag the star shows (G28, R9)");
        TrayDesktop.Run(() => AssertStar(StarButton(shown), favorite));
    }
}
