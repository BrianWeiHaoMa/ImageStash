using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using SkiaSharp;

namespace ImageStash.Tests.Store;

/// <summary>
/// The demo stash the README's pictures are taken over: reaction pictures this check draws, imported, named, tagged,
/// favorited and used as a person would, one saved with a Text, a Draw and a Hide layer, all read back as recorded
/// after the store closes and opens again (G12, F9, G17, G10, G26, G28). When <c>IMAGESTASH_README_STASH</c> names a
/// new or empty folder, a copy of the stash is left there for tools/make-readme-screenshots.ps1 to run the published
/// build over.
/// </summary>
[TestClass]
public sealed class DemoStashTests
{
    // Imported in this order, a minute apart, so All lists them last first; "ship it" is the only one "sh" finds.
    private static readonly DemoPicture[] Pictures =
    [
        new("nope", "NOPE", new SKColor(0xD8, 0x3B, 0x3B), 480, 360, ["no", "mood"], Favorite: true, Uses: 1),
        new("yes", "YES!", new SKColor(0x2E, 0x9E, 0x5B), 400, 400, ["yes"]),
        new("ship it", "SHIP IT", new SKColor(0x2F, 0x6B, 0xFF), 560, 320, ["work", "yes"], Uses: 3),
        new("thank you", "THANK YOU", new SKColor(0xE8, 0x9A, 0x1C), 480, 320, ["thanks"], Uses: 1),
        new("mind blown", "MIND BLOWN", new SKColor(0x7B, 0x4B, 0xC9), 400, 480, ["wow", "mood"]),
        new("lgtm", "LGTM", new SKColor(0x15, 0x8F, 0x8A), 360, 360, ["work", "yes"], Favorite: true, Uses: 2),
        new("brb", "BRB", new SKColor(0x5F, 0x6B, 0x7A), 480, 270, ["away"]),
        new("on it", "ON IT", new SKColor(0xF0, 0x6C, 0x2A), 420, 300, ["work"]),
        new("wow", "WOW", new SKColor(0xE0, 0x4F, 0x9B), 320, 400, ["wow"]),
        new("coffee", "COFFEE?", new SKColor(0x7A, 0x52, 0x37), 400, 300, ["away", "mood"]),
        new("good night", "GOOD NIGHT", new SKColor(0x1F, 0x2E, 0x5C), 520, 320, ["away"]),
        new("deal", "DEAL", new SKColor(0x2C, 0x6E, 0x3F), 360, 300, ["yes"], Favorite: true),
        new("monday", "MONDAY", new SKColor(0x3A, 0x45, 0x58), 640, 400, ["mood", "work"], Note: "standup moved to 7:30"),
    ];

    // The edit the README's editor picture shows live: Meme words over the top, an arrow at the note, the note blurred.
    private static readonly LayerList MondayEdit = new(
    [
        new TextLayer(TextPreset.Meme, "NOT AGAIN", "Impact", 64, new Rgba(0xFF, 0xFF, 0xFF, 0xFF), Outline.Auto, new PixelRect(20, 12, 600, 80), 0, null),
        new HideLayer(new PixelRect(126, 296, 388, 60), HideKind.Blur, null),
        new DrawMarkLayer(MarkKind.Arrow, [new PixelPoint(600, 250), new PixelPoint(500, 312)], null, new Rgba(0xFF, 0xD4, 0x3B, 0xFF), Thickness.Thick, null),
    ]);

    [TestMethod]
    public async Task DrawnPicturesImportedNamedTaggedFavoritedUsedAndOneEditedReadBackAsRecorded()
    {
        using var stash = new TestStash();
        stash.Clock.Now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var store = await stash.OpenCleanly();
        var items = new Dictionary<string, StashItem>();
        var originals = new Dictionary<string, byte[]>();
        foreach (var picture in Pictures)
        {
            stash.Clock.Now = stash.Clock.Now.AddMinutes(1);
            originals[picture.Name] = Draw(picture);
            items[picture.Name] = await stash.ImportPicture(picture.Name, picture.Name + ".png", originals[picture.Name]);
            foreach (var tag in picture.Tags)
            {
                var tagged = await store.AddTag([items[picture.Name]], tag);
                Assert.IsTrue(tagged.All(o => o.Succeeded), TestStash.Failures(tagged));
            }
        }

        var favorited = await store.SetFavorite([.. Pictures.Where(p => p.Favorite).Select(p => items[p.Name])], true);
        Assert.IsTrue(favorited.All(o => o.Succeeded), TestStash.Failures(favorited));
        var usedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);
        foreach (var picture in Pictures.Where(p => p.Uses > 0))
        {
            for (var use = 0; use < picture.Uses; use++)
            {
                usedAt = usedAt.AddMinutes(7);
                var used = await store.RecordUses([items[picture.Name]], usedAt);
                Assert.IsTrue(used.Succeeded, used.Failure?.ToString());
            }
        }

        var saved = await stash.SaveEdit(items["monday"], MondayEdit);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());

        stash.Close();
        store = await stash.OpenCleanly();
        var all = await stash.All();

        CollectionAssert.AreEqual(Pictures.Select(p => p.Name).Reverse().ToList(), all.Select(i => i.Name).ToList(), "every picture listed once, newest-added first (G4, G12)");
        foreach (var picture in Pictures)
        {
            var item = all.Single(i => i.Name == picture.Name);
            CollectionAssert.AreEquivalent(picture.Tags, item.Tags.ToList(), $"{picture.Name}'s tags (G17)");
            Assert.AreEqual(picture.Favorite, item.Favorite, $"{picture.Name}'s favorite flag (F9)");
            var history = (await store.UseHistory(item.Id)).Value!;
            Assert.AreEqual(picture.Uses, history.Count, $"{picture.Name}'s uses (G10)");
            Assert.AreEqual(picture.Name == "monday", item.Edited, $"only monday is edited (G26)");
        }

        CollectionAssert.AreEqual(new[] { "lgtm", "thank you", "ship it", "nope" }, (await store.Recent()).Value!.Select(i => i.Name).ToList(), "Recent, latest use first (G4)");
        CollectionAssert.AreEqual(new[] { "ship it" }, (await store.Search("sh")).Value!.Select(i => i.Name).ToList(), "the two letters the paste picture types find one item (G5)");
        var monday = await EditedItems.ForEdit(store, items["monday"].Id);
        Assert.AreEqual(MondayEdit.ToJson(), monday.Layers.ToJson(), "monday's layers, live for the next Edit (G26)");
        CollectionAssert.AreEquivalent(new[] { typeof(TextLayer), typeof(HideLayer), typeof(DrawMarkLayer) }, monday.Layers.Layers.Select(l => l.GetType()).ToList());
        CollectionAssert.AreEqual(originals["monday"], File.ReadAllBytes(monday.OriginalPath), "the original kept byte for byte (G29)");
        CollectionAssert.AreNotEqual(originals["monday"], File.ReadAllBytes(monday.Item.FilePath), "the finished picture is a render of the layers, not the original");

        if (Environment.GetEnvironmentVariable("IMAGESTASH_README_STASH") is { Length: > 0 } keep)
        {
            stash.Close();
            stash.CopyTo(keep);
        }
    }

    // A plain ground with the words in bold white, fitted to the width, and an optional smaller line under them.
    private static byte[] Draw(DemoPicture picture)
    {
        using var surface = SKSurface.Create(new SKImageInfo(picture.Width, picture.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Clear(picture.Ground);
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont(typeface, picture.Height * 0.32f);
        var width = font.MeasureText(picture.Words);
        if (width > picture.Width * 0.86f)
        {
            font.Size *= picture.Width * 0.86f / width;
            width = font.MeasureText(picture.Words);
        }

        var middle = picture.Note is null ? picture.Height / 2f : picture.Height * 0.42f;
        canvas.DrawText(picture.Words, (picture.Width - width) / 2, middle + font.Size * 0.35f, font, paint);
        if (picture.Note is { } note)
        {
            using var small = new SKFont(typeface, picture.Height * 0.08f);
            canvas.DrawText(note, (picture.Width - small.MeasureText(note)) / 2, picture.Height * 0.82f, small, paint);
        }

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private sealed record DemoPicture(string Name, string Words, SKColor Ground, int Width, int Height, string[] Tags, bool Favorite = false, int Uses = 0, string? Note = null);
}
