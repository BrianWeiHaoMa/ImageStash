using ImageStash.Media;
using ImageStash.Store;
using SQLitePCL;

namespace ImageStash.Tests.Support;

/// <summary>
/// The statements SQLite runs for one store query over a stash a check filled with SQL, counted by a trace on the store's
/// own connection, opened as the stash opens it (E4). The query runs as its <see cref="StashStore"/> call runs it, whole on
/// the store's thread, since only there is the connection reachable.
/// </summary>
internal static class StoreStatements
{
    /// <summary>
    /// The result of <paramref name="query"/> and the statements it ran, over a store at this build's version filled by the
    /// SQL <paramref name="fill"/> makes of the stash clock's time in UTC Unix milliseconds.
    /// </summary>
    public static async Task<(T Result, int Statements)> Count<T>(Func<long, string> fill, Func<StoreContext, T> query, Action<string>? trace = null)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Close();
        stash.Sql(fill(stash.Clock.Now.ToUnixTimeMilliseconds()));
        using var thread = new StoreThread();
        var opened = await thread.Run(() => StoreOpen.Open(stash.Folder, stash.RecycleBin, stash.Retry, stash.Clock, FinishedPicture.Render, null, MediaDecoder.ReadPicture, thread));
        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        var store = opened.Value.Store;
        try
        {
            return await thread.Read(() =>
            {
                var run = 0;
                raw.sqlite3_trace(store.Connection.Handle, (strdelegate_trace)((_, sql) =>
                {
                    run++;
                    trace?.Invoke(sql);
                }), null);
                return (query(store), run);
            });
        }
        finally
        {
            await thread.Run(() => { store.Connection.Dispose(); return true; });
        }
    }
}
