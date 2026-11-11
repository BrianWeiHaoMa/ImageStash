using System.Runtime.Versioning;
using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Move… from the Settings page over a real stash and a settings file (G30, G32, State: Move): the folder picker, Store's
/// Move, the stash opened at the new folder and listed from there, the old store let go; the page's row showing the
/// folder the Move started from until it has ended and the outcome only after the old folder's removal has answered
/// (R9); the clipboard's items written again at their new paths when nothing has changed it since (G26); a refusal or
/// failure named on the row and in a notice, nothing changed before the switch, and a Move asked again afterwards (G35);
/// and the wait: a busy state naming what it waits for while a recording, an edit (open, or still opening), Stash
/// clipboard image, a drag out or a notice or notification whose Undo stands is in the way, each Undo still acting
/// meanwhile (G16, G18, G20, G27), and every beginning of a change refused by name from the asking (State: Move).
/// </summary>
[TestClass]
public sealed class MovingTests
{
    private static readonly PixelRect Region = new(100, 50, 32, 24);

    [TestMethod]
    public async Task EverythingMovesToTheChosenFolderListedFromThereAndTheRowSaysSoOnlyAfterTheOldFolderIsGone()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var dog = await listed.Stash.Import("dog");
        listed.Stash.Tag(cat.Id, "funny");
        await listed.Organising.ToggleFavorite([dog]);
        var old = listed.Stash.Folder.Root;
        var chosen = Chose(listed);
        var page = Page(listed);
        var rowWhileAsked = new List<string>();
        listed.Moving.Changed += () =>
        {
            if (listed.Moving.Asked)
            {
                rowWhileAsked.Add(page.StashFolder);
            }
        };
        bool? oldGoneWhenSaid = null;
        listed.Notices.Changed += () => oldGoneWhenSaid ??= listed.Notices.Shown.Any(notice => notice.Text.StartsWith("Moved")) ? !Directory.Exists(old) : null;

        var moved = await page.Move(null!);

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { Moving.PickerTitle }, listed.Dialogs.FolderPickers, "the picker asked once, with its title");
        Assert.IsTrue(rowWhileAsked.Count > 0 && rowWhileAsked.All(folder => folder == old), "the row reads the folder the Move started from until it has ended (R9)");
        Assert.IsTrue(oldGoneWhenSaid, "said only once the old folder was gone (R9)");
        Assert.IsFalse(Directory.Exists(old), "the old folder removed (G30)");
        Assert.AreEqual(chosen, MovingStash.Saved(listed.Stash).StashFolder, "the settings file switched (State: Move)");
        Assert.IsNull(MovingStash.Saved(listed.Stash).MoveCopyInProgress);
        Assert.IsNull(MovingStash.Saved(listed.Stash).PreviousStashFolderPendingRemoval);
        Assert.AreEqual(chosen, page.StashFolder, "the row reads the new folder once the Move has ended");
        Assert.AreEqual($"Moved your stash to {chosen}.", page.MoveLine);
        Assert.IsNull(page.WarningOf(SettingsPage.StashFolderTitle));
        Assert.AreEqual($"Moved your stash to {chosen}.", listed.Notices.Shown.Single().Text);
        Assert.IsFalse(listed.Moving.Asked);
        Assert.IsNull(listed.Moving.Busy);
        await listed.Listing.ShowScope(Scope.All);
        var items = listed.Listing.Items.ToDictionary(item => item.Id);
        CollectionAssert.AreEquivalent(new[] { cat.Id, dog.Id }, items.Keys.ToList());
        Assert.IsTrue(items.Values.All(item => item.FilePath.StartsWith(chosen + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(item.FilePath)), "listed from the new folder (G30, E2)");
        CollectionAssert.AreEqual(new[] { "funny" }, items[cat.Id].Tags.ToList());
        Assert.IsTrue(items[dog.Id].Favorite);
        Assert.IsTrue((await listed.Renaming.Rename(items[cat.Id], "kitten")).Succeeded, "the new stash takes changes");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ACancelledPickerAsksNothingOfTheStoreShowsNoBusyStateAndSaysNothing()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var busy = new List<string?>();
        listed.Moving.Changed += () => busy.Add(listed.Moving.Busy);

        var moved = await Page(listed).Move(null!);

        Assert.IsTrue(moved.Succeeded);
        Assert.HasCount(1, listed.Dialogs.FolderPickers);
        Assert.IsEmpty(busy, "no busy state, nothing asked (G30)");
        Assert.IsEmpty(listed.Notices.Shown);
        Assert.IsTrue(Directory.Exists(listed.Stash.Folder.Root));
        Assert.IsNull(MovingStash.Saved(listed.Stash).StashFolder);
    }

    [TestMethod]
    public async Task AFolderThatIsNotEmptyIsRefusedByNameOnTheRowAndInANoticeAndTheStashStaysAsItWas()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var chosen = Chose(listed);
        File.WriteAllText(Path.Combine(chosen, "notes.txt"), "mine");
        var page = Page(listed);

        var moved = await page.Move(null!);

        var refused = new Failure(StashMove.Action, "new", MoveReasons.NotEmpty);
        Assert.AreEqual(refused, moved.Failure);
        Assert.AreEqual(refused, page.WarningOf(SettingsPage.StashFolderTitle), "on the row (G35)");
        CollectionAssert.AreEqual(new[] { refused.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList());
        Assert.IsFalse(listed.Moving.Asked, "a failed Move ends, so changes may begin again");
        Assert.IsNull(listed.Listing.WholeStashActAsked);
        Assert.IsTrue((await listed.Renaming.Rename(cat, "kitten")).Succeeded, "the old stash still takes changes");
        Assert.AreEqual(listed.Stash.Folder.Root, page.StashFolder);
    }

    [TestMethod]
    public async Task ASettingsWriteRefusedBeforeTheCopyEndsTheMoveWithNothingChangedTheRowReadingTheOldFolderThroughout()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var chosen = Chose(listed);
        var page = Page(listed);
        var rows = new List<string>();
        listed.Moving.Changed += () => rows.Add(page.StashFolder);

        Outcome moved;
        using (new FileStream(MovingStash.SettingsPath(listed.Stash), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            moved = await page.Move(null!);
        }

        Assert.IsFalse(moved.Succeeded);
        Assert.AreEqual(Settings.FileName, moved.Failure.Subject, "named as the settings write (G35)");
        Assert.IsTrue(rows.All(folder => folder == listed.Stash.Folder.Root), "the row read the old folder throughout (R9)");
        Assert.AreEqual(listed.Stash.Folder.Root, page.StashFolder);
        Assert.IsNull(listed.Settings.Current.StashFolder, "the keeper's location as it was");
        Assert.IsEmpty(Directory.GetFileSystemEntries(chosen), "nothing copied (G30)");
        Assert.HasCount(1, await listed.Stash.All());
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the clipboard unchanged since")]
    [DataRow(false, DisplayName = "another program changed the clipboard since")]
    public async Task TheClipboardsItemsAreWrittenAgainAtTheirNewPathsOnlyWhileNothingHasChangedItSince(bool unchanged)
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var dog = await listed.Stash.ImportFixture("dog", "transparent.png");
        Assert.IsTrue((await listed.Handoff.Copy(null!, [cat, dog])).Succeeded);
        listed.Clipboard.WritesAgain = Outcome<bool>.Of(unchanged);
        var chosen = Chose(listed);

        Assert.IsTrue((await Page(listed).Move(null!)).Succeeded);

        var again = listed.Clipboard.WrittenAgain.Single();
        CollectionAssert.AreEqual(new[] { cat.Id, dog.Id }, again.Content.Items.ToList(), "every item of the last write, in its order (G26)");
        CollectionAssert.AreEqual(new[] { Path.Combine(chosen, cat.FileName), Path.Combine(chosen, dog.FileName) }, again.Content.FilePaths.ToList(), "at the new paths");
        Assert.IsFalse(listed.Notices.Shown.Any(notice => notice.Text.Contains(Handoff.RefreshAction)), "a changed clipboard is no failure (G26)");
    }

    [TestMethod]
    public async Task ARemovalThatFailsAfterTheSwitchListsFromTheNewFolderNamesTheFailureAndAMoveMayBeAskedAgain()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var chosen = Chose(listed);
        var page = Page(listed);

        Outcome moved;
        // Read beside the copy, but never deleted while held.
        using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            moved = await page.Move(null!);
        }

        Assert.IsFalse(moved.Succeeded);
        Assert.AreEqual(StashMove.Action, moved.Failure.Action);
        Assert.AreEqual(moved.Failure, page.WarningOf(SettingsPage.StashFolderTitle), "named on the row (G35)");
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), moved.Failure.Sentence, "and in a notice");
        Assert.IsFalse(listed.Notices.Shown.Any(notice => notice.Text.StartsWith("Moved")), "never shown as done (G35)");
        Assert.AreEqual(chosen, listed.Settings.Current.StashFolder);
        await listed.Listing.ShowScope(Scope.All);
        Assert.AreEqual(Path.Combine(chosen, cat.FileName), listed.Listing.Items.Single().FilePath, "listed from the new folder");
        Assert.IsFalse(listed.Moving.Asked);

        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(null);
        Assert.IsTrue((await page.Move(null!)).Succeeded);
        Assert.HasCount(2, listed.Dialogs.FolderPickers, "a second Move… is asked");
    }

    [TestMethod]
    public async Task ANewFolderThatDoesNotOpenLeavesTheListingFailedHoldingNoStoreAndEveryChangeRefusedByName()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var chosen = Chose(listed);
        listed.BeforeMovedStashOpens = folder => File.WriteAllBytes(Path.Combine(folder, StashFolder.StoreFileName), TestStash.Bytes(7, 4096));

        var moved = await Page(listed).Move(null!);

        Assert.IsFalse(moved.Succeeded);
        Assert.AreEqual(StashState.Failed, listed.Listing.State);
        Assert.IsNull(listed.Listing.Store, "the old store let go of (G35)");
        Assert.IsEmpty(listed.Listing.Items);
        Assert.AreEqual(chosen, MovingStash.Saved(listed.Stash).StashFolder, "the settings name the new folder; the next start opens it again");
        var deleted = await listed.Deletion.Delete([cat]);
        Assert.AreEqual(new Failure(Deletion.Action, "“cat”", "your stash could not be opened"), deleted.Failure, "refused by name, never thrown");
        Assert.IsFalse((await listed.Handoff.Copy(null!, [cat])).Succeeded);
    }

    [TestMethod]
    public async Task ARecordingIsWaitedForThenItsNotificationsUndoWhileEveryNewChangeIsRefusedByName()
    {
        using var listed = await ListedStash.OpenRecording();
        listed.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, Region);
        MediaFrame[] pictures = [listed.Screen.Paint(RecordingScreen.Primary, seed: 1), listed.Screen.Paint(RecordingScreen.Primary, seed: 2)];
        listed.Screen.EachGrab = index => Outcome<MediaFrame>.Of(pictures[index % 2]);
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.ShowScope(Scope.All);
        var chosen = Chose(listed);
        var recording = listed.RecordClip.StartOrStop();
        await listed.RecordFromTheWait();
        listed.StepRecording(210);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the recording before moving your stash…");

        await listed.Screenshot.Take();
        await listed.Import.Drop(ListedStash.Contents([listed.Stash.Source("dropped.png", MediaFixtures.Bytes("still.png"))]));
        await listed.Editing.Open(cat, EditOrigin.Grid);
        await listed.Deletion.Delete([cat]);
        await listed.Renaming.Rename(cat, "dog");
        await listed.Organising.AddTag([cat], "funny");
        var openWith = await listed.OpenWith.Begin(null!, cat, _ => { });
        var refused = MoveReasons.InProgress;
        CollectionAssert.AreEqual(new[] { new Failure(Screenshot.Action, "the screen", refused).Sentence }, listed.Notifications.Shown.Select(notice => notice.Text).ToList(), "the screenshot refused above the tray");
        CollectionAssert.AreEqual(
            new[]
            {
                new Failure(Import.Action, "dropped.png", refused).Sentence,
                new Failure(Editing.Action, "cat", refused).Sentence,
                new Failure(Deletion.Action, "“cat”", refused).Sentence,
                new Failure(ItemRename.Action, "“cat”", refused).Sentence,
                new Failure(ItemTags.AddAction, "“cat”", refused).Sentence,
            },
            listed.Notices.Shown.Select(notice => notice.Text).ToList(),
            "each beginning refused by name with its own action (G35, State: Move)");
        Assert.AreEqual(new Failure(OpenWith.Action, "cat", refused), openWith.Failure);
        Assert.IsNull(listed.Editing.Session);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(chosen).Any(), "nothing copied while the recording runs");

        listed.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        listed.Frame.PressStop();
        await recording.WaitAsync(TimeSpan.FromSeconds(10));
        var clip = (await listed.Stash.All()).Single(item => item.Id != cat.Id);
        var card = listed.Notifications.Shown.Single(notice => notice.UndoLabel is not null);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notification before moving your stash…");
        Assert.IsFalse(moving.IsCompleted, "the capture notification's Undo still stands (G16)");

        await listed.Turns.Act(() => listed.Notifications.Undo(card));
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        var left = await listed.Stash.All();
        CollectionAssert.AreEqual(new[] { cat.Id }, left.Select(item => item.Id).ToList(), "the clip's Undo acted before the Move: only cat moved; " + string.Join(", ", left.Select(item => $"{item.Id} {item.Name}")) + $" (clip {clip.Id}) | " + string.Join(" | ", listed.Notices.Shown.Concat(listed.Notifications.Shown).Select(n => n.Text)));
        Assert.IsFalse(listed.Notifications.Shown.Any(notice => notice.Text.Contains(refused) && notice.Text.Contains(clip.Name)), "the Undo was never refused");
    }

    [TestMethod]
    public async Task AnOpenEditIsWaitedForItsCancelNoticesUndoReopensItAndTheMoveRunsOnceTheNoticeHasGone()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        var chosen = Chose(listed);
        Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the edit before moving your stash…");
        Assert.IsTrue(listed.Turns.Act(listed.Editing.Cancel));
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notice before moving your stash…");
        await listed.Turns.Act(() => listed.Notices.Undo(listed.Notices.Shown.Single()));
        Assert.IsNotNull(listed.Editing.Session, "the Cancel notice's Undo opened the edit again, not refused (G20)");
        Assert.IsFalse(moving.IsCompleted);

        Assert.IsTrue(listed.Turns.Act(listed.Editing.Cancel));
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.AreEqual(chosen, listed.Settings.Current.StashFolder);
    }

    [TestMethod]
    public async Task SaveACopyAskedOnceAMoveIsAskedIsRefusedByNameAndWritesNothingIntoTheChosenFolder()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        var chosen = Chose(listed);
        Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);
        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the edit before moving your stash…");
        var target = Path.Combine(chosen, "cat copy.png");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(target);

        var saved = await listed.ItemMenu.SaveACopy(null!, cat, _ => { });

        Assert.AreEqual(MoveReasons.InProgress, saved.Failure?.Reason, "nothing else starts from the asking (State: Move, G35)");
        Assert.IsFalse(File.Exists(target), "nothing written where the stash is about to be copied");
        Assert.IsTrue(listed.Turns.Act(listed.Editing.Cancel));
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);
    }

    [TestMethod]
    public async Task ASaveACopyStillWritingIsWaitedForAndItsFileLandsBesideTheStashNotInIt()
    {
        using var listed = await ListedStash.Open(saveACopyRetry: TimeSpan.FromSeconds(10));
        var cat = await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        var chosen = Chose(listed);
        var target = Path.Combine(chosen, "cat copy.png");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(target);
        Task<Outcome> saving;
        Task<Outcome> moving;
        // The picture held as a scanner might hold it (E3): the copy retries, and the Move is asked meanwhile.
        using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saving = listed.ItemMenu.SaveACopy(null!, cat, _ => { });
            await Handing.Eventually(() => listed.ItemMenu.SavingACopy);
            moving = listed.Moving.MoveTo(null!);
            await Handing.Eventually(() => listed.Moving.Busy == "Waiting for Save a copy before moving your stash…");
        }

        Assert.IsTrue((await saving).Succeeded);
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.AreEqual(MoveReasons.NotEmpty, moved.Failure?.Reason, "the folder the copy landed in is no longer empty, and the stash stays where it was (G30)");
        Assert.IsTrue(File.Exists(target));
        Assert.IsTrue(File.Exists(cat.FilePath), "the stash untouched");
    }

    [TestMethod]
    public async Task ASaveACopyWhosePickerIsAnsweredAfterAMoveWritesTheFinishedPictureFromItsNewPlace()
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var cat = await listed.Stash.Import("cat", still);
        await listed.Listing.ShowScope(Scope.All);
        var answer = new TaskCompletionSource<string?>();
        listed.Dialogs.SaveAnswer = () => answer.Task;
        var saving = listed.ItemMenu.SaveACopy(null!, cat, _ => { });
        Chose(listed);
        Assert.IsTrue((await listed.Moving.MoveTo(null!).WaitAsync(TimeSpan.FromSeconds(20))).Succeeded, "a picker up is no change of the stash, so the Move runs");
        var target = listed.Stash.Outside("cat copy.png");

        answer.SetResult(target);
        var saved = await saving;

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        CollectionAssert.AreEqual(still, File.ReadAllBytes(target), "the finished picture, read where the stash now keeps it (G9, G30)");
    }

    [TestMethod]
    public async Task AnOpenWithStillMakingItsCopyIsWaitedForAndBegunInTheStoreItWasAskedOf()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        using var reading = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        // The finished picture read for the copy held up, as a held file's retry holds it (E3).
        listed.Stash.Reader = (path, name, retry) =>
        {
            reading.Set();
            Assert.IsTrue(resume.Wait(TimeSpan.FromSeconds(20)), "resumed");
            return MediaDecoder.ReadPicture(path, name, retry);
        };
        var opening = listed.ItemMenu.OpenWith(null!, cat, _ => { });
        Assert.IsTrue(reading.Wait(TimeSpan.FromSeconds(10)), "the copy is being made");
        Chose(listed);
        Task<Outcome> moving;
        try
        {
            moving = listed.Moving.MoveTo(null!);
            await Handing.Eventually(() => listed.Moving.Busy == "Waiting for Open with… before moving your stash…");
        }
        finally
        {
            resume.Set();
        }

        Assert.IsTrue((await opening.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded, "begun, its dialog shown, before the switch (State: Move)");
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);
        CollectionAssert.Contains(listed.OpenWith.Watching.ToList(), cat.Id, "its session watched across the Move");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task AnEditStillOpeningOnAHeldOriginalIsWaitedForAndOpensOnTheStoreItWasAskedOf()
    {
        using var listed = await ListedStash.Open(editRetry: TimeSpan.FromSeconds(5));
        var cat = await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        Chose(listed);
        var held = HeldFiles.HeldFor(cat.FilePath, TimeSpan.FromSeconds(1));
        var opening = listed.Editing.Open(cat, EditOrigin.Grid);
        await Task.Delay(100);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the edit before moving your stash…");
        await held;
        Assert.IsTrue((await opening).Succeeded);
        var session = listed.Editing.Session!;
        Assert.AreEqual(cat.Id, session.Item.Id, "opened on the store it was asked of, which the Move has not touched");
        Assert.IsFalse(moving.IsCompleted);

        Assert.IsTrue(listed.Turns.Act(listed.Editing.Cancel));
        listed.Turns.Act(() => listed.Notices.Close(listed.Notices.Shown.Single()));
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);
    }

    [TestMethod]
    public async Task ADeleteNoticesUndoInTheSameStretchAsTheAskingActsAndTheMoveRunsAfterIt()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.ShowScope(Scope.All);
        await listed.Deletion.Delete([cat]);
        var notice = listed.Notices.Shown.Single();
        var chosen = Chose(listed);

        listed.Turns.Hold();
        var moving = listed.Moving.MoveTo(null!);
        var undoing = listed.Turns.Act(() => listed.Notices.Undo(notice));
        listed.Turns.Release();
        await undoing;
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        var back = (await listed.Stash.All()).Single();
        Assert.AreEqual(cat.Id, back.Id, "the item back, then moved (G18)");
        Assert.IsTrue(back.FilePath.StartsWith(chosen, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(listed.Notices.Shown.Any(shown => shown.Text.Contains(MoveReasons.InProgress)), "the Undo was never refused");
    }

    [TestMethod]
    public async Task ADeletesAndAnAdoptionsUndoBothActDuringTheWaitAndTheNewStoreOpensWithNothingPending()
    {
        using var listed = await ListedStash.Open();
        var dog = await listed.Stash.Import("dog");
        var still = MediaFixtures.Bytes("still.png");
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        await OpenedWith.AwaitingRewrite(listed.Stash, cat, MediaFixtures.DistinctPng(52, 6, 4));
        var adopted = await OpenedWith.Adopt(listed.Stash, cat.Id);
        await listed.Listing.ShowScope(Scope.All);
        new AdoptionNotifications(listed.Listing, listed.Notifications, listed.Editing, () => listed.Listing.Refresh()).Raise(adopted, adopted.RenderedRevision);
        await listed.Deletion.Delete([dog]);
        var chosen = Chose(listed);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notice and the Undo of a notification before moving your stash…");
        await listed.Turns.Act(() => listed.Notices.Undo(listed.Notices.Shown.Single()));
        await listed.Turns.Act(() => listed.Notifications.Undo(listed.Notifications.Shown.Single()));
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        // The window watches the moved session's copy again and classifies it once, staging it in .work for a moment.
        await Handing.Eventually(() => !listed.OpenWith.Running && Directory.GetFiles(Path.Combine(chosen, ".work")).Length == 0);
        var items = (await listed.Stash.All()).ToDictionary(item => item.Id);
        CollectionAssert.AreEquivalent(new[] { dog.Id, cat.Id }, items.Keys.ToList(), "dog back from its Delete (G18)");
        CollectionAssert.AreEqual(still, File.ReadAllBytes(items[cat.Id].FilePath), "cat's previous original back from its adoption (G27)");
        Assert.IsTrue(items.Values.All(item => item.FilePath.StartsWith(chosen, StringComparison.OrdinalIgnoreCase)));
        Assert.IsEmpty(listed.Notices.Shown.Concat(listed.Notifications.Shown).Where(notice => notice.Text.Contains("failed")).ToList());
    }

    [TestMethod]
    public async Task AnAdoptionTheWindowMadeAndItsUndoBeforeTheMoveIsNotAdoptedAgainAtTheNewFolder()
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.OpenWith.Begin(null!, cat, _ => { })).Succeeded);
        await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Single(), MediaFixtures.DistinctPng(53, 6, 4));
        await OpenedWith.Eventually(listed, () => listed.Notifications.Shown.Count == 1);
        Chose(listed);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notification before moving your stash…");
        await listed.Turns.Act(() => listed.Notifications.Undo(listed.Notifications.Shown.Single()));
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);

        // The window watches the moved session's copy again: the undone rewrite is the copy's recorded state, not a new one.
        var until = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < until)
        {
            var item = (await listed.Listing.Store!.All()).Value!.Single();
            CollectionAssert.AreEqual(still, File.ReadAllBytes(item.FilePath), "the undone picture stays undone after the Move (G27)");
            Assert.IsFalse(listed.Notifications.Shown.Any(), "nothing adopted again, nothing said (G27)");
            await Task.Delay(20);
        }
    }

    [TestMethod]
    public async Task ARewriteLandingDuringTheWaitWithItsEditOpenIsAdoptedOnceAfterTheSwitchAndSaidOnce()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.OpenWith.Begin(null!, cat, _ => { })).Succeeded);
        var copy = listed.Dialogs.OpenedWith.Single();
        Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);
        Chose(listed);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the edit before moving your stash…");
        var rewrite = MediaFixtures.DistinctPng(9, 10, 8);
        await OpenedWith.RewriteWhenFree(copy, rewrite);
        await Task.Delay(300);
        Assert.IsTrue(listed.Turns.Act(listed.Editing.Cancel));
        await Task.Delay(300);
        Assert.IsFalse(listed.Notifications.Shown.Any(), "no adoption while the Move waits, and nothing named");
        listed.Turns.Act(() => listed.Notices.Close(listed.Notices.Shown.Single()));
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);

        await Handing.Eventually(() => listed.Notifications.Shown.Count == 1);
        await Task.Delay(300);
        Assert.AreEqual("Replaced “cat” with the picture another program saved.", listed.Notifications.Shown.Single().Text, "adopted at the new folder, said once (G27)");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes((await listed.Stash.All()).Single().FilePath));
    }

    [TestMethod]
    public async Task ADragOutInProgressIsWaitedForItsDropLandingOnTheOldFiles()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.ShowScope(Scope.All);
        Chose(listed);
        var drop = new TaskCompletionSource<Outcome<bool>>();
        var dropped = new List<bool>();
        var dragging = listed.Handoff.DragOut([cat], paths =>
        {
            dropped.Add(paths.All(File.Exists));
            return drop.Task;
        }, _ => { }, () => { });

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the drag before moving your stash…");
        listed.Turns.Act(() => drop.SetResult(Outcome<bool>.Of(true)));
        Assert.IsTrue((await dragging).Succeeded);
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);

        CollectionAssert.AreEqual(new[] { true }, dropped, "the drop took the old files, still there (G9)");
        Assert.AreEqual(1, (await listed.Stash.Store!.UseHistory(cat.Id)).Value!.Count, "its use recorded before the copy, and moved with the stash (G10)");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task StashClipboardImageWhoseCommitRetriesIsWaitedForThenItsNotificationAndItsItemMoves()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(5));
        listed.Clipboard.ImageReading = Outcome<ClipboardImage?>.Of(new ClipboardImage(MediaFixtures.Bytes("transparent.png"), null));
        var chosen = Chose(listed);
        var held = HeldFiles.HeldFor(listed.Stash.Folder.StoreFile, TimeSpan.FromSeconds(1));
        var stashing = listed.StashClipboardImage.Stash();
        await Task.Delay(100);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for Stash clipboard image before moving your stash…");
        await held;
        await stashing;
        var card = listed.Notifications.Shown.Single();
        Assert.AreEqual("Undo", card.UndoLabel);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notification before moving your stash…");
        listed.Turns.Act(() => listed.Notifications.Close(card));
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);

        await listed.Listing.ShowScope(Scope.All);
        var item = listed.Listing.Items.Single();
        Assert.IsTrue(item.FilePath.StartsWith(chosen, StringComparison.OrdinalIgnoreCase) && File.Exists(item.FilePath), "the stashed image listed from the new folder (G13, G30)");
    }

    [TestMethod]
    public async Task OnceTheCopyIsStoresADragOutAndACopyAreRefusedByNameAndNothingIsDragged()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(5));
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var chosen = Chose(listed);
        var dragged = false;
        Task<Outcome> moving;
        Outcome drag;
        Outcome copy;
        // The copy's record waits on the held settings file, so the Move is Store's and its copy not yet under way.
        using (new FileStream(MovingStash.SettingsPath(listed.Stash), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            moving = listed.Moving.MoveTo(null!);
            await Handing.Eventually(() => listed.Listing.WholeStashActBegun);
            drag = await listed.Handoff.DragOut([cat], _ =>
            {
                dragged = true;
                return Task.FromResult(Outcome<bool>.Of(true));
            }, _ => { }, () => { });
            copy = await listed.Handoff.Copy(null!, [cat]);
        }

        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);
        Assert.AreEqual(MoveReasons.InProgress, drag.Failure?.Reason, "the drag out refused by name (State: Move, G35)");
        Assert.AreEqual(MoveReasons.InProgress, copy.Failure?.Reason, "and the copy");
        Assert.IsFalse(dragged, "no drag carried files the copy was about to leave behind (G9)");
        Assert.IsEmpty(listed.Clipboard.Written);
        Assert.IsTrue((await listed.Stash.All()).Single().FilePath.StartsWith(chosen, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue((await listed.Handoff.Copy(null!, [(await listed.Stash.All()).Single()])).Succeeded, "copies start again once the Move has ended");
    }

    [TestMethod]
    public async Task ASecondMoveAskedWhileTheFirstStillWritesTheClipboardAgainIsRefusedAndShowsNoPicker()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        Assert.IsTrue((await listed.Handoff.Copy(null!, [cat])).Succeeded);
        var held = new TaskCompletionSource();
        listed.Clipboard.HoldWriteAgain = held.Task;
        Chose(listed);

        var first = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Clipboard.WrittenAgain.Count == 1);
        Assert.IsFalse(listed.Moving.Asked, "the Move itself has ended; its refresh still runs");
        var asked = listed.Moving.MoveTo(null!);
        held.SetResult();
        var second = await asked.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.AreEqual(new Failure(StashMove.Action, "the stash folder", MoveReasons.InProgress), second.Failure);
        Assert.HasCount(1, listed.Dialogs.FolderPickers, "no second picker");
        Assert.IsTrue((await first.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);
        Assert.IsNull(listed.Listing.WholeStashActAsked);
    }

    private static SettingsPage Page(ListedStash listed) => listed.Page();

    /// <summary>An empty folder beside the stash, which the folder picker answers.</summary>
    private static string Chose(ListedStash listed)
    {
        var chosen = MovingStash.Chosen(listed.Stash);
        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(chosen);
        return chosen;
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    [DataRow("tag")]
    [DataRow("favorite")]
    [DataRow("card rename")]
    public async Task ATagFavoriteOrCardRenameWaitingOnAHeldStoreIsWaitedForByNameAndACopyStillStartsMeanwhile(string change)
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(5));
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var chosen = Chose(listed);
        var held = HeldFiles.HeldFor(listed.Stash.Folder.StoreFile, TimeSpan.FromSeconds(1));
        var changing = change switch
        {
            "tag" => listed.Organising.AddTag([cat], "later"),
            "favorite" => listed.Organising.SetFavorite([cat], true),
            _ => listed.CaptureNotification.Renaming.Rename(cat, "dog"),
        };
        await Task.Delay(100);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Listing.WholeStashActBegun || listed.Moving.Busy?.StartsWith("Waiting for") == true);
        var (copyBegun, busy) = (listed.Listing.WholeStashActBegun, listed.Moving.Busy);
        await held;
        Assert.IsTrue((await changing).Succeeded);
        Assert.IsTrue((await moving.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);

        Assert.IsFalse(copyBegun, "a handoff, a copy or a drag out still starts until the copy begins (State: Move)");
        Assert.AreEqual(change == "card rename" ? "Waiting for the rename before moving your stash…" : "Waiting for the tag or favorite change before moving your stash…", busy, "a busy state naming what it waits for (State: Move, G35)");
        await listed.Listing.ShowScope(Scope.All);
        var item = listed.Listing.Items.Single();
        Assert.IsTrue(item.FilePath.StartsWith(chosen, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(change switch { "tag" => item.Tags.Contains("later"), "favorite" => item.Favorite, _ => item.Name == "dog" }, "the change moved with the stash (G30)");
    }
}
