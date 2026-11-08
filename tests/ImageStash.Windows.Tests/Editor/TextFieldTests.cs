using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using PixelRect = ImageStash.Layers.PixelRect;
using Rect = Avalonia.Rect;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The field a text is typed in, on the real window (G21, G25, R4, N9, R5, G1, G2, G13, N3): a click adding a Meme opens
/// a focused, transparent text input over its box; what is typed, an emoji among it, reaches the layer at once and the
/// canvas draws it through Media; Enter adds a line, <c>Ctrl+Enter</c> and Esc leave; no paste of its own; a press
/// outside it closes it before the press acts, one inside it is its own; focus moving elsewhere in the editor closes it,
/// but for Text's options pressed before a letter is typed, which change the new layer while the field keeps it and,
/// once chosen, the focus; the window losing the foreground does not; the window's <c>Ctrl+O</c> and <c>Ctrl+V</c> still run and Enter never pastes;
/// a Meme's field holding the uppercase letters drawn, its caret on them.
/// </summary>
[TestClass]
public sealed class TextFieldTests
{
    /// <summary>A grey neither white nor black, which the lettering's anti-aliasing mixes only into other greys.</summary>
    internal static readonly SKColor Grey = new(110, 110, 110);

    [TestMethod]
    public async Task AClickOpensATransparentFieldOverTheBoxWhoseTypingTheCanvasDrawsEnterAddsALineCtrlEnterAndEscLeave()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        var before = await RenderedWindow.Of(shown);

        var height = TrayDesktop.Get(() =>
        {
            editor.Click(0.5, 0.25);
            var field = editor.Field;
            Assert.IsNotNull(field, "a click adds a Meme and opens its field (G21)");
            Assert.AreSame(field, shown.Focused, "focused, so typing reaches it");
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)field.Foreground!).Color, "its own letters never show (R4)");
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)field.Background!).Color);
            Assert.IsNull(field.ContextFlyout, "no menu offering a Paste (N9, R5)");
            var box = ((TextLayer)editor.Session!.Layers.Layers.Single()).Box;
            AssertOver(editor, field, box);
            ShownWindow.Type(field, "hello");
            Assert.AreEqual("HELLO", ((TextLayer)editor.Session.Layers.Layers.Single()).Text, "each character reaches the layer at once, a Meme in the uppercase it shows (G21)");
            return ((TextLayer)editor.Session.Layers.Layers.Single()).Box.Height;
        });
        var typed = await RenderedWindow.Of(shown);
        var place = TrayDesktop.Get(() => InRendering(shown, editor, ((TextLayer)editor.Session!.Layers.Layers.Single()).Box));
        Assert.DoesNotContain(0xFFFFFFFFu, before.Colors(place.X, place.Y, place.Width, place.Height), "no white there before");
        Assert.Contains(0xFFFFFFFFu, typed.Colors(place.X, place.Y, place.Width, place.Height), "the canvas draws the typed letters in the Meme's white (R4)");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Field!, Key.Enter);
            var meme = (TextLayer)editor.Session!.Layers.Layers.Single();
            Assert.AreEqual("HELLO\n", meme.Text, "Enter adds a line, stored as Media breaks lines (G25)");
            Assert.IsGreaterThan(height, meme.Box.Height, "and the box grows by it");
            AssertOver(editor, editor.Field!, meme.Box);
        });
        var withField = await RenderedWindow.Of(shown);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            Assert.IsNull(editor.Field, "Ctrl+Enter leaves the field (G25)");
            Assert.AreSame(editor.Canvas, shown.Focused, "for the canvas");
            Assert.IsTrue(editor.Session!.CanUndo && !editor.Session.Typing, "the typing run one step");
            Assert.AreEqual(0, editor.Session.Selected, "the layer still selected");
        });
        var left = await RenderedWindow.Of(shown);
        // Only the caret, a line at most two pixels wide, can differ: the field draws neither text nor a background.
        place = TrayDesktop.Get(() => InRendering(shown, editor, ((TextLayer)editor.Session!.Layers.Layers.Single()).Box));
        Assert.IsLessThanOrEqualTo((int)(2 * place.Height), EditorCanvasTests.Differences(withField, left, place), "the field adds nothing but its caret over the drawn text");

        TrayDesktop.Run(() =>
        {
            editor.DoubleClick(editor.OnCanvas(100, ((TextLayer)editor.Session!.Layers.Layers.Single()).Box.Y + 5));
            Assert.AreEqual("HELLO\n", editor.Field?.Text, "a double-click reopens the field on its text (G21)");
            ShownWindow.Press(editor.Field!, Key.Escape);
            Assert.IsNull(editor.Field, "Esc leaves the field");
            Assert.AreEqual(0, editor.Session.Selected, "and keeps the layer selected");
            ShownWindow.Press(editor.Canvas, Key.Escape);
            Assert.IsNull(editor.Session.Selected, "Esc then deselects");
            ShownWindow.Press(editor.Canvas, Key.Escape);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        Assert.IsTrue(TrayDesktop.Get(() => shown.Notices.Shown.Any(notice => notice.Text == "Discarded the edit of “grey”.")), "and Esc then leaves as Cancel does (G20)");
    }

    [TestMethod]
    public async Task AnEmojiTypedIntoTheFieldIsDrawnInColourInTheLayer()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi ");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
        });
        var letters = await RenderedWindow.Of(shown);
        var place = TrayDesktop.Get(() => Inside(InRendering(shown, editor, ((TextLayer)editor.Session!.Layers.Layers.Single()).Box)));
        Assert.AreEqual(0, Coloured(letters, place), "white letters with their outline over grey are greys alone");

        TrayDesktop.Run(() =>
        {
            editor.DoubleClick(editor.OnCanvas(100, ((TextLayer)editor.Session!.Layers.Layers.Single()).Box.Y + 5));
            // What the emoji panel inserts arrives as text input; one code unit at a time keeps the pair whole.
            ShownWindow.Type(editor.Field!, "😀");
            Assert.EndsWith("😀", ((TextLayer)editor.Session.Layers.Layers.Single()).Text, "the emoji in the layer (G21)");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
        });
        var emoji = await RenderedWindow.Of(shown);
        place = TrayDesktop.Get(() => Inside(InRendering(shown, editor, ((TextLayer)editor.Session!.Layers.Layers.Single()).Box)));
        Assert.IsGreaterThan(20, Coloured(emoji, place), "drawn in colour, glyph by glyph from the emoji font, inside the box (G21)");
    }

    [TestMethod]
    public async Task TheFieldHasNoPasteOfItsOwnCtrlVIsTheWindowsAndCtrlCCopiesItsSelectedText()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TestClipboard.PutText("pasted");
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "own");
            ShownWindow.Press(editor.Field!, Key.V, KeyModifiers.Control);
            ShownWindow.Press(editor.Field!, Key.Insert, KeyModifiers.Shift);
            Assert.AreEqual("OWN", editor.Field!.Text, "nothing pasted into the field (N9, R5)");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Said().Contains("it holds no picture or file")));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(editor.Session!.Typing, "the field still open beneath the window's paste");
            editor.Field!.SelectAll();
            ShownWindow.Press(editor.Field, Key.C, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TestClipboard.Text() == "OWN");
    }

    [TestMethod]
    public async Task CtrlZInTheFieldUndoesWhatWasTypedAndCtrlSSavesItAsTyped()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "abc");
            ShownWindow.Press(editor.Field!, Key.Z, KeyModifiers.Control);
            Assert.IsTrue(editor.Session!.Layers.IsEmpty && editor.Field is null, "Ctrl+Z in the field undoes the typing run, the click's layer with it (G25)");
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hey");
            ShownWindow.Press(editor.Field!, Key.S, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));
        await EditorOverlayTests.Open(shown, TrayDesktop.Get(() => shown.Listing.Items.Single()));
        Assert.AreEqual("HEY", TrayDesktop.Get(() => ((TextLayer)editor.Session!.Layers.Layers.Single()).Text), "Ctrl+S in the field saves what was typed (G25)");
    }

    [TestMethod]
    public async Task APressOutsideTheFieldClosesItFirstOneInsideIsItsOwnAndFocusElsewhereInTheEditorClosesIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Drag((0.1, 0.6), (0.4, 0.9));
            editor.Choose("Text");
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hello");
        });
        // A press inside the box lands on the field, never on the canvas beneath it, once the field is drawn.
        await RenderedWindow.Of(shown);
        TrayDesktop.Run(() =>
        {
            var meme = (TextLayer)editor.Session!.Layers.Layers[1];
            var inside = editor.Canvas.TranslatePoint(editor.OnCanvas(meme.Box.X + meme.Box.Width / 2f, meme.Box.Y + meme.Box.Height / 2f), shown.Window)!.Value;
            var hit = shown.Window.InputHitTest(inside) as Visual;
            Assert.IsTrue(hit is not null && (ReferenceEquals(hit, editor.Field) || editor.Field!.IsVisualAncestorOf(hit)), $"a press inside the box is the field's, not {hit}");
            var beside = editor.Canvas.TranslatePoint(editor.At(0.25, 0.75), shown.Window)!.Value;
            Assert.AreSame(editor.Canvas, shown.Window.InputHitTest(beside), "one beside it is the canvas's: the field's place covers nothing more");

            // A press over the hide: the field closes at the press, and the release selects the hide.
            editor.Press(editor.At(0.25, 0.75));
            Assert.IsFalse(editor.Session.Typing, "closed at the press");
            Assert.IsTrue(editor.Session.CanUndo && ((TextLayer)editor.Session.Layers.Layers[1]).Text == "HELLO", "what was typed committed");
            editor.Release(editor.At(0.25, 0.75));
            Assert.AreEqual(0, editor.Session.Selected, "the hide selected on the release");

            // After leaving the field, a drag inside the box moves the layer.
            editor.Click(0.5, 0.1);
            Assert.AreEqual(1, editor.Session.Selected);
            var from = editor.OnCanvas(meme.Box.X + meme.Box.Width / 2f, meme.Box.Y + meme.Box.Height / 2f);
            editor.Drag(from, from + new Vector(0, 30));
            Assert.AreEqual(meme.Box.Y + 30, ((TextLayer)editor.Session.Layers.Layers[1]).Box.Y, "moved by the drag (G21)");

            // Focus moving to another of the editor's controls closes the field, what was typed kept.
            editor.DoubleClick(from + new Vector(0, 30));
            ShownWindow.Type(editor.Field!, "?");
            editor.Button("Label").Focus();
            Assert.IsFalse(editor.Session.Typing, "focus on a preset closed the field");
            Assert.AreEqual("HELLO?", ((TextLayer)editor.Session.Layers.Layers[1]).Text);

            // A swatch clicked while typing: the field closes first, and the swatch colours the layer.
            editor.DoubleClick(from + new Vector(0, 30));
            ShownWindow.Press(editor.Field!, Key.Back);
            ShownWindow.Type(editor.Field!, "!");
        });
        // A click lands on what the window has drawn.
        await RenderedWindow.Of(shown);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(editor.Session!.Typing, "the field open as the swatch is clicked");
            shown.Click(editor.Button(ColorChoices.Hex(ColorChoices.Red)));
            Assert.IsFalse(editor.Session.Typing, "focus moving to another of the editor's controls closed the field");
            var red = (TextLayer)editor.Session.Layers.Layers[1];
            Assert.IsTrue(red.Text == "HELLO!" && red.Fill == ColorChoices.Red, $"and the swatch applied to the layer as typed: {red.Text}, {red.Fill}");
        });
    }

    [TestMethod]
    public async Task BeforeALetterIsTypedTextsOptionsChangeTheNewLayerWhileTheFieldKeepsItAndTheFocus()
    {
        using var shown = ShownWindow.Open(fonts: new PresetFonts());
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            Clicked(editor.Button(ColorChoices.Hex(ColorChoices.Red)));
            Assert.IsTrue(editor.Session!.Typing && editor.Field is not null, "a swatch pressed before a letter is typed leaves the field open (G21)");
            Assert.AreEqual(ColorChoices.Red, ((TextLayer)editor.Session.Layers.Layers.Single()).Fill, "and colours the new layer");
            Assert.AreSame(editor.Field, shown.Focused, "the field has the focus back, so typing goes on there");

            Clicked(editor.Button("Label"));
            var label = (TextLayer)editor.Session.Layers.Layers.Single();
            Assert.IsTrue(editor.Session.Typing && label.Preset == TextPreset.Label && label.Fill == ColorChoices.Red, "a preset pressed makes the new layer that preset, its colour kept");
            Assert.AreSame(editor.Field, shown.Focused);

            Clicked(editor.Button("More colors"));
            Assert.IsTrue(Picker(editor), "the colour picker opens");
            editor.Click(0.9, 0.9);
            Assert.IsFalse(Picker(editor), "a press on the picture closes it");
            Assert.IsTrue(editor.Session.Typing && editor.Session.Layers.Layers.Count == 1, "and does nothing more: the field and its layer stay");
            Assert.AreSame(editor.Field, shown.Focused);

            Clicked(editor.Button("Font"));
            Assert.IsTrue(editor.Session.Typing, "the font button pressed leaves the field open");
            Assert.IsNotNull(editor.OpenMenu, "and opens the font menu");
            ShownWindow.Press(editor.OpenMenu.Items.OfType<MenuItem>().First(), Key.Escape);
            editor.OpenMenu?.Close();
            Assert.IsTrue(editor.Session.Typing && editor.Session.Layers.Layers.Count == 1, "Esc in the menu leaves the menu, never the field: its layer stays");

            Clicked(editor.Button("Font"));
            editor.ClickMenuEntry("Georgia");
            Assert.IsTrue(editor.Session.Typing && ((TextLayer)editor.Session.Layers.Layers.Single()).Font == "Georgia", "a font chosen from it goes on the new layer");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Field is { } field && ReferenceEquals(shown.Focused, field)));

        TrayDesktop.Run(() =>
        {
            Clicked(editor.Button("Font"));
            editor.ClickMenuEntry("All fonts…");
        });
        await Task.Delay(200);
        TrayDesktop.Run(() =>
        {
            var list = editor.Overlay.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "All fonts");
            var entry = list.GetVisualDescendants().OfType<ToggleButton>().First(each => each.IsEffectivelyVisible && AutomationProperties.GetName(each) != "Georgia");
            var family = AutomationProperties.GetName(entry);
            Clicked(entry);
            Assert.IsTrue(editor.Session!.Typing && ((TextLayer)editor.Session.Layers.Layers.Single()).Font == family, "a family from All fonts… goes on the new layer, the field open");
            Assert.IsTrue(editor.Overlay.GetVisualDescendants().Contains(list) && list.IsEffectivelyVisible && entry.IsChecked == true, "the list stays open, the family checked");
            Assert.AreSame(editor.Field, shown.Focused);

            ShownWindow.Press(editor.Field!, Key.Escape);
            Assert.IsFalse(editor.Overlay.GetVisualDescendants().Contains(list), "Esc closes the list");
            Assert.IsTrue(editor.Session.Typing && editor.Session.Layers.Layers.Count == 1, "and leaves the field and its layer");
            Assert.AreSame(editor.Field, shown.Focused);

            ShownWindow.Type(editor.Field!, "go");
            var typed = (TextLayer)editor.Session.Layers.Layers.Single();
            Assert.AreEqual(("go", TextPreset.Label, family, ColorChoices.Red), (typed.Text, typed.Preset, typed.Font, typed.Fill), "what is typed lands in the layer as chosen");
            ShownWindow.Press(editor.Field!, Key.Escape);
            Assert.IsFalse(editor.Session.Typing);
            editor.Session.Undo();
            Assert.IsEmpty(editor.Session.Layers.Layers, "the add, the choices and the letters are one step (G25)");

            // With a letter typed (into a Label, the preset chosen last), a swatch pressed closes the field first, what was typed
            // kept, and colours the layer as its own step.
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "ab");
            Clicked(editor.Button(ColorChoices.Hex(ColorChoices.Red)));
            var lettered = (TextLayer)editor.Session.Layers.Layers.Single();
            Assert.IsTrue(!editor.Session.Typing && lettered.Text == "ab" && lettered.Fill == ColorChoices.Red, $"the field closed and the swatch applied: {lettered.Text}, {lettered.Fill}");
            editor.Session.Undo();
            var undone = (TextLayer)editor.Session.Layers.Layers.Single();
            Assert.AreEqual(("ab", ColorChoices.Swatches[0]), (undone.Text, undone.Fill), "the colour its own step after the typed one, the new Label's own white back");
        });
    }

    [TestMethod]
    public async Task WithTheRealPointerAnEmptyFieldKeepsItsLayerThroughAHeldSwatchTheFontMenuAndAllFontsAndTakesTheKeysTypedAfter()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open(fonts: new PresetFonts());
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        var window = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        // The real pointer lands where the last rendered frame drew things.
        await Task.Delay(200);
        TestMouse.Click(TrayDesktop.Get(() => editor.Canvas.PointToScreen(editor.At(0.5, 0.25))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Field is { } field && ReferenceEquals(shown.Focused, field)));
        await Task.Delay(200);

        var red = TestMouse.Centre(TrayDesktop.Get(() => editor.Button(ColorChoices.Hex(ColorChoices.Red))));
        TestMouse.Press(red);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Button(ColorChoices.Hex(ColorChoices.Red)).IsPressed));
        TrayDesktop.Run(() => Assert.IsTrue(editor.Session!.Typing && editor.Session.Layers.Layers.Count == 1, "a swatch held down before a letter is typed leaves the field and its layer"));
        TestMouse.Release(red);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Layers.Layers.Single() is TextLayer { Fill: var fill } && fill == ColorChoices.Red));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Typing && ReferenceEquals(shown.Focused, editor.Field)));

        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => editor.Button("Font"))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.OpenMenu is { IsOpen: true }));
        await Task.Delay(200);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => editor.OpenMenu!.Items.OfType<MenuItem>().Single(entry => (string)entry.Header! == "Georgia"))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Layers.Layers.Single() is TextLayer { Font: "Georgia" } && editor.OpenMenu is not { IsOpen: true }));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Typing && ReferenceEquals(shown.Focused, editor.Field)));

        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => editor.Button("Font"))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.OpenMenu is { IsOpen: true }));
        await Task.Delay(200);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => editor.OpenMenu!.Items.OfType<MenuItem>().Single(entry => (string)entry.Header! == "All fonts…"))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => AllFonts(editor) is not null));
        await Task.Delay(200);
        var (entry, family) = TrayDesktop.Get(() =>
        {
            var chosen = AllFonts(editor)!.GetVisualDescendants().OfType<ToggleButton>().First(each => each.IsEffectivelyVisible && AutomationProperties.GetName(each) != "Georgia");
            return (TestMouse.Centre(chosen), AutomationProperties.GetName(chosen));
        });
        TestMouse.Click(entry);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Layers.Layers.Single() is TextLayer { Font: var font } && font == family));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Typing && ReferenceEquals(shown.Focused, editor.Field)));
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { family }, AllFonts(editor)!.GetVisualDescendants().OfType<ToggleButton>().Where(each => each.IsChecked == true).Select(AutomationProperties.GetName).ToList(), "the list stays open, the family chosen checked"));
        // The list stands over the picture's right part: the press goes to the picture beside it, not to an entry under it.
        var beside = TrayDesktop.Get(() =>
        {
            var list = AllFonts(editor)!;
            var at = editor.Canvas.PointToScreen(editor.At(0.1, 0.9));
            var (from, to) = (list.PointToScreen(default), list.PointToScreen(new Avalonia.Point(list.Bounds.Width, list.Bounds.Height)));
            Assert.IsFalse(at.X >= from.X && at.X < to.X && at.Y >= from.Y && at.Y < to.Y, $"the press at {at} is on the picture, beside the list at {from}..{to}");
            return at;
        });
        TestMouse.Click(beside);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => AllFonts(editor) is null));
        TrayDesktop.Run(() => Assert.IsTrue(editor.Session!.Typing && editor.Session.Layers.Layers.Count == 1 && ReferenceEquals(shown.Focused, editor.Field), "a press on the picture closes the list and does nothing more"));

        // The keys go to the window in front: typed only once it is this one, never into another program.
        await ShownWindow.Eventually(() => FrontWindow.GetForegroundWindow() == window);
        TestHotKeys.Tap('G');
        TestHotKeys.Tap('O');
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Layers.Layers.Single() is TextLayer { Text: "GO" }));
        TrayDesktop.Run(() =>
        {
            var typed = (TextLayer)editor.Session!.Layers.Layers.Single();
            Assert.AreEqual((ColorChoices.Red, family), (typed.Fill, typed.Font), "the letters typed after the choices land in the layer as chosen");
        });
    }

    [TestMethod]
    public async Task ATextOptionReachedByKeysKeepsTheFocusThroughItsChoiceAndOtherChangesWhileTheEmptyFieldStaysOpen()
    {
        using var shown = ShownWindow.Open(fonts: new PresetFonts());
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            var label = editor.Button("Label");
            label.Focus(NavigationMethod.Tab);
            Assert.IsTrue(editor.Session!.Typing, "Tab onto Text's options before a letter is typed leaves the field open");

            ShownWindow.Press(label, Key.Enter);
            Assert.IsTrue(editor.Session.Typing && editor.Session.Layers.Layers.Single() is TextLayer { Preset: TextPreset.Label }, "Enter chooses the preset for the new layer");
            Assert.AreSame(label, shown.Focused, "and focus stays where the keys put it, as a key's choice leaves it");
            editor.Session.Compare = true;
            editor.Session.Compare = false;
            Assert.AreSame(label, shown.Focused, "nor does another change of the session's take it");

            var red = editor.Button(ColorChoices.Hex(ColorChoices.Red));
            red.Focus(NavigationMethod.Directional);
            ShownWindow.Press(red, Key.Space);
            red.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space, Source = red });
            Assert.IsTrue(editor.Session.Typing && editor.Session.Layers.Layers.Single() is TextLayer { Fill: var fill } && fill == ColorChoices.Red, "Space chooses the swatch");
            Assert.AreSame(red, shown.Focused);

            Clicked(editor.Button(ColorChoices.Hex(ColorChoices.Swatches[4])));
            Assert.AreSame(editor.Field, shown.Focused, "a choice made with the pointer gives the field the focus back");
        });
    }

    [TestMethod]
    public async Task WithTheFieldOpenTheFlyoutStaysASummonReturnsToTheFieldCtrlOOpensThePickerAndEnterNeverPastes()
    {
        var target = new PresetTarget { Before = null };
        var dialogs = new PresetDialogs();
        using var shown = ShownWindow.Open(target: target, dialogs: dialogs);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => Grey))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, PresetTarget.App("Relay")), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            shown.Deactivate();
        });
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible && editor.Session!.Typing && editor.Field is not null, "the flyout and the field stay on losing the foreground (G2)");
            shown.Window.SummonFromShortcut(new ForegroundReading(false, PresetTarget.App("Relay")), new SummonAnchor(null));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Field is { } field && ReferenceEquals(shown.Focused, field)));

        TrayDesktop.Run(() => ShownWindow.Press(editor.Field!, Key.O, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => dialogs.OpenedImportPickers == 1));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(editor.Session!.Typing && editor.Field?.Text == "HI", "Ctrl+O is the window's, and the field stays open (G13)");
            ShownWindow.Press(editor.Field!, Key.Enter);
            Assert.AreEqual("HI\n", editor.Field!.Text, "Enter is the field's new line");
        });
        await Task.Delay(300);
        Assert.IsEmpty(target.Pasted, "Enter never pastes in the editor (G20, N3)");
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Field!, Key.Tab);
            Assert.IsTrue(shown.Focused is Visual focus && editor.Overlay.IsVisualAncestorOf(focus), $"Tab moves among the editor's own controls, never to the grid beneath (G7): {shown.Focused}");
            Assert.IsFalse(editor.Session!.Typing, "and leaving the field ends the typing");
        });
    }

    [TestMethod]
    public async Task AMemesCaretEndsAtTheUppercaseLettersItShows()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("meme.png", 400, 300, (_, _) => Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.2);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            shown.Window.UpdateLayout();
            editor.Session!.Tools.OfType<TextTool>().Single().Choose("Georgia");
            editor.Session.BeginTyping();
            var field = editor.Field!;
            field.SelectAll();
            // As keys and the emoji panel reach it: text input the box inserts at its caret.
            ShownWindow.Type(field, "minimum");
            shown.Window.UpdateLayout();
            var layer = editor.Session.Layers.Layers.OfType<TextLayer>().Single();
            Assert.AreEqual(TextPreset.Meme, layer.Preset);
            var presenter = field.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
            var scale = editor.Canvas.Mapping()!.Box.Width / editor.Canvas.Mapping()!.Composition.Width;
            var width = ImageStash.Media.LayerComposer.TextSize(layer with { Preset = TextPreset.Label, Text = "MINIMUM" }).Width;
            var expected = (layer.Box.Width + width) / 2d * scale;
            Assert.AreEqual(field.Text!.Length, field.CaretIndex, "the caret at the end of what was typed");
            var actual = presenter.TextLayout.HitTestTextPosition(field.CaretIndex).X;
            Assert.AreEqual(expected, actual, 2.0, $"the caret, after {field.Text}, follows the uppercase lettering the canvas shows (G21, G25), two units for rounding");
            Assert.AreEqual("MINIMUM", layer.Text, "the Meme keeps the letters it shows");

            field.CaretIndex = 3;
            ShownWindow.Type(field, "ß");
            Assert.AreEqual("MINSSIMUM", field.Text, "a character whose capital is two letters lands as both");
            Assert.AreEqual(5, field.CaretIndex, "with the caret after both, where the next letter goes");
            Assert.AreEqual("MINSSIMUM", editor.Session.Layers.Layers.OfType<TextLayer>().Single().Text);
        });
    }

    /// <summary>
    /// A click on <paramref name="button"/> as the pointer makes one, without the rendered frame a raised release needs to
    /// land on it: the press's focus moved to it, then its Click.
    /// </summary>
    private static void Clicked(Button button)
    {
        button.Focus(NavigationMethod.Pointer);
        ShownWindow.RaiseClick(button);
    }

    /// <summary>The list All fonts… shows in the editor's pane, or null while it shows none.</summary>
    private static ScrollViewer? AllFonts(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "All fonts");

    /// <summary>Whether the colour picker shows in the editor.</summary>
    private static bool Picker(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<Control>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Color picker");

    /// <summary>Checks that <paramref name="field"/> lies over <paramref name="box"/> as the canvas shows it, within two units.</summary>
    private static void AssertOver(ShownEditor editor, TextBox field, PixelRect box)
    {
        field.UpdateLayout();
        var expected = editor.Canvas.Mapping()!.ToCanvas(new System.Drawing.RectangleF(box.X, box.Y, box.Width, box.Height));
        var at = field.TranslatePoint(default, editor.Canvas)!.Value;
        Assert.IsTrue(Math.Abs(at.X - expected.X) <= 2 && Math.Abs(at.Y - expected.Y) <= 2 && Math.Abs(field.Bounds.Width - expected.Width) <= 2 && Math.Abs(field.Bounds.Height - expected.Height) <= 2,
            $"the field at {new Rect(at, field.Bounds.Size)} over the box at {expected}");
    }

    /// <summary>A rectangle of the original's grid where the rendering shows it, in physical pixels.</summary>
    internal static Rect InRendering(ShownWindow shown, ShownEditor editor, PixelRect box)
    {
        var canvas = RenderedWindow.Place(shown.Window, editor.Canvas);
        var shownBox = editor.Canvas.Mapping()!.ToCanvas(new System.Drawing.RectangleF(box.X, box.Y, box.Width, box.Height));
        var scaling = shown.Window.RenderScaling;
        return new Rect(canvas.X + shownBox.X * scaling, canvas.Y + shownBox.Y * scaling, shownBox.Width * scaling, shownBox.Height * scaling);
    }

    /// <summary>The inside of a box's place, clear of its outline and its corner handles.</summary>
    private static Rect Inside(Rect place) => place.Deflate(6);

    /// <summary>How many pixels inside <paramref name="area"/> are coloured rather than grey: their channels apart by more than 40.</summary>
    private static int Coloured(RenderedWindow rendered, Rect area)
    {
        var count = 0;
        for (var y = (int)Math.Ceiling(area.Y); y < (int)(area.Y + area.Height); y++)
        {
            for (var x = (int)Math.Ceiling(area.X); x < (int)(area.X + area.Width); x++)
            {
                var pixel = rendered.At(x, y);
                var (r, g, b) = ((int)(pixel >> 16) & 0xFF, (int)(pixel >> 8) & 0xFF, (int)pixel & 0xFF);
                count += Math.Max(Math.Max(r, g), b) - Math.Min(Math.Min(r, g), b) > 40 ? 1 : 0;
            }
        }

        return count;
    }
}
