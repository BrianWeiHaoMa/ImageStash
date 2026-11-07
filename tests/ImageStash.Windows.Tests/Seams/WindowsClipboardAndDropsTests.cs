using System.Diagnostics;
using System.Reflection;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// What a drop carries, read from Avalonia's own data transfer as Windows hands it over: a file by its local path, a
/// folder by its path, and a link as a link (G11, N2). The file and folder items are Avalonia's own, from a window's
/// storage provider, since Avalonia's storage interfaces cannot be implemented outside it; a browser's picture, which
/// arrives as a shell virtual file without a path, is the person's to observe with the running app. The clipboard's
/// write, read back from the signed-in user's real clipboard as a paste would find it: the files, the PNG and a
/// bitmap with alpha for one still, a copy rather than a move, a clipboard another program holds retried and then
/// named with nothing of the write left (G8, G29, E3, G35); and nothing in either assembly reads the clipboard but the
/// seam's two reads, the one only the paste calls and the image-only one only Stash clipboard image calls (N9, R5), nor
/// reaches its owner or its sequence number, which are no data, but the write and its write again (How G26 holds).
/// </summary>
[TestClass]
public sealed class WindowsClipboardAndDropsTests
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    /// <summary>Avalonia's clipboard, whose every reading member is a read (Avalonia 12.1.4).</summary>
    private static readonly string[] ReadingTypes = ["Avalonia.Input.Platform.IClipboard", "Avalonia.Input.Platform.ClipboardExtensions"];

    /// <summary>The Win32 calls that read the clipboard's data or formats, or listen for its changes.</summary>
    private static readonly string[] ReadingEntryPoints =
    [
        "GetClipboardData", "OleGetClipboard", "GetPriorityClipboardFormat", "EnumClipboardFormats", "IsClipboardFormatAvailable",
        "CountClipboardFormats", "GetUpdatedClipboardFormats", "AddClipboardFormatListener", "SetClipboardViewer",
    ];

    /// <summary>The clipboard's owner, a window, and its sequence number, a counter: no data, read only for G26's refresh (How G26 holds, N9).</summary>
    private static readonly string[] OwnerAndNumber = ["GetClipboardOwner", "GetClipboardSequenceNumber"];

    /// <summary>The write, which remembers the owner and the number after its close, and the write again, which compares them.</summary>
    private static readonly string[] TheWrites = ["ImageStash.Windows.Seams.WindowsClipboardAndDrops.Write", "ImageStash.Windows.Seams.WindowsClipboardAndDrops.WriteAgain"];

    /// <summary>The paste's type, the one caller of the seam's read (N9, R5); its compiler-made state machines and lambdas are nested in it.</summary>
    private const string ThePaste = "ImageStash.Window.ClipboardPaste";

    /// <summary>Stash clipboard image's type, the one caller of the seam's image-only read (N9, R5).</summary>
    private const string TheStash = "ImageStash.Window.StashClipboardImage";

    /// <summary>The seam's two reads, the only places a clipboard read may be reached from.</summary>
    private static readonly string[] TheReads = ["ImageStash.Windows.Seams.WindowsClipboardAndDrops.Read", "ImageStash.Windows.Seams.WindowsClipboardAndDrops.ReadImage"];

    private static readonly Assembly[] Product = [typeof(App).Assembly, typeof(WindowsClipboardAndDrops).Assembly];

    [TestMethod]
    public async Task OneStillGoesAsItsFileItsPngAndABitmapWithAlphaAndSeveralFilesAsFilesOnly()
    {
        using var files = new CopiedFiles();
        var (png, frame) = TransparentCorner();
        var one = files.Path("cat.png");
        var two = files.Path("dog.gif");

        Assert.IsTrue((await Write(new ClipboardContent([one], png, frame, []), ShortRetry)).Succeeded);

        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { one }, read.Files.ToList());
        CollectionAssert.AreEqual(png, read.Png, "the PNG goes as given");
        var dib = read.DibV5!;
        Assert.AreEqual((124, 4, 3), (BitConverter.ToInt32(dib, 0), BitConverter.ToInt32(dib, 4), BitConverter.ToInt32(dib, 8)), "a BITMAPV5HEADER, bottom-up rows");
        Assert.AreEqual(((ushort)32, 3u), (BitConverter.ToUInt16(dib, 14), BitConverter.ToUInt32(dib, 16)), "32 bits per pixel, BI_BITFIELDS");
        Assert.AreEqual(0xFF000000u, BitConverter.ToUInt32(dib, 52), "an alpha mask");
        Assert.AreEqual(0x73524742u, BitConverter.ToUInt32(dib, 56), "sRGB");
        var rows = dib[124..];
        CollectionAssert.AreEqual(frame.Pixels[0..16], rows[32..48], "the top row is stored last, its pixels as they are");
        Assert.AreEqual(0, rows[32 + 3], "the transparent top-left pixel keeps alpha 0");
        Assert.AreEqual(255, rows[32 + 7]);
        Assert.AreEqual(1u, read.DropEffect, "a paste into a folder copies the file (G29)");

        Assert.IsTrue((await Write(new ClipboardContent([one, two], null, null, []), ShortRetry)).Succeeded);

        read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { one, two }, read.Files.ToList());
        Assert.IsNull(read.Png, "the earlier write's PNG is gone");
        Assert.IsNull(read.DibV5);
        Assert.AreEqual(1u, read.DropEffect);
    }

    [TestMethod]
    public async Task AClipboardAnotherProgramHoldsIsRetriedThenNamedAndKeepsWhatItHad()
    {
        using var files = new CopiedFiles();
        var before = files.Path("before.png");
        var attempted = files.Path("attempted.png");
        var (png, frame) = TransparentCorner();
        Assert.IsTrue((await Write(new ClipboardContent([before], null, null, []), ShortRetry)).Succeeded);

        Outcome refused;
        var clock = new Stopwatch();
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            clock.Start();
            refused = await Write(new ClipboardContent([attempted], png, frame, []), ShortRetry);
            clock.Stop();
            holder.Release();
        }

        Assert.AreEqual("Copy failed: the clipboard, another program is holding the clipboard open", refused.Failure?.Sentence);
        Assert.IsGreaterThanOrEqualTo(280, clock.ElapsedMilliseconds, "retried before it was named");
        Assert.IsLessThan(1500, clock.ElapsedMilliseconds, "named once the retry ended");
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { before }, read.Files.ToList(), "nothing of the refused write is on the clipboard");
        Assert.IsNull(read.Png);

        Assert.IsTrue((await Write(new ClipboardContent([attempted], png, frame, []), ShortRetry)).Succeeded, "once it lets go, the write works");
        read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { attempted }, read.Files.ToList());
        CollectionAssert.AreEqual(png, read.Png);
        Assert.IsNotNull(read.DibV5);
    }

    [TestMethod]
    public async Task AClipboardLetGoWithinTheRetryIsWritten()
    {
        using var files = new CopiedFiles();
        var path = files.Path("waited.png");
        Outcome written;
        var clock = new Stopwatch();
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            clock.Start();
            var writing = Write(new ClipboardContent([path], null, null, []), TimeSpan.FromSeconds(5));
            await Task.Delay(600);
            Assert.IsFalse(writing.IsCompleted, "the write waits while the clipboard is held");
            holder.Release();
            written = await writing;
        }

        Assert.IsTrue(written.Succeeded, written.Failure?.Sentence);
        Assert.IsGreaterThanOrEqualTo(600, clock.ElapsedMilliseconds);
        CollectionAssert.AreEqual(new[] { path }, TestClipboard.Read().Files.ToList());
    }

    [TestMethod]
    public async Task AFileGoneWhileTheClipboardWasHeldIsRefusedByNameAndNothingIsWritten()
    {
        using var files = new CopiedFiles();
        var before = files.Path("before.png");
        var undone = files.Path("undone.gif");
        Assert.IsTrue((await Write(new ClipboardContent([before], null, null, []), ShortRetry)).Succeeded);

        Outcome refused;
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            var writing = Write(new ClipboardContent([undone], null, null, []), TimeSpan.FromSeconds(5));
            await Task.Delay(300);
            File.Delete(undone);
            holder.Release();
            refused = await writing;
        }

        Assert.AreEqual("Copy failed: the clipboard, undone.gif no longer exists", refused.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { before }, TestClipboard.Read().Files.ToList(), "nothing of the refused write is on the clipboard");
    }

    /// <summary>
    /// G8: the item's batch undone and another picture of the same name added while the clipboard was held puts another
    /// file at the same path, whose creation time the file system may even carry over; the write is refused by name,
    /// also when the path is longer than MAX_PATH, as a long name in a deep stash folder makes it.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AFileReplacedWhileTheClipboardWasHeldIsRefusedByNameAndNothingIsWritten(bool pastMaxPath)
    {
        using var files = new CopiedFiles();
        var before = files.Path("before.png");
        var name = pastMaxPath ? $"reaction {new string('x', 200)}.png" : "reaction.png";
        var chosen = files.Path(name);
        Assert.AreEqual(pastMaxPath, chosen.Length > 260, $"the path is {chosen.Length} characters long");
        var newcomer = files.Path("newcomer.png");
        Assert.IsTrue((await Write(new ClipboardContent([before], null, null, []), ShortRetry)).Succeeded);

        Outcome refused;
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            var writing = Write(new ClipboardContent([chosen], null, null, []), TimeSpan.FromSeconds(5));
            await Task.Delay(300);
            File.Delete(chosen);
            File.Move(newcomer, chosen);
            holder.Release();
            refused = await writing;
        }

        Assert.AreEqual($"Copy failed: the clipboard, {name} was replaced by another file", refused.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { before }, TestClipboard.Read().Files.ToList(), "nothing of the refused write is on the clipboard");
    }

    [TestMethod]
    public void OnlyThePasteReadsTheClipboard()
    {
        var avalonia = Product.SelectMany(assembly => CompiledCalls.ClipboardReads(assembly, ReadingTypes, [])).ToList();
        var reads = Product.SelectMany(assembly => CompiledCalls.ClipboardReads(assembly, [], ReadingEntryPoints)).Order(StringComparer.Ordinal).ToList();
        var callers = Product.SelectMany(assembly => CompiledCalls.CallersOf(assembly, ["ImageStash.Seams.IClipboardAndDrops.Read", "ImageStash.Windows.Seams.WindowsClipboardAndDrops.Read"])).ToList();
        var imageCallers = Product.SelectMany(assembly => CompiledCalls.CallersOf(assembly, ["ImageStash.Seams.IClipboardAndDrops.ReadImage", "ImageStash.Windows.Seams.WindowsClipboardAndDrops.ReadImage"])).ToList();
        var outsideTheRead = Product.SelectMany(assembly => CompiledCalls.ReadsOutsideTheRead(assembly, ReadingEntryPoints, TheReads)).ToList();

        Assert.IsEmpty(avalonia, $"Avalonia's clipboard: {string.Join(", ", avalonia)}");
        Assert.IsEmpty(outsideTheRead, $"Reaching a clipboard read other than through the seam's two reads: {string.Join(", ", outsideTheRead)}");
        CollectionAssert.AreEqual(
            new[]
            {
                "ImageStash.Windows.Seams.WindowsClipboardAndDrops.EnumClipboardFormats → user32.dll!EnumClipboardFormats",
                "ImageStash.Windows.Seams.WindowsClipboardAndDrops.GetClipboardData → user32.dll!GetClipboardData",
            },
            reads,
            $"Clipboard reads, each declared once, in the seam, and no listener: {string.Join(", ", reads)}");
        Assert.IsEmpty(callers.Where(caller => !caller.StartsWith(ThePaste + ".", StringComparison.Ordinal) && !caller.StartsWith(ThePaste + "+", StringComparison.Ordinal)).ToList(), $"Callers of the read: {string.Join(", ", callers)}");
        Assert.IsNotEmpty(callers, "the paste does read the clipboard through the seam, so the rule above has its one caller to find");
        // Inside the act's one entry, Stash (its state machine), so nothing of the type reads at construction or elsewhere.
        Assert.IsEmpty(imageCallers.Where(caller => !caller.StartsWith(TheStash + "+<Stash>", StringComparison.Ordinal)).ToList(), $"Callers of the image-only read: {string.Join(", ", imageCallers)}");
        Assert.IsNotEmpty(imageCallers, "Stash clipboard image does read the clipboard through the seam, so the rule above has its one caller to find");
        // And the paste runs only from the user's two acts: Ctrl+V's key and Paste image's menu entry.
        var pastes = Product.SelectMany(assembly => CompiledCalls.CallersOf(assembly, [ThePaste + ".Paste"])).Order(StringComparer.Ordinal).ToList();
        Assert.HasCount(2, pastes, $"Callers of the paste: {string.Join(", ", pastes)}");
        // A lambda of the + menu's builder, compiled into a closure type of AddMenu whose number the compiler chooses.
        Assert.StartsWith("ImageStash.Window.AddMenu+", pastes[0], "the + menu's Paste image");
        Assert.Contains(".<Button>b__", pastes[0], "the + menu's Paste image");
        Assert.AreEqual("ImageStash.Window.StashWindow.RunKeyAction", pastes[1], "Ctrl+V");
    }

    [TestMethod]
    public void OnlyTheWriteAndItsWriteAgainReachTheClipboardsOwnerAndSequenceNumber()
    {
        var declared = Product.SelectMany(assembly => CompiledCalls.ClipboardReads(assembly, [], OwnerAndNumber)).Order(StringComparer.Ordinal).ToList();
        var outside = Product.SelectMany(assembly => CompiledCalls.ReadsOutsideTheRead(assembly, OwnerAndNumber, TheWrites)).ToList();

        CollectionAssert.AreEqual(
            new[]
            {
                "ImageStash.Windows.Seams.WindowsClipboardAndDrops.GetClipboardOwner → user32.dll!GetClipboardOwner",
                "ImageStash.Windows.Seams.WindowsClipboardAndDrops.GetClipboardSequenceNumber → user32.dll!GetClipboardSequenceNumber",
            },
            declared,
            $"Each declared once, in the seam: {string.Join(", ", declared)}");
        Assert.IsEmpty(outside, $"Reaching the clipboard's owner or number other than through the write and its write again: {string.Join(", ", outside)}");
    }

    [TestMethod]
    public void TheNoReadCheckFindsWhatItLooksFor()
    {
        // The seam does call SetClipboardData and does reach TopLevel: listed as forbidden, each is found by name.
        var found = Product.SelectMany(assembly => CompiledCalls.ClipboardReads(assembly, ["Avalonia.Controls.TopLevel"], ["SetClipboardData"])).ToList();
        // The handoff does call the seam's write, from inside its own state machine.
        var callers = Product.SelectMany(assembly => CompiledCalls.CallersOf(assembly, ["ImageStash.Seams.IClipboardAndDrops.Write"])).ToList();
        // The write again does compare the owner and the number: with the write alone allowed, it is found outside.
        var outsideTheWrite = Product.SelectMany(assembly => CompiledCalls.ReadsOutsideTheRead(assembly, OwnerAndNumber, [TheWrites[0]])).ToList();

        Assert.IsTrue(found.Any(name => name.EndsWith("user32.dll!SetClipboardData", StringComparison.Ordinal)), string.Join(", ", found));
        Assert.IsTrue(found.Any(name => name == "Avalonia.Controls.TopLevel.TryGetPlatformHandle"), string.Join(", ", found));
        Assert.IsTrue(callers.Any(caller => caller.StartsWith("ImageStash.Window.Handoff+", StringComparison.Ordinal)), string.Join(", ", callers));
        Assert.IsTrue(outsideTheWrite.Any(method => method.Contains("<WriteAgain>", StringComparison.Ordinal)), string.Join(", ", outsideTheWrite));
    }

    [TestMethod]
    public async Task FilesAndFoldersArriveByTheirLocalPaths()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ImageStash.Windows.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "dropped"));
        var cat = Path.Combine(folder, "cat.png");
        File.WriteAllBytes(cat, [1, 2, 3]);
        try
        {
            var contents = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var window = new Avalonia.Controls.Window();
                try
                {
                    var data = new DataTransfer();
                    data.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFileFromPathAsync(cat))!));
                    data.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFolderFromPathAsync(Path.Combine(folder, "dropped")))!));
                    return new WindowsClipboardAndDrops().ReadDrop(data);
                }
                finally
                {
                    window.Close();
                }
            });

            using (contents)
            {
                Assert.HasCount(2, contents.Items);
                Assert.AreEqual(("cat.png", cat, false), (contents.Items[0].Name, contents.Items[0].LocalPath, contents.Items[0].IsFolder));
                Assert.IsNull(contents.Items[0].OpenRead, "a file with a path is copied from its path");
                Assert.AreEqual("dropped", contents.Items[1].Name);
                Assert.AreEqual(Path.Combine(folder, "dropped"), contents.Items[1].LocalPath!.TrimEnd(Path.DirectorySeparatorChar));
                Assert.IsTrue(contents.Items[1].IsFolder);
                Assert.IsFalse(contents.OnlyLink);
                Assert.IsFalse(contents.FilesUnavailable);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("https://example.com/cat.gif", true)]
    [DataRow(" http://example.com/ ", true)]
    [DataRow("ftp://example.com/cat.gif", false)]
    [DataRow("a cat, in words", false)]
    public void APayloadWithoutFilesIsALinkOnlyWhenItsTextIsAWebAddress(string text, bool link)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));

        var contents = new WindowsClipboardAndDrops().ReadDrop(data);

        Assert.IsEmpty(contents.Items);
        Assert.AreEqual(link, contents.OnlyLink);
        Assert.IsFalse(contents.FilesUnavailable);
    }

    [TestMethod]
    public async Task DraggedFilesLandAsThemselvesWithACopyTheOnlyEffectOffered()
    {
        using var files = new CopiedFiles();
        var (cat, dog) = (files.Path("cat.png"), files.Path("dog.gif"));
        using var target = await DropWindow.UnderTheCursor(accept: true);

        var dragged = await target.Drag([cat, dog]);

        Assert.IsTrue(dragged.Succeeded, dragged.Failure?.Sentence);
        Assert.IsTrue(dragged.Value, "a drop taken as a copy has landed");
        Assert.AreEqual(DragDropEffects.Copy, target.Offered, "a copy and nothing else: a move would take the stash's file away (G29)");
        CollectionAssert.AreEqual(new[] { cat, dog }, target.Dropped, "the files themselves, in order (G9)");
        Assert.IsTrue(File.Exists(cat) && File.Exists(dog), "the dragged files stay where they were");
    }

    [TestMethod]
    public async Task ADragNothingTakesHasNotLanded()
    {
        using var files = new CopiedFiles();
        using var target = await DropWindow.UnderTheCursor(accept: false);

        var dragged = await target.Drag([files.Path("cat.png")]);

        Assert.IsTrue(dragged.Succeeded, dragged.Failure?.Sentence);
        Assert.IsFalse(dragged.Value);
        Assert.IsNull(target.Dropped);
    }

    [TestMethod]
    public async Task ADragOfAFileNoLongerThereIsNamedAndNeverStarts()
    {
        using var files = new CopiedFiles();
        var cat = files.Path("cat.png");
        var gone = files.Path("gone.png");
        File.Delete(gone);
        using var target = await DropWindow.UnderTheCursor(accept: true);

        var dragged = await target.Drag([cat, gone]);

        Assert.AreEqual(("Drag out", "gone.png"), (dragged.Failure?.Action, dragged.Failure?.Subject), dragged.Failure?.Sentence);
        Assert.IsFalse(target.Entered, "no drag ran");
    }

    /// <summary>The seam's write as the window makes it: on the UI thread, with a window of its own as the clipboard's owner.</summary>
    private static Task<Outcome> Write(ClipboardContent content, TimeSpan retry) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = new Avalonia.Controls.Window();
            try
            {
                return await new WindowsClipboardAndDrops().Write(owner, content, retry);
            }
            finally
            {
                owner.Close();
            }
        });

    /// <summary>A 4×3 PNG whose top-left pixel is fully transparent, and its frame in the boundary pixel contract.</summary>
    private static (byte[] Png, MediaFrame Frame) TransparentCorner()
    {
        var pixels = new byte[4 * 3 * 4];
        for (var i = 0; i < 12; i++)
        {
            (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]) = ((byte)(i * 20), (byte)(200 - i * 10), 90, (byte)(i == 0 ? 0 : 255));
        }

        var info = new SKImageInfo(4, 3, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var image = SKImage.FromPixelCopy(info, pixels);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return (png.ToArray(), new MediaFrame(4, 3, 0, pixels));
    }
}
