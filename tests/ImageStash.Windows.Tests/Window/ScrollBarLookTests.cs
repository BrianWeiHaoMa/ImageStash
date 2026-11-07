using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Every scroll bar of the product is one thin bar (F10, read as the look's own rule): Fluent's thin thumb with
/// semicircular ends, a little wider than its two pixels and narrower than its sixteen-pixel strip, which keeps that
/// width, and shows no ground, track or arrow, while the real pointer rests on it past Fluent's delay and while it is
/// expanded at once; under the
/// pointer the thumb takes Fluent's own hover colour. The strip keeps its presses (G7), the thin thumb drags the grid
/// and stays thin while held, and the wheel still scrolls (G6);
/// the Settings page's bar, light or dark, is the grid's (G32), and a bar across the other axis is the same bar.
/// These checks rest the real pointer on the stash window's grid at the primary display's left edge, and on the
/// Settings page centred on the primary display; a window of their own off every display holds a sideways scroller.
/// </summary>
[TestClass]
public sealed class ScrollBarLookTests
{
    /// <summary>Fluent's sixteen-pixel strip, its <c>ScrollBarSize</c>.</summary>
    private const double Strip = 16;

    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task TheGridsBarKeepsOneThinThumbWithNoTrackOrArrowsUnderTheRestingPointerAndTheWheelStillScrolls()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = await Scrolling();
        var (bar, thumb) = TrayDesktop.Get(() => Parts(Scroller(shown)));
        var atRest = TrayDesktop.Get(() => RenderedWidth(thumb, bar));
        Assert.IsTrue(atRest is > 2.5 and < Strip - 0.5, $"a thumb wider than Fluent's two pixels and narrower than the strip, not {atRest}");
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(bar.IsExpanded, "at rest");
            RoundEnded(thumb, atRest, thumb.Bounds.Width, "at rest");
        });

        await DrawnAt(shown.Window, TestMouse.Centre(thumb), part => part == thumb);
        TestMouse.MoveTo(TestMouse.Centre(thumb));
        // Past Fluent's half-second show delay, so Fluent has expanded it, and past its 0.1 s transitions.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => bar.IsExpanded));
        await Task.Delay(1000);

        TrayDesktop.Run(() =>
        {
            ThinAsAtRest(bar, thumb, atRest, "under the resting pointer");
            var hover = bar.TryFindResource("ScrollBarThumbFillPointerOver", bar.ActualThemeVariant, out var brush) ? brush : null;
            Assert.IsNotNull(hover, "Fluent's hover colour for the thumb");
            Assert.AreEqual(hover, ThumbGround(thumb), "the thumb in Fluent's own hover colour");
        });

        var scroller = TrayDesktop.Get(() => Scroller(shown));
        TestMouse.WheelDown(TestMouse.Centre(thumb), 2);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y) > 0);
    }

    [TestMethod]
    public async Task TheGridsBarExpandedAtOnceKeepsTheSameThinThumbWithNoTrackOrArrows()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = await Scrolling();
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        var (bar, thumb) = TrayDesktop.Get(() => Parts(scroller));
        var (atRest, restingGround) = TrayDesktop.Get(() => (RenderedWidth(thumb, bar), thumb.Background));
        try
        {
            // The bar expands at once, with no timer, while its scroller does not hide it.
            TrayDesktop.Run(() => scroller.AllowAutoHide = false);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => bar.IsExpanded));
            await Task.Delay(300);
            TrayDesktop.Run(() =>
            {
                ThinAsAtRest(bar, thumb, atRest, "expanded");
                Assert.AreEqual(restingGround, thumb.Background, "the thumb in its resting colour while the pointer is not on it");
            });
        }
        finally
        {
            TrayDesktop.Run(() => scroller.AllowAutoHide = true);
        }
    }

    [TestMethod]
    public async Task APressOnTheStripBelowTheThumbPagesTheGridAndLeavesTheSelectionAndFocus()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = await Scrolling();
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        var (bar, thumb) = TrayDesktop.Get(() => Parts(scroller));
        TrayDesktop.Run(() => Assert.AreEqual(Strip, bar.Bounds.Width, 0.5, "Fluent's strip, as wide as before, for a person to press (G7)"));
        var (selection, focused) = TrayDesktop.Get(() => (shown.Listing.Selection.ToList(), shown.Focused));
        var belowThumb = TrayDesktop.Get(() => thumb.PointToScreen(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height + 40)));
        Assert.IsGreaterThan(belowThumb.Y, TrayDesktop.Get(() => bar.PointToScreen(new Point(0, bar.Bounds.Height)).Y), "the press lands on the strip, below the thumb");
        await DrawnAt(shown.Window, belowThumb, part => part is RepeatButton { Name: "PART_PageDownButton" });

        TestMouse.Click(belowThumb);

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y) > scroller.Viewport.Height / 2);
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(selection, shown.Listing.Selection.ToList(), "the selection as it was");
            Assert.AreSame(focused, shown.Focused, "the focus as it was: the scroll bar keeps its own presses (G7)");
        });
    }

    [TestMethod]
    public async Task TheThinThumbDraggedWithTheRealPointerScrollsTheGridAndStaysThinWhileHeld()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = await Scrolling();
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        var (bar, thumb) = TrayDesktop.Get(() => Parts(scroller));
        var atRest = TrayDesktop.Get(() => RenderedWidth(thumb, bar));
        var start = TestMouse.Centre(thumb);
        await DrawnAt(shown.Window, start, part => part == thumb);

        TestMouse.Press(start);
        try
        {
            for (var step = 1; step <= 6; step++)
            {
                TestMouse.MoveTo(new PixelPoint(start.X, start.Y + step * 15));
                await Task.Delay(50);
            }

            await ShownWindow.Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y) > 0);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => bar.IsExpanded));
            await Task.Delay(300);
            TrayDesktop.Run(() => ThinAsAtRest(bar, thumb, atRest, "while its thumb is dragged"));
        }
        finally
        {
            TestMouse.Release(new PixelPoint(start.X, start.Y + 90));
        }

        var dropped = TrayDesktop.Get(() => scroller.Offset.Y);
        await Task.Delay(300);
        Assert.AreEqual(dropped, TrayDesktop.Get(() => scroller.Offset.Y), 0.5, "the grid stays where the let-go left it");
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task TheSettingsPagesBarAtItsSmallestIsTheSameThinBarUnderTheRestingPointer(string theme)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        double gridThumb;
        using (var shown = await Scrolling())
        {
            gridThumb = TrayDesktop.Get(() => RenderedWidth(Parts(Scroller(shown)).Thumb, Parts(Scroller(shown)).Bar));
        }

        using var settings = new SettingsFolder();
        var requested = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        var window = TrayDesktop.Get(() =>
        {
            Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            return SettingsWindow.Open(null, () => settings.Page());
        });
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible && window.FrameSize is not null && InsideWorkingArea(window)));
            // The page places itself once more at its default size at this priority (SettingsWindow.ShowCentred): shrunk
            // before then, it grows back and moves after the thumb is found, and the pointer misses the bar.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            TrayDesktop.Run(() =>
            {
                window.Width = 1;
                window.Height = 1;
            });
            var minimum = new Size(SettingsWindow.MinimumSize.Width, SettingsWindow.MinimumSize.Height);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.ClientSize == minimum));
            var viewer = TrayDesktop.Get(() => (ScrollViewer)window.Content!);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => viewer.Extent.Height > viewer.Viewport.Height + 1));
            var (bar, thumb) = TrayDesktop.Get(() => Parts(viewer));
            var atRest = TrayDesktop.Get(() => RenderedWidth(thumb, bar));
            Assert.AreEqual(gridThumb, atRest, 0.5, "the grid's thin thumb (G32)");

            await DrawnAt(window, TestMouse.Centre(thumb), part => part == thumb);
            TestMouse.MoveTo(TestMouse.Centre(thumb));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => bar.IsExpanded));
            await Task.Delay(1000);
            TrayDesktop.Run(() =>
            {
                ThinAsAtRest(bar, thumb, atRest, $"on the {theme} page under the resting pointer");
                Assert.IsTrue(ThumbGround(thumb) is ISolidColorBrush { Color.A: > 0 }, $"the thumb drawn in Fluent's {theme} colour");
            });
        }
        finally
        {
            TrayDesktop.Run(() =>
            {
                window.Close();
                Application.Current!.RequestedThemeVariant = requested;
            });
        }
    }

    [TestMethod]
    public async Task AHorizontalBarExpandedAtOnceIsTheSameThinBarAcrossItsAxis()
    {
        // No scroller of the product scrolls sideways: a window of the check's own, off every display, holds one.
        var (window, scroller) = TrayDesktop.Get(() =>
        {
            var wide = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new Border { Width = 900, Height = 40 },
            };
            var own = new Avalonia.Controls.Window { Position = new PixelPoint(-8000, 100), Width = 300, Height = 120, ShowActivated = false, ShowInTaskbar = false, Content = wide };
            own.Show();
            return (own, wide);
        });
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => scroller.Extent.Width > scroller.Viewport.Width + 1));
            var (bar, thumb, atRest, restingGround) = TrayDesktop.Get(() =>
            {
                var horizontal = scroller.GetVisualDescendants().OfType<ScrollBar>().Single(each => each.Orientation == Orientation.Horizontal && each.TemplatedParent == scroller);
                var its = horizontal.GetVisualDescendants().OfType<Thumb>().Single();
                return (horizontal, its, RenderedHeight(its, horizontal), its.Background);
            });
            Assert.IsTrue(atRest is > 2.5 and < Strip - 0.5, $"a thumb taller than Fluent's two pixels and shorter than the strip, not {atRest}");
            TrayDesktop.Run(() => RoundEnded(thumb, atRest, thumb.Bounds.Height, "at rest"));

            TrayDesktop.Run(() => scroller.AllowAutoHide = false);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => bar.IsExpanded));
            await Task.Delay(300);
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(atRest, RenderedHeight(thumb, bar), 0.5, "the thumb as thin as at rest, expanded");
                RoundEnded(thumb, RenderedHeight(thumb, bar), thumb.Bounds.Height, "expanded");
                Assert.AreEqual(restingGround, thumb.Background, "the thumb in its resting colour, expanded");
                Assert.AreEqual(0, bar.GetVisualDescendants().OfType<Rectangle>().Single(each => each.Name == "TrackRect").Opacity, "no track, expanded");
                Assert.IsTrue(bar.GetVisualDescendants().OfType<RepeatButton>().Where(each => each.Name is "PART_LineUpButton" or "PART_LineDownButton").All(arrow => arrow.Opacity == 0), "no arrows, expanded");
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    /// <summary>A flyout at its default size over sixty pictures, so its grid scrolls, with the first selected and no notice up.</summary>
    private static async Task<ShownWindow> Scrolling()
    {
        var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("first.png", 8, 6, (_, _) => new SkiaSharp.SKColor(0xC8, 0x28, 0x0A)));
        shown.AddPictures(59, _ => "pile");
        await Dispatcher.UIThread.InvokeAsync(() => shown.Listing.Refresh());
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() =>
        {
            foreach (var notice in shown.Notices.Shown.ToList())
            {
                shown.Notices.Close(notice);
            }
        });
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => scroller.Extent.Height > scroller.Viewport.Height * 2));
        return shown;
    }

    /// <summary>
    /// The real pointer reaches only what has been drawn: waits until the window's last frame shows <paramref name="part"/>
    /// at <paramref name="at"/>, in physical pixels on the desktop.
    /// </summary>
    private static Task DrawnAt(TopLevel window, PixelPoint at, Func<Visual, bool> part) =>
        ShownWindow.Eventually(() => TrayDesktop.Get(() => window.InputHitTest(window.PointToClient(at)) is Visual hit && hit.GetSelfAndVisualAncestors().Any(part)));

    private static ScrollViewer Scroller(ShownWindow shown) => shown.Grid.FindAncestorOfType<ScrollViewer>()!;

    /// <summary>The scroller's own vertical bar, not one of a scroller inside it, and its thumb.</summary>
    private static (ScrollBar Bar, Thumb Thumb) Parts(ScrollViewer scroller)
    {
        var bar = scroller.GetVisualDescendants().OfType<ScrollBar>().Single(each => each.Orientation == Orientation.Vertical && each.TemplatedParent == scroller);
        return (bar, bar.GetVisualDescendants().OfType<Thumb>().Single());
    }

    /// <summary>The thumb's width as drawn, its render transform applied, in the bar's units.</summary>
    private static double RenderedWidth(Thumb thumb, ScrollBar bar) =>
        thumb.TranslatePoint(new Point(thumb.Bounds.Width, 0), bar)!.Value.X - thumb.TranslatePoint(default, bar)!.Value.X;

    /// <summary>A horizontal thumb's height as drawn, its render transform applied, in the bar's units.</summary>
    private static double RenderedHeight(Thumb thumb, ScrollBar bar) =>
        thumb.TranslatePoint(new Point(0, thumb.Bounds.Height), bar)!.Value.Y - thumb.TranslatePoint(default, bar)!.Value.Y;

    /// <summary>What the thumb's template paints it with.</summary>
    private static IBrush? ThumbGround(Thumb thumb) => thumb.GetVisualDescendants().OfType<Border>().First().Background;

    /// <summary>
    /// The thumb is drawn <paramref name="drawn"/> thick as it is laid out, <paramref name="laidOut"/>, so no transform
    /// squashes its ends into ellipses, and its template rounds each end into a semicircle across it.
    /// </summary>
    private static void RoundEnded(Thumb thumb, double drawn, double laidOut, string when)
    {
        Assert.AreEqual(laidOut, drawn, 0.5, $"the thumb drawn as thin as it is laid out {when}");
        var corners = thumb.GetVisualDescendants().OfType<Border>().First().CornerRadius;
        Assert.IsTrue(new[] { corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft }.All(radius => Math.Abs(radius - drawn / 2) < 0.5),
            $"each end of the thumb a semicircle {when}, not corners of {corners} on a thumb {drawn} thick");
    }

    /// <summary>The bar, now expanded, draws its thumb at its resting width and no ground, track or arrow.</summary>
    private static void ThinAsAtRest(ScrollBar bar, Thumb thumb, double atRest, string when)
    {
        Assert.IsTrue(bar.IsExpanded, $"Fluent expanded the bar {when}");
        Assert.AreEqual(atRest, RenderedWidth(thumb, bar), 0.5, $"the thumb as thin as at rest {when}");
        RoundEnded(thumb, RenderedWidth(thumb, bar), thumb.Bounds.Width, when);
        Assert.IsTrue(thumb.IsEffectivelyVisible && thumb.Opacity > 0 && ThumbGround(thumb) is ISolidColorBrush { Color.A: > 0, Opacity: > 0 }, $"the thumb drawn {when}");
        var track = bar.GetVisualDescendants().OfType<Rectangle>().Single(each => each.Name == "TrackRect");
        Assert.AreEqual(0, track.Opacity, $"no track {when}");
        foreach (var arrow in bar.GetVisualDescendants().OfType<RepeatButton>().Where(each => each.Name is "PART_LineUpButton" or "PART_LineDownButton"))
        {
            Assert.AreEqual(0, arrow.Opacity, $"no {arrow.Name} {when}");
        }

        var root = bar.GetVisualDescendants().OfType<Grid>().Single(each => each.Name == "Root");
        Assert.IsTrue(root.Background is null || root.Background is ISolidColorBrush { Color.A: 0 }, $"no ground behind the bar {when}, not {root.Background}");
    }
}
