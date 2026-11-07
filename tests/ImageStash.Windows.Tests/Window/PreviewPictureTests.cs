using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The preview's picture in the stash window laid out by the real desktop platform (G19, G6, R7): a still at its own
/// size, one picture pixel to one display pixel, over a checkerboard under its transparent pixels; one larger than the
/// area scaled down whole; an animation playing in the preview while no tile beneath plays, a click pausing it on the
/// frame showing and another resuming from the next, and the tile playing again once the preview closes; hidden, the
/// window's preview plays nothing and goes on as it was left once shown.
/// </summary>
[TestClass]
public sealed class PreviewPictureTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;

    [TestMethod]
    public async Task AStillShowsAtItsOwnSizeOverACheckerboardAndOneLargerThanTheAreaIsScaledDownWhole()
    {
        using var shown = ShownWindow.Open();
        // Transparent on its left half, opaque red on its right.
        var halfClear = shown.Picture("preview clear.png", 64, 48, (x, _) => x < 32 ? SKColors.Transparent : new SKColor(255, 0, 0));
        var large = shown.Picture("preview large.png", 3000, 2000, (_, _) => new SKColor(0, 0, 255));
        var items = await shown.OpenStash(halfClear, large);
        var (clear, big) = (items.Single(item => item.Name == "preview clear"), items.Single(item => item.Name == "preview large"));

        TrayDesktop.Run(() => shown.OpenPreview(clear));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is not null));
        var small = await RenderedWindow.Of(shown);
        var redPixels = small.Count(Red);
        var underClear = small.Colors(small.Picture.X, small.Picture.Y, 32 * small.Scaling, small.Picture.Height);

        Assert.AreEqual(Math.Round(32 * small.Scaling) * Math.Round(48 * small.Scaling), redPixels, 1, "its own size: one picture pixel to one display pixel, not enlarged");
        Assert.HasCount(2, underClear, "a checkerboard of two colours under its transparent pixels (G6)");
        Assert.DoesNotContain(small.Background, underClear, "neither is the area's neutral fill");

        TrayDesktop.Run(() => shown.Preview.Open(big));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is { PixelSize.Width: 3000 }));
        var scaled = await RenderedWindow.Of(shown);
        var (left, top, right, bottom) = scaled.Bounds(Blue);

        // The focus ring, two pixels inside the area's edge, draws over the picture's outermost columns.
        Assert.AreEqual(scaled.Area.Width - 4 * scaled.Scaling, right - left + 1, 1, "scaled down to the area's width");
        Assert.AreEqual(scaled.Area.Width * 2 / 3.0, bottom - top + 1, 2, "whole, at its own ratio");
    }

    [TestMethod]
    public async Task AnAnimationPlaysInThePreviewWhileNoTilePlaysAClickPausesAndResumesAndClosingLetsTheTilePlayAgain()
    {
        // The hover below is the check's own pointer; a real cursor resting over the window's grid raises its own entered
        // and exited there, which clears that hover, so the tile it waits for would be one no pointer is over.
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        var dancing = shown.Fixture("long.gif", "preview dancing.gif");
        var hovered = shown.Fixture("animated.gif", "preview hovered.gif");
        var items = await shown.OpenStash(dancing, hovered);
        var (animated, other) = (items.Single(item => item.Name == "preview dancing"), items.Single(item => item.Name == "preview hovered"));
        TrayDesktop.Run(() => shown.MovePointer(other.Id, 0, 0));
        await ShownWindow.Eventually(() => ShownWindow.Held(other.FilePath));

        TrayDesktop.Run(() => shown.OpenPreview(animated));
        var colors = await ColorsOver(shown, TimeSpan.FromSeconds(1.5));
        Assert.IsGreaterThanOrEqualTo(2, colors.Count, "its frames advance");
        var drawn = new HashSet<uint>();
        for (var sample = 0; sample < 6; sample++)
        {
            var rendered = await RenderedWindow.Of(shown);
            drawn.Add(rendered.At((int)rendered.Picture.Center.X, (int)rendered.Picture.Center.Y));
        }

        Assert.IsGreaterThanOrEqualTo(2, drawn.Count, "and the window draws them, not only decodes them");
        await ShownWindow.Eventually(() => !ShownWindow.Held(other.FilePath));
        await Task.Delay(500);
        Assert.IsFalse(ShownWindow.Held(other.FilePath), "the hovered tile beneath plays nothing while the preview's animation plays (G6)");

        // Paused mid-clip, so that a restart from the first frame could not show one just ahead of the paused one.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is { } image && FrameOf(ColorOf(image)) is >= 15 and <= 30));
        TrayDesktop.Run(() => shown.Click(shown.PreviewPicture));
        var pausedOn = TrayDesktop.Get(() => shown.PreviewPicture.Image);
        await Task.Delay(700);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.PreviewPicture.Paused);
            Assert.IsNotNull(pausedOn, "the frame showing stays, never a blank picture");
            Assert.AreSame(pausedOn, shown.PreviewPicture.Image, "and no other frame comes");
        });

        var pausedColor = TrayDesktop.Get(() => ColorOf(pausedOn!));
        var pausedRender = await RenderedWindow.Of(shown);
        Assert.AreEqual(pausedColor | 0xFF000000, pausedRender.At((int)pausedRender.Picture.Center.X, (int)pausedRender.Picture.Center.Y), "the paused frame is what the window draws");
        TrayDesktop.Run(() => shown.Click(shown.PreviewPicture));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !ReferenceEquals(pausedOn, shown.PreviewPicture.Image)));
        var resumedColor = TrayDesktop.Get(() => ColorOf(shown.PreviewPicture.Image!));
        // A frame lasts 10 to 50 ms and the check looks every 20 ms or more, so it may first see a frame a few past the next.
        var (paused, resumed) = (FrameOf(pausedColor), FrameOf(resumedColor));
        Assert.IsTrue((resumed - paused + 60) % 60 is >= 1 and <= 15, $"it goes on from the frame after the one it paused on ({paused}), not from the first: {resumed}");

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Escape));
        await ShownWindow.Eventually(() => ShownWindow.Held(other.FilePath));
        await ShownWindow.Eventually(() => !ShownWindow.Held(animated.FilePath));
        TrayDesktop.Run(() => Assert.IsNull(shown.PreviewPicture.Image, "the preview's play stopped and its file closed"));
    }

    [TestMethod]
    public async Task WhileTheWindowIsHiddenThePreviewsAnimationHoldsItsFrameAndShownAgainGoesOnPlayingOrStaysPaused()
    {
        using var shown = ShownWindow.Open();
        var dancing = shown.Fixture("long.gif", "preview hidden.gif");
        var animated = (await shown.OpenStash(dancing)).Single();
        TrayDesktop.Run(() => shown.OpenPreview(animated));
        Assert.IsGreaterThanOrEqualTo(2, (await ColorsOver(shown, TimeSpan.FromSeconds(1))).Count, "it plays");

        TrayDesktop.Run(shown.Window.Hide);
        var heldOn = TrayDesktop.Get(() => shown.PreviewPicture.Image);
        var heldColor = TrayDesktop.Get(() => ColorOf(heldOn!));
        await Task.Delay(700);
        TrayDesktop.Run(() =>
        {
            Assert.IsNotNull(heldOn);
            Assert.AreSame(heldOn, shown.PreviewPicture.Image, "hidden, no frame comes: the preview plays nothing nobody sees (G6)");
            Assert.IsTrue(shown.Preview.IsOpen, "and stays open as it was left (G1)");
        });

        TrayDesktop.Run(shown.Window.Show);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !ReferenceEquals(heldOn, shown.PreviewPicture.Image)));
        var resumedColor = TrayDesktop.Get(() => ColorOf(shown.PreviewPicture.Image!));
        Assert.AreEqual((FrameOf(heldColor) + 1) % 60, FrameOf(resumedColor), "shown again it goes on from the frame after the one it held");
        TrayDesktop.Run(() => Assert.IsFalse(shown.PreviewPicture.Paused, "playing, as it was when hidden"));

        TrayDesktop.Run(() => shown.Click(shown.PreviewPicture));
        var pausedOn = TrayDesktop.Get(() => shown.PreviewPicture.Image);
        TrayDesktop.Run(shown.Window.Hide);
        await Task.Delay(300);
        TrayDesktop.Run(shown.Window.Show);
        await Task.Delay(700);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.PreviewPicture.Paused, "paused before the hide, still paused after the show");
            Assert.AreSame(pausedOn, shown.PreviewPicture.Image, "on the frame it was paused on");
        });
    }

    /// <summary>The colours the preview's picture showed at its corner, sampled over <paramref name="time"/>.</summary>
    private static async Task<HashSet<uint>> ColorsOver(ShownWindow shown, TimeSpan time)
    {
        var colors = new HashSet<uint>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < time)
        {
            if (TrayDesktop.Get(() => shown.PreviewPicture.Image is { } image ? ColorOf(image) : (uint?)null) is { } color)
            {
                colors.Add(color);
            }

            await Task.Delay(20);
        }

        return colors;
    }

    /// <summary>The top-left pixel of a decoded frame, as BGRA.</summary>
    private static uint ColorOf(Bitmap image)
    {
        using var locked = ((WriteableBitmap)image).Lock();
        return (uint)Marshal.ReadInt32(locked.Address);
    }

    /// <summary>Which of long.gif's sixty frames a colour is: each carries its index in its red channel, four apart.</summary>
    private static int FrameOf(uint color) => (int)((color >> 16) & 0xFF) / 4;
}
