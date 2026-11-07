using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The footer's estimated size (G24; Composition: Media's estimate): Media's estimate for the live layers in the
/// finished format, the saved file's length for a still; an answer for an earlier list never shown for the live one, and
/// the size of a list a drag puts back stated again; a refusal leaving no
/// size while the output's dimensions stay known, and a read a rename moved the original from measured again where it
/// is now; never an estimate beside a Save; an estimate running when the layers
/// change again, or when a Save starts, cancelled so that the next one, or the Save, goes ahead (G26).
/// </summary>
[TestClass]
public sealed class SizeEstimateTests
{
    [TestMethod]
    public async Task TheEstimateIsMediasForTheLiveLayersAndTheSavedFilesLength()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var session = await Open(stash, cat);
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        session.Hide().Choose(HideKind.Black);
        var expected = FinishedPicture.Estimate(session.Edit.OriginalPath, session.Edit.OriginalSha256, "cat", session.Layers, session.Edit.FinishedFormat, TestStash.ShortRetry);
        Assert.IsTrue(expected.Succeeded, expected.Failure?.ToString());

        await Until(() => session.EstimatedBytes == expected.Value, "the estimate never came for the live layers");
        var saved = await session.Save();

        Assert.AreEqual(expected.Value, new FileInfo(saved.Item!.FilePath).Length);
    }

    [TestMethod]
    public async Task AnAnswerForAnEarlierListIsDroppedAndTheLiveListsShown()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var firstAsked = new SemaphoreSlim(0);
        using var firstMayAnswer = new SemaphoreSlim(0);
        SizeEstimator slowForOneLayer = (_, _, _, layers, _, _, _) =>
        {
            if (layers.Layers.Count == 1)
            {
                firstAsked.Release();
                firstMayAnswer.Wait();
            }

            return Outcome<long>.Of(1000 + layers.Layers.Count);
        };
        using var session = await Open(stash, cat, estimator: slowForOneLayer);
        session.Tool = session.Hide();
        var shown = new List<long?>();
        session.Changed += () =>
        {
            lock (shown)
            {
                shown.Add(session.EstimatedBytes);
            }
        };

        session.DragThrough((1, 1), (4, 3));
        Assert.IsTrue(await firstAsked.WaitAsync(TimeSpan.FromSeconds(10)));
        session.DragThrough((6.5f, 5.2f), (7.9f, 5.9f));
        Assert.HasCount(2, session.Layers.Layers);
        firstMayAnswer.Release();

        await Until(() => session.EstimatedBytes == 1002, "the live list's estimate never came");
        lock (shown)
        {
            Assert.DoesNotContain(1001L, shown, "the answer for the earlier list was shown");
        }
    }

    [TestMethod]
    public async Task ARefusedEstimateLeavesNoSizeWhileTheDimensionsAreKnown()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var asked = 0;
        SizeEstimator refusing = (_, _, name, _, _, _, _) =>
        {
            Interlocked.Increment(ref asked);
            return new Failure("Save the picture", name, "it would be too large");
        };
        using var session = await Open(stash, cat, estimator: refusing);
        session.Tool = session.Hide();

        session.DragThrough((1, 1), (4, 3));
        await Until(() => Volatile.Read(ref asked) >= 1 && !session.Estimating, "the estimate was never asked");

        Assert.IsNull(session.EstimatedBytes);
        Assert.AreEqual(new PixelSize(8, 6), session.OutputSize);
    }

    [TestMethod]
    public async Task NoEstimateStartsWhileASaveRuns()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var rendering = 0;
        var besideASave = 0;
        var asked = 0;
        using var renderStarted = new SemaphoreSlim(0);
        using var renderMayFinish = new SemaphoreSlim(0);
        FinishedPictureRenderer slow = (original, name, layers, format, work, retry) =>
        {
            Volatile.Write(ref rendering, 1);
            renderStarted.Release();
            renderMayFinish.Wait();
            Volatile.Write(ref rendering, 0);
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        SizeEstimator counting = (original, originalSha256, name, layers, format, retry, cancel) =>
        {
            Interlocked.Increment(ref asked);
            if (Volatile.Read(ref rendering) == 1)
            {
                Interlocked.Increment(ref besideASave);
            }

            return FinishedPicture.Estimate(original, originalSha256, name, layers, format, retry, cancel);
        };
        using var session = await Open(stash, cat, slow, estimator: counting);
        session.Tool = session.Hide();
        await Until(() => Volatile.Read(ref asked) == 1 && !session.Estimating, "the opening estimate never came");

        // The change's estimate is still in its pause when the Save starts, and would start during the render.
        session.DragThrough((1, 1), (4, 3));
        var saving = session.Save();
        Assert.IsTrue(await renderStarted.WaitAsync(TimeSpan.FromSeconds(10)));
        await Task.Delay(600);
        Assert.AreEqual(1, Volatile.Read(ref asked), "an estimate started while the Save rendered");
        renderMayFinish.Release();
        await saving;

        Assert.AreEqual(0, Volatile.Read(ref besideASave));
    }

    [TestMethod]
    public async Task ASaveCancelsTheEstimateRunningAndRendersOnceTheEstimatorLeavesTheFrameItIsOn()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var entered = new SemaphoreSlim(0);
        using var nextFrame = new SemaphoreSlim(0);
        var token = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var framesDone = 0;
        var rendererEntered = 0;
        // An estimate over a long clip, one frame at a time, heeding its token between frames as Media's does.
        SizeEstimator heedingItsToken = (_, _, name, layers, _, _, cancel) =>
        {
            if (layers.Layers.Count == 0)
            {
                return Outcome<long>.Of(1);
            }

            token.TrySetResult(cancel);
            entered.Release();
            for (var frame = 0; frame < 1000; frame++)
            {
                nextFrame.Wait();
                Interlocked.Increment(ref framesDone);
                if (cancel.IsCancellationRequested)
                {
                    return new Failure("Save the picture", name, "the estimate was cancelled");
                }
            }

            return Outcome<long>.Of(123);
        };
        FinishedPictureRenderer renderer = (path, name, layers, format, work, retry) =>
        {
            Interlocked.Increment(ref rendererEntered);
            return FinishedPicture.Render(path, name, layers, format, work, retry);
        };
        using var session = await Open(stash, cat, renderer, estimator: heedingItsToken);
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        Assert.IsTrue(await entered.WaitAsync(TimeSpan.FromSeconds(10)), "the estimate never started");

        var saving = session.Save();
        try
        {
            Assert.IsTrue((await token.Task).WaitHandle.WaitOne(TimeSpan.FromSeconds(10)), "the Save did not cancel the estimate running");
            Assert.AreEqual(0, Volatile.Read(ref rendererEntered), "the render started while the estimator held the encoder");
            nextFrame.Release();
            Assert.AreSame(saving, await Task.WhenAny(saving, Task.Delay(TimeSpan.FromSeconds(10))), "the Save waited for the rest of the clip");
        }
        finally
        {
            nextFrame.Release(1000);
        }

        var saved = await saving;
        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual((1, 1), (Volatile.Read(ref framesDone), Volatile.Read(ref rendererEntered)), "the estimator left at the frame it was on, then the render ran once");
        Assert.IsNull(session.EstimatedBytes, "a cancelled estimate leaves no size (G24)");
    }

    [TestMethod]
    public async Task ASessionGoingOnAfterASaveThatCancelledItsEstimateStatesItsSizeAgain()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var firstEntered = new SemaphoreSlim(0);
        var calls = 0;
        // The first estimate of the layers runs until it is cancelled; every later one answers at once.
        SizeEstimator firstUntilCancelled = (_, _, _, layers, _, _, cancel) =>
        {
            if (layers.Layers.Count == 0)
            {
                return Outcome<long>.Of(1);
            }

            if (Interlocked.Increment(ref calls) == 1)
            {
                firstEntered.Release();
                cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            }

            return Outcome<long>.Of(4242);
        };
        using var session = await Open(stash, cat, estimator: firstUntilCancelled);
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        Assert.IsTrue(await firstEntered.WaitAsync(TimeSpan.FromSeconds(10)));

        var continued = await session.SaveAsNewItem();

        Assert.AreEqual(EditEnding.Continued, continued.Ending, continued.Failure?.Sentence);
        await Until(() => session.EstimatedBytes == 4242, "the size was not stated again for the layers going on (G24)");
    }

    [TestMethod]
    public async Task AnEstimateRunningWhenTheLayersChangeIsCancelledAndTheLiveListIsMeasured()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var firstAsked = new SemaphoreSlim(0);
        var firstCancelled = false;
        SizeEstimator oneLayerUntilCancelled = (_, _, _, layers, _, _, cancel) =>
        {
            if (layers.Layers.Count == 1)
            {
                firstAsked.Release();
                Volatile.Write(ref firstCancelled, cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)));
            }

            return Outcome<long>.Of(1000 + layers.Layers.Count);
        };
        using var session = await Open(stash, cat, estimator: oneLayerUntilCancelled);
        session.Tool = session.Hide();
        var shown = new List<long?>();
        session.Changed += () =>
        {
            lock (shown)
            {
                shown.Add(session.EstimatedBytes);
            }
        };

        session.DragThrough((1, 1), (4, 3));
        Assert.IsTrue(await firstAsked.WaitAsync(TimeSpan.FromSeconds(10)));
        session.DragThrough((6.5f, 5.2f), (7.9f, 5.9f));

        await Until(() => session.EstimatedBytes == 1002, "the live list's estimate never came");
        Assert.IsTrue(Volatile.Read(ref firstCancelled), "the estimate for the earlier list was not cancelled");
        lock (shown)
        {
            Assert.DoesNotContain(1001L, shown, "the cancelled estimate's answer was shown");
        }
    }

    [TestMethod]
    public async Task AColourDragEndedWithoutItsLetGoStatesTheSizeOfTheLayersItPutBack()
    {
        var chosen = new Rgba(0x20, 0x40, 0x60, 0xFF);
        var dragged = new Rgba(0xE0, 0x10, 0x10, 0xFF);
        using var chosenAsked = new SemaphoreSlim(0);
        using var chosenMayAnswer = new SemaphoreSlim(0);
        // The committed colour's estimate answers only once the next drag shows another colour.
        SizeEstimator slowForTheChosenFill = (_, _, _, layers, _, _, _) =>
        {
            var fill = layers.Layers.OfType<TextLayer>().Single().Fill;
            if (fill == chosen)
            {
                chosenAsked.Release();
                chosenMayAnswer.Wait(TimeSpan.FromSeconds(10));
            }

            return Outcome<long>.Of(fill == chosen ? 4321 : 1234);
        };
        using var editing = await StillInTheEditor.Open(List(TextToolTests.Label("oh no", 8, 10, 10)), estimator: slowForTheChosenFill);
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        text.BeginColor();
        text.PreviewFill(chosen);
        Assert.IsTrue(text.LetGoFill(chosen));

        text.BeginColor();
        text.PreviewFill(dragged);
        Assert.IsTrue(await chosenAsked.WaitAsync(TimeSpan.FromSeconds(10)));
        chosenMayAnswer.Release();
        await Until(() => !session.Estimating, "the committed colour's estimate never ended");
        text.AbandonColor();

        Assert.AreEqual(chosen, ((TextLayer)session.Layers.Layers.Single()).Fill, "the drag put back the committed colour");
        await Until(() => session.EstimatedBytes == 4321, "the footer states no size for the layers the drag put back (G24)");
    }

    [TestMethod]
    [DataRow("dog")]
    [DataRow("dog cat")]
    public async Task ARenameLandingUnderTheEstimatesReadOfTheOriginalStillLeavesTheFooterStatingTheSize(string renames)
    {
        var names = renames.Split(' ');
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var calls = 0;
        // The item renamed, its file with it, after the estimate has found where the original was and before it reads
        // it: once, or away and back again, so the original is where it was by the time the failed read is judged.
        SizeEstimator renamedUnderTheRead = (path, sha256, name, layers, format, retry, cancel) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                var item = Renamed(cat, names[0]);
                var read = FinishedPicture.Estimate(path, sha256, name, layers, format, retry, cancel);
                foreach (var next in names.Skip(1))
                {
                    item = Renamed(item, next);
                }

                return read;
            }

            return FinishedPicture.Estimate(path, sha256, name, layers, format, retry, cancel);
        };
        using var session = await Open(stash, cat, estimator: renamedUnderTheRead);
        await Until(() => Volatile.Read(ref calls) >= 1 && !session.Estimating, "the estimate was never asked");
        await session.Reread();
        var expected = FinishedPicture.Estimate(session.Edit.OriginalPath, session.Edit.OriginalSha256, names[^1], session.Layers, session.Edit.FinishedFormat, TestStash.ShortRetry);
        Assert.IsTrue(expected.Succeeded, expected.Failure?.ToString());

        await Until(() => session.EstimatedBytes == expected.Value, "the footer states no size for the live layers after the rename (G24)");

        StashItem Renamed(StashItem item, string name)
        {
            var renamed = stash.Store!.Rename(item, name).GetAwaiter().GetResult();
            Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
            return renamed.Value!;
        }
    }
}
