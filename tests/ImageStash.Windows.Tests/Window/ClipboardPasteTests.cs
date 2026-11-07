using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The paste in the real stash window over the real clipboard seam and the signed-in user's real clipboard (G13):
/// <c>Ctrl+V</c> from the search box, the grid, the tag field, the preview and its name and tag fields, and Paste image
/// in the <c>+</c> menu, each one paste per press, no field taking the clipboard's text (G7, N9, R5); a copied PNG, a
/// bitmap alone and copied files each become items, a picture named like a screenshot (F12, F3, G12); the preview
/// closing for an import and staying for a notice; the <c>+</c> menu's entries and the empty stash's ways in (G2, R10).
/// Then what real programs put there: Microsoft Edge's Copy image, File Explorer's Copy of files, PowerShell's
/// Set-Clipboard of an address or of words, an emptied clipboard and one another program holds open (G11, G12, N2, G35);
/// words pasted with <c>Ctrl+V</c> into the search box through the same one read (G13).
/// </summary>
[TestClass]
public sealed class ClipboardPasteTests
{
    private const string StillOnly = "A pasted picture is always a still; to add an animated one, drag its file in.";

    [TestMethod]
    public async Task CtrlVInTheBoxPastesACopiedPngAsAScreenshotItemFirstAndSelectedAndTypesNothing()
    {
        using var shown = Open();
        var older = (await shown.OpenStash(shown.Picture("paste older.png"))).Single();
        var png = File.ReadAllBytes(shown.Picture("paste copied.png"));
        TestClipboard.Put((TestClipboard.Png, png), TestClipboard.Text("pasted words"));

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Release(shown.Search, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Import.Busy is null));

        var name = AutoName.Screenshot(shown.Notices.Clock.GetLocalNow());
        TrayDesktop.Run(() =>
        {
            var pasted = shown.Listing.Items[0];
            Assert.AreEqual(name, pasted.Name);
            Assert.AreEqual(name + ".png", pasted.FileName);
            CollectionAssert.AreEqual(png, File.ReadAllBytes(pasted.FilePath), "the clipboard's PNG as it is, in the stash folder's root");
            CollectionAssert.AreEqual(new[] { pasted.Id }, shown.Listing.Selection.ToList());
            Assert.AreEqual(older.Id, shown.Listing.Items[1].Id);
            Assert.AreEqual("", shown.Search.Text ?? "", "the clipboard's text was not typed into the box");
            Assert.AreEqual($"Added 1 to your stash: {name}. {StillOnly}", shown.Notices.Shown[^1].Text);
            Assert.IsTrue(shown.Grid.Rows.SelectMany(row => row.Tiles).Any(tile => tile.Id == pasted.Id), "the grid shows it");
        });
    }

    [TestMethod]
    public async Task CtrlVFromTheGridAndTheTagFieldPastesAndTheFieldKeepsItsText()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("paste grid.png"))).Single();

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste from grid.png"))), TestClipboard.Text("pasted words"));
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            Press(shown.Grid, Key.V, KeyModifiers.Control);
            Release(shown.Grid, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Import.Busy is null));

        TrayDesktop.Run(() =>
        {
            shown.RightClick(item.Id);
            shown.Submenu("Add tag");
            shown.ClickSubmenuEntry("Add tag", "Type a tag…");
            shown.CloseMenu();
        });
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste from tag field.png"))), TestClipboard.Text("pasted words"));
        TrayDesktop.Run(() =>
        {
            Type(shown.TagBox, "typed");
            Press(shown.TagBox, Key.V, KeyModifiers.Control);
            Release(shown.TagBox, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 3 && shown.Import.Busy is null));

        TrayDesktop.Run(() => Assert.AreEqual("typed", shown.TagBox.Text, "the field's own text, the clipboard's never typed into it"));
        Assert.IsEmpty(shown.TagsOf(item.Id), "nothing was committed as a tag");
    }

    [TestMethod]
    public async Task CtrlVFromThePreviewClosesItAndSelectsThePastedItemAndAnEmptyClipboardLeavesItOpen()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("paste preview.png"))).Single();

        TestClipboard.Put(TestClipboard.Text("pasted words"));
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            Press(shown.PreviewPicture, Key.V, KeyModifiers.Control);
            Release(shown.PreviewPicture, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown[^1].Text == "Paste failed: the clipboard, it holds no picture or file"));
        TrayDesktop.Run(() => Assert.AreEqual(item.Id, shown.Preview.Item?.Id, "a notice leaves the preview open"));

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste into preview.png"))));
        TrayDesktop.Run(() =>
        {
            Press(shown.PreviewPicture, Key.V, KeyModifiers.Control);
            Release(shown.PreviewPicture, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Import.Busy is null));

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Preview.IsOpen, "an import's end closes the preview, so All shows the new item (G12)");
            CollectionAssert.AreEqual(new[] { shown.Listing.Items[0].Id }, shown.Listing.Selection.ToList());
            Assert.AreNotEqual(item.Id, shown.Listing.Items[0].Id);
        });
    }

    [TestMethod]
    public async Task CtrlVFromThePreviewsNameAndTagFieldsPastesAndNeitherTakesTheClipboardsText()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("paste name.png"))).Single();

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste from name.png"))), TestClipboard.Text("pasted words"));
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            Press(shown.PreviewPicture, Key.F2);
            Assert.IsTrue(shown.Preview.EditingName);
            Press(shown.NameBox, Key.V, KeyModifiers.Control);
            Assert.AreEqual("paste name", shown.NameBox.Text, "the name's own text, the clipboard's never typed into it");
            Release(shown.NameBox, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Import.Busy is null));

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste from tags.png"))), TestClipboard.Text("pasted words"));
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(shown.Listing.Items.Single(each => each.Id == item.Id));
            shown.PreviewTagBox.Focus();
            Type(shown.PreviewTagBox, "typed");
            Press(shown.PreviewTagBox, Key.V, KeyModifiers.Control);
            Assert.AreEqual("typed", shown.PreviewTagBox.Text, "the tag field's own text");
            Release(shown.PreviewTagBox, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 3 && shown.Import.Busy is null));

        TrayDesktop.Run(() => Assert.AreEqual("paste name", shown.Listing.Items.Single(each => each.Id == item.Id).Name, "no rename was committed"));
        Assert.IsEmpty(shown.TagsOf(item.Id), "no tag was committed");
    }

    [TestMethod]
    public async Task APressHeldDownPastesOnceAndTheNextPressPastesAgain()
    {
        using var shown = Open();
        await shown.OpenStash();
        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste held.png"))));

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            // Windows repeats the press of a held key.
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Press(shown.Search, Key.V, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1 && shown.Import.Busy is null));
        await Task.Delay(500);
        TrayDesktop.Run(() => Assert.HasCount(1, shown.Listing.Items, "one paste for one press"));

        TrayDesktop.Run(() =>
        {
            Release(shown.Search, Key.V);
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Release(shown.Search, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Import.Busy is null));
    }

    [TestMethod]
    public async Task ThePlusMenuOffersImportFilesPasteImageTakeAScreenshotAndRecordAClipWithTheirShortcutsAndItsPasteImagePastes()
    {
        using var shown = Open();
        await shown.OpenStash();
        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste from menu.png"))));

        var entries = TrayDesktop.Get(() => shown.AddMenu().Select(entry => $"{entry.Header} {entry.InputGesture}").ToList());
        TrayDesktop.Run(() => shown.AddMenu().Single(entry => (string)entry.Header! == "Paste image").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1 && shown.Import.Busy is null));

        CollectionAssert.AreEqual(new[] { "Import files… Ctrl+O", "Paste image Ctrl+V", $"Take a screenshot {new KeyGesture(Key.PrintScreen, KeyModifiers.Control)}", $"Record a clip {new KeyGesture(Key.PrintScreen, KeyModifiers.Control | KeyModifiers.Shift)}" }, entries,
            "the ways in, the captures' shortcuts as the settings read them, and nothing that does not work (R10)");
        TrayDesktop.Run(() => Assert.AreEqual(AutoName.Screenshot(shown.Notices.Clock.GetLocalNow()), shown.Listing.Items.Single().Name));
    }

    [TestMethod]
    public async Task TheEmptyStashListsThePasteAmongItsWaysInWithItsShortcut()
    {
        using var shown = Open();
        await shown.OpenStash();

        var lines = TrayDesktop.Get(() =>
        {
            var heading = shown.Window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == "Your stash is empty");
            return ((Panel)heading.Parent!).Children.OfType<TextBlock>().Select(text => text.Text ?? "").ToList();
        });

        CollectionAssert.AreEqual(new[] { "Your stash is empty", "Drop images and GIFs here, from a browser, a chat or File Explorer", "Paste a copied image    Ctrl+V", "Take a screenshot    Ctrl+PrtScn", "Record a clip    Ctrl+Shift+PrtScn", "Stash the clipboard image    Ctrl+Alt+V", "Import files    Ctrl+O" }, lines,
            $"every way in, with its shortcut, and nothing else (G2, R10): {string.Join(" | ", lines)}");
    }

    [TestMethod]
    public async Task ABitmapAloneIsPastedAsAPngOfItsPixels()
    {
        using var shown = Open();
        await shown.OpenStash();
        // BGRA top-down, opaque: blue, then red.
        byte[] pixels = [255, 0, 0, 255, 0, 0, 255, 255];
        TestClipboard.PutBitmap(2, 1, pixels);

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Release(shown.Search, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1 && shown.Import.Busy is null));

        var pasted = TrayDesktop.Get(() => shown.Listing.Items.Single());
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 2, 1, false, 1, 0), pasted.Facts);
        using var decoder = MediaDecoder.Open(pasted.FilePath, pasted.Name, TimeSpan.FromSeconds(1)).Value!;
        CollectionAssert.AreEqual(pixels, decoder.NextFrame(2).Value!.Pixels);
    }

    [TestMethod]
    public async Task CopiedFilesArePastedAsADropWouldAddThem()
    {
        using var shown = Open();
        await shown.OpenStash();
        var cat = shown.Picture("copied cat.png");
        var dog = shown.Fixture("animated.gif", "copied dog.gif");
        TestClipboard.Put(TestClipboard.FileList(cat, dog));

        TrayDesktop.Run(() =>
        {
            // The empty stash shows its ways in where the grid would be.
            shown.Search.Focus();
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Release(shown.Search, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Import.Busy is null));

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEquivalent(new[] { "copied cat.png", "copied dog.gif" }, shown.Listing.Items.Select(item => item.FileName).ToList(), "each the file it came as, named after it");
            Assert.AreEqual("Added 2 to your stash: copied cat.png and copied dog.gif.", shown.Notices.Shown[^1].Text);
        });
        Assert.IsTrue(File.Exists(cat) && File.Exists(dog), "the sources stay");
    }

    [TestMethod]
    public async Task EdgesCopyImagePastedInTheBoxTheGridAndThePlusMenuIsThreeStillPngsOfThePictureWithItsTransparency()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var edge = CopiedInEdge();
        Assert.IsEmpty(TestClipboard.Read().Files, "a browser's copied picture is no file list, so its paste is the picture's, not an import of files (G13)");
        using var shown = Open();
        await shown.OpenStash();
        await PasteFrom(shown, () => shown.Search, 1);
        // The grid takes the empty stash's place once the window has laid out its first item.
        await Eventually(() => TrayDesktop.Get(() => shown.Window.GetVisualDescendants().OfType<ThumbnailGrid>().Any()));
        await PasteFrom(shown, () => shown.Grid, 2);
        TrayDesktop.Run(() => shown.AddMenu().Single(entry => (string)entry.Header! == "Paste image").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 3 && shown.Import.Busy is null));

        var name = AutoName.Screenshot(shown.Notices.Clock.GetLocalNow());
        var items = TrayDesktop.Get(() => shown.Listing.Items.ToList());
        CollectionAssert.AreEqual(new[] { $"{name} 3.png", $"{name} 2.png", $"{name}.png" }, items.Select(item => item.FileName).ToList(),
            "newest first, each named like a screenshot at its paste, a later one in the same second taking \" 2\" on its file (F12, G12)");
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { items[0].Id }, shown.Listing.Selection.ToList(), "the last pasted, first in All and selected (G12)");
            Assert.AreEqual($"Added 1 to your stash: {name}. {StillOnly}", shown.Notices.Shown[^1].Text);
        });
        var copied = Pixels(EdgePicture);
        Assert.IsTrue(copied.Where((_, at) => at % 4 == 3).Any(alpha => alpha < 255), "the copied picture has transparency to keep");
        foreach (var item in items)
        {
            Assert.AreEqual(name, item.Name);
            Assert.AreEqual(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), item.Facts, $"{item.FileName} is a still PNG of the copied size (F3, G13)");
            var pasted = Pixels(item.FilePath);
            for (var at = 0; at < copied.Length; at += 4)
            {
                Assert.AreEqual(copied[at + 3], pasted[at + 3], $"{item.FileName}'s alpha at pixel {at / 4}: the transparency Edge offered as \"PNG\"");
                // Where it is translucent, its colour within the rounding a premultiplied copy costs.
                var tolerance = copied[at + 3] == 255 ? 0 : 2;
                for (var channel = at; channel < at + 3 && copied[at + 3] > 0; channel++)
                {
                    Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(copied[channel] - pasted[channel]), $"{item.FileName}'s colour at pixel {at / 4}: {pasted[channel]} for {copied[channel]}");
                }
            }
        }
    }

    [TestMethod]
    public async Task WithATagChipLitEdgesCopiedPictureIsPastedCarryingThatTag()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var edge = CopiedInEdge();
        using var shown = Open();
        var tagged = await shown.OpenStash(shown.Picture("paste tagged before.png"));
        await shown.Tag(tagged, "reaction");
        TrayDesktop.Run(() => shown.Chip("#reaction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Lit("#reaction")));
        await PasteFrom(shown, () => shown.Search, 2);

        var pasted = TrayDesktop.Get(() => shown.Listing.Items[0]);
        Assert.AreEqual(AutoName.Screenshot(shown.Notices.Clock.GetLocalNow()), pasted.Name);
        CollectionAssert.AreEqual(new[] { "reaction" }, shown.TagsOf(pasted.Id), "the tag lit at the paste (G11)");
    }

    [TestMethod]
    public async Task FileExplorersCopyOfAGifAndAnMp4AddsTheGifAsItCameAndRefusesTheMp4AndTheGifCopiedAgainIsAlreadyInTheStash()
    {
        using var shown = Open();
        await shown.OpenStash();
        var gif = shown.Fixture("animated.gif", "copied dancing.gif");
        var folder = Path.GetDirectoryName(gif)!;
        var mp4 = Path.Combine(folder, "copied clip.mp4");
        File.WriteAllBytes(mp4, [0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109]);

        using (ShellCopy.Of(folder, "copied dancing.gif", "copied clip.mp4"))
        {
            CollectionAssert.AreEquivalent(new[] { gif, mp4 }, TestClipboard.Read().Files.ToList(), "the shell's copy is a file list");
            await PasteFrom(shown, () => shown.Search, 1);
        }

        var added = TrayDesktop.Get(() => shown.Listing.Items.Single());
        Assert.AreEqual("copied dancing.gif", added.FileName, "the GIF as the file it came as, named after it (F3, G12)");
        CollectionAssert.AreEqual(File.ReadAllBytes(gif), File.ReadAllBytes(added.FilePath));
        Assert.IsTrue(added.Facts.Animated);
        TrayDesktop.Run(() => Assert.AreEqual($"Added 1 to your stash: copied dancing.gif. Skipped copied clip.mp4 (a video): ImageStash keeps {Import.Formats}.", shown.Notices.Shown[^1].Text));

        var noticed = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        using (ShellCopy.Of(folder, "copied dancing.gif"))
        {
            TrayDesktop.Run(() =>
            {
                shown.Grid.Focus();
                Press(shown.Grid, Key.V, KeyModifiers.Control);
                Release(shown.Grid, Key.V);
            });
            await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count > noticed && shown.Import.Busy is null));
        }

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Nothing was added. copied dancing.gif was already in your stash as “copied dancing”.", shown.Notices.Shown[^1].Text);
            Assert.HasCount(1, shown.Listing.Items);
        });
    }

    [TestMethod]
    public async Task CtrlVInTheBoxPutsTheClipboardsWordsInPlaceOfItsSelectionAndAHeldPressPutsThemOnce()
    {
        using var shown = Open();
        await shown.OpenStash(shown.Picture("words cat.png"));
        TestClipboard.PutText("hello");
        var noticed = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Search.Focus();
            shown.Search.SelectionStart = 1;
            shown.Search.SelectionEnd = 3;
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Press(shown.Search, Key.V, KeyModifiers.Control);
            Release(shown.Search, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Search.Text == "chello" && shown.Listing.Query == "chello" && shown.Listing.Settled));
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("chello", shown.Search.Text, "the held press pasted once");
            Assert.AreEqual(6, shown.Search.CaretIndex, "the caret after the words");
            Assert.HasCount(noticed, shown.Notices.Shown, "no notice");
            Assert.IsEmpty(shown.Listing.Items, "the box searches them; nothing added");
        });
    }

    [TestMethod]
    public async Task ACopiedAddressIsRefusedAsALinkWithNothingFetchedAndAnEmptyClipboardOrWordsHoldNoPictureOrFile()
    {
        using var shown = Open();
        await shown.OpenStash(shown.Picture("address older.png"));
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            TestClipboard.SetClipboard($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/cat.gif");
            Assert.AreEqual("Paste failed: the link, links are not taken and nothing is downloaded; copy the picture itself", await PasteForNotice(shown, () => shown.Grid));
            await Task.Delay(500);
            Assert.IsFalse(server.Pending(), "nothing asked the address for its picture (N2)");
        }
        finally
        {
            server.Stop();
        }

        TestClipboard.Empty();
        Assert.AreEqual("Paste failed: the clipboard, it holds no picture or file", await PasteForNotice(shown, () => shown.Search), "an emptied clipboard, from the box too (G13)");
        TestClipboard.SetClipboard("a reaction for later");
        Assert.AreEqual("Paste failed: the clipboard, it holds no picture or file", await PasteForNotice(shown, () => shown.Grid), "words, from the grid (G13)");
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(1, shown.Listing.Items, "nothing was added");
            Assert.AreEqual("", shown.Search.Text ?? "", "and the box took nothing from the grid's paste");
        });
    }

    [TestMethod]
    public async Task AClipboardHeldPastTheRetryIsNamedWithNothingAddedAndOnceLetGoTheNextPastePastes()
    {
        using var shown = Open();
        await shown.OpenStash();
        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("paste once let go.png"))));

        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            var clock = Stopwatch.StartNew();
            var naming = PasteForNotice(shown, () => shown.Search);
            await Task.Delay(500);
            TrayDesktop.Run(() => Assert.IsTrue(shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == "Pasting from the clipboard…"), "the busy state names the paste while it waits (G35)"));
            Assert.AreEqual($"Paste failed: the clipboard, {HeldClipboard.Reason}", await naming);
            Assert.IsGreaterThanOrEqualTo(HeldClipboard.Retry.TotalMilliseconds - 100, clock.ElapsedMilliseconds, "retried for the whole retry before it was named (E3)");
            holder.Release();
        }

        TrayDesktop.Run(() => Assert.IsEmpty(shown.Listing.Items, "nothing was added"));
        await PasteFrom(shown, () => shown.Search, 1);
    }

    private static string EdgePicture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "transparent.png");

    /// <summary>
    /// Edge showing the half-transparent fixture, its Copy image done; the check is inconclusive without Edge. The
    /// copy comes before the stash window shows, as a person copies and then summons it: Edge coming to the front
    /// hides a flyout already showing (G2).
    /// </summary>
    private static EdgeApp CopiedInEdge()
    {
        var edge = EdgeApp.Picture(EdgePicture);
        if (edge is null)
        {
            Assert.Inconclusive("Microsoft Edge is not installed on this PC, so no browser copies the picture");
        }

        try
        {
            Assert.IsTrue(edge.CopyImage(), "Edge's Copy image put the picture on the clipboard");
        }
        catch
        {
            edge.Dispose();
            throw;
        }

        return edge;
    }

    private static async Task PasteFrom(ShownWindow shown, Func<Control> place, int count)
    {
        TrayDesktop.Run(() =>
        {
            var at = place();
            at.Focus();
            Press(at, Key.V, KeyModifiers.Control);
            Release(at, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == count && shown.Import.Busy is null));
    }

    /// <summary>One <c>Ctrl+V</c> with focus on what <paramref name="from"/> gives and the notice it raised.</summary>
    private static async Task<string> PasteForNotice(ShownWindow shown, Func<Control> from)
    {
        var noticed = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        TrayDesktop.Run(() =>
        {
            var focused = from();
            focused.Focus();
            Press(focused, Key.V, KeyModifiers.Control);
            Release(focused, Key.V);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count > noticed));
        return TrayDesktop.Get(() => shown.Notices.Shown[^1].Text);
    }

    private static byte[] Pixels(string path)
    {
        using var decoder = MediaDecoder.Open(path, Path.GetFileNameWithoutExtension(path), TimeSpan.FromSeconds(1)).Value!;
        return decoder.NextFrame(4096).Value!.Pixels;
    }
}
