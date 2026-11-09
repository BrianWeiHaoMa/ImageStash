using Avalonia;
using ImageStash.Windows.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// Import files…'s picker as the Windows dialog receives it: one file type for the four formats, then every file
/// (G12); Save a copy…'s, with the item's name, its one format and the dialog's question before replacing a file (G9,
/// S16); the shell's Open-with dialog shown on a file, modal to the window asked for, the call ending once it has closed
/// and not before, that window in front again, and a path the shell refuses named with no dialog (G27, S16, F4, G35);
/// Reveal in File Explorer refusing a file that is not there by name (G35); and Open folder showing a folder itself in
/// File Explorer, and refusing, by the folder's name, a folder that is not there and a file, which it never starts
/// (G32, G35); Move…'s folder picker answering a folder typed into it, and nothing when cancelled (G30, F13); Import
/// stash…'s one-file picker answering a file typed into it, and nothing when cancelled (G36, F13). The file
/// pickers themselves and Explorer's selection are the person's to observe; the Open-with dialog is
/// shown on a picture of the check's own and closed as a cancel, and an OpenWith.exe host it leaves running is ended;
/// the Explorer window Open folder shows on a folder of the check's own is closed.
/// </summary>
[TestClass]
public sealed class WindowsDialogsAndExplorerTests
{
    [TestMethod]
    public void ThePickerListsTheFourFormatsThenAllFiles()
    {
        var types = WindowsDialogsAndExplorer.FileTypes(Import.PickerFilters);

        Assert.HasCount(2, types);
        Assert.AreEqual("Pictures (GIF, PNG, JPG, WebP)", types[0].Name);
        CollectionAssert.AreEqual(new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" }, types[0].Patterns!.ToList());
        Assert.AreEqual("All files", types[1].Name);
        CollectionAssert.AreEqual(new[] { "*.*" }, types[1].Patterns!.ToList());
    }

    [TestMethod]
    public void SaveACopysPickerSuggestsTheFileOffersItsFormatAloneAndAsksBeforeReplacing()
    {
        var options = WindowsDialogsAndExplorer.SaveOptions("Save a copy", "cat.gif", ItemMenu.FileType("cat.gif"));

        Assert.AreEqual(("Save a copy", "cat.gif", "gif"), (options.Title, options.SuggestedFileName, options.DefaultExtension), "a name typed without an extension gets the format's");
        var type = options.FileTypeChoices!.Single();
        Assert.AreEqual("GIF picture", type.Name);
        CollectionAssert.AreEqual(new[] { "*.gif" }, type.Patterns!.ToList());
        Assert.IsTrue(options.ShowOverwritePrompt, "the dialog asks before an existing file is replaced (G9, S16)");
    }

    [TestMethod]
    public void SaveACopysChosenPathSpellsItsFolderAsTheFolderItReaches()
    {
        // The stash folder reached through a substituted drive, a junction or its short name is still the stash
        // folder, so Save a copy… refuses it there however the picker spelled it (G9, G29).
        var parent = Path.Combine(Path.GetTempPath(), "imagestash-final-path-check-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(parent, "stash folder");
        var junction = parent + " junction";
        Directory.CreateDirectory(folder);
        var drive = SubstDrive.For(parent);
        try
        {
            SubstDrive.Run("cmd.exe", $"/c mklink /J \"{junction}\" \"{parent}\"");
            var expected = Path.Combine(folder, "cat.png");

            Assert.AreEqual(expected, WindowsDialogsAndExplorer.FinalPath(Path.Combine(drive.Letter + @"\", "stash folder", "cat.png")), true, "a substituted drive");
            Assert.AreEqual(expected, WindowsDialogsAndExplorer.FinalPath(Path.Combine(junction, "stash folder", "cat.png")), true, "a junction");
            if (ShortName(folder) is { } shortFolder && !string.Equals(shortFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                Assert.AreEqual(expected, WindowsDialogsAndExplorer.FinalPath(Path.Combine(shortFolder, "cat.png")), true, "a short name");
            }

            var nowhere = Path.Combine(parent, "no such folder", "cat.png");
            Assert.AreEqual(nowhere, WindowsDialogsAndExplorer.FinalPath(nowhere), "a folder that is not there keeps its spelling");
        }
        finally
        {
            drive.Dispose();
            if (Directory.Exists(junction))
            {
                // The junction alone, never what it reaches.
                Directory.Delete(junction);
            }

            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public async Task OpenWithShowsTheShellsDialogOnTheFileModalToTheWindowAndEndsOnceItHasClosed()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var dialogs = new OpenWithDialog();
        using var owner = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        owner.ClickToFront();
        var folder = Path.Combine(Path.GetTempPath(), "imagestash-open-with-check-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(folder, "open with check.png");
        Directory.CreateDirectory(folder);
        using (var bitmap = new SKBitmap(new SKImageInfo(8, 6, SKColorType.Bgra8888, SKAlphaType.Opaque)))
        {
            bitmap.Erase(new SKColor(20, 160, 90));
            File.WriteAllBytes(file, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        var bytes = File.ReadAllBytes(file);
        try
        {
            var closed = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().OpenWith(owner.Window, file));
            var dialog = await OpenWithDialog.InFront();

            Assert.AreEqual("OpenWith", OpenWithDialog.ProcessOf(dialog), "the shell's own dialog, a window of its host (S16)");
            Assert.IsFalse(OpenWithDialog.TakesInput(owner.Handle), "modal to the window it was asked for, which takes no input while it is up");
            await Task.Delay(1000);
            Assert.IsFalse(closed.IsCompleted, "the call waits while the dialog is up");
            await OpenWithDialog.Close(dialog);
            var outcome = await closed.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Sentence);
            Assert.AreEqual((nint)0, OpenWithDialog.Find(), "no dialog left up (F4)");
            Assert.IsTrue(OpenWithDialog.TakesInput(owner.Handle), "the window takes input again");
            Assert.AreEqual(owner.Handle, FrontWindow.GetForegroundWindow(), "and is in front again, the dialog closed back to it");
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file), "the file as it was: a cancel starts no program");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task OpenWithOnAPathTheShellRefusesIsNamedAndShowsNoDialog()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var dialogs = new OpenWithDialog();
        using var owner = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        owner.ClickToFront();

        // A name Windows does not allow, which the shell refuses at once; a file that is only missing still gets the dialog (observed).
        var opened = await TrayDesktop.Get(() => new WindowsDialogsAndExplorer().OpenWith(owner.Window, @"C:\nowhere\mis|sing.png")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(("Open with…", "mis|sing.png"), (opened.Failure?.Action, opened.Failure?.Subject), opened.Failure?.Sentence ?? "answered done (G35)");
        Assert.IsFalse(string.IsNullOrWhiteSpace(opened.Failure!.Reason));
        Assert.AreEqual((nint)0, OpenWithDialog.Find(), "the shell refused it without a dialog (F4)");
        Assert.IsTrue(OpenWithDialog.TakesInput(owner.Handle), "and the window takes input");
    }

    [TestMethod]
    public async Task TheFolderPickerAnswersAFolderTypedIntoItAndACancelAnswersNothing()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var owner = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        owner.ClickToFront();
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "imagestash-folder-picker-check-" + Guid.NewGuid().ToString("N"))).FullName;
        var picker = (nint)0;
        try
        {
            var chosen = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().PickFolder(owner.Window, Moving.PickerTitle));
            picker = await PickerInFront(owner.Handle);
            // The folder box has the focus as the picker opens; the path given to it as typing would leave it. Enter may
            // first open the typed folder in the picker, and Enter again then chooses the folder open there.
            SendMessageW(FrontWindow.FocusedHandle(), WmSetText, 0, folder);
            Thread.Sleep(200);
            TestHotKeys.Tap(VkReturn);
            await Task.WhenAny(chosen, Task.Delay(1500));
            if (!chosen.IsCompleted)
            {
                TestHotKeys.Tap(VkReturn);
            }

            var answered = await chosen.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(folder, answered, true, "the folder chosen, as its local path (G30, F13)");

            var cancelled = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().PickFolder(owner.Window, Moving.PickerTitle));
            picker = await PickerInFront(owner.Handle);
            TestHotKeys.Tap(VkEscape);
            Assert.IsNull(await cancelled.WaitAsync(TimeSpan.FromSeconds(10)), "a cancel answers nothing");
            picker = 0;
            Assert.AreEqual(owner.Handle, FrontWindow.GetForegroundWindow(), "no picker left up (F4)");
        }
        finally
        {
            if (picker != 0 && FrontWindow.GetForegroundWindow() == picker)
            {
                TestHotKeys.Tap(VkEscape);
            }

            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheOneFilePickerAnswersAFileTypedIntoItAndACancelAnswersNothing()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var owner = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        owner.ClickToFront();
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "imagestash-file-picker-check-" + Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(folder, "export.zip");
        File.WriteAllBytes(file, [1]);
        var picker = (nint)0;
        try
        {
            var chosen = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().OpenFile(owner.Window, Transferring.ImportPickerTitle, [Transferring.FileType]));
            picker = await PickerInFront(owner.Handle);
            // The file name box has the focus as the picker opens; the path given to it as typing would leave it.
            SendMessageW(FrontWindow.FocusedHandle(), WmSetText, 0, file);
            Thread.Sleep(200);
            TestHotKeys.Tap(VkReturn);
            var answered = await chosen.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(file, answered, true, "the file chosen, as its local path (G36, F13)");

            var cancelled = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().OpenFile(owner.Window, Transferring.ImportPickerTitle, [Transferring.FileType]));
            picker = await PickerInFront(owner.Handle);
            TestHotKeys.Tap(VkEscape);
            Assert.IsNull(await cancelled.WaitAsync(TimeSpan.FromSeconds(10)), "a cancel answers nothing");
            picker = 0;
            Assert.AreEqual(owner.Handle, FrontWindow.GetForegroundWindow(), "no picker left up (F4)");
        }
        finally
        {
            if (picker != 0 && FrontWindow.GetForegroundWindow() == picker)
            {
                TestHotKeys.Tap(VkEscape);
            }

            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void RevealingAFileThatIsNotThereIsNamedAndOpensNothing()
    {
        var revealed = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().Reveal(@"C:\nowhere\missing.png"));

        Assert.AreEqual(("Reveal in File Explorer", "missing.png"), (revealed.Failure?.Action, revealed.Failure?.Subject), revealed.Failure?.Sentence);
        Assert.IsFalse(string.IsNullOrWhiteSpace(revealed.Failure!.Reason));
    }

    [TestMethod]
    public void OpeningAFolderThatIsNotThereIsNamedAndOpensNothing()
    {
        var opened = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().OpenFolder(@"C:\nowhere\stash"));

        Assert.AreEqual(("Open folder", "stash"), (opened.Failure?.Action, opened.Failure?.Subject), opened.Failure?.Sentence);
        Assert.IsFalse(string.IsNullOrWhiteSpace(opened.Failure!.Reason));
    }

    [TestMethod]
    public async Task OpeningAFolderShowsThatFolderItselfInFileExplorer()
    {
        var folder = Path.Combine(Path.GetTempPath(), "imagestash-open-folder-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var opened = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().OpenFolder(folder));

            Assert.IsTrue(opened.Succeeded, opened.Failure?.Sentence);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => ExplorerWindowsOn(folder).Count) == 1);
            Assert.IsEmpty(TrayDesktop.Get(() => ExplorerWindowsOn(Path.GetDirectoryName(folder)!)), "the folder itself, not its parent with it selected (G32)");
        }
        finally
        {
            TrayDesktop.Run(() => ExplorerWindowsOn(folder).ForEach(window => window.Quit()));
            Directory.Delete(folder);
        }
    }

    [TestMethod]
    public void OpeningAFileAsAFolderIsRefusedAndStartsNothing()
    {
        var file = Path.Combine(Path.GetTempPath(), "imagestash-open-folder-check-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "not a folder");
        try
        {
            var opened = TrayDesktop.Get(() => new WindowsDialogsAndExplorer().OpenFolder(file));

            Assert.AreEqual(new Failure("Open folder", Path.GetFileName(file), "it is not a folder"), opened.Failure, "refused before the shell could start a program on it");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The File Explorer windows showing <paramref name="folder"/>, as the shell lists its open windows.</summary>
    private static List<dynamic> ExplorerWindowsOn(string folder)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        var showing = new List<dynamic>();
        foreach (var window in shell.Windows())
        {
            string? path = null;
            try
            {
                path = window.Document?.Folder?.Self?.Path;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }

            if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase))
            {
                showing.Add(window);
            }
        }

        return showing;
    }

    /// <summary>The folder's short name, when the volume makes them; null otherwise.</summary>
    private static string? ShortName(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathNameW(path, buffer, buffer.Length);
        return length == 0 || length > buffer.Length ? null : new string(buffer, 0, (int)length);
    }

    /// <summary>The picker of this process in front of <paramref name="owner"/>, once it has settled.</summary>
    private static async Task<nint> PickerInFront(nint owner)
    {
        using var targets = new WindowsTarget();
        await ShownWindow.Eventually(() => FrontWindow.GetForegroundWindow() is var front && front != 0 && front != owner && targets.Foreground().IsMine);
        Thread.Sleep(500);
        return FrontWindow.GetForegroundWindow();
    }

    private const uint WmSetText = 0x000C;
    private const ushort VkReturn = 0x0D;
    private const ushort VkEscape = 0x1B;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetShortPathNameW(string longPath, char[] shortPath, int length);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, string lParam);
}
