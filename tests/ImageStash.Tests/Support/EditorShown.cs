using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ImageStash.Editor;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// The editor as the stash window shows it, over a session: the real <see cref="EditorOverlay"/>, made and read on
/// <see cref="ControlThread"/>, where a check using it runs, its controls found by what a screen reader calls them, as
/// the desktop checks find them. With no Avalonia platform here, what it shows is read from the controls it holds.
/// </summary>
internal sealed class EditorShown
{
    private EditorShown(EditorOverlay overlay) => Overlay = overlay;

    public EditorOverlay Overlay { get; }

    /// <summary>The names of the head's buttons that show, left to right.</summary>
    public IReadOnlyList<string?> HeadShown =>
        [.. Head.GetLogicalDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(AutomationProperties.GetName)];

    private Control Head => (Control)Control<Button>("Back").GetLogicalParent()!;

    /// <summary>Shows <paramref name="session"/> in a new overlay, as the window does when an edit opens.</summary>
    public static EditorShown Over(EditorSession session)
    {
        var overlay = new EditorOverlay(_ => { }, new TenFonts());
        overlay.Show(session);
        return new EditorShown(overlay);
    }

    /// <summary>The one control of type <typeparamref name="T"/> called <paramref name="name"/>, shown or not.</summary>
    public T Control<T>(string name)
        where T : Control =>
        Overlay.GetLogicalDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    public Button Button(string name) => Control<Button>(name);

    /// <summary>Whether the control called <paramref name="name"/> shows and can be clicked: absent, held dimmed, or able.</summary>
    public (bool Shown, bool Able) State(string name)
    {
        var control = Control<Control>(name);
        return (control.IsEffectivelyVisible, control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
    }

    /// <summary>Clicks <paramref name="name"/> as a pointer's click would, through its Click event.</summary>
    public void Click(string name) => Control<Button>(name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    /// <summary>F8's ten, each installed.</summary>
    private sealed class TenFonts : IFonts
    {
        public IReadOnlyList<string> Families() => FontMenu.Listed;

        public bool IsInstalled(string family) => true;
    }
}
