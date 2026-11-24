using ImageStash.Window;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The real clipboard's files read at the moment the window's listing first lists an item it did not list when this was
/// made, as a capture's end lists the stash again: what the clipboard already held before the window listed the capture
/// (G14, G15). Made and read on the UI thread; it stops watching once disposed.
/// </summary>
public sealed class ClipboardWhenListed : IDisposable
{
    private readonly Listing listing;
    private readonly HashSet<long> before;

    public ClipboardWhenListed(Listing listing)
    {
        this.listing = listing;
        before = listing.Items.Select(item => item.Id).ToHashSet();
        listing.Changed += Seen;
    }

    /// <summary>The files on the clipboard when the listing first listed a new item; null until it has.</summary>
    public IReadOnlyList<string>? Files { get; private set; }

    public void Dispose() => listing.Changed -= Seen;

    private void Seen()
    {
        if (Files is null && listing.Items.Any(item => !before.Contains(item.Id)))
        {
            Files = TestClipboard.Read().Files.ToList();
        }
    }
}
