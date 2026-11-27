using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Layers;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Editor;

[TestClass]
public sealed class EditorActionTests
{
    [TestMethod]
    public async Task DeleteUndoAndRedoActOnTheSelectedMarkAndSaveOnlyTheSurvivor()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("marks.png", 300, 240, (_, _) => SKColors.White))).Single();
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));

        TrayDesktop.Run(() =>
        {
            AssertActions(editor, undo: false, redo: false, delete: false);
            DrawTwoMarks(editor);
            var session = editor.Session!;
            var first = session.Layers.Layers[0];
            var second = session.Layers.Layers[1];
            AssertActions(editor, undo: true, redo: false, delete: true);

            editor.Choose("Delete");
            CollectionAssert.AreEqual(new[] { first }, session.Layers.Layers.ToArray());
            Assert.IsNull(session.Selected);
            AssertActions(editor, undo: true, redo: false, delete: false);
            editor.Choose("Undo");
            CollectionAssert.AreEqual(new[] { first, second }, session.Layers.Layers.ToArray());
            Assert.AreEqual(1, session.Selected);
            AssertActions(editor, undo: true, redo: true, delete: true);
            editor.Choose("Redo");
            CollectionAssert.AreEqual(new[] { first }, session.Layers.Layers.ToArray());
            Assert.IsNull(session.Selected);
            AssertActions(editor, undo: true, redo: false, delete: false);
            editor.Choose("Save");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));

        var store = shown.Listing.Store!;
        var saved = (await store.Item(item.Id)).Value!;
        using var picture = SKBitmap.Decode(store.Folder.InRoot(saved.FileName));
        Assert.AreNotEqual(SKColors.White, picture.GetPixel(60, 132), "the first mark is rendered");
        Assert.AreEqual(SKColors.White, picture.GetPixel(240, 132), "the deleted mark is gone from the file");
        Assert.HasCount(1, (await store.All()).Value!, "the stash item still exists");
        await TrayDesktop.Get(() => shown.Editing.Open(saved, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(1, editor.Session!.Layers.Layers);
            AssertActions(editor, undo: false, redo: false, delete: false);
        });
    }

    [TestMethod]
    public async Task TabReachesTheHistoryButtonsAndANewChangeDropsRedo()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("history.png", 300, 240, (_, _) => SKColors.White))).Single();
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            editor.Choose("Draw");
            editor.Drag((0.2, 0.2), (0.2, 0.8));
            var back = editor.Button("Back");
            Assert.IsTrue(back.Focus(NavigationMethod.Tab));
            ShownWindow.Tap(back, Key.Tab);
            var undo = editor.Button("Undo");
            Assert.IsTrue(undo.IsFocused, "the next enabled control is Undo");
            ShownWindow.Tap(undo, Key.Enter);
            Assert.IsTrue(editor.Session!.Layers.IsEmpty);
            AssertActions(editor, undo: false, redo: true, delete: false);
            Assert.IsTrue(undo.IsEffectivelyVisible && editor.Shows("Redo") && editor.Shows("Compare"), "undone to nothing, Undo dimmed in its place beside Redo (G20)");
            Assert.IsFalse(editor.Shows("Delete"), "Delete absent with nothing selected (G20)");
            Assert.IsTrue(shown.Focused is Visual focused && editor.Overlay.IsVisualAncestorOf(focused),
                "focus stays in the editor when an action disables the focused button");

            back.Focus(NavigationMethod.Tab);
            ShownWindow.Tap(back, Key.Tab);
            var redo = editor.Button("Redo");
            Assert.IsTrue(redo.IsFocused, "Tab skips unavailable Undo");
            ShownWindow.Tap(redo, Key.Enter);
            Assert.HasCount(1, editor.Session.Layers.Layers);
            editor.Choose("Undo");
            editor.Drag((0.7, 0.2), (0.7, 0.8));
            AssertActions(editor, undo: true, redo: false, delete: true);
            ShownWindow.Tap(editor.Canvas, Key.Escape);
            AssertActions(editor, undo: true, redo: false, delete: false);
        });
    }

    [TestMethod]
    [DataRow("Caption bar", "new caption", false, false)]
    [DataRow("Caption bar", "new caption", false, true)]
    [DataRow("Meme", "", true, false)]
    [DataRow("Meme", "", true, true)]
    public async Task PressingUndoFromTheTextFieldMatchesCtrlZ(string preset, string words, bool olderMark, bool pinned)
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("typing.png", 400, 300, (_, _) => SKColors.White))).Single();
        if (pinned)
        {
            TrayDesktop.Run(shown.Window.SummonPinned);
        }

        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        var byKey = TrayDesktop.Get(() =>
        {
            BeginTyping(shown, editor, preset, words, olderMark);
            ShownWindow.Tap(editor.Field!, Key.Z, KeyModifiers.Control);
            var session = editor.Session!;
            Assert.HasCount(olderMark ? 1 : 0, session.Layers.Layers);
            return (session.Layers, session.Selected, session.CanUndo, session.CanRedo);
        });
        Assert.IsTrue(TrayDesktop.Get(() => shown.Editing.Cancel()));
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            BeginTyping(shown, editor, preset, words, olderMark);
            AssertActions(editor, undo: true, redo: false, delete: true);
            var field = editor.Field!;
            editor.PressButton("Undo");
            Assert.IsTrue(field.IsFocused && editor.Session!.Typing, "a pointer press does not close the typing run before Click");
            // Release outside the button and invoke Click separately so the check needs no compositor hit test.
            editor.Release(editor.Button("Undo"), new Point(-5, -5));
            Assert.IsTrue(field.IsFocused && editor.Session!.Typing, "a cancelled press leaves typing open");
            editor.Choose("Undo");
            var session = editor.Session!;
            Assert.AreEqual(byKey, (session.Layers, session.Selected, session.CanUndo, session.CanRedo));
            Assert.IsFalse(session.Typing);
        });
    }

    [TestMethod]
    public async Task DelEditsTheTextWhileTheTrashButtonDeletesItsLayer()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("text.png", 400, 300, (_, _) => SKColors.White))).Single();
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            BeginTyping(shown, editor, "Meme", "hey", olderMark: false);
            var field = editor.Field!;
            field.CaretIndex = field.SelectionStart = field.SelectionEnd = 0;
            ShownWindow.Tap(field, Key.Delete);
            Assert.AreEqual("EY", ((TextLayer)editor.Session!.SelectedLayer!).Text);
            editor.PressButton("Delete");
            Assert.IsTrue(field.IsFocused && editor.Session.Typing);
            editor.Release(editor.Button("Delete"), new Point(-5, -5));
            editor.Choose("Delete");
            Assert.IsTrue(editor.Session.Layers.IsEmpty);
            Assert.IsFalse(editor.Session.Typing);
            AssertActions(editor, undo: true, redo: false, delete: false);
            editor.Choose("Undo");
            Assert.AreEqual("EY", ((TextLayer)editor.Session.SelectedLayer!).Text);
        });
    }

    [TestMethod]
    public async Task RedoStaysAvailableForABlankDraftAndGivesWayToANewTypingStep()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("draft.png", 400, 300, (_, _) => SKColors.White))).Single();
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            DrawTwoMarks(editor);
            editor.Choose("Undo");
            editor.Choose("Text");
            BeginTyping(shown, editor, "Meme", "", olderMark: false);
            AssertActions(editor, undo: true, redo: true, delete: true);
            var field = editor.Field!;
            ShownWindow.Type(field, "change");
            AssertActions(editor, undo: true, redo: false, delete: true);
            ShownWindow.Tap(field, Key.A, KeyModifiers.Control);
            ShownWindow.Tap(field, Key.Delete);
            AssertActions(editor, undo: true, redo: true, delete: true);
            editor.Choose("Redo");
            Assert.HasCount(2, editor.Session!.Layers.Layers.OfType<DrawMarkLayer>());
            Assert.HasCount(2, editor.Session.Layers.Layers);
            Assert.IsFalse(editor.Session.Typing);
            AssertActions(editor, undo: true, redo: false, delete: true);
        });
    }

    [TestMethod]
    public async Task BusyAndRetiredButtonsCannotChangeThisSaveOrTheNextItem()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("one.png", 300, 240, (_, _) => SKColors.White),
            shown.Picture("two.png", 300, 240, (_, _) => SKColors.Gray));
        await TrayDesktop.Get(() => shown.Editing.Open(items[0], EditOrigin.Grid));
        var (undo, redo, delete, expected) = TrayDesktop.Get(() =>
        {
            DrawTwoMarks(editor);
            editor.Choose("Undo");
            AssertActions(editor, undo: true, redo: true, delete: true);
            return (editor.Button("Undo"), editor.Button("Redo"), editor.Button("Delete"), editor.Session!.Layers);
        });

        Task saving;
        using (shown.HoldStore())
        {
            saving = TrayDesktop.Get(() =>
            {
                var save = shown.Editing.Save();
                Assert.IsNotNull(editor.Session!.Busy);
                AssertActions(editor, undo: false, redo: false, delete: false);
                foreach (var button in new[] { undo, redo, delete })
                {
                    ShownWindow.RaiseClick(button);
                }

                Assert.AreEqual(expected, editor.Session.Layers);
                return save;
            });
        }

        await saving;
        var saved = (await shown.Listing.Store!.ForEdit(items[0].Id)).Value!;
        Assert.AreEqual(expected, saved.RenderedLayers);
        await TrayDesktop.Get(() => shown.Editing.Open(items[1], EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            DrawTwoMarks(editor);
            foreach (var button in new[] { undo, redo, delete })
            {
                ShownWindow.RaiseClick(button);
            }

            Assert.HasCount(2, editor.Session!.Layers.Layers, "buttons from the old item have no action on this edit");
            Assert.AreEqual(1, editor.Session.Selected);
        });
    }

    [TestMethod]
    public async Task SaveAsNewItemStartsFreshHistoryAndRetiresTheSourceItemsButtons()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("source.png", 300, 240, (_, _) => SKColors.White))).Single();
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        var (session, sourceUndo) = TrayDesktop.Get(() =>
        {
            DrawTwoMarks(editor);
            return (editor.Session!, editor.Button("Undo"));
        });
        Assert.IsTrue((await TrayDesktop.Get(() => shown.Editing.SaveAsNewItem())).Succeeded);

        TrayDesktop.Run(() =>
        {
            Assert.AreSame(session, editor.Session, "the editing session continues on the copy");
            Assert.AreNotEqual(item.Id, session.Item.Id);
            AssertActions(editor, undo: false, redo: false, delete: true);
            Assert.IsTrue(editor.Shows("Undo") && editor.Shows("Redo") && editor.Shows("Compare"), "the three stay for the edit going on with the copy (G20)");
            editor.Choose("Delete");
            AssertActions(editor, undo: true, redo: false, delete: false);
            ShownWindow.RaiseClick(sourceUndo);
            Assert.HasCount(1, session.Layers.Layers, "the source item's Undo cannot act on the copy");
            editor.Choose("Undo");
            Assert.HasCount(2, session.Layers.Layers);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LayerActionsWorkWithTheRealPointerFromCanvasAndFromTyping(bool pinned)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("pointer.png", 400, 300, (_, _) => SKColors.White))).Single();
        if (pinned)
        {
            TrayDesktop.Run(shown.Window.SummonPinned);
        }

        await shown.ClickSearchInFront();
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            DrawTwoMarks(editor);
        });
        await ClickOnDesktop(shown, editor, "Delete");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Layers.Layers.Count == 1 && editor.Session.Selected is null));
        await ClickOnDesktop(shown, editor, "Undo");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Layers.Layers.Count == 2 && editor.Session.Selected == 1));
        await ClickOnDesktop(shown, editor, "Redo");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Layers.Layers.Count == 1 && editor.Session.Selected is null));

        TrayDesktop.Run(() =>
        {
            editor.Choose("Text");
            BeginTyping(shown, editor, "Meme", "", olderMark: false);
        });
        await ClickOnDesktop(shown, editor, "Undo");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !editor.Session!.Typing));
        TrayDesktop.Run(() =>
        {
            Assert.IsInstanceOfType<DrawMarkLayer>(editor.Session!.Layers.Layers.Single(), "the older mark survived Undo from the field");
            BeginTyping(shown, editor, "Caption bar", "caption", olderMark: false);
        });
        await ClickOnDesktop(shown, editor, "Delete");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !editor.Session!.Typing));
        TrayDesktop.Run(() => Assert.IsInstanceOfType<DrawMarkLayer>(editor.Session!.Layers.Layers.Single()));
    }

    [TestMethod]
    [DataRow("short")]
    [DataRow("A long picture title that should trim before it pushes the other editor controls out of the head")]
    public async Task ActionButtonsAndTheExistingRowsFitAtTheMinimum(string title)
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture(title + ".png", 200, 1200, (_, _) => SKColors.White))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            WindowMinimum.AssertAt(shown.Window.Bounds.Size, "the window at its minimum");
            var before = (editor.Canvas.Bounds, editor.Box);
            Assert.IsFalse(editor.Shows("Undo") || editor.Shows("Redo") || editor.Shows("Compare") || editor.Shows("Delete"), "a fresh item: nothing to step, compare or delete (G20)");
            foreach (var words in new[] { "Back", "More options", "Cancel", "Save", "Output size", "Caption bar" })
            {
                TextOptionRowTests.AssertInside(shown, editor.Button(words), words);
            }

            // The first change brings Undo, Redo and Compare, and the layer it selects brings Delete.
            editor.Click(0.5, 0.25);
            shown.Window.UpdateLayout();
            foreach (var (words, tip) in new[] { ("Undo", "Undo (Ctrl+Z)"), ("Redo", "Redo (Ctrl+Y)"), ("Delete", "Delete (Del)") })
            {
                var button = editor.Button(words);
                Assert.AreEqual(tip, ToolTip.GetTip(button));
                Assert.AreEqual(36, button.Bounds.Width, 0.5);
                Assert.AreEqual(words == "Delete" ? 32 : 36, button.Bounds.Height, 0.5);
                Assert.IsTrue(button.Focusable);
                TextOptionRowTests.AssertInside(shown, button, words);
            }

            foreach (var words in new[] { "Back", "Compare", "More options", "Cancel", "Save", "Output size", "Caption bar" })
            {
                TextOptionRowTests.AssertInside(shown, editor.Button(words), words);
            }

            var editing = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().Single(label => label.Text == "editing");
            Control[] head = [editor.Button("Back"), editing, editor.Button("Undo"), editor.Button("Redo"), editor.Button("Compare"), editor.Button("More options")];
            for (var index = 1; index < head.Length; index++)
            {
                var previous = head[index - 1];
                Assert.IsGreaterThanOrEqualTo(previous.TranslatePoint(new Point(previous.Bounds.Width, 0), editor.Overlay)!.Value.X,
                    head[index].TranslatePoint(default, editor.Overlay)!.Value.X);
            }

            Assert.IsTrue(editor.Button("Delete").IsEnabled);
            Assert.AreEqual(before, (editor.Canvas.Bounds, editor.Box), "the head's three and the trash button appearing leave the picture in place");
            Assert.Contains("Drag to move, corners resize, round handle turns. Double-click to edit.", editor.Texts());
            ShownWindow.Type(editor.Field!, "compare");
            ShownWindow.Tap(editor.Field!, Key.Enter, KeyModifiers.Control);
            editor.PressButton("Compare");
            Assert.IsTrue(editor.Session!.ShownLayers.IsEmpty, "Compare still shows the original while held");
            editor.ReleaseButton("Compare");
            Assert.IsFalse(editor.Session.ShownLayers.IsEmpty);
        });
    }

    private static void DrawTwoMarks(ShownEditor editor)
    {
        editor.Choose("Draw");
        editor.Drag((0.2, 0.2), (0.2, 0.8));
        editor.Choose("Arrow");
        editor.Drag((0.8, 0.2), (0.8, 0.8));
    }

    private static async Task ClickOnDesktop(ShownWindow shown, ShownEditor editor, string words)
    {
        var button = TrayDesktop.Get(() => editor.Button(words));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
        {
            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), shown.Window);
            return centre is { } point && shown.Window.InputHitTest(point) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(button);
        }));
        TestMouse.Click(TestMouse.Centre(button));
    }

    private static void BeginTyping(ShownWindow shown, ShownEditor editor, string preset, string words, bool olderMark)
    {
        shown.Window.UpdateLayout();
        if (olderMark)
        {
            editor.Choose("Draw");
            editor.Drag((0.2, 0.5), (0.8, 0.8));
            editor.Choose("Text");
        }

        editor.Choose(preset);
        editor.Click(0.5, 0.25);
        shown.Window.UpdateLayout();
        var field = editor.Field!;
        Assert.IsTrue(field.IsFocused);
        ShownWindow.Type(field, words);
    }

    /// <summary>Whether each action can be clicked: shown and enabled; an absent one cannot.</summary>
    private static void AssertActions(ShownEditor editor, bool undo, bool redo, bool delete)
    {
        Assert.AreEqual(undo, Able(editor, "Undo"), "Undo");
        Assert.AreEqual(redo, Able(editor, "Redo"), "Redo");
        Assert.AreEqual(delete, Able(editor, "Delete"), "Delete");
    }

    private static bool Able(ShownEditor editor, string words) => editor.Shows(words) && editor.Button(words).IsEffectivelyEnabled;
}
