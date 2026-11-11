using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using SkiaSharp;

namespace ImageStash.Tests.Support;

/// <summary>
/// For checks of the editor: a session opened on an item of a test stash as the window opens one, the tools by kind,
/// pointer gestures on the original's grid, and a wait for what the session does off the check's thread.
/// </summary>
internal static class EditorSessions
{
    /// <summary>
    /// Opens a session on the listed item, rendering its Saves with <paramref name="renderer"/> (the product's by default),
    /// an animation played by <paramref name="time"/> (the product's clock by default), and requires it to open.
    /// </summary>
    public static async Task<EditorSession> Open(
        TestStash stash, StashItem item, FinishedPictureRenderer? renderer = null, LayerList? startOn = null, SizeEstimator? estimator = null, ColorChoices? colors = null,
        TimeProvider? time = null)
    {
        var edit = await EditedItems.ForEdit(stash.Store!, item.Id);
        var opened = await EditorSession.Open(stash.Store!, edit, renderer ?? FinishedPicture.Render, colors ?? new ColorChoices(), TestStash.ShortRetry, startOn, estimator, time);
        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        return opened.Value!;
    }

    public static TextTool Text(this EditorSession session) => session.Tools.OfType<TextTool>().Single();

    public static CropTool Crop(this EditorSession session) => session.Tools.OfType<CropTool>().Single();

    public static TrimTool Trim(this EditorSession session) => session.Tools.OfType<TrimTool>().Single();

    /// <summary>The trim's <paramref name="end"/> handle pressed, dragged to frame <paramref name="frame"/> and let go.</summary>
    public static void DragHandle(this TrimTool trim, RangeEnd end, int frame)
    {
        trim.PressHandle(end);
        trim.DragHandle(frame);
        trim.ReleaseHandle();
    }

    /// <summary>The selected layer's bar's <paramref name="end"/> pressed, dragged to frame <paramref name="frame"/> and let go.</summary>
    public static void DragBarEnd(this EditorSession session, RangeEnd end, int frame)
    {
        session.PressRangeEnd(end);
        session.DragRangeEnd(frame);
        session.ReleaseRangeEnd();
    }

    public static HideTool Hide(this EditorSession session) => session.Tools.OfType<HideTool>().Single();

    public static DrawTool Draw(this EditorSession session) => session.Tools.OfType<DrawTool>().Single();

    /// <summary>Makes Draw current with <paramref name="mark"/> chosen.</summary>
    public static DrawTool Drawing(this EditorSession session, MarkKind mark)
    {
        session.Tool = session.Draw();
        session.Draw().Choose(mark);
        return session.Draw();
    }

    /// <summary>A press at the first point, moved through the others, released at the last.</summary>
    public static void DragThrough(this EditorSession session, params (float X, float Y)[] points)
    {
        session.Press(new PointF(points[0].X, points[0].Y));
        foreach (var (x, y) in points.Skip(1))
        {
            session.Drag(new PointF(x, y));
        }

        session.Release(new PointF(points[^1].X, points[^1].Y));
    }

    /// <summary>A press and a release at one point.</summary>
    public static void Click(this EditorSession session, float x, float y)
    {
        session.Press(new PointF(x, y));
        session.Release(new PointF(x, y));
    }

    /// <summary>A layer list built in the check, of the layers given in order.</summary>
    public static LayerList List(params Layer[] layers) => new(layers);

    public static DrawMarkLayer Box(int x, int y, int width, int height, Rgba? color = null, Thickness thickness = Thickness.Thin) =>
        new(MarkKind.Box, [], new PixelRect(x, y, width, height), color ?? ColorChoices.Red, thickness, null);

    /// <summary>Waits until <paramref name="condition"/> holds, for at most ten seconds.</summary>
    public static async Task Until(Func<bool> condition, string never)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, never);
            await Task.Delay(5);
        }
    }
}

/// <summary>A test stash holding the 8 × 6 still "cat", with a session open on it as the window opens one.</summary>
internal sealed class CatInTheEditor : IDisposable
{
    private CatInTheEditor(TestStash stash, StashItem cat, EditorSession session) => (Stash, Cat, Session) = (stash, cat, session);

    public TestStash Stash { get; }

    public StashItem Cat { get; }

    public EditorSession Session { get; }

    public static async Task<CatInTheEditor> Open(LayerList? startOn = null)
    {
        var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        return new CatInTheEditor(stash, cat, await EditorSessions.Open(stash, cat, startOn: startOn));
    }

    public void Dispose()
    {
        Session.Dispose();
        Stash.Dispose();
    }
}

/// <summary>
/// A test stash holding an opaque 64 × 48 still "still", grey unless the check gives its PNG, large enough for letters at
/// a readable size, with a session open on it.
/// </summary>
internal sealed class StillInTheEditor : IDisposable
{
    public static readonly PixelSize Size = new(64, 48);

    private StillInTheEditor(TestStash stash, StashItem still, EditorSession session) => (Stash, Still, Session) = (stash, still, session);

    public TestStash Stash { get; }

    public StashItem Still { get; }

    public EditorSession Session { get; }

    public static async Task<StillInTheEditor> Open(LayerList? startOn = null, byte[]? png = null, SizeEstimator? estimator = null)
    {
        var stash = new TestStash();
        await stash.OpenCleanly();
        var bytes = png ?? MediaFixtures.DistinctPng(0x808080, Size.Width, Size.Height);
        var staged = await stash.Stage("still.png", bytes);
        var committed = await stash.Store!.CommitImport(TestStash.Candidate(staged, "still", bytes, TestStash.StillPng with { Width = Size.Width, Height = Size.Height }));
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        var still = committed.Value!.Item;
        return new StillInTheEditor(stash, still, await EditorSessions.Open(stash, still, startOn: startOn, estimator: estimator));
    }

    /// <summary>An opaque 64 × 48 PNG whose every pixel differs from every other, for checks of which pixels a render keeps.</summary>
    public static byte[] Gradient()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Size.Width, Size.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        for (var y = 0; y < Size.Height; y++)
        {
            for (var x = 0; x < Size.Width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 5), 90));
            }
        }

        return bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    public void Dispose()
    {
        Session.Dispose();
        Stash.Dispose();
    }
}

/// <summary>
/// A test stash holding an animation, with a session open on it as the window opens one, its play timed by a clock the
/// check steps, so frames change only when the check moves the time.
/// </summary>
internal sealed class AnimationInTheEditor : IDisposable
{
    private AnimationInTheEditor(TestStash stash, StashItem clip, EditorSession session, SteppedClock clock) => (Stash, Clip, Session, Clock) = (stash, clip, session, clock);

    public TestStash Stash { get; }

    public StashItem Clip { get; }

    public EditorSession Session { get; }

    public SteppedClock Clock { get; }

    /// <summary>A session on the fixture <paramref name="fixture"/>, imported as "clip".</summary>
    public static Task<AnimationInTheEditor> Open(string fixture) => Open(stash => stash.ImportFixture("clip", fixture));

    /// <summary>A session on a GIF the check made, imported as "clip".</summary>
    public static Task<AnimationInTheEditor> Open(byte[] gif) => Open(stash => stash.ImportPicture("clip", "clip.gif", gif));

    /// <summary>A session on the item <paramref name="add"/> puts in a fresh stash.</summary>
    public static async Task<AnimationInTheEditor> Open(Func<TestStash, Task<StashItem>> add, LayerList? startOn = null)
    {
        var stash = new TestStash();
        await stash.OpenCleanly();
        var clip = await add(stash);
        var clock = new SteppedClock();
        return new AnimationInTheEditor(stash, clip, await EditorSessions.Open(stash, clip, startOn: startOn, time: clock), clock);
    }

    /// <summary>
    /// Waits until the frame after the one showing has been read and waits for its time on the clock, so only the clock
    /// can show it. The player arms that wait on another thread once the frame is read, from the time it read just
    /// before, so a step of the clock in between would put the frame's time that step later.
    /// </summary>
    public Task NextFrameRead() => EditorSessions.Until(() => Session.FullSizeFramesHeld == 2 && Clock.NextDue is not null, "the next frame is never read and waited for");

    /// <summary>Moves the time on by <paramref name="ms"/> once the player waits for a frame's time, and waits until frame <paramref name="index"/> shows.</summary>
    public async Task Advance(int ms, int index)
    {
        await EditorSessions.Until(() => Clock.NextDue is not null, $"the player never waits for frame {index}'s time");
        Clock.Advance(TimeSpan.FromMilliseconds(ms));
        await EditorSessions.Until(() => Session.FrameIndex == index, $"frame {index} never shows; frame {Session.FrameIndex} does");
    }

    public void Dispose()
    {
        Session.Dispose();
        Stash.Dispose();
    }
}
