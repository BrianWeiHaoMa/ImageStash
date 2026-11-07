using System.Diagnostics;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// The paste's read of the signed-in user's real clipboard, as the formats are placed there by the check as a browser,
/// File Explorer, WPF or WinForms would place them: files first, then "PNG", then the bitmap the program placed itself
/// before the ones Windows makes from it, then a link, taking only the format it reads, which formats rendered on
/// request show (How G13 holds, N2, N9, R5); a clipboard another program holds retried then named, and a bitmap that
/// cannot be read named, never an exception (E3, G35). And Stash clipboard image's image-only read: "PNG", else the
/// bitmap, its files and its text never asked for, no image otherwise, its failures named with its act (G13).
/// </summary>
[TestClass]
public sealed class WindowsClipboardReadTests
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    /// <summary>ImageStash's own CF_DIBV5 of 2×1: an opaque red pixel and a half-transparent blue one, bottom-up.</summary>
    private static readonly byte[] OwnBitmap = Dib(124, 2, 1, 3, [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000], [0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x80]);

    [TestMethod]
    public async Task FilesComeFirstAndNothingElseIsTaken()
    {
        using var files = new Pictures();
        var cat = files.File("cat.png");
        var dog = files.File("dog.gif");
        Outcome<ClipboardReading> read;
        using (var clipboard = ClipboardOnRequest.Put([TestClipboard.FileList(cat, dog)], [(TestClipboard.Png, [1, 2, 3]), (TestClipboard.CfDibV5, OwnBitmap), TestClipboard.Text("https://example.com/cat.gif")]))
        {
            read = await Read(ShortRetry);
            CollectionAssert.AreEqual(Array.Empty<uint>(), clipboard.Requested.ToList(), "no picture or text was asked for");
        }

        var reading = Succeeded(read);
        Assert.AreEqual(
            "cat.png at " + cat + ", dog.gif at " + dog,
            string.Join(", ", reading.Files.Select(file => $"{file.Name} at {file.LocalPath}")));
        Assert.IsTrue(reading.Files.All(file => file.OpenRead is null && !file.IsFolder));
        Assert.AreEqual((null, null, false), (reading.Png, reading.Image, reading.OnlyLink));
    }

    [TestMethod]
    public async Task AFolderIsAFolderAndAFileHeldWithoutSharingIsListedAnyway()
    {
        using var files = new Pictures();
        var folder = files.Folder("reactions");
        var held = files.File("held.png");
        using var holding = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
        TestClipboard.Put(TestClipboard.FileList(folder, held));

        var reading = Succeeded(await Read(ShortRetry));

        Assert.AreEqual("reactions folder, held.png file", string.Join(", ", reading.Files.Select(file => $"{file.Name} {(file.IsFolder ? "folder" : "file")}")));
    }

    [TestMethod]
    public async Task APngComesBeforeABitmapAndAnAddressAndIsHandedOverAsItIs()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7];
        Outcome<ClipboardReading> read;
        using (var clipboard = ClipboardOnRequest.Put([(TestClipboard.Png, png)], [(TestClipboard.CfDibV5, OwnBitmap), TestClipboard.Text("https://example.com/cat.gif")]))
        {
            read = await Read(ShortRetry);
            CollectionAssert.AreEqual(Array.Empty<uint>(), clipboard.Requested.ToList(), "neither the bitmap nor the text was asked for");
        }

        var reading = Succeeded(read);
        CollectionAssert.AreEqual(png, reading.Png, "the bytes as they are, not decoded");
        Assert.AreEqual((0, null, false), (reading.Files.Count, reading.Image, reading.OnlyLink));
    }

    [TestMethod]
    public async Task ABitmapAloneIsAskedForAndComesOutInThePixelContract()
    {
        Outcome<ClipboardReading> read;
        using (var clipboard = ClipboardOnRequest.Put([], [(TestClipboard.CfDibV5, OwnBitmap), TestClipboard.Text("https://example.com/cat.gif")]))
        {
            read = await Read(ShortRetry);
            CollectionAssert.AreEqual(new[] { TestClipboard.CfDibV5 }, clipboard.Requested.ToList(), "the bitmap and nothing else");
        }

        var reading = Succeeded(read);
        var image = reading.Image!;
        Assert.AreEqual((2, 1, 0), (image.Width, image.Height, image.DurationMs));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x80 }, image.Pixels);
        Assert.AreEqual((0, null, false), (reading.Files.Count, reading.Png, reading.OnlyLink));
    }

    [TestMethod]
    public async Task TheBitmapAProgramPlacedIsReadBeforeTheOneWindowsMakesFromIt()
    {
        // Every alpha declared zero: the V5 is fully transparent, while the CF_DIB Windows makes from it, without the
        // declaration, reads as opaque.
        TestClipboard.Put((TestClipboard.CfDibV5, Dib(124, 2, 1, 3, [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000], [0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x00])));

        var image = Succeeded(await Read(ShortRetry)).Image!;

        CollectionAssert.AreEqual(new byte[] { 0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x00 }, image.Pixels);
    }

    [TestMethod]
    public async Task ACfDibPlacedAloneIsReadWithItsAlphaBytes()
    {
        TestClipboard.Put((TestClipboard.CfDib, Dib(40, 2, 1, 0, null, [0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x80])));

        var image = Succeeded(await Read(ShortRetry)).Image!;

        CollectionAssert.AreEqual(new byte[] { 0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x80 }, image.Pixels);
    }

    [TestMethod]
    public async Task AGdiBitmapAloneIsReadAsWindowsConvertsItKeepingItsAlphaBytes()
    {
        // WinForms' 2×2 blended onto light gray, straight: transparent, half, opaque green, opaque white (top-down).
        byte[] pixels = [0xD3, 0xD3, 0xD3, 0x00, 0xE9, 0x69, 0x69, 0x80, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        TestClipboard.PutBitmap(2, 2, pixels);

        var image = Succeeded(await Read(ShortRetry)).Image!;

        Assert.AreEqual((2, 2), (image.Width, image.Height));
        CollectionAssert.AreEqual(pixels, image.Pixels);
    }

    [TestMethod]
    public async Task ABitmapThatCannotBeReadIsNamed()
    {
        TestClipboard.Put((TestClipboard.CfDib, new byte[12]));

        var read = await Read(ShortRetry);

        Assert.AreEqual("Paste failed: the clipboard, its picture could not be read: it is too short to hold a bitmap header", read.Failure?.Sentence);
    }

    [TestMethod]
    [DataRow("https://example.com/cat.gif", true)]
    [DataRow("  http://example.com/  ", true)]
    [DataRow("cat.gif", false)]
    [DataRow("ftp://example.com/cat.gif", false)]
    [DataRow("look at https://example.com/cat.gif", false)]
    public async Task TextAloneIsALinkOnlyWhenItIsAWebAddress(string text, bool link)
    {
        TestClipboard.Put(TestClipboard.Text(text));

        var reading = Succeeded(await Read(ShortRetry));

        Assert.AreEqual((0, null, null, link), (reading.Files.Count, reading.Png, reading.Image, reading.OnlyLink));
        Assert.AreEqual(text, reading.Text, "the text as it stands, for the search box's paste (G13)");
    }

    [TestMethod]
    public async Task AnEmptiedClipboardHoldsNothing()
    {
        TestClipboard.Put();

        var reading = Succeeded(await Read(ShortRetry));

        Assert.AreEqual((0, null, null, false), (reading.Files.Count, reading.Png, reading.Image, reading.OnlyLink));
        Assert.IsNull(reading.Text);
    }

    [TestMethod]
    public async Task AClipboardAnotherProgramHoldsIsRetriedThenNamed()
    {
        TestClipboard.Put(TestClipboard.Text("https://example.com/cat.gif"));
        Outcome<ClipboardReading> read;
        var clock = new Stopwatch();
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            clock.Start();
            read = await Read(ShortRetry);
            clock.Stop();
            holder.Release();
        }

        Assert.AreEqual("Paste failed: the clipboard, another program is holding the clipboard open", read.Failure?.Sentence);
        Assert.IsGreaterThanOrEqualTo(280, clock.ElapsedMilliseconds, "retried before it was named");
        Assert.IsLessThan(1500, clock.ElapsedMilliseconds, "named once the retry ended");
        Assert.IsTrue(Succeeded(await Read(ShortRetry)).OnlyLink, "once it lets go, the read works");
    }

    [TestMethod]
    public async Task AClipboardLetGoWithinTheRetryIsRead()
    {
        TestClipboard.Put(TestClipboard.Text("https://example.com/cat.gif"));
        Outcome<ClipboardReading> read;
        var clock = new Stopwatch();
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            clock.Start();
            var reading = Read(TimeSpan.FromSeconds(5));
            await Task.Delay(600);
            Assert.IsFalse(reading.IsCompleted, "the read waits while the clipboard is held");
            holder.Release();
            read = await reading;
        }

        Assert.IsGreaterThanOrEqualTo(600, clock.ElapsedMilliseconds);
        Assert.IsTrue(Succeeded(read).OnlyLink);
    }

    [TestMethod]
    public async Task ReadsAtOnceEachFindWhatTheClipboardHolds()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7];
        TestClipboard.Put((TestClipboard.Png, png));

        // Two pastes pressed close together: Windows opens the clipboard for a second thread of a process while another
        // of its threads holds it, and the second one's close would close it under the first.
        var reads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Read(ShortRetry)));

        var found = reads.Select(read => read.Failure?.Sentence ?? (read.Value!.Png is { } bytes && bytes.SequenceEqual(png) ? "the PNG" : "something else")).ToList();
        CollectionAssert.AreEqual(Enumerable.Repeat("the PNG", 16).ToList(), found, string.Join(" | ", found));
    }

    [TestMethod]
    public async Task TheImageOnlyReadTakesThePngBesideFilesAndAsksForNothingElse()
    {
        using var files = new Pictures();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7];
        Outcome<ClipboardImage?> read;
        using (var clipboard = ClipboardOnRequest.Put([], [TestClipboard.FileList(files.File("cat.png")), (TestClipboard.Png, png), (TestClipboard.CfDibV5, OwnBitmap), TestClipboard.Text("https://example.com/cat.gif")]))
        {
            read = await ReadImage(ShortRetry);
            CollectionAssert.AreEqual(new[] { TestClipboard.Png }, clipboard.Requested.ToList(), "the PNG alone: the files do not count (G13) and are never asked for");
        }

        var image = SucceededImage(read)!;
        CollectionAssert.AreEqual(png, image.Png, "the bytes as they are, not decoded");
        Assert.IsNull(image.Frame);
    }

    [TestMethod]
    public async Task TheImageOnlyReadTakesTheBitmapBesideFilesWhenThereIsNoPng()
    {
        using var files = new Pictures();
        Outcome<ClipboardImage?> read;
        using (var clipboard = ClipboardOnRequest.Put([], [TestClipboard.FileList(files.File("cat.png")), (TestClipboard.CfDibV5, OwnBitmap), TestClipboard.Text("words")]))
        {
            read = await ReadImage(ShortRetry);
            CollectionAssert.AreEqual(new[] { TestClipboard.CfDibV5 }, clipboard.Requested.ToList(), "the bitmap and nothing else");
        }

        var image = SucceededImage(read)!;
        Assert.IsNull(image.Png);
        Assert.AreEqual((2, 1), (image.Frame!.Width, image.Frame.Height));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x80 }, image.Frame.Pixels, "in the pixel contract, alpha kept");
    }

    [TestMethod]
    public async Task TheImageOnlyReadOfAGdiBitmapIsAsWindowsConvertsIt()
    {
        byte[] pixels = [0xD3, 0xD3, 0xD3, 0x00, 0xE9, 0x69, 0x69, 0x80, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        TestClipboard.PutBitmap(2, 2, pixels);

        var image = SucceededImage(await ReadImage(ShortRetry))!;

        CollectionAssert.AreEqual(pixels, image.Frame!.Pixels);
    }

    [TestMethod]
    public async Task FilesOnlyWordsALinkOrNothingIsNoImageAndNeitherFilesNorTextAreAskedFor()
    {
        using var files = new Pictures();
        foreach (var held in new[] { TestClipboard.FileList(files.File("cat.png")), TestClipboard.Text("words"), TestClipboard.Text("https://example.com/cat.gif") })
        {
            using var clipboard = ClipboardOnRequest.Put([], [held]);
            Assert.IsNull(SucceededImage(await ReadImage(ShortRetry)), $"format {held.Format} is no image");
            CollectionAssert.AreEqual(Array.Empty<uint>(), clipboard.Requested.ToList(), $"format {held.Format} was not asked for");
        }

        TestClipboard.Put();
        Assert.IsNull(SucceededImage(await ReadImage(ShortRetry)), "an emptied clipboard");
    }

    [TestMethod]
    public async Task TheImageOnlyReadsFailuresNameStashClipboardImage()
    {
        TestClipboard.Put((TestClipboard.CfDib, new byte[12]));
        Assert.AreEqual(
            "Stash clipboard image failed: the clipboard, its picture could not be read: it is too short to hold a bitmap header",
            (await ReadImage(ShortRetry)).Failure?.Sentence);

        TestClipboard.Put((TestClipboard.Png, [1, 2, 3]));
        Outcome<ClipboardImage?> read;
        var clock = new Stopwatch();
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            clock.Start();
            read = await ReadImage(ShortRetry);
            clock.Stop();
            holder.Release();
        }

        Assert.AreEqual("Stash clipboard image failed: the clipboard, another program is holding the clipboard open", read.Failure?.Sentence);
        Assert.IsGreaterThanOrEqualTo(280, clock.ElapsedMilliseconds, "retried before it was named");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, SucceededImage(await ReadImage(ShortRetry))!.Png, "once it lets go, the read works");
    }

    /// <summary>The seam's read as the paste makes it, awaited, from a thread that is not the one doing the reading.</summary>
    private static Task<Outcome<ClipboardReading>> Read(TimeSpan retry) => new WindowsClipboardAndDrops().Read(retry);

    /// <summary>The seam's image-only read as Stash clipboard image makes it.</summary>
    private static Task<Outcome<ClipboardImage?>> ReadImage(TimeSpan retry) => new WindowsClipboardAndDrops().ReadImage(retry);

    private static ClipboardImage? SucceededImage(Outcome<ClipboardImage?> read)
    {
        Assert.IsTrue(read.Succeeded, read.Failure?.Sentence);
        return read.Value;
    }

    private static ClipboardReading Succeeded(Outcome<ClipboardReading> read)
    {
        Assert.IsTrue(read.Succeeded, read.Failure?.Sentence);
        return read.Value!;
    }

    /// <summary>
    /// A 32-bit bitmap, its header <paramref name="headerSize"/> bytes, BI_BITFIELDS with <paramref name="masks"/>
    /// (red, green, blue, alpha) when given, else BI_RGB, its one row of <paramref name="row"/>.
    /// </summary>
    private static byte[] Dib(int headerSize, int width, int height, uint compression, uint[]? masks, byte[] row)
    {
        var dib = new byte[headerSize + row.Length];
        BitConverter.GetBytes(headerSize).CopyTo(dib, 0);
        BitConverter.GetBytes(width).CopyTo(dib, 4);
        BitConverter.GetBytes(height).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
        BitConverter.GetBytes(masks is null ? 0u : compression).CopyTo(dib, 16);
        BitConverter.GetBytes((uint)row.Length).CopyTo(dib, 20);
        if (masks is not null)
        {
            for (var i = 0; i < 4; i++)
            {
                BitConverter.GetBytes(masks[i]).CopyTo(dib, 40 + i * 4);
            }

            BitConverter.GetBytes(0x73524742u).CopyTo(dib, 56);
        }

        row.CopyTo(dib, headerSize);
        return dib;
    }

    /// <summary>Small files and folders in a fresh folder under %TEMP%, for the clipboard to list; removed afterwards.</summary>
    private sealed class Pictures : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "ImageStash.Windows.Tests", Guid.NewGuid().ToString("N"));

        public Pictures() => Directory.CreateDirectory(folder);

        public string File(string name)
        {
            var path = Path.Combine(folder, name);
            System.IO.File.WriteAllBytes(path, [1, 2, 3]);
            return path;
        }

        public string Folder(string name) => Directory.CreateDirectory(Path.Combine(folder, name)).FullName;

        public void Dispose() => Directory.Delete(folder, recursive: true);
    }
}
