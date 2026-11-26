using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Editor;
using ImageStash.Windows.Tests.Support;
using PixelPoint = Avalonia.PixelPoint;
using PixelRect = Avalonia.PixelRect;
using PixelSize = Avalonia.PixelSize;
using Point = Avalonia.Point;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The pin in reach wherever the window's content is (G2): one pin, in the grid's head, the preview's or the editor's,
/// following the window's state whichever pinned or unpinned it; its click keeping the preview with a name half typed
/// (G19, G1) and the edit with letters being typed into a text layer and its keys (G20, G25), and the paste target (G8);
/// every head control whole inside the window at its minimum (G20, L2); and the first pin's place, where the flyout
/// stood, kept for every later pin (G2). These show the window on the primary display for a moment.
/// </summary>
[TestClass]
public sealed class PinReachTests
{
    private static readonly PasteTarget Relay = PresetTarget.App("Relay");

    [TestMethod]
    public async Task OnePinShowsInTheGridThePreviewAndTheEditorAndFollowsTheWindowsStateWhateverChangedIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("pinned.png"))).Single();
        await SummonFlyout(shown);

        TrayDesktop.Run(() =>
        {
            AssertPin(shown, pinned: false, within: null, "the grid's head");
            shown.OpenPreview(item);
            shown.Window.UpdateLayout();
            AssertPin(shown, pinned: false, within: shown.Preview, "the preview's head");
            Toggle(shown);
        });
        await PinnedIs(shown, true);
        TrayDesktop.Run(() => AssertPin(shown, pinned: true, within: shown.Preview, "pinned from the preview"));

        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Preview));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.IsOpen));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            AssertPin(shown, pinned: true, within: editor.Overlay, "the editor's head, still reading Unpin");
            Toggle(shown);
        });
        await PinnedIs(shown, false);
        TrayDesktop.Run(() =>
        {
            AssertPin(shown, pinned: false, within: editor.Overlay, "unpinned from the editor");
            ShownWindow.Tap(editor.Canvas, Key.Escape);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            AssertPin(shown, pinned: false, within: shown.Preview, "back in the preview");
            ShownWindow.Tap(shown.PreviewPicture, Key.Escape);
            shown.Window.UpdateLayout();
            AssertPin(shown, pinned: false, within: null, "back in the grid's head");
        });

        // Whatever else pins or unpins the window: the first run's pinned showing, Close, and a summon.
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonPinned();
        });
        await PinnedIs(shown, true);
        TrayDesktop.Run(() =>
        {
            AssertPin(shown, pinned: true, within: null, "the first run's pinned window");
            shown.Window.Close();
            // Hidden now, so nothing of it is in sight: the one control says it at once.
            var pin = shown.Window.GetVisualDescendants().OfType<ToggleButton>().Single(button => AutomationProperties.GetName(button) is "Keep open" or "Unpin");
            Assert.AreEqual("Keep open", AutomationProperties.GetName(pin), "Close unpins (G2)");
            Assert.IsFalse(pin.IsChecked ?? true, "Close unpins");
        });
        await SummonFlyout(shown);
        TrayDesktop.Run(() => AssertPin(shown, pinned: false, within: null, "a summon's flyout"));
    }

    [TestMethod]
    public async Task ThePinFromThePreviewKeepsItsItemTheNameHalfTypedWithFocusInItAndThePasteTarget()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("first.png"), shown.Picture("kept name.png"));
        var item = items.Single(each => each.Name == "kept name");
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible));
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            shown.Preview.EditName();
            shown.Window.UpdateLayout();
            shown.NameBox.Text = "kept na";
            Assert.AreEqual("Paste into Relay", AutomationProperties.GetName(shown.HandoffButton), "the press's target");
            Toggle(shown);
        });
        await PinnedIs(shown, true);
        TrayDesktop.Run(() => AssertPreviewKept(shown, item, "pinned"));

        TrayDesktop.Run(() => Toggle(shown));
        await PinnedIs(shown, false);
        TrayDesktop.Run(() =>
        {
            AssertPreviewKept(shown, item, "unpinned");
            // The next Esc is the preview's field's, cancelling the name, and the one after closes the preview (G19, G2).
            ShownWindow.Tap(shown.NameBox, Key.Escape);
            Assert.IsFalse(shown.Preview.EditingName, "Esc cancels the name");
            Assert.AreEqual(item.Id, shown.Preview.Item?.Id, "the preview still open");
            Assert.AreEqual("", shown.Search.Text, "the search untouched");
        });
    }

    [TestMethod]
    public async Task ThePinFromTheEditorKeepsTheEditTheLettersBeingTypedItsHistoryAndThePasteTarget()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("edited.png", 400, 300, (_, _) => TextFieldTests.Grey))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible));
        var before = TrayDesktop.Get(shown.FooterHints);
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.IsOpen));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hey");
            Toggle(shown);
        });
        await PinnedIs(shown, true);
        TrayDesktop.Run(() =>
        {
            AssertEditKept(shown, editor, "HEY", "pinned");
            // Typed after the pin, the letters still land in the layer; Ctrl+Z is the editor's (G25).
            ShownWindow.Type(editor.Field!, "!");
            Assert.AreEqual("HEY!", Text(editor), "typing goes on into the layer");
        });

        TrayDesktop.Run(() => Toggle(shown));
        await PinnedIs(shown, false);
        TrayDesktop.Run(() =>
        {
            AssertEditKept(shown, editor, "HEY!", "unpinned: a flyout that stays while the edit is open (G2)");
            ShownWindow.Tap(editor.Field!, Key.Enter, KeyModifiers.Control);
            Assert.IsFalse(editor.Session!.Typing, "Ctrl+Enter leaves the text");
            ShownWindow.Tap(editor.Canvas, Key.Z, KeyModifiers.Control);
            Assert.IsEmpty(editor.Session.Layers.Layers, "Ctrl+Z undoes the editor's step: the layer with its letters, one step");
            ShownWindow.Tap(editor.Canvas, Key.Y, KeyModifiers.Control);
            Assert.AreEqual("HEY!", Text(editor), "and Ctrl+Y redoes it");
            shown.Editing.Cancel();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        await TrayDesktop.Get(() => shown.Listing.Settle());
        CollectionAssert.AreEqual(before, TrayDesktop.Get(shown.FooterHints), "the pin and the unpin read no target (G8)");
    }

    [TestMethod]
    public async Task AtTheMinimumWidthEveryHeadControlOfTheEditorAndThePreviewLiesWhollyInsideTheWindow()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var longName = string.Concat(Enumerable.Repeat("a very long name ", 8))[..120];
        var item = (await shown.OpenStash(shown.Picture(longName + ".png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.Bounds.Width <= WindowSize.Minimum.Width + 0.5));

        TrayDesktop.Run(() =>
        {
            WindowMinimum.AssertAt(shown.Window.ClientSize, "the window at its minimum");
            shown.OpenPreview(item);
            shown.Window.UpdateLayout();
            AssertHeadWhole(shown.Preview, ["Back", PreviewStar.Name(false), "More options", "Keep open"], "the preview");
        });
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Preview));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.IsOpen));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            AssertHeadWhole(editor.Overlay, ["Back", "Undo", "Redo", "Compare", "More options", "Keep open"], "the editor");
            var name = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible && text.Text == longName);
            var natural = new TextBlock { Text = longName, FontSize = name.FontSize, FontWeight = name.FontWeight };
            natural.Measure(Size.Infinity);
            Assert.IsLessThan(natural.DesiredSize.Width, name.Bounds.Width, "the long name gives way, trimmed (G20)");
        });
    }

    [TestMethod]
    public async Task TheFirstPinComesWhereTheFlyoutStandsAndEveryLaterPinComesThereAgain()
    {
        using var shown = ShownWindow.Open();
        var primary = DisplayPlaces.Primary();
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        // Near the working area's bottom-right corner, as above the tray: the pinned window, larger, is moved inside.
        var corner = new PixelPoint(area.Right - DisplayPlaces.Scaled(primary, 40), area.Bottom - DisplayPlaces.Scaled(primary, 40));
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(corner));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible));
        await Task.Delay(300);
        var (flyout, expected) = TrayDesktop.Get(() =>
        {
            var window = shown.Window;
            var standing = new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling));
            Assert.IsNull(shown.Settings.Current.PinnedPosition, "no pinned place remembered");
            Toggle(shown);
            return (standing, Summon.Pinned(null, Summon.DisplaysOf(window.Screens), WindowSize.PinnedDefault, standing));
        });
        await PinnedIs(shown, true);
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            Assert.AreEqual(expected.Position, window.Position, "where the flyout stood, moved inside the working area");
            AssertWhole(expected.Size, window, "at the pinned size, its title bar included");
            Assert.IsTrue(expected.Intersects(flyout), "over the flyout's place");
            Assert.IsTrue(area.Contains(new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling))), "wholly inside the working area");
            Assert.AreEqual(new WindowPosition(expected.X, expected.Y), shown.Settings.Current.PinnedPosition, "the pinned window's place from then on (G2)");
            Assert.AreEqual(WindowSize.PinnedDefault, shown.Settings.Current.PinnedSize, "its size untouched");
            shown.Window.Close();
        });

        // Summoned elsewhere and pinned again: where the first pin put it.
        var elsewhere = new PixelPoint(area.X + DisplayPlaces.Scaled(primary, 30), area.Y + DisplayPlaces.Scaled(primary, 30));
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(elsewhere)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible));
        await Task.Delay(300);
        TrayDesktop.Run(() => Toggle(shown));
        await PinnedIs(shown, true);
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.AreEqual(expected.Position, shown.Window.Position, "the remembered place, not the new flyout's"));
    }

    [TestMethod]
    public async Task TheFirstRunsPinnedWindowIsCentredOnThePrimaryDisplayWhereverTheHiddenWindowLastStood()
    {
        using var shown = ShownWindow.Open();
        var primary = DisplayPlaces.Primary();
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(new PixelPoint(area.X + DisplayPlaces.Scaled(primary, 20), area.Y + DisplayPlaces.Scaled(primary, 20))));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible));
        await Task.Delay(300);

        // G2: "On first run with an empty stash the window opens pinned", with no flyout showing to pin in place of.
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonPinned();
        });
        await PinnedIs(shown, true);
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            var expected = Summon.Pinned(null, Summon.DisplaysOf(shown.Window.Screens), WindowSize.PinnedDefault);
            Assert.AreEqual(expected.Position, shown.Window.Position, "centred on the primary display, not where the hidden flyout last stood");
            Assert.IsNull(shown.Settings.Current.PinnedPosition, "a place the window chose, not the user's");
        });
    }

    /// <summary>
    /// The pin pressed, as a person's press reaches the window's focus, which it leaves where it was, let go off it, so the
    /// release clicks nothing on any desktop, then clicked: run where no frame renders, as in a service session, too.
    /// </summary>
    private static void Toggle(ShownWindow shown)
    {
        var pin = ShownPin.Of(shown);
        var mouse = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var centre = pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), shown.Window)!.Value;
        pin.RaiseEvent(new PointerPressedEventArgs(pin, mouse, shown.Window, centre, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        pin.RaiseEvent(new PointerReleasedEventArgs(pin, mouse, shown.Window, new Point(-100, -100), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        ShownWindow.RaiseClick(pin);
    }

    private static async Task SummonFlyout(ShownWindow shown)
    {
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && !shown.Window.ShowInTaskbar));
    }

    private static Task PinnedIs(ShownWindow shown, bool pinned) =>
        ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && shown.Window.ShowInTaskbar == pinned), TimeSpan.FromSeconds(10));

    /// <summary>The one pin, standing in <paramref name="within"/>'s head, else the grid's, and saying the window's state.</summary>
    private static void AssertPin(ShownWindow shown, bool pinned, Control? within, string because)
    {
        var pin = ShownPin.Of(shown);
        Assert.AreEqual(pinned ? "Unpin" : "Keep open", AutomationProperties.GetName(pin), because);
        Assert.AreEqual(pinned ? "Unpin" : "Keep open", ToolTip.GetTip(pin), because);
        Assert.AreEqual(pinned, pin.IsChecked, because);
        Assert.AreEqual(pinned ? "" : "", ((TextBlock)pin.Content!).Text, because);
        Assert.AreEqual(pinned, shown.Window.ShowInTaskbar, $"{because}: the window's state");
        var standsIn = pin.GetVisualAncestors().FirstOrDefault(ancestor => ancestor is Preview or EditorOverlay);
        Assert.AreSame(within, standsIn, $"{because}: where it stands");
    }

    private static void AssertPreviewKept(ShownWindow shown, StashItem item, string because)
    {
        Assert.AreEqual(item.Id, shown.Preview.Item?.Id, $"{because}: the same item");
        Assert.IsTrue(shown.Preview.EditingName, $"{because}: the name still in edit");
        Assert.AreEqual("kept na", shown.NameBox.Text, $"{because}: with what was typed");
        Assert.AreSame(shown.NameBox, shown.Focused, $"{because}: focus in it, never the search box behind");
        Assert.AreEqual("Paste into Relay", AutomationProperties.GetName(shown.HandoffButton), $"{because}: the target as it was (G8)");
    }

    private static void AssertEditKept(ShownWindow shown, ShownEditor editor, string letters, string because)
    {
        Assert.IsTrue(shown.Editing.IsOpen, $"{because}: the edit open");
        Assert.IsTrue(editor.Session!.Typing, $"{because}: the text still being typed");
        Assert.AreEqual(letters, Text(editor), $"{because}: the letters in the layer");
        Assert.AreSame(editor.Field, shown.Focused, $"{because}: focus in the layer's text, so Enter adds a line and never pastes (G20, G25)");
        Assert.HasCount(1, editor.Session.Layers.Layers, because);
    }

    private static string Text(ShownEditor editor) => ((TextLayer)editor.Session!.Layers.Layers.Single()).Text.ToUpperInvariant();

    /// <summary>Each named control of <paramref name="overlay"/>'s head lies whole inside it.</summary>
    private static void AssertHeadWhole(Control overlay, string[] names, string because)
    {
        foreach (var name in names)
        {
            var control = overlay.GetVisualDescendants().OfType<Control>().Single(each => each.IsEffectivelyVisible && AutomationProperties.GetName(each) == name && each is Button or ToggleButton);
            var left = control.TranslatePoint(default, overlay)!.Value.X;
            var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), overlay)!.Value.X;
            Assert.IsGreaterThanOrEqualTo(0, left, $"{because}: {name} starts inside");
            Assert.IsLessThanOrEqualTo(overlay.Bounds.Width, right, $"{because}: {name} ends inside (G20)");
        }
    }

    private static void AssertWhole(PixelSize expected, Avalonia.Controls.Window window, string because)
    {
        var outer = PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling);
        Assert.IsLessThanOrEqualTo(1, Math.Abs(outer.Width - expected.Width), $"{because}: width {outer.Width}, not {expected.Width}");
        Assert.IsLessThanOrEqualTo(1, Math.Abs(outer.Height - expected.Height), $"{because}: height {outer.Height}, not {expected.Height}");
    }
}
