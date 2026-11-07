using ImageStash.Files;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// G29, S9: a file goes to the real Recycle Bin and its recycled identity comes back; a file that cannot go fails
/// alone; a picture Media holds open, as a playing animation does, goes too (G29); a recycled entry is removed for
/// good by its identity, and nothing else is, one half removed while its record was held finished when asked again
/// (G18, G29, G35). Each run leaves the four small files the recycle checks
/// recycled in the user's Recycle Bin, where emptying the bin removes them; the removal checks remove their own.
/// </summary>
[TestClass]
public sealed class WindowsRecycleBinTests
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    private readonly string folder = Path.Combine(Path.GetTempPath(), "ImageStash.Windows.Tests", Guid.NewGuid().ToString("N"));

    [TestInitialize]
    public void CreateFolder() => Directory.CreateDirectory(folder);

    [TestCleanup]
    public void RemoveFolder() => Directory.Delete(folder, recursive: true);

    [TestMethod]
    public void AFileIsRecycledAndItsIdentityComesBack()
    {
        var path = Write("recycle me.txt");

        var outcome = new WindowsRecycleBin().Recycle([path], ShortRetry).Single();

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Reason);
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(path, outcome.Value!.Path);
        StringAssert.Contains(outcome.Value.RecycledIdentity, "$Recycle.Bin");
    }

    [TestMethod]
    public void AMissingFileFailsAloneNamingIt()
    {
        var present = Write("present.txt");
        var missing = Path.Combine(folder, "missing.txt");

        var outcomes = new WindowsRecycleBin().Recycle([missing, present], ShortRetry);

        Assert.HasCount(2, outcomes);
        Assert.IsNotNull(outcomes[0].Failure);
        Assert.AreEqual("missing.txt", outcomes[0].Failure!.Subject);
        Assert.IsTrue(outcomes[1].Succeeded, outcomes[1].Failure?.Reason);
        Assert.IsFalse(File.Exists(present));
    }

    [TestMethod]
    public void AFileStillHeldAfterTheRetryFailsNamingItAndStays()
    {
        var path = Write("held.txt");

        ImageStash.Outcome<ImageStash.Seams.RecycledFile> outcome;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = new WindowsRecycleBin().Recycle([path], ShortRetry).Single();
        }

        Assert.IsNotNull(outcome.Failure);
        Assert.AreEqual("held.txt", outcome.Failure.Subject);
        Assert.AreEqual(HeldFileRetry.HeldReason, outcome.Failure.Reason);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void AFileReleasedWithinTheRetryIsRecycled()
    {
        var path = Write("released.txt");
        var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(300).ContinueWith(_ => hold.Dispose());

        var outcome = new WindowsRecycleBin().Recycle([path], TimeSpan.FromSeconds(5)).Single();

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Reason);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void APictureOpenForItsFramesIsRecycledAndItsFramesStillCome()
    {
        var path = Path.Combine(folder, "playing.png");
        using (var bitmap = new SkiaSharp.SKBitmap(4, 3))
        {
            bitmap.Erase(SkiaSharp.SKColors.Teal);
            File.WriteAllBytes(path, bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray());
        }

        var opened = ImageStash.Media.MediaDecoder.Open(path, "playing", ShortRetry);
        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        using var decoder = opened.Value!;
        Assert.IsTrue(decoder.NextFrame(4).Succeeded);

        var outcome = new WindowsRecycleBin().Recycle([path], ShortRetry).Single();

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Reason);
        Assert.IsFalse(File.Exists(path));
        decoder.Rewind();
        var again = decoder.NextFrame(4);
        Assert.IsTrue(again.Succeeded, again.Failure?.ToString());
        Assert.IsNotNull(again.Value, "the open read through the recycle goes on");
    }

    [TestMethod]
    public void ARecycledFileIsRemovedForGoodByItsIdentityWithItsRecord()
    {
        var name = "remove me " + Guid.NewGuid().ToString("N") + ".txt";
        var bin = new WindowsRecycleBin();
        var identity = Recycled(bin, Write(name));
        var record = RecordOf(identity);
        Assert.IsTrue(File.Exists(identity) && File.Exists(record), "the entry is a $R file with its $I record beside it");
        Assert.AreEqual(1, EntriesInTheBin(name, identity));

        var removed = bin.Remove([identity], ShortRetry).Single();

        Assert.IsTrue(removed.Succeeded, removed.Failure?.Reason);
        Assert.IsFalse(File.Exists(identity));
        Assert.IsFalse(File.Exists(record));
        Assert.AreEqual(0, EntriesInTheBin(name, identity));
    }

    [TestMethod]
    public void AnIdentityNamingNoEntryFailsNamingIt()
    {
        var bin = new WindowsRecycleBin();
        var identity = Recycled(bin, Write("emptied meanwhile.txt"));
        // The user empties the bin while the notice shows.
        File.Delete(identity);
        File.Delete(RecordOf(identity));

        var removed = bin.Remove([identity], ShortRetry).Single();

        Assert.AreEqual(new ImageStash.Failure("Remove from the Recycle Bin", Path.GetFileName(identity), "the file does not exist"), removed.Failure);
    }

    [TestMethod]
    public void AnythingThisBinDidNotRecycleIsRefusedAndLeftAlone()
    {
        var named = Write("$Rlooks recycled.txt");
        var plain = Write("in the stash.png");
        var other = new WindowsRecycleBin();
        var elsewhere = Recycled(other, Write("recycled by someone else.txt"));
        try
        {
            var outcomes = new WindowsRecycleBin().Remove([named, plain, @"C:\$Recycle.Bin\S-1-5-21\..\..\$Rx.png", elsewhere], ShortRetry);

            Assert.HasCount(4, outcomes);
            Assert.IsTrue(outcomes.All(o => o.Failure?.Reason == "it is not an entry ImageStash put in the Recycle Bin"));
            Assert.AreEqual("in the stash.png", outcomes[1].Failure!.Subject);
            Assert.IsTrue(File.Exists(named) && File.Exists(plain));
            Assert.IsTrue(File.Exists(elsewhere) && File.Exists(RecordOf(elsewhere)), "an entry this bin did not give back stays whole");
        }
        finally
        {
            other.Remove([elsewhere], ShortRetry);
        }
    }

    [TestMethod]
    public void AnEntryStillHeldAfterTheRetryIsNamedAndStaysInTheBin()
    {
        var name = "held entry " + Guid.NewGuid().ToString("N") + ".txt";
        var bin = new WindowsRecycleBin();
        var identity = Recycled(bin, Write(name));
        try
        {
            ImageStash.Outcome removed;
            using (new FileStream(identity, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                removed = bin.Remove([identity], ShortRetry).Single();
            }

            Assert.AreEqual(new ImageStash.Failure("Remove from the Recycle Bin", Path.GetFileName(identity), HeldFileRetry.HeldReason), removed.Failure);
            Assert.IsTrue(File.Exists(identity));
            Assert.AreEqual(1, EntriesInTheBin(name, identity));
        }
        finally
        {
            bin.Remove([identity], ShortRetry);
        }
    }

    [TestMethod]
    [DataRow(true, DisplayName = "its $I record held")]
    [DataRow(false, DisplayName = "its $R file held")]
    public void AnEntryOneOfWhoseFilesIsHeldIsRefusedAsHeldAndRemovedWholeWhenAskedAgain(bool recordHeld)
    {
        var name = "half removed " + Guid.NewGuid().ToString("N") + ".txt";
        var bin = new WindowsRecycleBin();
        var identity = Recycled(bin, Write(name));
        var record = RecordOf(identity);
        try
        {
            ImageStash.Outcome refused;
            using (new FileStream(recordHeld ? record : identity, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // No retry of its own, as the store asks it, which retries a held answer itself.
                refused = bin.Remove([identity], TimeSpan.Zero).Single();
            }

            Assert.AreEqual(new ImageStash.Failure("Remove from the Recycle Bin", Path.GetFileName(identity), HeldFileRetry.HeldReason), refused.Failure);
            Assert.AreEqual(!recordHeld, File.Exists(identity), "the $R file goes first, so a held record leaves the entry half removed");
            Assert.IsTrue(File.Exists(record));

            var removed = bin.Remove([identity], TimeSpan.Zero).Single();

            Assert.IsTrue(removed.Succeeded, removed.Failure?.Reason);
            Assert.IsFalse(File.Exists(identity));
            Assert.IsFalse(File.Exists(record));
            Assert.AreEqual(0, EntriesInTheBin(name, identity));
        }
        finally
        {
            bin.Remove([identity], ShortRetry);
        }
    }

    [TestMethod]
    public void AFolderIsRecycledWholeAsOneEntryAndRemovedForGoodByItsIdentity()
    {
        var name = "replaced stash " + Guid.NewGuid().ToString("N");
        var replaced = Directory.CreateDirectory(Path.Combine(folder, name)).FullName;
        File.WriteAllText(Path.Combine(replaced, "a.png"), "ImageStash recycle check");
        Directory.CreateDirectory(Path.Combine(replaced, "originals"));
        File.WriteAllText(Path.Combine(replaced, "originals", "b.png"), "ImageStash recycle check");
        var bin = new WindowsRecycleBin();

        var recycled = bin.Recycle([replaced], ShortRetry);

        var identity = recycled.Single().Value?.RecycledIdentity;
        try
        {
            Assert.IsNotNull(identity, recycled.Single().Failure?.Reason);
            Assert.IsFalse(Directory.Exists(replaced));
            Assert.IsTrue(File.Exists(Path.Combine(identity, "originals", "b.png")), "the folder is in the bin whole, with what it held");
            Assert.AreEqual(1, EntriesInTheBin(name, identity));

            var removed = bin.Remove([identity], ShortRetry).Single();

            Assert.IsTrue(removed.Succeeded, removed.Failure?.Reason);
            Assert.IsFalse(Directory.Exists(identity));
            Assert.IsFalse(File.Exists(RecordOf(identity)));
            Assert.AreEqual(0, EntriesInTheBin(name, identity));
        }
        finally
        {
            if (identity is not null)
            {
                bin.Remove([identity], ShortRetry);
            }
        }
    }

    [TestMethod]
    public void AFolderHoldingAFileAnotherProgramHasOpenIsRefusedAsHeldAndStaysWhole()
    {
        var replaced = Directory.CreateDirectory(Path.Combine(folder, "held stash")).FullName;
        var playing = Path.Combine(replaced, "playing.gif");
        File.WriteAllText(playing, "ImageStash recycle check");
        File.WriteAllText(Path.Combine(replaced, "b.png"), "ImageStash recycle check");

        ImageStash.Outcome<ImageStash.Seams.RecycledFile> outcome;
        // Shared as Media opens a playing picture: Windows still refuses to move the folder holding it.
        using (new FileStream(playing, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            outcome = new WindowsRecycleBin().Recycle([replaced], ShortRetry).Single();
        }

        Assert.AreEqual(new ImageStash.Failure("Move to the Recycle Bin", "held stash", HeldFileRetry.HeldReason), outcome.Failure);
        Assert.IsTrue(File.Exists(playing) && File.Exists(Path.Combine(replaced, "b.png")), "nothing of the folder went");
    }

    [TestMethod]
    public void AFileOnADriveWithNoRecycleBinIsRemovedForGoodAndAnsweredDoneWithNoIdentity()
    {
        // A substituted drive has no Recycle Bin, so the shell removes the file for good, as it does on a drive whose
        // bin is turned off or for a file larger than the bin takes: the removal is done, with nothing to remove (G29, S9).
        var path = Write("no bin here.txt");
        using var drive = SubstDrive.For(folder);
        var onDrive = Path.Combine(drive.Letter + @"\", "no bin here.txt");

        var outcome = new WindowsRecycleBin().Recycle([onDrive], ShortRetry).Single();

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Reason);
        Assert.AreEqual(onDrive, outcome.Value!.Path);
        Assert.IsNull(outcome.Value.RecycledIdentity, "nothing went to a Recycle Bin");
        Assert.IsFalse(File.Exists(path), "the file is gone for good");
        Assert.AreEqual(0, EntriesInTheBin("no bin here.txt", onDrive));
    }

    private string Write(string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "ImageStash recycle check");
        return path;
    }

    private static string Recycled(WindowsRecycleBin bin, string path)
    {
        var recycled = bin.Recycle([path], ShortRetry).Single();
        Assert.IsTrue(recycled.Succeeded, recycled.Failure?.Reason);
        return recycled.Value!.RecycledIdentity ?? throw new AssertFailedException("the file went to no Recycle Bin");
    }

    /// <summary>The $I record the shell keeps beside a $R entry.</summary>
    private static string RecordOf(string identity) => Path.Combine(Path.GetDirectoryName(identity)!, "$I" + Path.GetFileName(identity)[2..]);

    /// <summary>How many entries the shell's Recycle Bin folder lists as this file, by its $R path or its shown name.</summary>
    private static int EntriesInTheBin(string name, string identity)
    {
        var count = 0;
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                dynamic items = shell.NameSpace(10).Items();
                for (var i = 0; i < (int)items.Count; i++)
                {
                    dynamic item = items.Item(i);
                    if (string.Equals((string)item.Path, identity, StringComparison.OrdinalIgnoreCase) || string.Equals((string)item.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
            }
            catch (Exception e)
            {
                failed = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failed is null ? count : throw new AssertFailedException("reading the Recycle Bin folder failed", failed);
    }
}
