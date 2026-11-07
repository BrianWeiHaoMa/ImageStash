using Avalonia.Threading;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// G26's refresh at the seam, on the signed-in user's real clipboard (How G26 holds, S14, N9, R5, G35): the identities
/// of the latest completed write remembered; the same write again only while nothing has changed the clipboard since,
/// the number remembered being the one after the write's own close, so a write again right after it writes; another
/// program's write since, or an item not among those written, writing nothing and failing nothing; a write that failed
/// before replacing anything leaving the earlier write as it was; a clipboard another program holds retried then named;
/// a file gone refused by name before anything is replaced.
/// </summary>
[TestClass]
public sealed class WindowsClipboardRefreshTests
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    [TestMethod]
    public async Task AWriteAgainForAnItemOfTheLatestWriteReplacesTheClipboardEvenRightAfterTheWrite()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        var (one, two) = (files.Path("one.png"), files.Path("two.png"));
        var (png, frame) = Still(40);
        var (otherPng, otherFrame) = Still(200);

        Assert.IsTrue((await seam.Write(new ClipboardContent([one], png, frame, [1]))).Succeeded);
        CollectionAssert.AreEqual(new long[] { 1 }, seam.Real.LastWritten.ToList(), "the identities written are remembered");

        var again = await seam.Real.WriteAgain(1, new ClipboardContent([two], otherPng, otherFrame, [1]), ShortRetry);

        Assert.IsTrue(again.Value, $"written: the number remembered is the one after the write's own close ({again.Failure?.Sentence})");
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { two }, read.Files.ToList());
        CollectionAssert.AreEqual(otherPng, read.Png);
        Assert.IsNotNull(read.DibV5, "every format of the write, as Write writes it");
        Assert.AreEqual(1u, read.DropEffect);
        Assert.IsTrue((await seam.Real.WriteAgain(1, new ClipboardContent([one], png, frame, [1]), ShortRetry)).Value, "and again after a write again");
        CollectionAssert.AreEqual(new[] { one }, TestClipboard.Read().Files.ToList());
    }

    [TestMethod]
    public async Task AnotherProgramsWriteSinceMakesAWriteAgainWriteNothingAndFailNothing()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        var one = files.Path("one.png");
        Assert.IsTrue((await seam.Write(new ClipboardContent([one], null, null, [1]))).Succeeded);
        TestClipboard.SetClipboard("elsewhere");

        var again = await seam.Real.WriteAgain(1, new ClipboardContent([files.Path("two.png")], null, null, [1]), ShortRetry);

        Assert.IsTrue(again.Succeeded, "a changed clipboard is no failure");
        Assert.IsFalse(again.Value, "G26: nothing replaced once something else changed the clipboard");
        Assert.AreEqual("elsewhere", TestClipboard.Text(), "the other program's text stands");
        Assert.IsEmpty(TestClipboard.Read().Files);
    }

    [TestMethod]
    public async Task AWriteAgainForAnItemTheLatestWriteDidNotCarryWritesNothing()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        var dog = files.Path("dog.png");
        Assert.IsTrue((await seam.Write(new ClipboardContent([dog], null, null, [2]))).Succeeded);

        var again = await seam.Real.WriteAgain(1, new ClipboardContent([files.Path("cat.png")], null, null, [1]), ShortRetry);

        Assert.IsFalse(again.Value);
        CollectionAssert.AreEqual(new[] { dog }, TestClipboard.Read().Files.ToList());
    }

    [TestMethod]
    public async Task AWriteThatFailedBeforeReplacingAnythingLeavesTheEarlierWriteRemembered()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        using var fresh = await OwnedSeam.Open();
        var one = files.Path("one.png");
        var gone = Path.Combine(Path.GetDirectoryName(one)!, "gone.png");

        Assert.IsFalse((await fresh.Write(new ClipboardContent([gone], null, null, [7]))).Succeeded);
        Assert.IsEmpty(fresh.Real.LastWritten, "a seam whose one write failed remembers nothing");
        Assert.IsFalse((await fresh.Real.WriteAgain(7, new ClipboardContent([one], null, null, [7]), ShortRetry)).Value);

        Assert.IsTrue((await seam.Write(new ClipboardContent([one], null, null, [1]))).Succeeded);
        Assert.AreEqual("Copy failed: the clipboard, gone.png no longer exists", (await seam.Write(new ClipboardContent([gone], null, null, [2]))).Failure?.Sentence);
        CollectionAssert.AreEqual(new long[] { 1 }, seam.Real.LastWritten.ToList(), "the clipboard still holds the earlier write, unchanged since");
        Assert.IsTrue((await seam.Real.WriteAgain(1, new ClipboardContent([files.Path("two.png")], null, null, [1]), ShortRetry)).Value);
    }

    [TestMethod]
    public async Task AClipboardAnotherProgramHoldsIsRetriedThenNamedAndAFileGoneIsRefusedBeforeAnythingIsReplaced()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        var one = files.Path("one.png");
        Assert.IsTrue((await seam.Write(new ClipboardContent([one], null, null, [1]))).Succeeded);

        Outcome<bool> held;
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            held = await seam.Real.WriteAgain(1, new ClipboardContent([files.Path("two.png")], null, null, [1]), ShortRetry);
            holder.Release();
        }

        Assert.AreEqual($"Copy failed: the clipboard, {HeldClipboard.Reason}", held.Failure?.Sentence, "E3, G35");
        CollectionAssert.AreEqual(new[] { one }, TestClipboard.Read().Files.ToList());

        var gone = Path.Combine(Path.GetDirectoryName(one)!, "gone.png");
        var refused = await seam.Real.WriteAgain(1, new ClipboardContent([gone], null, null, [1]), ShortRetry);

        Assert.AreEqual("Copy failed: the clipboard, gone.png no longer exists", refused.Failure?.Sentence, "refused as Write refuses it");
        CollectionAssert.AreEqual(new[] { one }, TestClipboard.Read().Files.ToList(), "nothing emptied: the check runs before the clipboard is replaced");
        Assert.IsTrue((await seam.Real.WriteAgain(1, new ClipboardContent([files.Path("three.png")], null, null, [1]), ShortRetry)).Value, "and the write it held stays remembered");
    }

    /// <summary>A 3×2 opaque still of one grey, its PNG and its frame in the boundary pixel contract.</summary>
    private static (byte[] Png, MediaFrame Frame) Still(byte grey)
    {
        var pixels = Enumerable.Range(0, 6).SelectMany(_ => new[] { grey, grey, grey, (byte)255 }).ToArray();
        using var image = SKImage.FromPixelCopy(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul), pixels);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return (png.ToArray(), new MediaFrame(3, 2, 0, pixels));
    }

    /// <summary>One seam and the window that owns its writes, as the stash window owns the product's, kept for the check.</summary>
    private sealed class OwnedSeam : IDisposable
    {
        private readonly Avalonia.Controls.Window owner;

        private OwnedSeam(Avalonia.Controls.Window owner) => this.owner = owner;

        public WindowsClipboardAndDrops Real { get; } = new();

        public static async Task<OwnedSeam> Open() => await Dispatcher.UIThread.InvokeAsync(() => new OwnedSeam(new Avalonia.Controls.Window()));

        /// <summary>The seam's write as the window makes it, on the UI thread.</summary>
        public Task<Outcome> Write(ClipboardContent content) => Dispatcher.UIThread.InvokeAsync(() => Real.Write(owner, content, ShortRetry));

        public void Dispose() => Dispatcher.UIThread.Invoke(owner.Close);
    }
}
