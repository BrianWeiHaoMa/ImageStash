using System.Diagnostics;

namespace ImageStash.Tests.Support;

/// <summary>
/// G1 under E3: a read the summon makes, asked while a change on the store's thread waits on a file another program
/// briefly holds, is answered between the change's attempts, within half a second, while the change still runs; the
/// change then completes once the hold goes. The store must be opened with the product's retry,
/// <see cref="ImageStash.Files.HeldFileRetry.Duration"/>, so the hold, longer than a second, ends within it.
/// </summary>
public static class ReadsWhileAChangeWaits
{
    /// <summary>How long after the change starts the read is asked.</summary>
    public static readonly TimeSpan AskedAfter = TimeSpan.FromMilliseconds(200);

    /// <summary>How soon the read must be answered (G1's second, with the summon's own work around it).</summary>
    public static readonly TimeSpan AnsweredWithin = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the file stays held from the change's start: past one second, short of the retry.</summary>
    public static readonly TimeSpan HeldFor = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Starts <paramref name="change"/> while <paramref name="hold"/> holds the file it needs, asks <paramref name="read"/>
    /// <see cref="AskedAfter"/> later and requires it answered within <see cref="AnsweredWithin"/> while the change has not
    /// completed, lets the hold go <see cref="HeldFor"/> after the start, and answers the read's answer and the change's.
    /// </summary>
    public static async Task<(TRead Read, TChange Changed)> Asked<TChange, TRead>(IDisposable hold, Func<Task<TChange>> change, Func<Task<TRead>> read)
    {
        var clock = Stopwatch.StartNew();
        Task<TChange> changing;
        TRead answer;
        using (hold)
        {
            changing = change();
            await Task.Delay(AskedAfter);
            var asked = clock.Elapsed;
            var reading = read();
            var answeredInTime = await Task.WhenAny(reading, Task.Delay(AnsweredWithin)) == reading;
            var changingWhenAnswered = changing.IsCompleted;
            Assert.IsTrue(answeredInTime, $"the read asked at {asked.TotalMilliseconds:F0} ms is answered within {AnsweredWithin.TotalMilliseconds} ms (G1)");
            Assert.IsFalse(changingWhenAnswered, "the read is answered while the change still waits on the held file");
            answer = await reading;
            var left = HeldFor - clock.Elapsed;
            if (left > TimeSpan.Zero)
            {
                await Task.Delay(left);
            }
        }

        return (answer, await changing);
    }
}
