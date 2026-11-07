using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Look;

/// <summary>
/// The take on the desktop (F10): the surfaces drawn by hand (a tile's ground, the preview's and the editor's surround)
/// show a take applied at once on the screen, with no other input to repaint them; the head's buttons, the search box's
/// focus mark and the chips show the take's keys, at rest and hovered, lit and not; and a focused field, the search box
/// and the tag field alike, keeps its grey edge on every side on the screen with its bottom edge in the accent.
/// </summary>
[TestClass]
public sealed class LooksTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    /// <summary>Whatever a check applied, the next sees the product's take.</summary>
    [TestCleanup]
    public void PutTheProductsTakeBack() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task TheTileThePreviewsAndTheEditorsSurroundsShowATakeAppliedAtOnceOnTheScreen()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        var clear = await shown.OpenStash(shown.Picture("clear.png", 120, 80, (_, _) => SKColors.Transparent));
        var item = clear[0];
        await Task.Delay(500);

        await Follows("the tile's ground (G6)", LookKeys.TileGround, () => shown.TileOnScreen(item.Id));

        TrayDesktop.Run(() => shown.OpenPreview(item));
        await Task.Delay(500);
        await Follows("the preview's surround", LookKeys.PictureSurround, () => shown.PreviewPicture.PointToScreen(new Point(shown.PreviewPicture.Bounds.Width / 2, 6)));

        TrayDesktop.Run(() => shown.PreviewPicture.Focus());
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Preview));
        await Task.Delay(800);
        using var editor = new ShownEditor(shown);
        await Follows("the editor's surround", LookKeys.PictureSurround, () =>
        {
            var box = editor.Box;
            var canvas = editor.Canvas;
            // Beside the picture, away from the canvas's rounded corners.
            var beside = box.X > 12 ? new Point(box.X / 2, box.Center.Y) : new Point(box.Center.X, box.Y / 2);
            Assert.IsTrue(new Rect(canvas.Bounds.Size).Contains(beside) && !box.Contains(beside), $"a point of the surround: {beside} in {canvas.Bounds}, the picture at {box}");
            return canvas.PointToScreen(beside);
        });
    }

    [TestMethod]
    public async Task TheHeadsButtonsTheSearchBoxAndTheChipsShowTheTakesKeysAtRestHoveredAndLit()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("tagged.png", 40, 30, (_, _) => SKColors.Teal));
        await shown.Tag(items, "joy");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.ShownChips().Contains("#joy")));
        // A take whose lit chip is no accent of Fluent's, so a chip left in Fluent's accent shows.
        TrayDesktop.Run(() => Looks.Apply(Application.Current!, new CheckTake(), Looks.Accent(Application.Current!)));

        foreach (var name in new[] { "Settings", "Add to your stash", "Keep open" })
        {
            var button = TrayDesktop.Get(() => shown.Window.GetVisualDescendants().OfType<Button>().First(each => Avalonia.Automation.AutomationProperties.GetName(each) == name));
            TrayDesktop.Run(() => Assert.AreEqual(Colors.Transparent, Ground(button), $"{name}: no ground of its own at rest, the window's showing (G32)"));
            TestMouse.MoveTo(TrayDesktop.Get(() => button.PointToScreen(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2))));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => button.IsPointerOver));
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(AppliedLook.Of(button, LookKeys.IconButtonHoverGround), Ground(button), $"{name}: the take's hover ground");
                Assert.AreEqual(AppliedLook.Of(button, LookKeys.IconButtonHoverWords), Words(button), $"{name}: the take's hover words");
            });
            TestMouse.MoveTo(TestMouse.Away);
        }

        TrayDesktop.Run(() =>
        {
            var favorites = shown.Chip("Favorites");
            var all = shown.Chip("All");
            var joy = shown.Chip("#joy");
            Assert.IsTrue(shown.Lit("All") && !shown.Lit("Favorites") && !shown.Lit("#joy"), "All lit after the import (G12)");
            Assert.AreEqual(AppliedLook.Of(favorites, LookKeys.ScopeChipGround), Ground(favorites), "a scope chip's ground");
            Assert.AreEqual(AppliedLook.Of(favorites, LookKeys.ScopeChipBorder), Solid(Presenter(favorites).BorderBrush), "a scope chip's edge");
            Assert.AreEqual(AppliedLook.Of(favorites, LookKeys.ScopeChipWords), Words(favorites), "a scope chip's words");
            Assert.AreEqual(AppliedLook.Of(all, LookKeys.LitChipGround), Ground(all), "a lit chip's ground");
            Assert.AreEqual(AppliedLook.Of(all, LookKeys.LitChipWords), Words(all), "a lit chip's words");
            Assert.AreEqual(AppliedLook.Of(joy, LookKeys.TagChipGround), Ground(joy), "a tag chip's ground");
            Assert.IsTrue(favorites.TryFindResource(LookKeys.ChipRadius, favorites.ActualThemeVariant, out var radius));
            Assert.AreEqual(radius, Presenter(favorites).CornerRadius, "a chip's corners");
        });

        var chip = TrayDesktop.Get(() => shown.Chip("Favorites"));
        TestMouse.MoveTo(TrayDesktop.Get(() => chip.PointToScreen(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => chip.IsPointerOver));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(AppliedLook.Of(chip, LookKeys.ScopeChipHoverGround), Ground(chip), "a hovered scope chip's wash (G32)");
            Assert.AreEqual(AppliedLook.Of(chip, LookKeys.ScopeChipHoverWords), Words(chip), "a hovered scope chip's words");
        });
        TestMouse.MoveTo(TestMouse.Away);

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            shown.Window.UpdateLayout();
            var border = shown.Search.GetVisualDescendants().OfType<Border>().First(each => each.Name == "PART_BorderElement");
            Assert.IsTrue(shown.Search.TryFindResource(LookKeys.FluentFieldFocusedThickness, shown.Search.ActualThemeVariant, out var thickness));
            Assert.AreEqual(thickness, border.BorderThickness, "the focused search box's mark is the take's (F10)");
            Assert.IsTrue(shown.Search.TryFindResource(LookKeys.FluentFieldFocusedBorder, shown.Search.ActualThemeVariant, out var focusBrush));
            Assert.AreSame(focusBrush, border.BorderBrush, "in the take's focus brush");
        });
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task AFocusedFieldKeepsItsGreyEdgeOnEverySideOnTheScreenWithItsBottomEdgeInTheAccent(string theme)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        var requested = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        try
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark);
            var items = await shown.OpenStash(shown.Picture("edges.png", 40, 30, (_, _) => SKColors.Teal));

            TrayDesktop.Run(() => shown.Grid.Focus());
            await EdgesEventually(shown, () => shown.Search, focused: false, "the search box at rest");
            TrayDesktop.Run(() => shown.Search.Focus());
            await EdgesEventually(shown, () => shown.Search, focused: true, "the search box focused");

            TrayDesktop.Run(() =>
            {
                shown.RightClick(items[0].Id);
                shown.Submenu("Add tag");
                shown.ClickSubmenuEntry("Add tag", "Type a tag…");
                shown.CloseMenu();
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
            await EdgesEventually(shown, () => shown.TagBox, focused: true, "the tag field focused");
        }
        finally
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = requested);
        }
    }

    /// <summary>
    /// Reads the screen at the middle of the left, top and bottom edges of <paramref name="field"/>'s border until they show
    /// the field's resting grey all round at rest, or, while <paramref name="focused"/>, an edge on the left and the top
    /// that is neither the field's ground nor the window's, and a bottom edge in the accent's mark, apart from the sides;
    /// fails, naming what each showed, after ten seconds.
    /// </summary>
    private static async Task EdgesEventually(ShownWindow shown, Func<TextBox> field, bool focused, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            await Task.Delay(200);
            var (left, top, bottom, edge, mark, ground, window) = TrayDesktop.Get(() =>
            {
                var box = field();
                var border = box.GetVisualDescendants().OfType<Border>().First(each => each.Name == "PART_BorderElement");
                var size = border.Bounds.Size;
                var (origin, scale) = (border.PointToScreen(default), TopLevel.GetTopLevel(border)!.RenderScaling);
                PixelPoint At(double x, double y) => new(origin.X + (int)Math.Floor(x * scale), origin.Y + (int)Math.Floor(y * scale));
                return (Pixel(At(0.5, size.Height / 2)), Pixel(At(size.Width / 2, 0.5)), Pixel(At(size.Width / 2, size.Height - 1)),
                    AppliedLook.Of(box, LookKeys.FluentFieldBorder), AppliedLook.Of(box, LookKeys.SelectionRing),
                    AppliedLook.Of(box, focused ? LookKeys.FluentFieldFocusedGround : LookKeys.FluentFieldGround), AppliedLook.Of(shown.Window, LookKeys.FluentWindowGround));
            });
            var held = focused
                ? Near(left, edge) && Near(top, edge) && left != ground && top != ground && left != window && top != window && Near(bottom, mark) && !Near(bottom, left)
                : Near(left, edge) && Near(top, edge) && Near(bottom, edge);
            if (held)
            {
                return;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow,
                $"{what}: left {left}, top {top}, bottom {bottom}; the resting grey {edge}, the accent's mark {mark}, the field's ground {ground}, the window's {window}");
        }
    }

    private static Color Pixel(PixelPoint at)
    {
        var grab = new WindowsScreen().Grab(new PixelRect(at, new PixelSize(1, 1)), withPointer: false);
        Assert.IsTrue(grab.Succeeded, $"the screen at {at} could not be read");
        var pixels = grab.Value!.Pixels;
        return Color.FromRgb(pixels[2], pixels[1], pixels[0]);
    }

    private static bool Near(Color one, Color other) => Math.Abs(one.R - other.R) + Math.Abs(one.G - other.G) + Math.Abs(one.B - other.B) <= 24;

    /// <summary>
    /// The screen at <paramref name="where"/> shows <paramref name="key"/>'s colour under the product's take, then the
    /// checks' own at once when it is applied, then the product's again, with nothing but the take's change to repaint it.
    /// </summary>
    private static async Task Follows(string what, string key, Func<PixelPoint> where)
    {
        var at = TrayDesktop.Get(where);
        var variant = TrayDesktop.Get(() => Application.Current!.ActualThemeVariant);
        var product = TrayDesktop.Get(() => ((ISolidColorBrush)Looks.All[0].Resources(variant, Looks.Accent(Application.Current!))[key]!).Color);
        await ShowsEventually(at, product, $"{what} under the product's take");

        TrayDesktop.Run(() => Looks.Apply(Application.Current!, new CheckTake(), Looks.Accent(Application.Current!)));
        await ShowsEventually(at, CheckTake.ColorOf(key, variant), $"{what} under the checks' take, at once");

        TrayDesktop.UseProductTheme();
        await ShowsEventually(at, product, $"{what} under the product's take again");
    }

    private static async Task ShowsEventually(PixelPoint at, Color expected, string what)
    {
        var screen = new WindowsScreen();
        var seen = default(Color);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var grab = screen.Grab(new PixelRect(at, new PixelSize(1, 1)), withPointer: false);
            Assert.IsTrue(grab.Succeeded, $"{what}: the screen at {at} could not be read");
            var pixels = grab.Value!.Pixels;
            seen = Color.FromRgb(pixels[2], pixels[1], pixels[0]);
            if (seen == Color.FromRgb(expected.R, expected.G, expected.B))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"{what}: the screen at {at} shows {seen}, not {expected}");
    }

    private static ContentPresenter Presenter(Button button) => button.GetVisualDescendants().OfType<ContentPresenter>().First(each => each.Name == "PART_ContentPresenter");

    private static Color Ground(Button button) => Solid(Presenter(button).Background);

    private static Color Words(Button button) => Solid(Presenter(button).Foreground);

    private static Color Solid(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;
}
