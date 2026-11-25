using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The hosts' live carrier counts, their independent marks, and the space for both text columns at display scale.
/// </summary>
[TestClass]
public sealed class TagPickerTests
{
    [TestMethod]
    public async Task ChipPickerCountsCarriersAcrossSearchAndFollowsDeleteAndUndo()
    {
        using var listed = await ListedStash.Open();
        var (named, captioned, outside) = await Reactions(listed);
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var row = new ChipRow(listed.Listing, listed.Organising, () => null);
            await listed.Listing.SetQuery("reaction shared");
            CollectionAssert.AreEquivalent(new[] { named.Id, captioned.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
            Open(row);
            AssertEntries(row.Picker, ("shared", "2 items", true), ("singleton", "1 item", false));

            Assert.IsTrue((await listed.Deletion.Delete([outside])).Succeeded);
            AssertEntries(row.Picker, ("shared", "1 item", true));
            await listed.Listing.SetQuery("");
            Assert.AreEqual(0, listed.Listing.Chips.Single(place => place.Tag == "singleton").Items);
            AssertEntries(row.Picker, ("shared", "1 item", false));

            await listed.Notices.Undo(listed.Notices.Shown.Single());
            await listed.Listing.SetQuery("reaction shared");
            AssertEntries(row.Picker, ("shared", "2 items", true), ("singleton", "1 item", false));
        }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SelectionPickerShowsTheStashCountAndMarksOnlyCommittedSharedTags(bool refuse)
    {
        using var listed = await ListedStash.Open();
        var (named, captioned, _) = await Reactions(listed);
        await listed.Listing.ShowScope(Scope.All);
        if (refuse)
        {
            listed.Stash.SqlWhileOpen($"CREATE TRIGGER refuse_tag BEFORE INSERT ON tags WHEN NEW.item_id = {captioned.Id} BEGIN SELECT RAISE(ABORT, 'refused'); END");
        }

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.SetQuery("reaction shared");
            listed.Listing.Select([named.Id, captioned.Id], named.Id);
            var host = new TagFieldOverlay(listed.Listing, listed.Organising, listed.Notices);
            host.Open(listed.Listing.SelectedItems, new Button());
            await OpenFieldPicker(host.Field);
            AssertEntries(host.Field.Picker, ("shared", "2 items", false), ("singleton", "1 item", false));

            if (refuse)
            {
                await listed.WaitForOrganising(() => Click(Entry(host.Field.Picker, host.Field.Picker.Entries[0])));
                Assert.IsTrue(listed.Notices.Shown.Single().NamesAProblem);
                AssertEntries(host.Field.Picker, ("shared", "2 items", false), ("singleton", "1 item", false));
            }
            else
            {
                await PickAndWaitForMark(listed, host.Field.Picker, "shared", marked: true);
                AssertEntries(host.Field.Picker, ("shared", "3 items", true), ("singleton", "1 item", false));
            }
        }));
    }

    [TestMethod]
    public async Task AFailedCatalogueRefreshClosesTheStaleChipPicker()
    {
        using var listed = await ListedStash.Open();
        var (named, _, outside) = await Reactions(listed);
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var row = new ChipRow(listed.Listing, listed.Organising, () => null);
            Open(row);
            AssertEntries(row.Picker, ("shared", "2 items", false), ("singleton", "1 item", false));

            Assert.IsTrue((await listed.Stash.Store!.Delete([outside.Id])).Single().Succeeded);
            using (listed.Stash.HoldStore())
            {
                await listed.Listing.Refresh();
                Assert.IsFalse(row.Picker.IsOpen, "the old count is no longer current");
                Assert.IsNotEmpty(listed.Failures);
            }

            await listed.Listing.Refresh();
            Open(row);
            AssertEntries(row.Picker, ("shared", "1 item", false));
            Assert.IsTrue(listed.Listing.Items.Any(item => item.Id == named.Id), "the other carrier stays usable");
        }));
    }

    [TestMethod]
    public async Task OpeningTheChipPickerDuringDeleteWaitsForCurrentCarriers()
    {
        using var listed = await ListedStash.Open();
        var (_, _, outside) = await Reactions(listed);
        await listed.Listing.ShowScope(Scope.All);
        var recycling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        listed.Stash.RecycleBin.BeforeEach = _ =>
        {
            recycling.TrySetResult();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
        };

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var row = new ChipRow(listed.Listing, listed.Organising, () => null);
            var deleting = listed.Deletion.Delete([outside]);
            try
            {
                await recycling.Task;
                Open(row);
                Assert.IsFalse(row.Picker.IsOpen, "the picker must wait for the current catalogue");
            }
            finally
            {
                release.Set();
                Assert.IsTrue((await deleting).Succeeded);
            }

            await WaitForOpen(row.Picker);
            AssertEntries(row.Picker, ("shared", "1 item", false));
        }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AStaleChipPickerPickRespectsNewerTypingAndNamesAVanishedTag(bool typeAfterPick)
    {
        using var listed = await ListedStash.Open();
        var (_, _, outside) = await Reactions(listed);
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var row = new ChipRow(listed.Listing, listed.Organising, () => null);
            Open(row);
            var tag = typeAfterPick ? "shared" : "singleton";
            Assert.IsTrue((await listed.Stash.Store!.RemoveTag([outside], "singleton")).Single().Succeeded);
            Click(Entry(row.Picker, row.Picker.Entries.Single(entry => entry.Tag == tag)));
            if (typeAfterPick)
            {
                await listed.Listing.SetQuery("fresh");
            }

            await listed.Listing.Settle();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await listed.Listing.Settle();
            Assert.AreEqual(typeAfterPick ? "fresh" : "", listed.Listing.Query);
            if (typeAfterPick)
            {
                Assert.IsEmpty(listed.Failures);
            }
            else
            {
                Assert.HasCount(1, listed.Failures);
                Assert.AreEqual("#singleton", listed.Failures.Single().Subject);
            }
        }));
    }

    [TestMethod]
    public async Task WhenTheFirstCountsFailTheChipPickerCanRetryFromTheRow()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("one picture");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsEmpty(listed.Listing.Tags);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var row = new ChipRow(listed.Listing, listed.Organising, () => null);
            Assert.IsTrue((await listed.Stash.Store!.AddTag([item], "shared")).Single().Succeeded);
            using (listed.Stash.HoldStore())
            {
                await listed.Listing.Refresh();
            }

            var size = new Size(500, 40);
            row.Measure(size);
            row.Arrange(new Rect(size));
            Assert.IsGreaterThan(Opener(row).Bounds.Left, row.Bounds.Width, "the opener is inside the clipped row");
            Open(row);
            await WaitForOpen(row.Picker);
            AssertEntries(row.Picker, ("shared", "1 item", false));
        }));
    }

    [TestMethod]
    public async Task PreviewPickerCountsTheStashAndMarksThePreviewedItem()
    {
        using var listed = await ListedStash.Open();
        var (named, _, _) = await Reactions(listed);
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var preview = PreviewOf(listed);
            preview.Open(named);
            try
            {
                var field = preview.TagField;
                await OpenFieldPicker(field);
                AssertEntries(field.Picker, ("shared", "2 items", true), ("singleton", "1 item", false));

                await PickAndWaitForMark(listed, field.Picker, "shared", marked: false);
                AssertEntries(field.Picker, ("shared", "1 item", false), ("singleton", "1 item", false));
            }
            finally
            {
                await preview.LetGo();
            }
        }));
    }

    [TestMethod]
    public async Task PreviewPickerCanRetryItsCatalogueAfterTheStoreRecovers()
    {
        using var listed = await ListedStash.Open();
        var (named, _, _) = await Reactions(listed);
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var preview = PreviewOf(listed);
            preview.Open(named);
            try
            {
                var field = preview.TagField;
                await OpenFieldPicker(field);
                using (listed.Stash.HoldStore())
                {
                    await preview.Refresh();
                    Assert.IsFalse(field.Picker.IsOpen);
                    Assert.IsTrue(listed.Notices.Shown.Single().NamesAProblem);
                }

                Assert.IsTrue(Opener(field).IsVisible, "Every tag can retry from the same field");
                Open(field);
                await WaitForOpen(field.Picker);
                AssertEntries(field.Picker, ("shared", "2 items", true), ("singleton", "1 item", false));
            }
            finally
            {
                await preview.LetGo();
            }
        }));
    }

    private static Preview PreviewOf(ListedStash listed) =>
        new(listed.Listing, listed.Handoff, listed.Organising, listed.Deletion, listed.Renaming, listed.Editing,
            listed.ItemMenu, listed.Notices, listed.Stash.Clock, listed.PasteTarget, _ => { }, new Button());

    private static async Task<(StashItem Named, StashItem Captioned, StashItem Outside)> Reactions(ListedStash listed)
    {
        var named = await listed.Stash.ImportFixture("reaction by name", "animated.gif");
        var outside = await listed.Stash.Import("outside the search");
        var captioned = await listed.Stash.ImportFixture("a captioned picture", "still.png");
        var layers = new LayerList([new CaptionBarLayer("reaction shared", "Segoe UI", 12, new(0, 0, 0, 255), Outline.None)]);
        var saved = await listed.Stash.SaveEdit(captioned, layers);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        Assert.IsTrue((await listed.Stash.Store!.AddTag([named, outside], "shared")).All(result => result.Succeeded));
        Assert.IsTrue((await listed.Stash.Store.AddTag([outside], "singleton")).Single().Succeeded);
        return (named, saved.Value!, outside);
    }

    private static void Open(Control host) => Click(Opener(host));

    private static Button Opener(Control host) => host.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == "Every tag");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitForOpen(TagPicker picker)
    {
        if (picker.IsOpen)
        {
            return;
        }

        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TagPicker.IsOpenProperty && picker.IsOpen)
            {
                opened.TrySetResult();
            }
        }

        picker.PropertyChanged += Changed;
        try
        {
            await opened.Task;
        }
        finally
        {
            picker.PropertyChanged -= Changed;
        }
    }

    private static async Task OpenFieldPicker(TagField field)
    {
        var button = Opener(field);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.IsVisibleProperty && button.IsVisible)
            {
                ready.TrySetResult();
            }
        }

        button.PropertyChanged += Changed;
        try
        {
            if (!button.IsVisible)
            {
                await ready.Task;
            }

            Click(button);
        }
        finally
        {
            button.PropertyChanged -= Changed;
        }
    }

    private static async Task PickAndWaitForMark(ListedStash listed, TagPicker picker, string tag, bool marked)
    {
        var button = Entry(picker, picker.Entries.Single(entry => entry.Tag == tag));
        var mark = ((Grid)button.Content!).Children[0];
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.OpacityProperty && mark.Opacity == (marked ? 1 : 0))
            {
                changed.TrySetResult();
            }
        }

        mark.PropertyChanged += Changed;
        try
        {
            await listed.WaitForOrganising(() => Click(button));
            await changed.Task;
        }
        finally
        {
            mark.PropertyChanged -= Changed;
        }
    }

    private static Button Entry(TagPicker picker, TagPickerEntry entry)
    {
        var items = PickerItems(picker);
        return (Button)items.ItemTemplate!.Build(items.Items[picker.Entries.ToList().IndexOf(entry)])!;
    }

    private static ItemsControl PickerItems(TagPicker picker) => (ItemsControl)((ScrollViewer)((Border)picker.Child!).Child!).Content!;

    private static void AssertEntries(TagPicker picker, params (string Tag, string Count, bool Marked)[] expected)
    {
        Assert.IsTrue(picker.IsOpen);
        CollectionAssert.AreEqual(expected, picker.Entries.Select(entry =>
        {
            var button = Entry(picker, entry);
            return (AutomationProperties.GetName(button), AutomationProperties.GetHelpText(button), entry.Marked);
        }).ToArray());
    }

    [TestMethod]
    public void ACountRefreshUpdatesTheExistingRowAndKeepsItsPick()
    {
        ControlThread.Run(() =>
        {
            var picker = new TagPicker();
            string? picked = null;
            picker.Open(new Button(), [new("long-name", 999, false)], tag => picked = tag);
            var row = Entry(picker, picker.Entries.Single());
            var grid = (Grid)row.Content!;
            var count = (TextBlock)grid.Children[2];
            Assert.AreEqual("999", count.Text);
            Assert.AreEqual(TextAlignment.Right, count.TextAlignment);
            Assert.IsLessThan(1, count.Opacity);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, ((TextBlock)grid.Children[1]).TextTrimming);

            picker.Show([new("long-name", 1000, false)]);
            Assert.AreEqual("1000", count.Text);
            Assert.AreEqual("1000 items", AutomationProperties.GetHelpText(row));
            Assert.AreEqual(0, grid.Children[0].Opacity);

            picker.Show([new("long-name", 1000, true)]);
            Assert.AreEqual(1, grid.Children[0].Opacity);
            Click(row);
            Assert.AreEqual("long-name", picked);
        });
    }

    [TestMethod]
    public void ARecycledContainerAcceptsClearingItsContent()
    {
        ControlThread.Run(() =>
        {
            var picker = new TagPicker();
            picker.Show([new("tag", 1, false)]);
            var presenter = PresentFirstEntry(picker);

            presenter.Content = null;
            presenter.UpdateChild();

            Assert.IsNull(presenter.Child);
        });
    }

    [TestMethod]
    [DoNotParallelize]
    public void ARowModelDoesNotRetainAnUnrealizedButton()
    {
        ControlThread.Run(() =>
        {
            var picker = new TagPicker();
            picker.Show([new("tag", 1, false)]);
            var discarded = DiscardFirstEntry(picker);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsFalse(discarded.TryGetTarget(out _), "the catalogue must not keep the old button alive");
            GC.KeepAlive(picker);
        });
    }

    private static ContentPresenter PresentFirstEntry(TagPicker picker)
    {
        var items = PickerItems(picker);
        var presenter = new ContentPresenter { ContentTemplate = items.ItemTemplate, Content = items.Items[0] };
        presenter.UpdateChild();
        Assert.IsInstanceOfType<Button>(presenter.Child);
        return presenter;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Control> DiscardFirstEntry(TagPicker picker)
    {
        var presenter = PresentFirstEntry(picker);
        var discarded = new WeakReference<Control>(presenter.Child!);
        presenter.ContentTemplate = null;
        presenter.Content = null;
        presenter.UpdateChild();
        return discarded;
    }

    /// <summary>
    /// Fixed measures round to nearest, halves to even. Each text width rounds up independently.
    /// </summary>
    [TestMethod]
    [DataRow(1.0, 65 + 10 + 199 + 21)]
    [DataRow(1.25, 81 + 12 + 248 + 26)]
    [DataRow(1.5, 98 + 15 + 298 + 31)]
    [DataRow(1.75, 114 + 18 + 348 + 36)]
    [DataRow(2.0, 130 + 20 + 397 + 41)]
    [DataRow(2.25, 146 + 22 + 447 + 46)]
    public void TheListFitsTheNameAndCountInWholePhysicalPixels(double scaling, int pixels)
    {
        var width = TagPicker.CardWidth(198.3, 20.4, scaling);

        Assert.AreEqual(pixels, width * scaling, 1e-6, $"at {scaling}: {width} units");
    }

    [TestMethod]
    public void WholeWidthsNeedNoExtraPixels()
    {
        Assert.AreEqual(295, TagPicker.CardWidth(199, 21, 1));
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    [DataRow(2.0)]
    [DataRow(2.25)]
    public void AShortNameGivesTheNarrowestListAndANameWiderThanItsWidestTheWidest(double scaling)
    {
        Assert.AreEqual(180, TagPicker.CardWidth(12, 20, scaling), 1e-9, "never narrower than its narrowest");
        Assert.AreEqual(320, TagPicker.CardWidth(400, 20, scaling), 1e-9, "never wider than its widest: the name is trimmed");
    }
}
