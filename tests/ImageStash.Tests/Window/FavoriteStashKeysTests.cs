using Avalonia.Input;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Where <c>Ctrl+D</c> goes (G7, G17, G19): in the grid, and in the preview on its picture or a focused button, its star
/// among them, the favorite's toggle; in the search box, a tag field, the preview's name in edit and the editor, theirs.
/// </summary>
[TestClass]
public sealed class FavoriteStashKeysTests
{
    [TestMethod]
    [DataRow(KeyPlace.Grid, KeyAction.ToggleFavorite)]
    [DataRow(KeyPlace.Preview, KeyAction.ToggleFavorite)]
    [DataRow(KeyPlace.PreviewButton, KeyAction.ToggleFavorite)]
    [DataRow(KeyPlace.SearchBox, KeyAction.None)]
    [DataRow(KeyPlace.Field, KeyAction.None)]
    [DataRow(KeyPlace.PreviewName, KeyAction.None)]
    [DataRow(KeyPlace.Editor, KeyAction.None)]
    public void CtrlDTogglesTheFavoriteFromTheGridAndThePreviewAndIsTheOwnKeyOfEveryField(KeyPlace place, KeyAction expected)
    {
        Assert.AreEqual(expected, StashKeys.Route(Key.D, KeyModifiers.Control, place));
    }

    [TestMethod]
    public void SpaceOnAFocusedPreviewButtonIsTheButtonsOwnPress()
    {
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.PreviewButton), "the star, focused, takes Space as its press (G7)");
        Assert.AreEqual(KeyAction.TogglePreview, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.Preview));
    }
}
