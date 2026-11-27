using ImageStash.Seams;
using ImageStash.Windows.Tests.Support;
using OwnedSeam = ImageStash.Windows.Tests.Seams.WindowsClipboardRefreshTests.OwnedSeam;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// A capture card's Undo at the seam, on the signed-in user's real clipboard (G16, How G26 holds, S14, N9, R5, G35): the
/// clipboard emptied while it still holds the seam's latest write, that item among its identities, and the write then
/// forgotten, so no refresh writes it back; another program's write since, or an item the write did not carry, left
/// as it is and failing nothing; a clipboard another program holds retried then named, nothing emptied.
/// </summary>
[TestClass]
public sealed class WindowsClipboardEmptyingTests
{
    private static readonly TimeSpan ShortRetry = WindowsClipboardRefreshTests.ShortRetry;

    [TestMethod]
    public async Task TheLatestWriteHoldingTheItemAmongOthersIsEmptiedAndForgotten()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        var (one, two) = (files.Path("one.png"), files.Path("two.png"));
        var (png, frame) = WindowsClipboardRefreshTests.Still(40);
        Assert.IsTrue((await seam.Write(new ClipboardContent([two], png, frame, [2]))).Succeeded);
        Assert.IsTrue((await seam.Real.Empty(2, ShortRetry)).Value, "a single still's write, its bitmap among it");
        Assert.IsNull(TestClipboard.Read().DibV5, "no bitmap left to paste");
        Assert.IsTrue((await seam.Write(new ClipboardContent([one, two], null, null, [1, 2]))).Succeeded);

        var emptied = await seam.Real.Empty(2, ShortRetry);

        Assert.IsTrue(emptied.Value, $"emptied: the number remembered is the one after the write's own close ({emptied.Failure?.Sentence})");
        var read = TestClipboard.Read();
        Assert.IsEmpty(read.Files, "a paste gets no file that is gone");
        Assert.IsNull(read.Png);
        Assert.IsNull(TestClipboard.Text());
        Assert.IsEmpty(seam.Real.LastWritten, "the write is forgotten");
        Assert.IsFalse((await seam.Real.WriteAgain(1, new ClipboardContent([one], null, null, [1]), ShortRetry)).Value, "so a later Save's refresh writes nothing back (G26)");
        Assert.IsEmpty(TestClipboard.Read().Files);
    }

    [TestMethod]
    public async Task AnotherProgramsWriteSinceOrAnItemNotWrittenLeavesTheClipboardAsItIs()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        var one = files.Path("one.png");
        Assert.IsTrue((await seam.Write(new ClipboardContent([one], null, null, [1]))).Succeeded);

        var notWritten = await seam.Real.Empty(7, ShortRetry);

        Assert.IsTrue(notWritten.Succeeded);
        Assert.IsFalse(notWritten.Value, "an item the write did not carry");
        CollectionAssert.AreEqual(new[] { one }, TestClipboard.Read().Files.ToList());
        CollectionAssert.AreEqual(new long[] { 1 }, seam.Real.LastWritten.ToList(), "the write stays remembered");

        TestClipboard.SetClipboard("elsewhere");
        var changed = await seam.Real.Empty(1, ShortRetry);

        Assert.IsTrue(changed.Succeeded, "a changed clipboard is no failure");
        Assert.IsFalse(changed.Value);
        Assert.AreEqual("elsewhere", TestClipboard.Text(), "the other program's text stands");
    }

    [TestMethod]
    public async Task AClipboardAnotherProgramHoldsIsRetriedThenNamedAndNothingIsEmptied()
    {
        using var files = new CopiedFiles();
        using var seam = await OwnedSeam.Open();
        var one = files.Path("one.png");
        Assert.IsTrue((await seam.Write(new ClipboardContent([one], null, null, [1]))).Succeeded);

        Outcome<bool> held;
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            held = await seam.Real.Empty(1, ShortRetry);
            holder.Release();
        }

        Assert.AreEqual($"Couldn't clear the clipboard (the clipboard): {HeldClipboard.Reason}.", held.Failure?.Sentence, "E3, G35");
        CollectionAssert.AreEqual(new[] { one }, TestClipboard.Read().Files.ToList());
        Assert.IsTrue((await seam.Real.Empty(1, ShortRetry)).Value, "the write stays remembered, and empties once let go");
    }
}
