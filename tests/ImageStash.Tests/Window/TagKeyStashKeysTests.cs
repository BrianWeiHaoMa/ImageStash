using Avalonia.Input;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Where <c>Ctrl+T</c> goes (G7, G17, G19): from the search box and the grid, the window's, so the box never takes it as
/// typing; from the preview, its buttons and its name in edit, the move to the preview's tag box; inside a tag field's
/// own box and in the editor, nothing. The name in edit keeps every other key a field keeps.
/// </summary>
[TestClass]
public sealed class TagKeyStashKeysTests
{
    [TestMethod]
    [DataRow(KeyPlace.SearchBox, KeyAction.AddTags)]
    [DataRow(KeyPlace.Grid, KeyAction.AddTags)]
    [DataRow(KeyPlace.Preview, KeyAction.AddTags)]
    [DataRow(KeyPlace.PreviewButton, KeyAction.AddTags)]
    [DataRow(KeyPlace.PreviewName, KeyAction.AddTags)]
    [DataRow(KeyPlace.Field, KeyAction.None)]
    [DataRow(KeyPlace.Editor, KeyAction.None)]
    public void CtrlTWorksFromTheBoxTheGridAndThePreviewAndNotInATagFieldOrTheEditor(KeyPlace place, KeyAction expected)
    {
        Assert.AreEqual(expected, StashKeys.Route(Key.T, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.T, KeyModifiers.None, place), "T alone is typing");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.T, KeyModifiers.Control | KeyModifiers.Shift, place));
    }

    [TestMethod]
    public void CtrlTFromTheBoxIsTheWindowsWhateverTheBoxHoldsSelected() =>
        Assert.AreEqual(KeyAction.AddTags, StashKeys.Route(Key.T, KeyModifiers.Control, KeyPlace.SearchBox, boxTextSelected: true), "handled before the box, so its text stays as it was (G7)");

    [TestMethod]
    public void ThePreviewsNameInEditIsToldApartFromATagFieldOnlyWhileThePreviewShowsAndNoEditCoversIt()
    {
        Assert.AreEqual(KeyPlace.PreviewName, StashKeys.PlaceOf(inSearchBox: false, inOtherField: true, previewOpen: true, onPreviewButton: false, editOpen: false, inPreviewName: true));
        Assert.AreEqual(KeyPlace.Field, StashKeys.PlaceOf(inSearchBox: false, inOtherField: true, previewOpen: true, onPreviewButton: false, editOpen: false, inPreviewName: false), "the preview's tag box is a tag field");
        Assert.AreEqual(KeyPlace.Field, StashKeys.PlaceOf(inSearchBox: false, inOtherField: true, previewOpen: true, onPreviewButton: false, editOpen: true, inPreviewName: true), "an editor's field over it");
        Assert.AreEqual(KeyPlace.Field, StashKeys.PlaceOf(inSearchBox: false, inOtherField: true, previewOpen: false, onPreviewButton: false, editOpen: false, inPreviewName: true));
    }

    [TestMethod]
    public void TheNameInEditKeepsAFieldsOwnKeys()
    {
        foreach (var (key, modifiers) in new[] { (Key.Enter, KeyModifiers.None), (Key.Escape, KeyModifiers.None), (Key.Delete, KeyModifiers.None), (Key.Space, KeyModifiers.None), (Key.F2, KeyModifiers.None), (Key.Z, KeyModifiers.Control), (Key.A, KeyModifiers.Control), (Key.C, KeyModifiers.Control), (Key.E, KeyModifiers.Control), (Key.Left, KeyModifiers.None), (Key.Up, KeyModifiers.None) })
        {
            Assert.AreEqual(StashKeys.Route(key, modifiers, KeyPlace.Field), StashKeys.Route(key, modifiers, KeyPlace.PreviewName), $"{modifiers}+{key}");
        }

        Assert.AreEqual(KeyAction.Paste, StashKeys.Route(Key.V, KeyModifiers.Control, KeyPlace.PreviewName), "the window's paste reaches it as it reaches any field (G13)");
    }

    [TestMethod]
    public void CtrlTActsOnEveryPressItHasNoRepeatToHoldBack()
    {
        var keys = new PressOnce();

        Assert.AreEqual(KeyAction.AddTags, keys.Acting(Key.T, KeyAction.AddTags));
        Assert.AreEqual(KeyAction.AddTags, keys.Acting(Key.T, KeyAction.AddTags), "a repeat lands in the field it opened, which takes it as its own");
    }
}
