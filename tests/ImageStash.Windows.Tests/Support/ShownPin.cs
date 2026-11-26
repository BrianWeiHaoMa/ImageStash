using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The stash window's pin as a person and the published scripts find it (G2): the one control named "Keep open" or
/// "Unpin", in sight, wherever it stands. Run on the UI thread.
/// </summary>
public static class ShownPin
{
    /// <summary>The pin, failing unless exactly one control carries either name, in sight or not, and it is in sight.</summary>
    public static ToggleButton Of(Avalonia.Controls.Window window)
    {
        var named = window.GetVisualDescendants().OfType<Control>().Where(control => AutomationProperties.GetName(control) is "Keep open" or "Unpin").ToList();
        Assert.HasCount(1, named, "one pin, never one per head");
        Assert.IsTrue(named[0].IsEffectivelyVisible, "the pin in sight");
        return (ToggleButton)named[0];
    }

    /// <summary>The pin of <paramref name="shown"/>'s window.</summary>
    public static ToggleButton Of(ShownWindow shown) => Of(shown.Window);
}
