using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The preview's ‹ and › on the real desktop, clicked with the real pointer (G19, G7, G6): they step as Left and Right do
/// and are unavailable at the ends; a press leaves focus, and a name or tag in edit, in its field, which the click then
/// commits to the item it was typed for, a chosen suggestion included, before the step; they lie over the picture's edges
/// without scaling it down or pausing it; faint with the pointer away from the picture's row, at full strength over it they
/// read over white and black, light and dark; Esc pressed while the click's
/// commit is held closes the preview on the item it was typed for.
/// </summary>
[TestClass]
public sealed class PreviewStepButtonsTests
{
    [TestMethod]
    public async Task TheButtonsStepAndSelectOneItemAndAreUnavailableAtTheEnds()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("step alpha.png"), shown.Picture("step beta.png"));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await shown.ClickSearchInFront();
        var items = TrayDesktop.Get(() => shown.Listing.Items.ToArray());
        var buttons = shown.Preview.StepButtons;

        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(items[0]);
            AssertButton(buttons.Back, "Previous", "Previous (Left)");
            AssertButton(buttons.On, "Next", "Next (Right)");
            Assert.IsFalse(buttons.Back.IsEnabled, "Previous at the first item");
            Assert.IsTrue(buttons.On.IsEnabled);
        });

        await ClickStep(shown, buttons.On);
        await WaitForStep(shown, items[1]);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(buttons.Back.IsEnabled);
            Assert.IsFalse(buttons.On.IsEnabled, "Next at the last item");
        });

        await ClickStep(shown, buttons.Back);
        await WaitForStep(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            Tap(shown.PreviewPicture, Key.Escape);
            shown.Search.Text = items[0].Name;
        });
        await TrayDesktop.Get(shown.Listing.Settle);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(items[0].Id, shown.Listing.Items.Single().Id);
            shown.OpenPreview(items[0]);
            Assert.IsFalse(buttons.Back.IsEnabled, "Previous at the only item");
            Assert.IsFalse(buttons.On.IsEnabled, "Next at the only item");
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NextCommitsTheNameBeforeSteppingInAllOrOutOfTheSearchResult(bool search)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("match alpha.png"), shown.Picture("match beta.png"), shown.Picture("outside.png"));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await shown.ClickSearchInFront();
        if (search)
        {
            TrayDesktop.Run(() => Type(shown.Search, "match"));
            await TrayDesktop.Get(shown.Listing.Settle);
        }

        var items = TrayDesktop.Get(() => shown.Listing.Items.ToArray());
        Assert.HasCount(search ? 2 : 3, items);
        var (typedFor, next) = (items[0], items[1]);
        const string renamed = "renamed picture";
        var box = StartRename(shown, typedFor, renamed);

        await ClickStep(shown, shown.Preview.StepButtons.On, () =>
        {
            Assert.AreSame(box, shown.Focused, "focus stays in the name through the button press");
            Assert.IsTrue(shown.Preview.EditingName);
            Assert.AreEqual(renamed, box.Text);
        });

        await WaitForStep(shown, next);
        var stored = await shown.Listing.Store!.Item(typedFor.Id);
        Assert.IsTrue(stored.Succeeded, stored.Failure?.Sentence);
        Assert.AreEqual(renamed, stored.Value!.Name);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(next.Name, shown.NameField.Shown);
            Assert.IsFalse(shown.Preview.EditingName);
            Assert.AreEqual(!search, shown.Listing.Items.Any(item => item.Id == typedFor.Id),
                "the rename removes its item from the search results");
        });
    }

    [TestMethod]
    [DataRow(false, "bright", "bright")]
    [DataRow(true, "sun", "sunny")]
    public async Task NextCommitsTheTypedTagOrChosenSuggestionToItsOwnItem(bool choose, string typed, string expected)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("tag alpha.png"), shown.Picture("tag beta.png"), shown.Picture("tag gamma.png"));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await shown.ClickSearchInFront();
        var items = TrayDesktop.Get(() => shown.Listing.Items.ToArray());
        var (typedFor, next) = (items[0], items[1]);
        await shown.Tag([items[2]], "sunny");
        TrayDesktop.Run(() => shown.OpenPreview(typedFor));
        await Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count > 0));
        var box = TrayDesktop.Get(() =>
        {
            Tap(shown.PreviewPicture, Key.T, KeyModifiers.Control);
            var field = shown.PreviewTagBox;
            Assert.AreSame(field, shown.Focused);
            Type(field, typed);
            if (choose)
            {
                shown.Window.UpdateLayout();
                Tap(field, Key.Down);
                Assert.AreEqual(expected, shown.PreviewTags.Field.Chosen);
            }

            return field;
        });

        await ClickStep(shown, shown.Preview.StepButtons.On, () =>
        {
            Assert.AreSame(box, shown.Focused, "focus stays in the tag box through the button press");
            Assert.AreEqual(typed, box.Text);
            Assert.AreEqual(choose ? expected : null, shown.PreviewTags.Field.Chosen);
        });

        await WaitForStep(shown, next);
        CollectionAssert.AreEqual(new[] { expected }, shown.TagsOf(typedFor.Id));
        Assert.IsEmpty(shown.TagsOf(next.Id));
    }

    [TestMethod]
    public async Task AFullWidthPictureKeepsItsDisplayPixelSizeWithTheButtonsOverIt()
    {
        using var shown = ShownWindow.Open();
        var seed = (await shown.OpenStash(shown.Picture("width seed.png"))).Single();
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        // Measure the row across the preview, including any room sibling buttons could take from the picture.
        var width = TrayDesktop.Get(() =>
        {
            shown.OpenPreview(seed);
            shown.Window.UpdateLayout();
            var margin = shown.PreviewPicture.Margin;
            return (int)Math.Floor((shown.Preview.Child!.Bounds.Width - margin.Left - margin.Right) * shown.Window.RenderScaling);
        });
        var path = shown.Picture("full width.png", width, 96, (_, _) => SKColors.Red);
        var drop = new DropContents([new DroppedItem("full width.png", path, null, false)], false, false, []);
        var added = await TrayDesktop.Get(() => shown.Import.Drop(drop));
        var fullWidth = added.Single().Item!;
        await shown.AddTo("width newest.png");
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(shown.Listing.Settle);
        TrayDesktop.Run(() =>
        {
            shown.Preview.Open(fullWidth);
            shown.EditButton.Focus(NavigationMethod.Tab);
            Assert.AreSame(shown.EditButton, shown.Focused, "the picture's focus ring would cover its outer pixels");
            Assert.IsTrue(shown.Preview.StepButtons.Back.IsEffectivelyVisible);
            Assert.IsTrue(shown.Preview.StepButtons.On.IsEffectivelyVisible);
            Assert.IsTrue(shown.Preview.StepButtons.Back.IsEnabled);
            Assert.IsTrue(shown.Preview.StepButtons.On.IsEnabled);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image?.PixelSize == new PixelSize(width, 96)));

        var rendered = await RenderedWindow.Of(shown);
        var (left, top, right, bottom) = rendered.Bounds(0xFFFF0000, rendered.Area);
        Assert.AreEqual(width, rendered.Picture.Width, 0.001, "the picture fits without scaling");
        Assert.AreEqual(width, right - left + 1, 1, "the rendered width in display pixels");
        Assert.AreEqual(96, bottom - top + 1, 1, "the rendered height in display pixels");
        TrayDesktop.Run(() =>
        {
            var back = RenderedWindow.Place(shown.Window, shown.Preview.StepButtons.Back);
            var next = RenderedWindow.Place(shown.Window, shown.Preview.StepButtons.On);
            Assert.IsTrue(rendered.Picture.Contains(back.Center), "Previous overlays the picture");
            Assert.IsTrue(rendered.Picture.Contains(next.Center), "Next overlays the picture");
            Assert.IsLessThan(back.Width, back.Center.X - rendered.Picture.X, "Previous sits at the left edge");
            Assert.IsLessThan(next.Width, rendered.Picture.Right - next.Center.X, "Next sits at the right edge");
        });
    }

    [TestMethod]
    public async Task TheButtonPressLeavesTheAnimationPlayingAndPressingThePicturePausesIt()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Fixture("long.gif", "clip alpha.gif"), shown.Fixture("animated.gif", "clip beta.gif"));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await shown.ClickSearchInFront();
        var items = TrayDesktop.Get(() => shown.Listing.Items.ToArray());
        TrayDesktop.Run(() => shown.OpenPreview(items[0]));
        await WaitForAnimationFrame(shown);

        await ClickStep(shown, shown.Preview.StepButtons.On, () =>
        {
            Assert.AreEqual(items[0].Id, shown.Preview.Item?.Id, "the button is still down");
            Assert.IsFalse(shown.PreviewPicture.Paused, "the button press never pauses the outgoing picture");
        });

        await WaitForStep(shown, items[1]);
        await WaitForAnimationFrame(shown);
        var picture = TrayDesktop.Get(() => shown.PreviewPicture);
        TestMouse.Click(await HitCentre(shown, picture));
        await Eventually(() => TrayDesktop.Get(() => picture.Paused));
    }

    [TestMethod]
    public async Task BothButtonGroundsAndGlyphsStandOutOnWhiteAndBlackInBothThemes()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(
            shown.Picture("oldest.png"),
            shown.Picture("white.png", 2048, 640, (_, _) => SKColors.White),
            shown.Picture("black.png", 2048, 640, (_, _) => SKColors.Black),
            shown.Picture("newest.png"));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        var requested = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = theme);
                await Eventually(() => TrayDesktop.Get(() => shown.Window.ActualThemeVariant == theme));
                var pill = TrayDesktop.Get(() => shown.Window.TryFindResource(LookKeys.PicturePill, theme, out var value) && value is ISolidColorBrush brush
                    ? brush
                    : throw new AssertFailedException($"no picture pill in {theme}"));
                foreach (var item in items.Where(item => item.Name is "white" or "black"))
                {
                    TrayDesktop.Run(() =>
                    {
                        shown.OpenPreview(item);
                        Assert.IsTrue(shown.Preview.StepButtons.Back.IsEnabled);
                        Assert.IsTrue(shown.Preview.StepButtons.On.IsEnabled);
                    });
                    await Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is not null));
                    TestMouse.MoveTo(TestMouse.Away);
                    await Eventually(() => TrayDesktop.Get(() => !shown.PreviewPicture.IsPointerOver && !shown.Preview.StepButtons.IsPointerOver));
                    var atRest = await RenderedWindow.Of(shown);
                    // Over the picture's row, on the picture between the buttons, so neither shows its own hover ground.
                    TestMouse.MoveTo(TestMouse.Centre(shown.PreviewPicture));
                    await Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.IsPointerOver));
                    var rendered = await RenderedWindow.Of(shown);
                    var color = item.Name == "white" ? 0xFFFFFFFFu : 0xFF000000u;
                    foreach (var button in new[] { shown.Preview.StepButtons.Back, shown.Preview.StepButtons.On })
                    {
                        var place = TrayDesktop.Get(() => RenderedWindow.Place(shown.Window, button));
                        var beside = place.Center - new Point(0, place.Height / 2 + 6 * rendered.Scaling);
                        var what = $"{theme}, {item.Name} picture, {TrayDesktop.Get(() => AutomationProperties.GetName(button))}";
                        Assert.IsTrue(rendered.Picture.Contains(beside), what);
                        Assert.AreEqual(color, rendered.At((int)beside.X, (int)beside.Y), $"picture beside {what}");

                        // The central rectangle excludes the circular edge. Its most common colour is the pill's ground.
                        var face = place.Deflate(6 * rendered.Scaling);
                        var ground = rendered.Commonest(face, 1).Single();
                        var standsOut = Math.Abs(Brightness(ground) - Brightness(color));
                        Assert.IsGreaterThanOrEqualTo(16, standsOut, $"ground against picture: {what}");
                        // Full strength is the pill itself over the picture: a fainter disc could still clear the bar above over black.
                        var pillOver = Brightness(color) + (pill.Color.G - Brightness(color)) * pill.Color.A / 255d * pill.Opacity;
                        Assert.AreEqual(pillOver, Brightness(ground), 4d, $"full strength: {what}");
                        var glyphPixels = rendered.ColorCounts(face.X, face.Y, face.Width, face.Height)
                            .Where(pixel => Brightness(pixel.Key) >= Brightness(ground) + 25)
                            .Sum(pixel => pixel.Value);
                        Assert.IsGreaterThanOrEqualTo(3, glyphPixels, $"light glyph against ground: {what}");

                        // With the pointer away, the disc departs from the picture at most half as far: faint, not solid.
                        var restGround = atRest.Commonest(face, 1).Single();
                        Assert.IsLessThanOrEqualTo(standsOut / 2, Math.Abs(Brightness(restGround) - Brightness(color)), $"faint at rest: {what}");
                    }
                }
            }
        }
        finally
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = requested);
        }
    }

    [TestMethod]
    public async Task EscapeDuringTheClicksHeldRenameClosesOnTheRenamedItem()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("held alpha.png"), shown.Picture("held beta.png"));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await shown.ClickSearchInFront();
        var typedFor = TrayDesktop.Get(() => shown.Listing.Items[0]);
        const string renamed = "renamed while closing";
        StartRename(shown, typedFor, renamed);
        var button = shown.Preview.StepButtons.On;
        var at = await HitCentre(shown, button);

        using (new FileStream(typedFor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TestMouse.Click(at);
            await Eventually(() => TrayDesktop.Get(() =>
            {
                if (shown.Preview.EditingName)
                {
                    return false;
                }

                Assert.AreEqual(typedFor.Id, shown.Preview.Item?.Id, "the step is waiting for the held rename");
                Assert.AreSame(shown.PreviewPicture, shown.Focused);
                Tap(shown.PreviewPicture, Key.Escape);
                Assert.IsTrue(shown.Preview.ClosingAfterCommit, "Esc arrived while the commit was pending");
                return true;
            }));
        }

        await Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(shown.Grid, shown.Focused);
            Assert.AreEqual(typedFor.Id, shown.Listing.Focused);
            CollectionAssert.AreEqual(new[] { typedFor.Id }, shown.Listing.Selection.ToList());
            Assert.AreEqual(renamed, shown.Listing.Items.Single(item => item.Id == typedFor.Id).Name);
        });
    }

    internal static async Task ClickStep(ShownWindow shown, Button button, Action? whilePressed = null)
    {
        var at = await HitCentre(shown, button);
        if (whilePressed is null)
        {
            TestMouse.Click(at);
            return;
        }

        TestMouse.Press(at);
        try
        {
            await Eventually(() => TrayDesktop.Get(() => button.IsPressed));
            TrayDesktop.Run(() =>
            {
                Assert.IsFalse(button.IsFocused);
                whilePressed();
            });
        }
        finally
        {
            TestMouse.Release(at);
        }
    }

    private static TextBox StartRename(ShownWindow shown, StashItem item, string name) => TrayDesktop.Get(() =>
    {
        shown.OpenPreview(item);
        Tap(shown.PreviewPicture, Key.F2);
        shown.Window.UpdateLayout();
        var box = shown.NameBox;
        Assert.AreSame(box, shown.Focused);
        Type(box, name);
        Assert.AreEqual(name, box.Text);
        return box;
    });

    private static Task WaitForStep(ShownWindow shown, StashItem item) => Eventually(() => TrayDesktop.Get(() =>
        shown.Preview.Item?.Id == item.Id && shown.Listing.Selection.SequenceEqual([item.Id])));

    private static async Task WaitForAnimationFrame(ShownWindow shown)
    {
        await Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is not null));
        var first = TrayDesktop.Get(() => shown.PreviewPicture.Image);
        await Eventually(() => TrayDesktop.Get(() =>
            shown.PreviewPicture.Image is { } frame && !ReferenceEquals(first, frame)));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.PreviewPicture.Animated);
            Assert.IsFalse(shown.PreviewPicture.Paused);
        });
    }

    private static async Task<PixelPoint> HitCentre(ShownWindow shown, Control target)
    {
        TrayDesktop.Run(shown.Window.UpdateLayout);
        await Eventually(() => TrayDesktop.Get(() =>
        {
            var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), shown.Window);
            return centre is { } point && shown.Window.InputHitTest(point) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(target);
        }));
        return TestMouse.Centre(target);
    }

    private static void AssertButton(Button button, string name, string tip)
    {
        Assert.IsTrue(button.IsEffectivelyVisible);
        Assert.AreEqual(name, AutomationProperties.GetAutomationId(button));
        Assert.AreEqual(name, AutomationProperties.GetName(button));
        Assert.AreEqual(tip, ToolTip.GetTip(button));
    }

    private static int Brightness(uint pixel) => (int)((pixel >> 8) & 0xFF);
}
