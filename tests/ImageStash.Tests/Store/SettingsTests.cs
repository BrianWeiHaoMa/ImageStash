using System.Text.Json;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>The settings file: G32's defaults, G31's earlier files, State's write-then-rename, G35's held and broken files.</summary>
[TestClass]
public sealed class SettingsTests
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    [TestMethod]
    public void FirstRunWritesEveryDefault()
    {
        using var scratch = new ScratchFolder();
        var folder = Path.Combine(scratch.Path, "ImageStash");

        var load = Settings.Load(folder, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.AreEqual(new Settings(), load.Settings);
        using var written = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, Settings.FileName)));
        var root = written.RootElement;
        Assert.AreEqual(1, root.GetProperty("version").GetInt32());
        var shortcuts = root.GetProperty("shortcuts");
        Assert.AreEqual("Ctrl+Alt+Space", shortcuts.GetProperty("open").GetString());
        Assert.AreEqual("Ctrl+PrtScn", shortcuts.GetProperty("screenshot").GetString());
        Assert.AreEqual("Ctrl+Shift+PrtScn", shortcuts.GetProperty("record").GetString());
        Assert.AreEqual("Ctrl+Alt+V", shortcuts.GetProperty("stashClipboardImage").GetString());
        Assert.IsTrue(root.GetProperty("alsoCopyCapture").GetBoolean());
        Assert.AreEqual(30, root.GetProperty("recordingMaxSeconds").GetInt32());
        Assert.IsTrue(root.GetProperty("recordPointer").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("stashFolder").ValueKind);
        Assert.IsTrue(root.GetProperty("startWithWindows").GetBoolean());
        Assert.AreEqual("system", root.GetProperty("theme").GetString());
        Assert.IsFalse(root.TryGetProperty("look", out _), "one look, so no take of it is kept (G32)");
        Assert.AreEqual("medium", root.GetProperty("thumbnailSize").GetString());
        Assert.AreEqual(460, root.GetProperty("flyoutSize").GetProperty("width").GetDouble());
        Assert.AreEqual(640, root.GetProperty("flyoutSize").GetProperty("height").GetDouble());
        Assert.AreEqual(880, root.GetProperty("pinnedSize").GetProperty("width").GetDouble());
        Assert.AreEqual(620, root.GetProperty("pinnedSize").GetProperty("height").GetDouble());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("pinnedPosition").ValueKind);
        Assert.IsTrue(load.FirstRun);
    }

    [TestMethod]
    public void AStartWithASettingsFileIsNotTheFirstRun()
    {
        using var scratch = new ScratchFolder();
        Settings.Load(scratch.Path, ShortRetry);

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsFalse(load.FirstRun);
        Assert.IsTrue(load.AsSaved);
    }

    [TestMethod]
    public void TheWindowsSizesAndPlaceAndTheThumbnailSizeAreReadBackAsWritten()
    {
        using var scratch = new ScratchFolder();
        var chosen = new Settings
        {
            ThumbnailSize = ThumbnailSize.Large,
            FlyoutSize = new WindowSize(500, 700),
            PinnedSize = new WindowSize(1200, 800),
            PinnedPosition = new WindowPosition(-1500, 60),
        };

        Settings.Write(scratch.Path, chosen, ShortRetry);
        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.AreEqual(chosen, load.Settings);
    }

    [TestMethod]
    [DataRow("small", ThumbnailSize.Small, 64.0)]
    [DataRow("medium", ThumbnailSize.Medium, 96.0)]
    [DataRow("large", ThumbnailSize.Large, 136.0)]
    public void AnEarlierBuildsFileOpensAtTheSizeItNamesAsLargeAsItWasWithItsOtherValues(string written, ThumbnailSize size, double rowHeight)
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), $$"""{"version":1,"theme":"dark","recordingMaxSeconds":60,"thumbnailSize":"{{written}}","stashFolder":"D:\\Stash"}""");

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.AreEqual((size, rowHeight), (load.Settings.ThumbnailSize, ImageStash.Window.JustifiedRows.RowHeight(load.Settings.ThumbnailSize)), "the size it named, its rows as tall as before (G31)");
        Assert.AreEqual((Theme.Dark, 60, @"D:\Stash"), (load.Settings.Theme, load.Settings.RecordingMaxSeconds, load.Settings.StashFolder), "its other values kept");
    }

    [TestMethod]
    [DataRow(ThumbnailSize.ExtraLarge, "extraLarge")]
    [DataRow(ThumbnailSize.Huge, "huge")]
    public void TheTwoLargerSizesAreWrittenByNameAndReadBack(ThumbnailSize size, string written)
    {
        using var scratch = new ScratchFolder();

        Settings.Write(scratch.Path, new Settings { ThumbnailSize = size }, ShortRetry);

        StringAssert.Contains(File.ReadAllText(scratch.File(Settings.FileName)), $"\"thumbnailSize\": \"{written}\"");
        Assert.AreEqual(size, Settings.Load(scratch.Path, ShortRetry).Settings.ThumbnailSize);
    }

    [TestMethod]
    public void AnEarlierFileWithoutTheWindowsValuesReadsTheirDefaults()
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1,"theme":"dark","stashFolder":"D:\\Stash"}""");

        var settings = Settings.Load(scratch.Path, ShortRetry).Settings;

        Assert.AreEqual(ThumbnailSize.Medium, settings.ThumbnailSize);
        Assert.AreEqual(WindowSize.FlyoutDefault, settings.FlyoutSize);
        Assert.AreEqual(WindowSize.PinnedDefault, settings.PinnedSize);
        Assert.IsNull(settings.PinnedPosition);
        Assert.AreEqual(@"D:\Stash", settings.StashFolder);
    }

    [TestMethod]
    public void AnEarlierFileWithOnlyItsVersionReadsAsTheDefaults()
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1}""");

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.AreEqual(new Settings(), load.Settings);
    }

    [TestMethod]
    public void AMissingValueTakesItsDefaultAndTheOthersAreKept()
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1,"shortcuts":{"open":"Ctrl+Alt+S"},"theme":"dark","startWithWindows":false}""");

        var settings = Settings.Load(scratch.Path, ShortRetry).Settings;

        Assert.AreEqual("Ctrl+Alt+S", settings.Shortcuts.Open);
        Assert.AreEqual("Ctrl+PrtScn", settings.Shortcuts.Screenshot);
        Assert.AreEqual(Theme.Dark, settings.Theme);
        Assert.IsFalse(settings.StartWithWindows);
        Assert.AreEqual(30, settings.RecordingMaxSeconds);
    }

    [TestMethod]
    public void AVersionOneFileWithEveryFieldChosenReadsEveryValueAsChosen()
    {
        using var scratch = new ScratchFolder();
        // As the last version-1 build wrote it: indented, camelCase names, enums as camelCase words, every value off its default.
        File.WriteAllText(scratch.File(Settings.FileName), """
            {
              "version": 1,
              "shortcuts": {
                "open": "Ctrl+Shift+Space",
                "screenshot": "Shift+PrtScn",
                "record": "Alt+PrtScn",
                "stashClipboardImage": "Ctrl+Shift+F9"
              },
              "alsoCopyCapture": false,
              "recordingMaxSeconds": 15,
              "recordPointer": false,
              "stashFolder": "D:\\Reactions",
              "startWithWindows": false,
              "theme": "dark",
              "thumbnailSize": "large",
              "flyoutSize": {
                "width": 520,
                "height": 700
              },
              "pinnedSize": {
                "width": 1100,
                "height": 760
              },
              "pinnedPosition": {
                "x": -1500,
                "y": 60
              }
            }
            """);

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.IsTrue(load.AsSaved);
        Assert.AreEqual(
            new Settings
            {
                Shortcuts = new Shortcuts { Open = "Ctrl+Shift+Space", Screenshot = "Shift+PrtScn", Record = "Alt+PrtScn", StashClipboardImage = "Ctrl+Shift+F9" },
                AlsoCopyCapture = false,
                RecordingMaxSeconds = 15,
                RecordPointer = false,
                StashFolder = @"D:\Reactions",
                StartWithWindows = false,
                Theme = Theme.Dark,
                ThumbnailSize = ThumbnailSize.Large,
                FlyoutSize = new WindowSize(520, 700),
                PinnedSize = new WindowSize(1100, 760),
                PinnedPosition = new WindowPosition(-1500, 60),
            },
            load.Settings,
            "every value of an earlier build's file as it was chosen (G31)");
    }

    [TestMethod]
    public void TheMoveFieldsAreAbsentByDefaultAndReadBackAsWrittenUnderVersionOne()
    {
        using var scratch = new ScratchFolder();
        Assert.IsNull(Settings.Write(scratch.Path, new Settings(), ShortRetry).Failure);
        using (var defaults = JsonDocument.Parse(File.ReadAllText(scratch.File(Settings.FileName))))
        {
            Assert.AreEqual(1, defaults.RootElement.GetProperty("version").GetInt32());
            foreach (var field in new[] { "moveCopyInProgress", "previousStashFolderPendingRemoval" })
            {
                Assert.IsTrue(!defaults.RootElement.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null, field);
            }
        }

        var moving = new Settings { StashFolder = @"D:\New", MoveCopyInProgress = @"D:\New", PreviousStashFolderPendingRemoval = @"C:\Old" };
        Assert.IsNull(Settings.Write(scratch.Path, moving, ShortRetry).Failure);
        Assert.AreEqual(moving, Settings.Load(scratch.Path, ShortRetry).Settings);
        Assert.Contains("\"previousStashFolderPendingRemoval\"", File.ReadAllText(scratch.File(Settings.FileName)));
    }

    [TestMethod]
    public void TheImportRecordIsAbsentByDefaultNullFromAnEarlierFileAndReadBackAsWrittenWithItsFourParts()
    {
        using var scratch = new ScratchFolder();
        Assert.IsNull(Settings.Write(scratch.Path, new Settings(), ShortRetry).Failure);
        using (var defaults = JsonDocument.Parse(File.ReadAllText(scratch.File(Settings.FileName))))
        {
            Assert.IsTrue(!defaults.RootElement.TryGetProperty("importPending", out var value) || value.ValueKind == JsonValueKind.Null);
        }

        // As a build without imports wrote it (G31).
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1,"recordPointer":false,"stashFolder":"D:\\Stash"}""");
        var earlier = Settings.Load(scratch.Path, ShortRetry);
        Assert.IsNull(earlier.Failure);
        Assert.IsNull(earlier.Settings.ImportPending);

        var travelling = new TravellingSettings(new Shortcuts { Open = "Ctrl+Alt+K" }, false, 60, false, false, Theme.Dark);
        var importing = new Settings
        {
            StashFolder = @"D:\Stash",
            ImportPending = new ImportPending(@"D:\Stash.importing-0123456789ab", @"D:\Stash.replaced-0123456789ab", ".imagestash-import-0123456789ab", travelling),
        };
        Assert.IsNull(Settings.Write(scratch.Path, importing, ShortRetry).Failure);
        Assert.AreEqual(importing, Settings.Load(scratch.Path, ShortRetry).Settings);
        Assert.Contains("\"importPending\"", File.ReadAllText(scratch.File(Settings.FileName)));
    }

    [TestMethod]
    public void TheTravellingValuesAreTheSixThatAreNotAboutThisPcAndApplyingThemKeepsEveryOther()
    {
        var here = new Settings
        {
            StashFolder = @"D:\Stash",
            ThumbnailSize = ThumbnailSize.Large,
            FlyoutSize = new WindowSize(500, 700),
            PinnedSize = new WindowSize(900, 650),
            PinnedPosition = new WindowPosition(4, 5),
            MoveCopyInProgress = @"E:\New",
        };
        var travelling = new TravellingSettings(new Shortcuts { Record = "Ctrl+Alt+R" }, false, 60, false, false, Theme.Light);

        var applied = travelling.AppliedTo(here);

        Assert.AreEqual(travelling, TravellingSettings.Of(applied));
        Assert.AreEqual(here, TravellingSettings.Of(here).AppliedTo(applied), "every other value kept");
    }

    [TestMethod]
    public void AnUnknownFieldIsReadWithoutAFailure()
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1,"recordPointer":false,"fromALaterBuild":{"x":1}}""");

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.IsFalse(load.Settings.RecordPointer);
    }

    [TestMethod]
    [DataRow("\"ink\"")]
    [DataRow("\"mist\"")]
    [DataRow("\"paper\"")]
    [DataRow("2")]
    public void AFileAnEarlierBuildWroteNamingATakeOfTheLookReadsWithEveryOtherValueKept(string look)
    {
        using var scratch = new ScratchFolder();
        // As the build that offered three takes wrote it, among values no default has; the take is no longer kept (G31).
        File.WriteAllText(scratch.File(Settings.FileName), $$$"""
            {"version":1,"shortcuts":{"open":"Ctrl+Shift+Space","screenshot":"Ctrl+Alt+PrtScn","record":"Ctrl+Alt+Shift+PrtScn","stashClipboardImage":"Ctrl+Shift+V"},
             "alsoCopyCapture":false,"recordingMaxSeconds":60,"recordPointer":false,"stashFolder":"D:\\Stash","startWithWindows":false,"theme":"dark",
             "look":{{{look}}},"thumbnailSize":"large","flyoutSize":{"width":500,"height":700},"pinnedSize":{"width":900,"height":650},"pinnedPosition":{"x":-1200,"y":40}}
            """);

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.IsTrue(load.AsSaved);
        Assert.AreEqual(new Settings
        {
            Shortcuts = new Shortcuts { Open = "Ctrl+Shift+Space", Screenshot = "Ctrl+Alt+PrtScn", Record = "Ctrl+Alt+Shift+PrtScn", StashClipboardImage = "Ctrl+Shift+V" },
            AlsoCopyCapture = false,
            RecordingMaxSeconds = 60,
            RecordPointer = false,
            StashFolder = @"D:\Stash",
            StartWithWindows = false,
            Theme = Theme.Dark,
            ThumbnailSize = ThumbnailSize.Large,
            FlyoutSize = new WindowSize(500, 700),
            PinnedSize = new WindowSize(900, 650),
            PinnedPosition = new WindowPosition(-1200, 40),
        }, load.Settings);
    }

    [TestMethod]
    public void ASettingsFileWrittenNowHoldsNoTakeOfTheLook()
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1,"theme":"light","look":"mist"}""");
        var read = Settings.Load(scratch.Path, ShortRetry).Settings;

        Assert.IsNull(Settings.Write(scratch.Path, read with { RecordPointer = false }, ShortRetry).Failure);

        using var written = JsonDocument.Parse(File.ReadAllText(scratch.File(Settings.FileName)));
        Assert.IsFalse(written.RootElement.TryGetProperty("look", out _), "the value is no longer kept");
        Assert.AreEqual("light", written.RootElement.GetProperty("theme").GetString());
    }

    [TestMethod]
    public void AThemeWrittenAsANumberIsRefused()
    {
        using var scratch = new ScratchFolder();
        const string file = """{"version":1,"theme":1}""";
        File.WriteAllText(scratch.File(Settings.FileName), file);

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNotNull(load.Failure, file);
        Assert.AreEqual(Settings.FileName, load.Failure.Subject, file);
        Assert.AreEqual(new Settings(), load.Settings, file);
    }

    [TestMethod]
    public void AFileThatDoesNotParseGivesTheDefaultsAndNamesTheFileAndIsLeftAlone()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File(Settings.FileName);
        File.WriteAllText(path, """{"version":1,"theme":""");

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.AreEqual(new Settings(), load.Settings);
        Assert.IsNotNull(load.Failure);
        Assert.AreEqual(Settings.FileName, load.Failure.Subject);
        Assert.AreEqual("""{"version":1,"theme":""", File.ReadAllText(path));
    }

    [TestMethod]
    public void WrittenSettingsReadBackAsWritten()
    {
        using var scratch = new ScratchFolder();
        var chosen = new Settings
        {
            Shortcuts = new Shortcuts { Record = "Ctrl+Alt+R" },
            AlsoCopyCapture = false,
            RecordingMaxSeconds = 60,
            RecordPointer = false,
            StashFolder = @"D:\Reactions",
            StartWithWindows = false,
            Theme = Theme.Light,
        };

        Assert.IsTrue(Settings.Write(scratch.Path, chosen, ShortRetry).Succeeded);

        Assert.AreEqual(chosen, Settings.Load(scratch.Path, ShortRetry).Settings);
    }

    [TestMethod]
    public void AWriteOverAHeldFileFailsNamingItAndKeepsTheOldContent()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File(Settings.FileName);
        Settings.Write(scratch.Path, new Settings { Theme = Theme.Dark }, ShortRetry);
        var before = File.ReadAllText(path);

        Outcome held;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            held = Settings.Write(scratch.Path, new Settings { Theme = Theme.Light }, ShortRetry);
        }

        Assert.IsNotNull(held.Failure);
        Assert.AreEqual(Settings.FileName, held.Failure.Subject);
        Assert.AreEqual(HeldFileRetry.HeldReason, held.Failure.Reason);
        Assert.AreEqual(before, File.ReadAllText(path));

        Assert.IsTrue(Settings.Write(scratch.Path, new Settings { Theme = Theme.Light }, ShortRetry).Succeeded);
        Assert.AreEqual(Theme.Light, Settings.Load(scratch.Path, ShortRetry).Settings.Theme);
        Assert.IsFalse(File.Exists(path + ".new"));
    }

    [TestMethod]
    public void AProgramReadingTheFileAgainAndAgainNeverHasAWriteRefused()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File(Settings.FileName);
        Settings.Write(scratch.Path, new Settings(), HeldFileRetry.Duration);
        using var done = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                try
                {
                    _ = File.ReadAllBytes(path);
                }
                catch (IOException)
                {
                    // Its read is refused for the moment the write looks at whether the file is held.
                }

                Thread.Sleep(1);
            }
        });

        var refused = Enumerable.Range(0, 400)
            .Select(round => Settings.Write(scratch.Path, new Settings { RecordingMaxSeconds = 10 + round }, HeldFileRetry.Duration).Failure)
            .FirstOrDefault(failure => failure is not null);
        done.Cancel();
        reader.Wait();

        Assert.IsNull(refused, "a file another program holds again and again is retried, not refused for good (G35, E3)");
        Assert.AreEqual(409, Settings.Load(scratch.Path, ShortRetry).Settings.RecordingMaxSeconds);
    }

    [TestMethod]
    public void AWriteSucceedsWhenTheHolderLetsGoWithinTheRetry()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File(Settings.FileName);
        Settings.Write(scratch.Path, new Settings(), ShortRetry);
        var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(200).ContinueWith(_ => hold.Dispose());

        var outcome = Settings.Write(scratch.Path, new Settings { Theme = Theme.Dark }, TimeSpan.FromSeconds(5));

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual(Theme.Dark, Settings.Load(scratch.Path, ShortRetry).Settings.Theme);
    }

    [TestMethod]
    public void AHeldFileAtReadGivesTheDefaultsForNowAndIsNotReplaced()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File(Settings.FileName);
        Settings.Write(scratch.Path, new Settings { Theme = Theme.Dark }, ShortRetry);
        var before = File.ReadAllText(path);

        SettingsLoad load;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            load = Settings.Load(scratch.Path, ShortRetry);
        }

        Assert.IsNotNull(load.Failure);
        Assert.AreEqual(Settings.FileName, load.Failure.Subject);
        Assert.AreEqual(HeldFileRetry.HeldReason, load.Failure.Reason);
        Assert.AreEqual(new Settings(), load.Settings);
        Assert.AreEqual(before, File.ReadAllText(path));
    }

    [TestMethod]
    public void ANewFileLeftBesideAValidOneByAKillIsIgnored()
    {
        using var scratch = new ScratchFolder();
        Settings.Write(scratch.Path, new Settings { Theme = Theme.Dark }, ShortRetry);
        File.WriteAllText(scratch.File(Settings.FileName + ".new"), """{"version":1,"theme":"li""");

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.AreEqual(Theme.Dark, load.Settings.Theme);
    }
}
