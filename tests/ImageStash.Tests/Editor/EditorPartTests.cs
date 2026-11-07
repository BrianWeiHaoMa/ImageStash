using ImageStash.Editor;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The Editor part's place (Composition: Window calls Editor and nothing calls Window): no type of the part names a
/// type of the window's, so the editor reaches the stash only through Store, Media, Layers and the seams.
/// </summary>
[TestClass]
public sealed class EditorPartTests
{
    [TestMethod]
    public void ThePartReferencesNothingOfTheWindow()
    {
        var part = typeof(EditorSession).Assembly.GetTypes().Where(type => type.Namespace == typeof(EditorSession).Namespace).ToList();

        var window = part
            .SelectMany(ProductCalls.ReferencedTypes)
            .Where(type => type.Namespace is { } space && (space == "ImageStash.Window" || space.StartsWith("ImageStash.Window.", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Distinct()
            .ToList();

        Assert.Contains(typeof(EditorSession), part);
        Assert.Contains(typeof(HideTool), part);
        Assert.IsEmpty(window, string.Join(", ", window));
    }
}
