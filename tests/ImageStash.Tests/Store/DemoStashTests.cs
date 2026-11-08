using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using SkiaSharp;

namespace ImageStash.Tests.Store;

/// <summary>
/// The demo stash the README's pictures are taken over: the photographs committed under DemoPictures, each imported
/// under its file's name, tagged, favorited and used as a person would, most captioned with a Meme the way the Text tool
/// places one and saved through the store, one carrying a Text, a Hide and a Draw layer, all read back as recorded after
/// the store closes and opens again (F3, F12, G12, F9, G17, G10, G21, G26, G28, G29). The folder's own rules are checked
/// too: every picture has its row in SOURCES.md under an accepted license, and is a photograph, not a word on a flat
/// ground. When <c>IMAGESTASH_README_STASH</c> names a new or empty folder, a copy of the stash is left there for
/// tools/make-readme-screenshots.ps1 to run the published build over.
/// </summary>
[TestClass]
public sealed class DemoStashTests
{
    // The two letters tools/make-readme-screenshots.ps1 types in its paste scene, which must find exactly one item (G5).
    private const string PasteLetters = "my";

    // A word on a flat ground counts tens of colours, a photograph thousands; this floor tells them apart.
    private const int ColourFloor = 1000;

    private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    private static readonly string[] AcceptedLicenses = ["CC0 1.0", "Public domain", "CC BY 4.0", "Unsplash License", "Pexels License"];

    private static readonly Rgba White = new(0xFF, 0xFF, 0xFF, 0xFF);

    // Imported in this order, a minute apart, so All lists them last first; a caption is typed as a person types it and
    // drawn in uppercase by the Meme preset.
    private static readonly DemoItem[] Items =
    [
        new("sunflower.jpg", ["happy", "thanks"]),
        new("snack time.jpg", ["food", "cute"]),
        new("pizza tonight.jpg", ["food", "plans"], Caption: "pizza tonight?", AtTop: false),
        new("party time.jpg", ["party", "birthday", "food"]),
        new("everyone staring.jpg", ["awkward", "late"], Caption: "when you walk in late"),
        new("who me.jpg", ["suspicious", "cute"], Caption: "who, me?", AtTop: false),
        new("i saw that.jpg", ["suspicious"], Caption: "i saw that", AtTop: false, Favorite: true, Uses: 1),
        new("nope.jpg", ["no"], Caption: "nope", Favorite: true, Uses: 1),
        new("coffee first.jpg", ["coffee", "morning", "tired"], Caption: "but first, coffee", Favorite: true, Uses: 2),
        new("five more minutes.jpg", ["tired", "cat"], Caption: "5 more minutes", AtTop: false),
        new("wait what.jpg", ["confused", "dog"], Caption: "wait, what?"),
        new("on my way.jpg", ["late", "plans"], Caption: "on my way", Favorite: true, Uses: 3),
        new("monday.jpg", ["cat", "tired", "mood"], Caption: "monday again"),
    ];

    private static string PicturesFolder => Path.Combine(AppContext.BaseDirectory, "DemoPictures");

    [TestMethod]
    public async Task CommittedPhotographsImportedCaptionedTaggedFavoritedAndUsedReadBackAsRecorded()
    {
        var folderFailures = FolderFailures(PicturesFolder);
        Assert.IsEmpty(folderFailures, string.Join(Environment.NewLine, folderFailures));
        CollectionAssert.AreEquivalent(Pictures(PicturesFolder).Select(Path.GetFileName).ToList(), Items.Select(i => i.File).ToList(), "every picture in DemoPictures has its entry here, and every entry its picture");

        using var stash = new TestStash();
        stash.Clock.Now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var store = await stash.OpenCleanly();
        var (items, importFailures) = await ImportEach(stash, PicturesFolder, Items.Select(i => i.File));
        Assert.IsEmpty(importFailures, string.Join(Environment.NewLine, importFailures));
        foreach (var entry in Items)
        {
            foreach (var tag in entry.Tags)
            {
                var tagged = await store.AddTag([items[entry.File]], tag);
                Assert.IsTrue(tagged.All(o => o.Succeeded), TestStash.Failures(tagged));
            }
        }

        var favorited = await store.SetFavorite([.. Items.Where(i => i.Favorite).Select(i => items[i.File])], true);
        Assert.IsTrue(favorited.All(o => o.Succeeded), TestStash.Failures(favorited));
        var usedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);
        foreach (var entry in Items.Where(i => i.Uses > 0))
        {
            for (var use = 0; use < entry.Uses; use++)
            {
                usedAt = usedAt.AddMinutes(7);
                var used = await store.RecordUses([items[entry.File]], usedAt);
                Assert.IsTrue(used.Succeeded, used.Failure?.ToString());
            }
        }

        var edits = new Dictionary<string, LayerList>();
        foreach (var entry in Items.Where(i => i.Caption is not null))
        {
            edits[entry.File] = Edit(entry, items[entry.File].Facts);
            var saved = await stash.SaveEdit(items[entry.File], edits[entry.File]);
            Assert.IsTrue(saved.Succeeded, $"{entry.File}: {saved.Failure}");
        }

        stash.Close();
        store = await stash.OpenCleanly();
        var all = await stash.All();

        CollectionAssert.AreEqual(Items.Select(i => NameOf(i.File)).Reverse().ToList(), all.Select(i => i.Name).ToList(), "every picture listed once under its file's name, newest-added first (F12, G4, G12)");
        foreach (var entry in Items)
        {
            var item = all.Single(i => i.Name == NameOf(entry.File));
            CollectionAssert.AreEquivalent(entry.Tags, item.Tags.ToList(), $"{item.Name}'s tags (G17)");
            Assert.AreEqual(entry.Favorite, item.Favorite, $"{item.Name}'s favorite flag (F9)");
            var history = (await store.UseHistory(item.Id)).Value!;
            Assert.AreEqual(entry.Uses, history.Count, $"{item.Name}'s uses (G10)");
            Assert.AreEqual(entry.Caption is not null, item.Edited, $"{item.Name} is edited exactly when it was captioned (G26)");
            if (entry.Caption is null)
            {
                continue;
            }

            var edited = await EditedItems.ForEdit(store, item.Id);
            Assert.AreEqual(edits[entry.File].ToJson(), edited.Layers.ToJson(), $"{item.Name}'s layers, live for the next Edit (G26)");
            Assert.AreEqual(TextPreset.Meme, edited.Layers.Layers.OfType<TextLayer>().Single().Preset, $"{item.Name}'s caption is a Meme text layer (F8, G21)");
            var committed = File.ReadAllBytes(Path.Combine(PicturesFolder, entry.File));
            CollectionAssert.AreEqual(committed, File.ReadAllBytes(edited.OriginalPath), $"{item.Name}'s original is the committed file byte for byte (G29)");
            CollectionAssert.AreNotEqual(committed, File.ReadAllBytes(edited.Item.FilePath), $"{item.Name}'s finished picture is a render of its layers, not the original");
        }

        var monday = await EditedItems.ForEdit(store, items["monday.jpg"].Id);
        CollectionAssert.AreEquivalent(new[] { typeof(TextLayer), typeof(HideLayer), typeof(DrawMarkLayer) }, monday.Layers.Layers.Select(l => l.GetType()).ToList(), "the item the preview and editor pictures show carries a Text, a Hide and a Draw layer");
        CollectionAssert.AreEqual(new[] { "on my way", "coffee first", "nope", "i saw that" }, (await store.Recent()).Value!.Select(i => i.Name).ToList(), "Recent, latest use first (G4)");
        CollectionAssert.AreEqual(new[] { "on my way" }, (await store.Search(PasteLetters)).Value!.Select(i => i.Name).ToList(), "the two letters the paste picture types find one item (G5)");

        if (Environment.GetEnvironmentVariable("IMAGESTASH_README_STASH") is { Length: > 0 } keep)
        {
            stash.Close();
            stash.CopyTo(keep);
        }
    }

    [TestMethod]
    public void APictureInAFifthFormatWithoutItsRowUnderAnotherLicenseOrOnAFlatGroundIsNamed()
    {
        using var scratch = new ScratchFolder();
        File.Copy(Path.Combine(PicturesFolder, "sunflower.jpg"), scratch.File("sunflower.jpg"));
        File.Copy(Path.Combine(PicturesFolder, "nope.jpg"), scratch.File("extra.jpg"));
        File.Copy(Path.Combine(PicturesFolder, "coffee first.jpg"), scratch.File("shared.jpg"));
        File.WriteAllBytes(scratch.File("flat.png"), MediaFixtures.DistinctPng(0x3B45D8, 480, 360));
        File.WriteAllBytes(scratch.File("clip.bmp"), [0x42, 0x4D]);
        File.WriteAllText(scratch.File("SOURCES.md"), string.Join("\n",
            "| File | Shows | Source | Author | License | Changes |",
            "|---|---|---|---|---|---|",
            "| `sunflower.jpg` | A sunflower | [Commons](https://commons.wikimedia.org/) | Someone | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | None |",
            "| `shared.jpg` | Coffee | [Commons](https://commons.wikimedia.org/) | Someone | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) | None |",
            "| `flat.png` | A word on a ground | [Commons](https://commons.wikimedia.org/) | Someone | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | None |",
            "| `gone.jpg` | Nothing now | [Commons](https://commons.wikimedia.org/) | Someone | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | None |"));

        CollectionAssert.AreEqual(
            new[]
            {
                "clip.bmp is not a PNG, JPEG, GIF or WebP file (F3)",
                "extra.jpg has no row in SOURCES.md naming its source and license",
                "flat.png counts 1 distinct colours, under the floor of 1,000 that tells a photograph from a word on a flat ground",
                "shared.jpg's row in SOURCES.md names the license '[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)', none of CC0 1.0, Public domain, CC BY 4.0, Unsplash License, Pexels License",
                "SOURCES.md has a row for gone.jpg, which is not in the folder",
            },
            FolderFailures(scratch.Path));
    }

    [TestMethod]
    public async Task TheSamePhotographUnderTwoNamesIsAddedOnceAndTheSecondNamed()
    {
        using var scratch = new ScratchFolder();
        File.Copy(Path.Combine(PicturesFolder, "nope.jpg"), scratch.File("nope.jpg"));
        File.Copy(Path.Combine(PicturesFolder, "nope.jpg"), scratch.File("nope again.jpg"));
        using var stash = new TestStash();
        await stash.OpenCleanly();

        var (_, failures) = await ImportEach(stash, scratch.Path, ["nope.jpg", "nope again.jpg"]);

        CollectionAssert.AreEqual(new[] { "nope again.jpg is byte for byte a picture already in the stash, so it was not added (G12)" }, failures);
        CollectionAssert.AreEqual(new[] { "nope" }, (await stash.All()).Select(i => i.Name).ToList());
    }

    // The item name an import gives a file: its name without its extension (F12).
    private static string NameOf(string file) => Path.GetFileNameWithoutExtension(file);

    private static IEnumerable<string> Pictures(string folder) =>
        Directory.GetFiles(folder).Where(p => Path.GetFileName(p) != "SOURCES.md").Order(StringComparer.Ordinal);

    // Each file imported a minute after the last under its file's name; a byte-equal one is named, not added (G12).
    private static async Task<(Dictionary<string, StashItem> Items, List<string> Failures)> ImportEach(TestStash stash, string folder, IEnumerable<string> files)
    {
        var items = new Dictionary<string, StashItem>();
        var failures = new List<string>();
        foreach (var file in files)
        {
            stash.Clock.Now = stash.Clock.Now.AddMinutes(1);
            var imported = await stash.ImportOrFind(NameOf(file), file, File.ReadAllBytes(Path.Combine(folder, file)));
            if (imported.AlreadyInStash)
            {
                failures.Add($"{file} is byte for byte a picture already in the stash, so it was not added (G12)");
            }
            else
            {
                items[file] = imported.Item;
            }
        }

        return (items, failures);
    }

    // Every picture in one of the four formats (F3), with a SOURCES.md row under an accepted license, and a photograph.
    private static List<string> FolderFailures(string folder)
    {
        var failures = new List<string>();
        var licenses = SourceRows(Path.Combine(folder, "SOURCES.md"));
        foreach (var path in Pictures(folder))
        {
            var name = Path.GetFileName(path);
            if (!PictureExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            {
                failures.Add($"{name} is not a PNG, JPEG, GIF or WebP file (F3)");
                continue;
            }

            if (!licenses.TryGetValue(name, out var license))
            {
                failures.Add($"{name} has no row in SOURCES.md naming its source and license");
            }
            else if (!AcceptedLicenses.Any(license.Contains))
            {
                failures.Add($"{name}'s row in SOURCES.md names the license '{license}', none of {string.Join(", ", AcceptedLicenses)}");
            }

            var colours = DistinctColours(path);
            if (colours < ColourFloor)
            {
                failures.Add($"{name} counts {colours:N0} distinct colours, under the floor of {ColourFloor:N0} that tells a photograph from a word on a flat ground");
            }
        }

        failures.AddRange(licenses.Keys.Where(row => !File.Exists(Path.Combine(folder, row))).Select(row => $"SOURCES.md has a row for {row}, which is not in the folder"));
        return failures;
    }

    // SOURCES.md's rows by file: a table line whose first cell is a backticked file name, and its fifth cell, the license.
    private static Dictionary<string, string> SourceRows(string path)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>())
        {
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length >= 7 && cells[1].Length > 2 && cells[1].StartsWith('`') && cells[1].EndsWith('`'))
            {
                rows[cells[1][1..^1]] = cells[5];
            }
        }

        return rows;
    }

    // The distinct colours of the picture's first frame, as SkiaSharp decodes it; 0 when it does not decode.
    private static int DistinctColours(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        return bitmap is null ? 0 : bitmap.Pixels.Select(c => (uint)c).Distinct().Count();
    }

    // The item's layers: its caption as the Text tool lays a new Meme (Impact at a tenth of the picture's width, across
    // it less a margin of 0.4 of that size, at the top or the bottom), and on monday a blur and an arrow as well.
    private static LayerList Edit(DemoItem entry, MediaFacts picture)
    {
        var size = (int)Math.Round(picture.Width * 0.10, MidpointRounding.AwayFromZero);
        var margin = (int)Math.Round(size * 0.4, MidpointRounding.AwayFromZero);
        var original = new PixelSize(picture.Width, picture.Height);
        var meme = LayerGeometry.Measured(new TextLayer(TextPreset.Meme, entry.Caption!, "Impact", size, White, Outline.Auto, new PixelRect(margin, 0, picture.Width - 2 * margin, 1), 0, null), LayerList.Empty, original);
        var caption = meme with { Box = meme.Box with { Y = entry.AtTop ? margin : picture.Height - margin - meme.Box.Height } };
        if (entry.File != "monday.jpg")
        {
            return new LayerList([caption]);
        }

        // The cat's raised back end blurred, and an arrow at its yawn.
        return new LayerList(
        [
            caption,
            new HideLayer(new PixelRect(690, 130, 270, 230), HideKind.Blur, null),
            new DrawMarkLayer(MarkKind.Arrow, [new PixelPoint(170, 300), new PixelPoint(420, 365)], null, new Rgba(0xFF, 0xD4, 0x3B, 0xFF), Thickness.Thick, null),
        ]);
    }

    private sealed record DemoItem(string File, string[] Tags, string? Caption = null, bool AtTop = true, bool Favorite = false, int Uses = 0);
}
