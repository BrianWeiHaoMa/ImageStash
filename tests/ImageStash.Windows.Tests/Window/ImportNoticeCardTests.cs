using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// An import's notice on the real window (G12, G16): its card shows the counts semi-bold above the files that were not
/// added, and its Undo names the key that runs it; with a tag chip lit, the tags it gave show whole inside the window at
/// its minimum size (G11, L2); after a search typed while it ran, its Show beside the Undo, inside the window at its
/// minimum size, brings the new picture up on a click that leaves the keyboard's focus where it was (G12, G7); a name or
/// tags typed in the preview that Show, or the end, closes are committed as Enter would, and nothing after Esc (G19, G17).
/// These checks show the stash window.
/// </summary>
[TestClass]
public sealed class ImportNoticeCardTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task TheCardShowsTheCountsSemiBoldAboveTheFilesNotAddedAndItsUndoNamesCtrlZ()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        var picture = shown.Picture("counted.png");
        var clip = Path.Combine(Path.GetDirectoryName(picture)!, "clip.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);

        await TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
            [new DroppedItem("counted.png", picture, null, IsFolder: false), new DroppedItem("clip.mp4", clip, null, IsFolder: false)], false, false, [])));
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var card = NoticeCards(shown).Single();
            var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Text is { Length: > 0 }
                && !text.GetVisualAncestors().OfType<Button>().Any()).ToList();
            var heading = texts.Single(text => text.Text == "Added 1 · 1 skipped");
            var details = texts.Single(text => text.Text!.StartsWith("Skipped clip.mp4 (a video)"));
            Assert.AreEqual(FontWeight.SemiBold, heading.FontWeight, "the counts in semi-bold");
            Assert.AreNotEqual(FontWeight.SemiBold, details.FontWeight);
            Assert.IsLessThan(Top(details, card), Top(heading, card), "the counts above the files not added");
            Assert.AreEqual("Undo (Ctrl+Z)", ToolTip.GetTip(UndoButton(card)));
        });
    }

    [TestMethod]
    public async Task TheCardOfADropWithAChipLitShowsTheTagsItGaveWholeInsideTheWindowAtItsMinimumSize()
    {
        using var shown = ShownWindow.Open();
        var held = (await shown.OpenStash(shown.Picture("held.png"))).Single();
        await shown.Tag(await shown.AddTo("carrying.png"), "cat");
        // The setting up's own import notices, whose time a stopped clock never ends, leave first.
        TransferredStash.CloseNotices(shown);
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Window.Width = WindowSize.Minimum.Width;
            shown.Window.Height = WindowSize.Minimum.Height;
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.LitTags.Contains("cat")
            && Math.Abs(shown.Window.ClientSize.Width - WindowSize.Minimum.Width) <= 0.5 && Math.Abs(shown.Window.ClientSize.Height - WindowSize.Minimum.Height) <= 0.5));

        await TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
            [new DroppedItem("newcomer.png", shown.Picture("newcomer.png"), null, IsFolder: false), new DroppedItem("held.png", shown.Picture("held.png"), null, IsFolder: false)], false, false, [])));
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));

        CollectionAssert.AreEqual(new[] { "cat" }, shown.TagsOf(held.Id), "the picture the stash already held got the tag");
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            WindowMinimum.AssertAt(shown.Window.ClientSize, "the window at its minimum");
            var card = NoticeCards(shown).Single();
            var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Text is { Length: > 0 }
                && !text.GetVisualAncestors().OfType<Button>().Any()).ToList();
            var heading = texts.Single(text => text.Text == "Added 1, tagged #cat · 1 already in your stash");
            var details = texts.Single(text => text.Text == "held.png was already in your stash as “held”. This import gave it #cat. Added “newcomer”.");
            var window = new Rect(shown.Window.ClientSize);
            foreach (var shownText in new[] { heading, details })
            {
                var bounds = new Rect(shownText.TranslatePoint(default, shown.Window)!.Value, shownText.Bounds.Size);
                Assert.IsTrue(window.Contains(bounds), $"'{shownText.Text}' lies whole inside the window: {bounds} in {window}");
                Assert.IsGreaterThanOrEqualTo(shownText.DesiredSize.Height - 0.5, shownText.Bounds.Height, $"'{shownText.Text}' wraps rather than being clipped");
            }

            Assert.AreEqual("Undo (Ctrl+Z)", ToolTip.GetTip(UndoButton(card)), "its Undo takes back the tag too");
        });
    }

    [TestMethod]
    public async Task AfterASearchTypedWhileItRanTheCardShowsShowBesideUndoInsideTheWindowAndAClickOnShowLeavesTheFocus()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("older.png"));
        TransferredStash.CloseNotices(shown);
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = WindowSize.Minimum.Width;
            shown.Window.Height = WindowSize.Minimum.Height;
        });
        await Eventually(() => TrayDesktop.Get(() => Math.Abs(shown.Window.ClientSize.Width - WindowSize.Minimum.Width) <= 0.5
            && Math.Abs(shown.Window.ClientSize.Height - WindowSize.Minimum.Height) <= 0.5));
        // The dropped picture's bytes come once released, as a browser's stream does: the search is typed meanwhile. The
        // skipped clip's long name fills the card's words to their width, beside Show and Undo.
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newcomer = shown.Picture("newcomer.png");
        var clip = Path.Combine(Path.GetDirectoryName(newcomer)!, "a clip whose name runs on for quite a while before it ends.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);
        var running = TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
            [new DroppedItem("newcomer.png", null, async () =>
            {
                await released.Task;
                return File.OpenRead(newcomer);
            }, IsFolder: false), new DroppedItem(Path.GetFileName(clip), clip, null, IsFolder: false)], false, false, [])));
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            shown.Search.Text = "older";
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "older"));
        released.SetResult();
        var added = (await running).First().Item!;
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));

        var (show, focusedBefore) = TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            WindowMinimum.AssertAt(shown.Window.ClientSize, "the window at its minimum");
            Assert.AreEqual("older", shown.Listing.Query, "the search typed while the import ran is kept (G12)");
            var card = NoticeCards(shown).Single();
            var (showButton, undo) = (ShowButton(card), UndoButton(card));
            var window = new Rect(shown.Window.ClientSize);
            var showBounds = new Rect(showButton.TranslatePoint(default, shown.Window)!.Value, showButton.Bounds.Size);
            var undoBounds = new Rect(undo.TranslatePoint(default, shown.Window)!.Value, undo.Bounds.Size);
            Assert.IsTrue(window.Contains(showBounds) && window.Contains(undoBounds), $"Show and Undo lie whole inside the window: {showBounds}, {undoBounds} in {window}");
            Assert.IsLessThanOrEqualTo(undoBounds.Left, showBounds.Right, "Show beside the Undo, before it");
            Assert.IsTrue(showBounds.Top < undoBounds.Bottom && undoBounds.Top < showBounds.Bottom, "side by side on one row");
            return (showButton, shown.Focused);
        });

        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.Click(TestMouse.Centre(show));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "" && shown.Listing.Selection.Contains(added.Id)));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Search.Text, "Show clears the search box");
            Assert.AreEqual(Scope.All, shown.Listing.LitChip, "and shows All");
            CollectionAssert.AreEqual(new[] { added.Id }, shown.Listing.Selection.ToList(), "the new picture selected");
            Assert.AreSame(focusedBefore, shown.Focused, "the click on Show took no focus (G7)");
        });
    }

    [TestMethod]
    [DataRow("name", true)]
    [DataRow("tags", true)]
    [DataRow("name, then Esc", true)]
    [DataRow("name", false)]
    [DataRow("tags", false)]
    public async Task ShowOrTheEndClosingThePreviewCommitsANameOrTagsTypedInItAndAfterEscNothing(string typed, bool byShow)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var older = (await shown.OpenStash(shown.Picture("older.png"))).Single();
        TransferredStash.CloseNotices(shown);
        // Opened while the import runs, the preview keeps the window at its end, whose notice offers Show; opened before
        // the drop, the end closes it itself (G12).
        if (!byShow)
        {
            await OpenPreview(shown, older);
        }

        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newcomer = shown.Picture("newcomer.png");
        var running = TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
            [new DroppedItem("newcomer.png", null, async () =>
            {
                await released.Task;
                return File.OpenRead(newcomer);
            }, IsFolder: false)], false, false, [])));
        if (byShow)
        {
            await OpenPreview(shown, older);
        }

        // The words typed in its name or tag field without Enter.
        TrayDesktop.Run(() =>
        {
            if (typed == "tags")
            {
                Tap(shown.PreviewPicture, Key.T, KeyModifiers.Control);
                Assert.AreSame(shown.PreviewTagBox, shown.Focused);
                Type(shown.PreviewTagBox, "bright sunny");
                return;
            }

            Tap(shown.PreviewPicture, Key.F2);
            shown.Window.UpdateLayout();
            Assert.AreSame(shown.NameBox, shown.Focused);
            Type(shown.NameBox, "typed name");
            if (typed == "name, then Esc")
            {
                Tap(shown.NameBox, Key.Escape);
            }
        });
        released.SetResult();
        var added = (await running).Single().Item!;
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));
        if (byShow)
        {
            var show = TrayDesktop.Get(() =>
            {
                shown.Window.UpdateLayout();
                Assert.IsTrue(shown.Preview.IsOpen, "the preview kept at the end");
                return ShowButton(NoticeCards(shown).Single());
            });
            await Eventually(() => TrayDesktop.Get(() =>
                show.TranslatePoint(new Point(show.Bounds.Width / 2, show.Bounds.Height / 2), shown.Window) is { } centre
                && shown.Window.InputHitTest(centre) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(show)));
            TestMouse.Click(TestMouse.Centre(show));
        }
        else
        {
            TrayDesktop.Run(() => Assert.IsFalse(NoticeCards(shown).Single().GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, ImportEnd.Show)),
                "the end showed its result: no Show"));
        }

        // The preview closes once the commit has been answered.
        await Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen && shown.Listing.Settled && shown.Listing.Query == "" && shown.Listing.Selection.Contains(added.Id)));
        var stored = await shown.Listing.Store!.Item(older.Id);
        Assert.IsTrue(stored.Succeeded, stored.Failure?.Sentence);
        Assert.AreEqual(typed == "name" ? "typed name" : "older", stored.Value!.Name, "a name typed is committed as Enter would, one cancelled with Esc is not (G19)");
        CollectionAssert.AreEquivalent(typed == "tags" ? new[] { "bright", "sunny" } : Array.Empty<string>(), shown.TagsOf(older.Id), "words typed in the tag field are added as Enter would (G17)");
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(Scope.All, shown.Listing.LitChip, "All shown");
            CollectionAssert.AreEqual(new[] { added.Id }, shown.Listing.Selection.ToList(), "with the import's picture selected (G12)");
        });
    }

    [TestMethod]
    public async Task AnImportClosingThePreviewCommitsAnotherNameTypedWhileTheFirstRenameIsHeld()
    {
        using var shown = ShownWindow.Open();
        var older = (await shown.OpenStash(shown.Picture("older.png"))).Single();
        TransferredStash.CloseNotices(shown);
        await OpenPreview(shown, older);
        Draft("first draft");

        var picture = shown.Picture("newcomer.png");
        Task<IReadOnlyList<ImportedFile>> running;
        using (new FileStream(older.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            running = TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
                [new DroppedItem("newcomer.png", picture, null, IsFolder: false)], false, false, [])));
            await Eventually(() => TrayDesktop.Get(() => shown.Preview.ClosingAfterCommit));
            Draft("second draft");
        }

        var added = (await running).Single().Item!;
        var stored = await shown.Listing.Store!.Item(older.Id);
        Assert.AreEqual("second draft", stored.Value!.Name);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Preview.IsOpen);
            CollectionAssert.AreEqual(new[] { added.Id }, shown.Listing.Selection.ToArray());
        });

        void Draft(string text) => TrayDesktop.Run(() =>
        {
            Tap(shown.PreviewPicture, Key.F2);
            shown.Window.UpdateLayout();
            Assert.IsTrue(shown.NameBox.IsFocused);
            Type(shown.NameBox, text);
        });
    }

    private static async Task OpenPreview(ShownWindow shown, StashItem item)
    {
        TrayDesktop.Run(() => shown.OpenPreview(item));
        await Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count > 0));
    }

    private static double Top(Visual text, Visual card) => text.TranslatePoint(default, card)!.Value.Y;

    /// <summary>The cards of the notices showing in the stash window.</summary>
    private static List<Border> NoticeCards(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<Border>().Where(border => border.Child is StackPanel row
            && row.Children.OfType<Button>().Any(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Close")).ToList();

    private static Button UndoButton(Control card) => card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));

    private static Button ShowButton(Control card) => card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, ImportEnd.Show));
}
