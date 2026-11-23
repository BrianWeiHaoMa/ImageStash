using Avalonia.Input;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The tile's menu's Add tags… (G17): for one item right after Rename, for several right before Add tag ▸, showing
/// <c>Ctrl+T</c>, beside Add tag ▸ and its Type a tag…, which stay; the preview's ⋯ menu unchanged (G19). The preview's
/// empty tag box names the key; the "Tags of …" field's does not.
/// </summary>
[TestClass]
public sealed class AddTagsItemMenuTests
{
    [TestMethod]
    public void ForOneItemAddTagsComesRightAfterRenameAndForSeveralRightBeforeAddTag()
    {
        var one = ItemMenu.Entries(TestItems.Many(1), null).Select(entry => entry.Action).ToList();
        var several = ItemMenu.Entries(TestItems.Many(3), RecordingTarget.App("Relay")).Select(entry => entry.Action).ToList();

        Assert.AreEqual(one.IndexOf(ItemMenuAction.Rename) + 1, one.IndexOf(ItemMenuAction.AddTags));
        Assert.AreEqual(one.IndexOf(ItemMenuAction.AddTags) + 1, one.IndexOf(ItemMenuAction.AddTag), "Add tag ▸ stays, after it");
        Assert.AreEqual(several.IndexOf(ItemMenuAction.AddTags) + 1, several.IndexOf(ItemMenuAction.AddTag), "beside the tag entries");
        Assert.AreEqual(1, one.Count(action => action == ItemMenuAction.AddTags));
        Assert.AreEqual(1, several.Count(action => action == ItemMenuAction.AddTags));
    }

    [TestMethod]
    public void AddTagsShowsCtrlTAsAnEntryShowsItsKey()
    {
        foreach (var entries in new[] { ItemMenu.Entries(TestItems.Many(1), null), ItemMenu.Entries(TestItems.Many(2), null) })
        {
            var entry = entries.Single(entry => entry.Action == ItemMenuAction.AddTags);
            Assert.AreEqual("Add tags…", entry.Label);
            Assert.AreEqual(new KeyGesture(Key.T, KeyModifiers.Control), entry.Gesture);
            Assert.IsNull(entry.Tag, "an entry of the menu itself, not of a tag list");
        }
    }

    [TestMethod]
    public void AddTagStillListsTypeATagFirstAndThePreviewsMenuHasNoAddTags()
    {
        var addTag = TagLists.AddTagEntries([], TestItems.Many(1));

        Assert.AreEqual((ItemMenuAction.TypeATag, "Type a tag…"), (addTag[0].Action, addTag[0].Label));
        Assert.IsFalse(ItemMenu.PreviewEntries(TestItems.Sized(1, 100, 100)).Any(entry => entry.Action == ItemMenuAction.AddTags), "the preview shows its own tag box (G19)");
        Assert.IsFalse(ItemMenu.PreviewEntries(TestItems.Sized(1, 100, 100) with { Edited = true }).Any(entry => entry.Action == ItemMenuAction.AddTags));
    }

    [TestMethod]
    public async Task ThePreviewsEmptyTagBoxNamesTheKeyAndTheTagsOfFieldsDoesNot()
    {
        using var listed = await ListedStash.Open();
        ControlThread.Run(() =>
        {
            Assert.AreEqual("Add a tag · Ctrl+T", new PreviewTags().Field.Placeholder);
            Assert.AreEqual("Type a tag", new TagFieldOverlay(listed.Listing, listed.Organising, listed.Notices).Field.Placeholder);
        });
    }
}
