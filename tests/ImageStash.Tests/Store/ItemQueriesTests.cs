using System.Diagnostics;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// G4's orders, G10's use rows, one item and its use history (G19), what Edit opens on (F3, State: A live failure, G35),
/// items confirmed by identity before a handoff (G8, G35), and E4's 5,000 items answered well within a second.
/// </summary>
[TestClass]
public sealed class ItemQueriesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AllListsNewestAddedFirstAndEqualTimesByLaterIdentity()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var first = await stash.Import("first");
        stash.Clock.Now = stash.Clock.Now.AddSeconds(1);
        var second = await stash.Import("second");
        var sameMillisecond = await stash.Import("same millisecond");
        stash.Clock.Now = stash.Clock.Now.AddSeconds(1);
        var third = await stash.Import("third");

        var all = await stash.All();

        CollectionAssert.AreEqual(new[] { third.Id, sameMillisecond.Id, second.Id, first.Id }, all.Select(i => i.Id).ToList());
    }

    [TestMethod]
    public async Task FavoritesListsNewestAddedFirstAndEqualTimesByLaterIdentity()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var first = await stash.Import("first");
        stash.Clock.Now = stash.Clock.Now.AddSeconds(1);
        var second = await stash.Import("second");
        var sameMillisecond = await stash.Import("same millisecond");
        var notFavorite = await stash.Import("not a favorite");
        stash.Clock.Now = stash.Clock.Now.AddSeconds(1);
        var markedLater = await stash.Import("marked later");
        // Favorited in an order of their own, so the flag's time cannot be what orders them.
        await store.SetFavorite([second], true);
        await store.SetFavorite([first, sameMillisecond, markedLater], true);
        await store.Delete([markedLater.Id]);
        await store.RecordUses([first], stash.Clock.Now);

        var favorites = await store.Favorites();

        Assert.IsTrue(favorites.Succeeded, favorites.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { sameMillisecond.Id, second.Id, first.Id }, favorites.Value!.Select(i => i.Id).ToList());
        Assert.IsTrue(favorites.Value!.All(i => i.Favorite));
        Assert.IsFalse((await stash.All()).Single(i => i.Id == notFavorite.Id).Favorite);
    }

    [TestMethod]
    public async Task RecentListsByLatestUseAndLeavesOutUnusedItems()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        await stash.Import("never used");
        var at = stash.Clock.Now;
        await store.RecordUses([a], at.AddSeconds(1));
        await store.RecordUses([b], at.AddSeconds(2));
        await store.RecordUses([a], at.AddSeconds(3));

        var recent = await store.Recent();

        CollectionAssert.AreEqual(new[] { a.Id, b.Id }, recent.Value!.Select(i => i.Id).ToList());
    }

    [TestMethod]
    public async Task RecentBreaksEqualLatestUsesByLaterIdentity()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        await store.RecordUses([b, a], stash.Clock.Now.AddSeconds(1));

        var recent = await store.Recent();

        CollectionAssert.AreEqual(new[] { b.Id, a.Id }, recent.Value!.Select(i => i.Id).ToList());
    }

    [TestMethod]
    public async Task AUseIsOneRowPerItemWithItsTimeAndChangesNothingElse()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        stash.Close();
        var before = ItemRows(stash);
        await stash.OpenCleanly();
        var at = new DateTimeOffset(2026, 10, 16, 13, 14, 15, 678, TimeSpan.Zero);

        var recorded = await stash.Store!.RecordUses([a, b], at);

        Assert.IsTrue(recorded.Succeeded, recorded.Failure?.ToString());
        stash.Close();
        var uses = stash.SqlQuery("SELECT item_id, used_at FROM uses ORDER BY item_id", r => (r.GetInt64(0), r.GetInt64(1)));
        CollectionAssert.AreEqual(new[] { (a.Id, at.ToUnixTimeMilliseconds()), (b.Id, at.ToUnixTimeMilliseconds()) }, uses);
        CollectionAssert.AreEqual(before, ItemRows(stash));
    }

    [TestMethod]
    public async Task AUseGoesOnEveryRowThatExistsADeletedOneTooAndTheItemsGoneAreAnsweredOnceThoughTheTransactionIsRetried()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(5) };
        var store = await stash.OpenCleanly();
        var listed = await stash.Import("listed");
        var deleted = await stash.Import("deleted");
        var waiting = await stash.Import("waiting");
        var gone = await stash.Import("gone");
        await store.Delete([deleted.Id]);
        await store.Remove([gone.Id]);
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", waiting.Id),
            ("$steps", PendingStep.Encode([PendingStep.Place("staged.png", "elsewhere.png")])));

        using var reader = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={stash.Folder.StoreFile};Pooling=False");
        reader.Open();
        using var hold = reader.CreateCommand();
        hold.CommandText = "BEGIN; SELECT COUNT(*) FROM items";
        hold.ExecuteScalar();
        var at = new DateTimeOffset(2026, 10, 16, 13, 14, 15, 678, TimeSpan.FromHours(5));
        var recording = store.RecordUses([gone, listed, listed, deleted, waiting], at);
        try
        {
            // A read lock lets the inserts in and keeps the commit out past SQLite's own wait, so the transaction is retried whole.
            await Task.Delay(2500);
            Assert.IsFalse(recording.IsCompleted, "the reader still holds the commit");
        }
        finally
        {
            hold.CommandText = "ROLLBACK";
            hold.ExecuteNonQuery();
        }

        var result = await recording;
        Assert.IsTrue(result.Succeeded, result.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { gone.Id }, result.Value!.Select(item => item.Id).ToList(), "the one gone, once");
        foreach (var item in new[] { listed, deleted, waiting })
        {
            var uses = (await store.UseHistory(item.Id)).Value!;
            Assert.AreEqual(1, uses.Count, item.Name);
            Assert.AreEqual(at.ToUniversalTime(), uses.Latest);
        }
    }

    [TestMethod]
    public async Task AnItemNamedTwiceInOneUseGetsOneUse()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");

        await store.RecordUses([a, a], stash.Clock.Now);

        stash.Close();
        Assert.HasCount(1, stash.SqlQuery("SELECT item_id FROM uses", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AUseHistoryCountsOnlyItsItemsUsesAndNamesTheLatestWhetherListedOrMarked()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var used = await stash.Import("used");
        var other = await stash.Import("other");
        var never = await stash.Import("never");
        var latest = stash.Clock.Now.AddMinutes(5);
        await store.RecordUses([used], stash.Clock.Now);
        await store.RecordUses([used, other], latest);
        await store.RecordUses([used], stash.Clock.Now.AddMinutes(1));

        var history = await store.UseHistory(used.Id);
        await store.Delete([used.Id]);
        var marked = await store.UseHistory(used.Id);

        Assert.IsTrue(history.Succeeded, history.Failure?.ToString());
        Assert.AreEqual(new UseHistory(3, latest), history.Value);
        Assert.AreEqual(TimeSpan.Zero, history.Value!.Latest!.Value.Offset, "in UTC");
        Assert.AreEqual(history.Value, marked.Value);
        Assert.AreEqual(new UseHistory(0, null), (await store.UseHistory(never.Id)).Value);
        Assert.AreEqual(new UseHistory(1, latest), (await store.UseHistory(other.Id)).Value);
    }

    [TestMethod]
    public async Task AnItemIsReadByItsIdentityAsAllListsItAndNotOnceTheStashNoLongerListsIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, listed) = await stash.GoneMarkedAndListed();
        await store.AddTag([listed], "mood");
        await store.SetFavorite([listed], true);
        var waiting = await stash.Import("waiting");
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", waiting.Id), ("$steps", PendingStep.Encode([PendingStep.Place("staged.png", "elsewhere.png")])));

        var read = await store.Item(listed.Id);

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        TestStash.AssertSameItem((await stash.All()).Single(), read.Value);
        Assert.AreEqual(("mood", true), (read.Value!.Tags.Single(), read.Value.Favorite));
        Assert.IsNull((await store.Item(gone.Id)).Value);
        Assert.IsNull((await store.Item(marked.Id)).Value);
        Assert.IsNull((await store.Item(waiting.Id)).Value);
        Assert.IsTrue((await store.Item(9999)).Succeeded);
        Assert.IsNull((await store.Item(9999)).Value);
    }

    [TestMethod]
    public async Task EditOpensOnAnUneditedItemsOneFileWithNoLayersInTheFormatF3Gives()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var still = await stash.Import("still", extension: ".webp");

        var opened = await store.ForEdit(still.Id);

        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        var edit = opened.Value!;
        TestStash.AssertSameItem((await store.Item(still.Id)).Value!, edit.Item);
        Assert.AreEqual(still.FilePath, edit.OriginalPath);
        Assert.AreEqual(still.Facts, edit.OriginalFacts);
        Assert.AreEqual((LayerList.Empty, LayerList.Empty), (edit.Layers, edit.RenderedLayers));
        Assert.AreEqual(ImageFormat.Png, edit.FinishedFormat);
    }

    [TestMethod]
    public async Task EditOpensOnAnEditedItemsOriginalWithItsRecordTheCommittedLayersAndTheLayersThePictureOnDiskCameFrom()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var clip = await stash.Import("clip", extension: ".webp");
        stash.Close();
        stash.MakeEdited(clip, TestStash.Bytes(1));
        var earlier = new LayerList([new CropLayer(new(0, 0, 4, 4))]);
        var committed = new LayerList([new CropLayer(new(0, 0, 2, 2)), new HideLayer(new(0, 0, 1, 1), HideKind.Black, null)]);
        // An animated WebP trimmed to one frame: its render is a GIF of one image, which reads back as a still.
        stash.Sql(
            """
            UPDATE items SET animated = 1, frame_count = 3, duration_ms = 600, layers = $layers, layer_revision = 1, rendered_revision = 1,
                finished_format = 'Gif', finished_width = 4, finished_height = 4, finished_animated = 0, finished_frame_count = 1, finished_duration_ms = 0
            WHERE id = $id
            """,
            ("$id", clip.Id), ("$layers", committed.ToJson()));
        var store = await stash.OpenCleanly();

        var edit = (await store.ForEdit(clip.Id)).Value!;

        Assert.AreEqual(stash.Folder.InOriginals("clip.webp"), edit.OriginalPath);
        Assert.AreEqual(new MediaFacts(ImageFormat.WebP, 8, 6, true, 3, 600), edit.OriginalFacts);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 4, 4, false, 1, 0), edit.Item.Facts);
        Assert.AreEqual((committed, committed), (edit.Layers, edit.RenderedLayers));
        Assert.AreEqual(ImageFormat.Gif, edit.FinishedFormat, "derived from the original's record, not the render's");

        // A Save committed and not yet rendered: the picture on disk is still the earlier list's.
        stash.SqlWhileOpen("UPDATE items SET previous_layers = $previous, layer_revision = 2 WHERE id = $id", ("$id", clip.Id), ("$previous", earlier.ToJson()));
        var behind = (await store.ForEdit(clip.Id)).Value!;

        Assert.AreEqual((committed, earlier), (behind.Layers, behind.RenderedLayers));
    }

    [TestMethod]
    public async Task ALayerListTheStoreCannotReadRefusesEditByNameAndTheItemStaysListed()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var damaged = await stash.Import("damaged");
        stash.SqlWhileOpen("UPDATE items SET layers = '{' WHERE id = $id", ("$id", damaged.Id));

        var edit = await store.ForEdit(damaged.Id);

        Assert.IsFalse(edit.Succeeded);
        Assert.StartsWith("Read the item's layers failed: damaged, ", edit.Failure!.Sentence);
        // Listed as it was, its list read as text only: one that is not the empty list's makes it an edited item.
        TestStash.AssertSameItem(damaged with { Edited = true }, (await store.Item(damaged.Id)).Value);
        Assert.AreEqual(damaged.Id, (await stash.All()).Single().Id);

        // The list the picture on disk came from is read only while the picture is that list's.
        stash.SqlWhileOpen("UPDATE items SET layers = '[]', previous_layers = 'not a list' WHERE id = $id", ("$id", damaged.Id));
        Assert.IsTrue((await store.ForEdit(damaged.Id)).Succeeded);
        stash.SqlWhileOpen("UPDATE items SET layer_revision = 1 WHERE id = $id", ("$id", damaged.Id));
        Assert.StartsWith("Read the item's layers failed: damaged, ", (await store.ForEdit(damaged.Id)).Failure!.Sentence);
    }

    [TestMethod]
    public async Task EditOpensOnNothingTheStashDoesNotList()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, listed) = await stash.GoneMarkedAndListed();
        var waiting = await stash.Import("waiting");
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", waiting.Id), ("$steps", PendingStep.Encode([PendingStep.Place("staged.png", "elsewhere.png")])));

        foreach (var id in new[] { gone.Id, marked.Id, waiting.Id, 9999 })
        {
            var edit = await store.ForEdit(id);
            Assert.IsTrue(edit.Succeeded, edit.Failure?.ToString());
            Assert.IsNull(edit.Value, $"item {id}");
        }

        Assert.AreEqual(listed.Id, (await store.ForEdit(listed.Id)).Value?.Item.Id);
    }

    [TestMethod]
    public async Task ItemsAreConfirmedInOrderAsTheStashListsThemNowAndTheOnesItNoLongerListsAreNamed()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, listed) = await stash.GoneMarkedAndListed();
        var waiting = await stash.Import("waiting");
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", waiting.Id), ("$steps", PendingStep.Encode([PendingStep.Place("staged.png", "elsewhere.png")])));
        await store.AddTag([listed], "mood");

        var confirmed = (await store.Confirm([listed, gone with { Name = "gone as shown" }, marked with { Name = "as shown" }, waiting with { Name = "as shown" }, listed with { Name = "as shown" }], "Copy")).Items;

        Assert.HasCount(5, confirmed);
        TestStash.AssertSameItem((await store.Item(listed.Id)).Value!, confirmed[0].Value);
        Assert.AreEqual("mood", confirmed[0].Value!.Tags.Single(), "as the stash lists it now, not as handed in");
        CollectionAssert.AreEqual(
            new[] { "Copy failed: gone as shown, it is no longer in the stash", "Copy failed: marked, it is no longer in the stash", "Copy failed: waiting, it is no longer in the stash" },
            confirmed.Skip(1).Take(3).Select(outcome => outcome.Failure?.Sentence).ToList(),
            "a row that is gone named as handed in, the others as the stash names them");
        Assert.AreSame(confirmed[0].Value, confirmed[4].Value, "a repeated identity is answered as its first");
    }

    [TestMethod]
    public async Task WhileTheStoreIsHeldAnItemReadSinceTheLatestCommitIsAnsweredAsHandedInAndAnOlderOneIsNamedWithWhyInOneWait()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        var older = (await store.Item(a.Id)).Value!;
        await store.AddTag([b], "mood");
        var newer = (await store.Item(b.Id)).Value!;

        TimeSpan oneRead, confirming;
        IReadOnlyList<Outcome<StashItem>> confirmed;
        using (stash.HoldStore())
        {
            var clock = Stopwatch.StartNew();
            Assert.IsFalse((await store.Item(b.Id)).Succeeded);
            oneRead = clock.Elapsed;
            clock.Restart();
            confirmed = (await store.Confirm([newer, older], "Copy")).Items;
            confirming = clock.Elapsed;
        }

        Assert.AreSame(newer, confirmed[0].Value, "nothing has committed since it was read");
        Assert.AreEqual($"Copy failed: a, the stash could not be read ({StashFolder.StoreFileName}: {HeldFileRetry.HeldReason})", confirmed[1].Failure?.Sentence);
        Assert.IsLessThan(oneRead.TotalMilliseconds * 1.6, confirming.TotalMilliseconds, $"one read {oneRead.TotalMilliseconds:F0} ms, the confirmation {confirming.TotalMilliseconds:F0} ms");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(true, DisplayName = "read while its rename waits")]
    [DataRow(false, DisplayName = "read after its rename was refused, the rename then asked again")]
    public async Task WhileTheStoreIsHeldAnItemWhoseFileItsRenameMovedSinceItWasReadIsNamedNeverHandedInUnderItsOldPath(bool readWhileWaiting)
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1) };
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(30));
        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);

        StashItem? listed = null;
        Outcome<StashItem> confirmed;
        var picture = TestStash.Hold(cat.FilePath);
        if (!readWhileWaiting)
        {
            Assert.IsFalse((await store.Rename(cat, "dog")).Succeeded, "its step refused while held: a live failure");
            listed = (await store.Recent()).Value!.Single();
        }

        var renaming = store.Rename(cat, "dog");
        await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
        if (readWhileWaiting)
        {
            // The summon's listing, answered while the rename waits on the held picture (G1), lists it where it still is.
            listed = (await store.Recent()).Value!.Single();
        }

        Assert.IsFalse(renaming.IsCompleted);
        using (stash.HoldStore())
        {
            // The picture's hold goes once the store is held: the rename moves the file and its clearing is refused.
            picture.Dispose();
            Assert.IsFalse((await renaming).Succeeded);
            using var confirmation = await store.Confirm([listed!], "Copy");
            confirmed = confirmation.Items.Single();
        }

        Assert.IsTrue(File.Exists(stash.Folder.InRoot("dog.png")));
        Assert.IsFalse(File.Exists(cat.FilePath));
        Assert.AreEqual(cat.FilePath, listed!.FilePath, "listed where its file was");
        Assert.StartsWith("the stash could not be read", confirmed.Failure?.Reason ?? $"handed {confirmed.Value?.FilePath}", "named, never the path its rename moved (G8, G35)");
        using var later = await store.Confirm([listed], "Copy");
        Assert.AreEqual(stash.Folder.InRoot("dog.png"), later.Items.Single().Value?.FilePath, "once the store answers, where its file is now");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task WhileTheStoreIsHeldAnItemReadWhileItsOpenWithAdoptionWaitedIsNamedOnceTheAdoptionEmptiedItsPath()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1) };
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.Bytes("photo.jpg"));
        var store = stash.Store!;
        var picture = TestStash.Hold(cat.FilePath);
        var adopting = store.AdoptRewrite(cat.Id);
        await EditorSessions.Until(() => EditedItems.Row(stash, cat.Id).Steps.Count > 0, "the adoption never committed");
        // Read while the adoption's step waits on the held picture, as the summon's listing is (G1): where it still is.
        var read = (await store.Item(cat.Id)).Value!;
        Outcome<StashItem> adopted;
        Outcome<StashItem> confirmed;
        using (stash.HoldStore())
        {
            // The picture's hold goes once the store is held: the adoption moves it out of the root, then its last
            // transaction is refused.
            picture.Dispose();
            adopted = await adopting;
            using var confirmation = await store.Confirm([read], "Copy");
            confirmed = confirmation.Items.Single();
        }

        Assert.IsFalse(adopted.Succeeded);
        Assert.IsFalse(File.Exists(read.FilePath), "the adoption emptied the path the item was read at");
        Assert.StartsWith("the stash could not be read", confirmed.Failure?.Reason ?? $"handed {confirmed.Value?.FilePath}", "named, never the path the adoption emptied (G8, G35)");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("staging", DisplayName = "after an import's staging")]
    [DataRow("classifying", DisplayName = "after another item's Open with… copy is classified unchanged")]
    [DataRow("another's rename", DisplayName = "after another item's rename moved its file, its clearing refused")]
    [DataRow("own refused rename", DisplayName = "its own rename refused while held, then an import's staging")]
    public async Task WhileTheStoreIsHeldAnItemReadBeforeAFileStepThatMovedNoneOfItsFilesIsStillHandedIn(string step)
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1) };
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        Assert.IsTrue((await store.BeginOpenWith(b)).Succeeded);
        if (step == "own refused rename")
        {
            using (TestStash.Hold(a.FilePath))
            {
                Assert.IsFalse((await store.Rename(a, "c")).Succeeded, "its step refused while held: a live failure, its steps kept");
            }
        }

        var read = (await store.Item(a.Id)).Value!;
        Outcome<StashItem> confirmed;
        var picture = step == "another's rename" ? TestStash.Hold(b.FilePath) : null;
        var renaming = picture is null ? null : store.Rename(b, "d");
        if (renaming is not null)
        {
            await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
            // Read after the waiting rename's commit, as the summon's listing is answered during its wait (G1).
            read = (await store.Item(a.Id)).Value!;
        }
        else
        {
            // A file step that commits nothing and leaves every file of the root and originals\ where it was.
            Assert.IsTrue(step == "classifying"
                ? (await store.ClassifyOpenWith(b.Id)).Value == OpenWithCopy.Unchanged
                : (await store.Stage(stash.Source("dropped.png", TestStash.Bytes(40)))).Succeeded);
        }

        using (stash.HoldStore())
        {
            if (renaming is not null)
            {
                // The rename moves b's file once its hold goes, and its clearing is refused while the store is held.
                picture!.Dispose();
                Assert.IsFalse((await renaming).Succeeded);
                Assert.IsTrue(File.Exists(stash.Folder.InRoot("d.png")));
            }

            using var confirmation = await store.Confirm([read], "Copy");
            confirmed = confirmation.Items.Single();
        }

        Assert.AreSame(read, confirmed.Value, $"handed in, its picture where it was read (G8, G35): {confirmed.Failure?.Sentence}");
        Assert.IsTrue(File.Exists(read.FilePath));
    }

    [TestMethod]
    public async Task AStoreThatCannotBeReadForAnotherReasonIsNamedAsItSaysAndAnItemReadSinceTheLatestCommitStillGoes()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        var older = (await store.Item(a.Id)).Value!;
        await store.SetFavorite([b], true);
        var newer = (await store.Item(b.Id)).Value!;
        stash.SqlWhileOpen("ALTER TABLE items RENAME TO items_elsewhere");

        var confirmed = (await store.Confirm([older, newer], "Copy")).Items;

        stash.SqlWhileOpen("ALTER TABLE items_elsewhere RENAME TO items");
        Assert.StartsWith("Copy failed: a, the stash could not be read (stash.db: ", confirmed[0].Failure?.Sentence);
        Assert.DoesNotContain(HeldFileRetry.HeldReason, confirmed[0].Failure!.Sentence, "not a held store: the store's own reason");
        Assert.AreSame(newer, confirmed[1].Value);
    }

    [TestMethod]
    public async Task FiveThousandItemsAreListedAndSearchedWellWithinASecond()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Close();
        // Inserted directly, as an earlier session's imports would have left them; only the queries are timed.
        stash.Sql($"""
            WITH RECURSIVE n(k) AS (SELECT 1 UNION ALL SELECT k + 1 FROM n WHERE k < 5000)
            INSERT INTO items (name, added_at, format, width, height, animated, frame_count, duration_ms, original_file, finished_file, original_sha256)
            SELECT 'reaction ' || k, {stash.Clock.Now.ToUnixTimeMilliseconds()} + k, 'Png', 8, 6, 0, 1, 0, 'reaction ' || k || '.png', 'reaction ' || k || '.png', randomblob(32) FROM n;
            INSERT INTO tags (item_id, tag) SELECT id, 'tag' || (id % 50) FROM items;
            INSERT INTO uses (item_id, used_at) SELECT id, {stash.Clock.Now.ToUnixTimeMilliseconds()} + id FROM items WHERE id % 2 = 0;
            UPDATE items SET favorite = 1 WHERE id % 5 = 0;
            """);
        var store = await stash.OpenCleanly();
        await store.All();

        var (all, allTime) = await Timed(store.All);
        var (recent, recentTime) = await Timed(store.Recent);
        var (search, searchTime) = await Timed(() => store.Search("reaction #tag4"));
        var (favorites, favoritesTime) = await Timed(store.Favorites);
        var (counts, countsTime) = await Timed(store.TagCounts);
        var (item, itemTime) = await Timed(() => store.Item(4000));
        var (history, historyTime) = await Timed(() => store.UseHistory(4000));

        TestContext.WriteLine($"5,000 items: All {allTime.TotalMilliseconds:F0} ms, Recent {recentTime.TotalMilliseconds:F0} ms, search {searchTime.TotalMilliseconds:F0} ms, Favorites {favoritesTime.TotalMilliseconds:F0} ms, tag counts {countsTime.TotalMilliseconds:F0} ms, one item {itemTime.TotalMilliseconds:F0} ms, its uses {historyTime.TotalMilliseconds:F0} ms");
        Assert.HasCount(5000, all.Value!);
        Assert.HasCount(2500, recent.Value!);
        Assert.HasCount(1100, search.Value!, "tag4 and tag40 to tag49 contain the word, each carried by 100 items");
        Assert.HasCount(1000, favorites.Value!);
        Assert.HasCount(50, counts.Value!);
        Assert.IsTrue(counts.Value!.All(c => c.Items == 100));
        CollectionAssert.AreEqual(new[] { "tag0", "tag1", "tag10" }, counts.Value!.Take(3).Select(c => c.Tag).ToList(), "equal counts by spelling");
        Assert.IsLessThan(500, allTime.TotalMilliseconds);
        Assert.IsLessThan(500, recentTime.TotalMilliseconds);
        Assert.IsLessThan(500, searchTime.TotalMilliseconds);
        Assert.IsLessThan(500, favoritesTime.TotalMilliseconds);
        Assert.IsLessThan(500, countsTime.TotalMilliseconds);
        Assert.AreEqual("reaction 4000", item.Value!.Name);
        Assert.AreEqual(1, history.Value!.Count);
        Assert.IsLessThan(100, itemTime.TotalMilliseconds, "one item's own rows, not the stash's");
        Assert.IsLessThan(100, historyTime.TotalMilliseconds, "one item's own uses, not the stash's");
    }

    private static async Task<(T Result, TimeSpan Time)> Timed<T>(Func<Task<T>> query)
    {
        var clock = Stopwatch.StartNew();
        var result = await query();
        return (result, clock.Elapsed);
    }

    private static List<string> ItemRows(TestStash stash) =>
        stash.SqlQuery("SELECT * FROM items ORDER BY id", r => string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => Convert.ToString(r.GetValue(i)))));
}
