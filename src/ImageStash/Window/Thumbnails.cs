using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// One thumbnail: the item, the revision its finished picture was rendered for, so a picture that changes gets a new
/// key (State: the store), and the size step.
/// </summary>
public sealed record ThumbnailKey(long Id, long Revision, ThumbnailSize Size)
{
    public static ThumbnailKey Of(StashItem item, ThumbnailSize size) => new(item.Id, item.RenderedRevision, size);
}

/// <summary>A tile in view asking for its thumbnail: where the picture is, the name failures would use, and the bound in pixels.</summary>
public sealed record ThumbnailRequest(ThumbnailKey Key, string Path, string Name, int Bound);

/// <summary>
/// The grid's thumbnails (G6, R7, R8): each is the first frame of the item's finished picture, decoded by Media on a
/// pool thread, scaled to a bound and never at full size, and turned into an image on the UI thread. They are kept in
/// memory only, in one cache bounded in bytes, least recently shown leaving first; what is in view is never evicted,
/// so the cache exceeds its bound only while the tiles in view alone do. Only what the grid last asked for is decoded:
/// a tile that left view before its turn is not. A picture that cannot be shown keeps the neutral tile with a mark and
/// never throws (G35): an unreadable one, which only a change behind ImageStash's back makes of an item (E2), is marked
/// silently for good; any other failure to read it, such as a file still held after the retry, is named and is tried
/// again after <see cref="RetryFailed"/>. A tile shows its own item's picture (Product): a read during which the grid
/// gave its key another path, even for a moment, may have found another item's file where renames put it while the read
/// waited on a held one (E3), so it is dropped unnamed and read again from the path given; a picture kept from a path
/// the grid no longer gives is read again; and once the grid asks at another thumbnail size, every picture and read at
/// any other size is let go, so the size stepped back to is read again too (G6).
/// </summary>
/// <param name="toImage">Turns a decoded frame into what the grid draws; runs on the UI thread.</param>
/// <param name="onUiThread">Runs an action on the UI thread.</param>
/// <param name="release">Frees an image the cache let go of.</param>
/// <param name="failed">Told, on the UI thread, of a failure to read a picture other than its being unreadable.</param>
public sealed class Thumbnails<TImage>(
    long byteBound,
    int decoders,
    TimeSpan heldFileRetry,
    Func<MediaFrame, TImage> toImage,
    Action<Action> onUiThread,
    Action<TImage> release,
    Action<Failure> failed)
    where TImage : class
{
    private readonly object gate = new();
    private readonly Dictionary<ThumbnailKey, (TImage Image, long Bytes, LinkedListNode<ThumbnailKey> Use, string Path)> cache = [];
    private readonly LinkedList<ThumbnailKey> uses = new();
    private readonly HashSet<ThumbnailKey> marked = [];
    private readonly HashSet<ThumbnailKey> failedToRead = [];
    private readonly Dictionary<ThumbnailKey, string> decoding = [];
    private readonly HashSet<ThumbnailKey> superseded = [];
    private IReadOnlyList<ThumbnailRequest> wanted = [];
    private Dictionary<ThumbnailKey, string> wantedPaths = [];
    private HashSet<ThumbnailSize> wantedSizes = [];
    private TaskCompletionSource idle = Completed();

    /// <summary>Raised on the UI thread when a thumbnail has arrived or could not be read.</summary>
    public event Action? Arrived;

    /// <summary>The bytes of every image held.</summary>
    public long Bytes { get; private set; }

    /// <summary>
    /// The bound, on the longer edge in physical pixels, at which a picture of these facts covers a tile of the given
    /// row height once cropped to the tile's clamped ratio (G6).
    /// </summary>
    public static int Bound(MediaFacts facts, double rowHeight, double scaling)
    {
        if (facts.Width <= 0 || facts.Height <= 0)
        {
            return Math.Max(1, (int)Math.Ceiling(rowHeight * scaling));
        }

        var tileWidth = JustifiedRows.Ratio(facts) * rowHeight;
        var scale = Math.Max(tileWidth / facts.Width, rowHeight / facts.Height) * scaling;
        return Math.Max(1, (int)Math.Ceiling(Math.Max(facts.Width, facts.Height) * scale));
    }

    public TImage? Get(ThumbnailKey key)
    {
        lock (gate)
        {
            if (!cache.TryGetValue(key, out var entry))
            {
                return null;
            }

            uses.Remove(entry.Use);
            uses.AddFirst(entry.Use);
            return entry.Image;
        }
    }

    /// <summary>Whether the tile shows the mark: its picture could not be read.</summary>
    public bool CouldNotRead(ThumbnailKey key)
    {
        lock (gate)
        {
            return marked.Contains(key);
        }
    }

    /// <summary>Tries again, when next asked for, every picture that failed for a reason other than being unreadable.</summary>
    public void RetryFailed()
    {
        lock (gate)
        {
            marked.ExceptWith(failedToRead);
            failedToRead.Clear();
            Start();
        }
    }

    /// <summary>
    /// The picture of <paramref name="key"/> could not be read after all, as its animation's play found: the tile
    /// drops what it showed and shows the mark, as if its thumbnail had failed so (G35). Called on the UI thread.
    /// </summary>
    public void ReadFailed(ThumbnailKey key, Failure failure)
    {
        lock (gate)
        {
            Drop(key);
            Mark(key, failure);
        }

        Told(failure);
        Arrived?.Invoke();
    }

    /// <summary>The tiles in view, in the order they should arrive; replaces what was asked for before.</summary>
    public void Want(IReadOnlyList<ThumbnailRequest> requests)
    {
        lock (gate)
        {
            wanted = requests;
            wantedPaths = [];
            var sizes = requests.Select(request => request.Key.Size).ToHashSet();
            if (sizes.Count > 0 && !sizes.SetEquals(wantedSizes))
            {
                wantedSizes = sizes;
                LetGoOfOtherSizes();
            }

            foreach (var request in requests)
            {
                wantedPaths[request.Key] = request.Path;
                if (cache.TryGetValue(request.Key, out var entry) && !SamePath(entry.Path, request.Path))
                {
                    Drop(request.Key);
                }

                if (decoding.TryGetValue(request.Key, out var reading) && !SamePath(reading, request.Path))
                {
                    superseded.Add(request.Key);
                }
            }

            Start();
        }
    }

    /// <summary>
    /// The stash these pictures came from is being replaced by an import (G36, G6): nothing is asked for any more, every
    /// picture kept and every mark is let go, and every read under way is dropped unkept and unmarked when it arrives,
    /// since the imported store may give its key to another item's picture. <see cref="Idle"/> completes once those
    /// reads have closed their files.
    /// </summary>
    public void LetGo()
    {
        lock (gate)
        {
            wanted = [];
            wantedPaths = [];
            foreach (var key in cache.Keys.ToList())
            {
                Drop(key);
            }

            marked.Clear();
            failedToRead.Clear();
            superseded.UnionWith(decoding.Keys);
            Start();
        }
    }

    /// <summary>Completes once nothing asked for is waiting, being decoded, or still being told of its arrival.</summary>
    public Task Idle()
    {
        lock (gate)
        {
            return idle.Task;
        }
    }

    private void Start()
    {
        foreach (var request in wanted)
        {
            if (decoding.Count >= decoders)
            {
                break;
            }

            if (cache.ContainsKey(request.Key) || marked.Contains(request.Key) || !decoding.TryAdd(request.Key, request.Path))
            {
                continue;
            }

            if (idle.Task.IsCompleted)
            {
                idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            Task.Run(() => Decode(request));
        }

        if (decoding.Count == 0)
        {
            idle.TrySetResult();
        }
    }

    private void Decode(ThumbnailRequest request)
    {
        MediaFrame? frame = null;
        Failure? failure = null;
        try
        {
            var opened = MediaDecoder.Open(request.Path, request.Name, heldFileRetry);
            if (opened.Succeeded)
            {
                using var decoder = opened.Value!;
                var next = decoder.NextFrame(request.Bound);
                (frame, failure) = (next.Value, next.Failure);
            }
            else
            {
                failure = opened.Failure;
            }
        }
        finally
        {
            // Whatever happened, the key stops decoding, so the tile is marked rather than waiting for ever.
            onUiThread(() => Arrive(request, frame, failure));
        }
    }

    private void Arrive(ThumbnailRequest request, MediaFrame? frame, Failure? failure)
    {
        var key = request.Key;
        var image = frame is null ? null : toImage(frame);
        bool stale;
        lock (gate)
        {
            // The grid gave this key another path, or asked at another size, while it was read: perhaps another item's
            // file was read, so it is neither kept nor marked, and read again from the path given, when the grid asks
            // for it, once the key stops decoding below.
            stale = superseded.Remove(key);
            if (stale)
            {
                if (image is not null)
                {
                    release(image);
                }
            }
            else if (image is null)
            {
                Mark(key, failure);
            }
            else
            {
                cache[key] = (image, (long)frame!.Width * frame.Height * 4, uses.AddFirst(key), request.Path);
                Bytes += cache[key].Bytes;
                Evict();
            }
        }

        try
        {
            if (!stale)
            {
                Told(failure);
                Arrived?.Invoke();
            }
        }
        finally
        {
            // The key stops decoding only once its failure is named and its tile told, so Idle() never completes first.
            lock (gate)
            {
                decoding.Remove(key);
                Start();
            }
        }
    }

    /// <summary>
    /// Marks a picture that could not be read: an unreadable one for good, any other until <see cref="RetryFailed"/>.
    /// Called holding the gate.
    /// </summary>
    private void Mark(ThumbnailKey key, Failure? failure)
    {
        marked.Add(key);
        if (Named(failure))
        {
            failedToRead.Add(key);
        }
    }

    /// <summary>Names a failure to read other than the picture's being unreadable, which only a change behind ImageStash's back makes (E2).</summary>
    private void Told(Failure? failure)
    {
        if (Named(failure))
        {
            failed(failure!);
        }
    }

    private static bool Named(Failure? failure) => failure is not null && !MediaDecoder.IsUnreadable(failure);

    private static bool SamePath(string kept, string given) => string.Equals(kept, given, StringComparison.Ordinal);

    /// <summary>
    /// Lets go of every picture kept, and drops every read under way, at a size the grid no longer asks for: one may
    /// hold another item's picture that renames put under its read (E3), and a tile shows it no later than the next
    /// thumbnail size, the size stepped back to included (G6), so each size is read again once the grid asks for it.
    /// Called holding the gate.
    /// </summary>
    private void LetGoOfOtherSizes()
    {
        foreach (var key in cache.Keys.Where(key => !wantedSizes.Contains(key.Size)).ToList())
        {
            Drop(key);
        }

        superseded.UnionWith(decoding.Keys.Where(key => !wantedSizes.Contains(key.Size)));
    }

    /// <summary>Lets go of the picture held for <paramref name="key"/>, if any. Called holding the gate.</summary>
    private void Drop(ThumbnailKey key)
    {
        if (cache.Remove(key, out var entry))
        {
            uses.Remove(entry.Use);
            Bytes -= entry.Bytes;
            release(entry.Image);
        }
    }

    private void Evict()
    {
        for (var use = uses.Last; use is not null && Bytes > byteBound;)
        {
            var older = use.Previous;
            if (!wantedPaths.ContainsKey(use.Value))
            {
                var entry = cache[use.Value];
                cache.Remove(use.Value);
                uses.Remove(use);
                Bytes -= entry.Bytes;
                release(entry.Image);
            }

            use = older;
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}
