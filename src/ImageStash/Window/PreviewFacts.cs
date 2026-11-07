using System.Globalization;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The facts the preview states about its item (G19), in two lines: its type, dimensions, file
/// size and, for an animation, its duration ("GIF · 480 × 300 · 1.7 MB · 2.2 s"); then when it was added and how often
/// and how recently it was used ("Added Oct 12, 2026 · Used 23 times, last 4 min ago"); and on an edited item a third,
/// naming its layer kinds ("Edited here: hide, draw mark · the original is kept"). Times are the
/// store's UTC shown in local time (What crosses every boundary); the size is the file's on disk, which the store does
/// not hold. The lines are a pure function of what they are given, so the preview states only what it last read.
/// </summary>
public static class PreviewFacts
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <param name="fileBytes">The finished picture's length on disk, or null when it could not be read, which leaves the size out.</param>
    /// <param name="layers">The item's layers as the store records them (<see cref="EditedLayers"/>); empty on an unedited item, which then has two lines.</param>
    public static IReadOnlyList<string> Lines(StashItem item, long? fileBytes, UseHistory uses, LayerList layers, DateTimeOffset now, TimeZoneInfo zone)
    {
        var picture = new List<string> { Type(item.Facts), $"{item.Facts.Width} × {item.Facts.Height}" };
        if (fileBytes is { } bytes)
        {
            picture.Add(FileSize.Words(bytes));
        }

        if (item.Facts.Animated)
        {
            picture.Add(ClipLength.Words(item.Facts.DurationMs));
        }

        var used = $"Added {Day(item.AddedAt, now, zone)} · {Use(uses, now)}";
        return layers.IsEmpty ? [string.Join(" · ", picture), used] : [string.Join(" · ", picture), used, Edited(layers)];
    }

    /// <summary>
    /// The layers "Edited here" names: the committed list, the item's layers as the store records them and the list the
    /// next start renders, even while a live failure leaves the picture on disk rendered from another (State: Save, A
    /// live failure); read only for an edited item, so an unedited one costs no read. A list the store cannot read is
    /// the failure that names it (G35).
    /// </summary>
    public static async Task<Outcome<LayerList>> EditedLayers(StashStore store, StashItem item)
    {
        if (!item.Edited)
        {
            return Outcome<LayerList>.Of(LayerList.Empty);
        }

        var read = await store.ForEdit(item.Id);
        return !read.Succeeded ? read.Failure : Outcome<LayerList>.Of(read.Value?.Layers ?? LayerList.Empty);
    }

    /// <summary>"Edited here:", the list's kinds once each in list order by F7's names, and that the original is kept.</summary>
    private static string Edited(LayerList layers) =>
        $"Edited here: {string.Join(", ", layers.Layers.Select(Kind).Distinct())} · the original is kept";

    /// <summary>A layer's kind as F7 names it.</summary>
    private static string Kind(Layer layer) => layer switch
    {
        TextLayer => "text",
        CaptionBarLayer => "caption bar",
        HideLayer => "hide",
        DrawMarkLayer => "draw mark",
        CropLayer => "crop",
        TrimLayer => "trim",
        ShrinkLayer => "shrink",
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "not a kind F7 names"),
    };

    /// <summary>The type by its format's name; WebP says whether it is animated, since its badge and its name do not.</summary>
    public static string Type(MediaFacts facts) => facts.Format switch
    {
        ImageFormat.Png => "PNG",
        ImageFormat.Jpeg => "JPG",
        ImageFormat.Gif => "GIF",
        _ => facts.Animated ? "Animated WebP" : "WebP",
    };

    /// <summary>A file's length on disk, or null when it cannot be read; reading it opens nothing, so a file held without sharing still gives it (G24).</summary>
    public static long? FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The day of <paramref name="at"/> in the local calendar: today, yesterday, else its date.</summary>
    public static string Day(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var day = TimeZoneInfo.ConvertTime(at, zone).Date;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        return day == today ? "today"
            : day == today.AddDays(-1) ? "yesterday"
            : day.ToString("MMM d, yyyy", English);
    }

    /// <summary>How often and how recently the item was used, or that it never was.</summary>
    public static string Use(UseHistory uses, DateTimeOffset now) => uses switch
    {
        { Count: 0 } or { Latest: null } => "Never used",
        { Count: 1, Latest: { } latest } => $"Used once, {Ago(latest, now)}",
        { Latest: { } latest } => $"Used {uses.Count.ToString(English)} times, last {Ago(latest, now)}",
    };

    /// <summary>How long ago <paramref name="at"/> was, in the unit that reads best; a moment from a clock set back reads as just now.</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var since = now - at;
        return since < TimeSpan.FromMinutes(1) ? "just now"
            : since < TimeSpan.FromHours(1) ? $"{(int)since.TotalMinutes} min ago"
            : since < TimeSpan.FromDays(1) ? $"{(int)since.TotalHours} hr ago"
            : since < TimeSpan.FromDays(2) ? "yesterday"
            : $"{(int)since.TotalDays} days ago";
    }
}
