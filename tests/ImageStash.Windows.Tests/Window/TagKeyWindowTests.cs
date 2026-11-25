using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// <c>Ctrl+T</c> and Add tags… on the real window (G7, G17, G19): from the grid and from the search box the key opens
/// "Tags of …" on the selection, the box's text as it was and Esc giving focus back there; in the preview it puts focus
/// in the preview's tag box, from its picture or its name in edit, whatever the grid selects; the tile's menu's Add
/// tags… opens the same field on the items it was opened on; the preview's empty tag box names the key. These checks
/// show the stash window.
/// </summary>
[TestClass]
public sealed class TagKeyWindowTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task CtrlTInTheGridWithThreeSelectedOpensTagsOfOnAllThreeAndEnterTagsEach()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("key grid a.png"), shown.Picture("key grid b.png"), shown.Picture("key grid c.png"), shown.Picture("key grid d.png"));
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[0].Id, items[1].Id, items[2].Id], items[1].Id);
            Tap(shown.Grid, Key.T, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEquivalent(new[] { items[0].Id, items[1].Id, items[2].Id }, shown.TagField.Items.Select(item => item.Id).ToList(), "every selected item (G17)");
            Type(shown.TagBox, "cat");
            shown.Window.UpdateLayout();
            Tap(shown.TagBox, Key.Enter);
        });

        await Eventually(() => new[] { 0, 1, 2 }.All(index => shown.TagsOf(items[index].Id).Contains("cat")));
        Assert.IsEmpty(shown.TagsOf(items[3].Id), "the one not selected carries nothing");
    }

    [TestMethod]
    public async Task CtrlTFromTheSearchBoxLeavesTheQueryAsItWasAndEscGivesFocusBackToTheBox()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("key box match.png"), shown.Picture("key box other.png"));
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            Type(shown.Search, "match");
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1));

        TrayDesktop.Run(() => Tap(shown.Search, Key.T, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("match", shown.Search.Text, "the box took no character from the key (G7)");
            Assert.AreEqual(items[0].Id, shown.TagField.Items.Single().Id);
            Tap(shown.TagBox, Key.Escape);
        });

        await Eventually(() => TrayDesktop.Get(() => !shown.TagField.IsVisible));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Search.IsFocused, "focus back where the key was pressed");
            Assert.AreEqual("match", shown.Search.Text);
        });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task HeldSearchResolvesKeysAfterCtrlTInTheTagField(bool openPreview, bool escape)
    {
        using var shown = ShownWindow.Open();
        var pictures = Enumerable.Range(1, 8).Select(index => shown.Picture($"cat {index:00}.png"))
            .Append(shown.Picture("dog.png"));
        var items = await shown.OpenStash(pictures.ToArray());
        TrayDesktop.Run(() => shown.Listing.Focus(items[^1].Id));
        var store = shown.Listing.Store!;
        var matches = (await store.Search("cat")).Value!.Select(item => item.Id).ToArray();
        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                shown.Search.Text = "cat";
                Assert.IsFalse(shown.Listing.Settled);
                Assert.IsTrue(shown.Grid.Focus());
                if (openPreview)
                {
                    Tap(shown.Grid, Key.Space);
                }

                Tap(shown.Grid, Key.T, KeyModifiers.Control);
                if (!escape)
                {
                    Tap(shown.Grid, Key.PageDown);
                }

                Tap(shown.Grid, escape ? Key.Escape : Key.Delete);
            });
        }

        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        CollectionAssert.AreEqual(matches, (await store.Search("cat")).Value!.Select(item => item.Id).ToArray());
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("cat", shown.Search.Text);
            Assert.AreEqual(matches[0], shown.Listing.Focused);
            Assert.AreEqual(openPreview, shown.Preview.IsOpen);
            Assert.AreEqual(!openPreview && !escape, shown.TagField.IsVisible);
            if (openPreview)
            {
                Assert.AreEqual(matches[0], shown.Preview.Item?.Id);
            }
            else if (!escape)
            {
                CollectionAssert.AreEqual(new[] { matches[0] }, shown.TagField.Items.Select(item => item.Id).ToArray());
            }

            var focused = (openPreview, escape) switch
            {
                (true, true) => shown.PreviewPicture.IsFocused,
                (true, false) => shown.PreviewTagBox.IsFocused,
                (false, true) => shown.Grid.IsFocused,
                _ => shown.TagBox.IsFocused,
            };
            Assert.IsTrue(focused, "focus follows the tag field's Esc or stays in its box");
        });
    }

    [TestMethod]
    public async Task InThePreviewCtrlTPutsFocusInItsTagBoxWhateverTheGridSelectsAndOpensNoTagsOf()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("key preview.png"), shown.Picture("key preview other.png"));
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(items[0]);
            shown.Listing.Select([], null);
            Tap(shown.PreviewPicture, Key.T, KeyModifiers.Control);
        });

        await Eventually(() => TrayDesktop.Get(() => shown.PreviewTagBox.IsFocused));
        TrayDesktop.Run(() => Assert.IsFalse(shown.TagField.IsVisible, "the \"Tags of …\" field does not open over the preview"));
    }

    [TestMethod]
    public async Task FromThePreviewsNameInEditCtrlTCommitsTheNameAndPutsFocusInTheTagBox()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("key name.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            shown.Preview.EditName();
            Type(shown.NameBox, "renamed by key");
            Tap(shown.NameBox, Key.T, KeyModifiers.Control);
        });

        await Eventually(() => TrayDesktop.Get(() => shown.PreviewTagBox.IsFocused));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Single().Name == "renamed by key"), TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task TheMenusAddTagsOpensTagsOfOnTheItemsItWasOpenedOn()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("key menu a.png"), shown.Picture("key menu b.png"), shown.Picture("key menu c.png"));
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[0].Id, items[2].Id], items[0].Id);
            var labels = shown.RightClick(items[0].Id);
            CollectionAssert.IsSubsetOf(new[] { "Add tags…", "Add tag" }, labels);
            Assert.AreEqual(labels.IndexOf("Add tags…") + 1, labels.IndexOf("Add tag"), "beside Add tag ▸, which stays");
            Assert.AreEqual(new KeyGesture(Key.T, KeyModifiers.Control), shown.MenuEntry("Add tags…").InputGesture);
            // A selection changed while the menu is open does not move what its entries act on.
            shown.Listing.Focus(items[1].Id);
            shown.ClickMenuEntry("Add tags…");
            shown.CloseMenu();
        });

        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
        TrayDesktop.Run(() => CollectionAssert.AreEquivalent(new[] { items[0].Id, items[2].Id }, shown.TagField.Items.Select(item => item.Id).ToList()));
    }

    [TestMethod]
    public async Task ThePreviewsEmptyTagBoxNamesTheKeyAndTagsOfKeepsItsWords()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("key placeholder.png"))).Single();
        TrayDesktop.Run(() => shown.OpenPreview(item));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Add a tag · Ctrl+T", shown.PreviewTagBox.PlaceholderText);
            Assert.AreEqual("Type a tag", shown.TagBox.PlaceholderText);
        });
    }
}
