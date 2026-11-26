using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Paste image and <c>Ctrl+V</c> in the window (G13), the window's one reader of the clipboard (N9, R5): the clipboard
/// read once per act, files first, through the seam, and what it holds decides. Its files are
/// imported as a drop's would be (G12); else its picture is saved as one still PNG item named like a screenshot at the
/// moment of the paste (F12, F3); else, from the search box or a tag field, its words go into that box; else a notice
/// says it holds no picture or file, or that a link is not taken and nothing is downloaded (N2). The tags lit at the
/// paste go on every item it adds (G11). While the stash cannot take an import nothing is read, unless the paste is the
/// search box's or a tag field's, whose words need no stash (G35, G13); a clipboard another program holds is retried, then named (E3).
/// Inside a layer's text or the color picker's hex box (<see cref="PasteText"/>) it never imports: words alone go in, a web address among
/// them, and anything else gets a notice that it holds no text to paste (G13).
/// </summary>
public sealed class ClipboardPaste(Listing listing, Notices notices, Import import, IClipboardAndDrops clipboard, TimeProvider clock, TimeSpan heldClipboardRetry)
{
    public const string Action = "Paste";

    private int reading;

    /// <summary>Raised whenever <see cref="Busy"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The busy state while the clipboard is being read, or null; the import's own names the batch after it (G35).</summary>
    public string? Busy => reading > 0 ? "Pasting from the clipboard…" : null;

    /// <summary>One paste: the user's act, from the keys or the <c>+</c> menu.</summary>
    /// <param name="intoField">Takes the clipboard's words when the paste is the <c>Ctrl+V</c> of the search box or a tag field and it holds neither files nor a picture; else null (G13).</param>
    public async Task Paste(Action<string>? intoField = null)
    {
        // The paste's act, which its refusal and the batch's result alike carry (G16).
        using var acting = UserAct.Begin(Action);
        var refused = import.Refusal("the clipboard");
        if (refused is not null && intoField is null)
        {
            notices.Raise(refused);
            return;
        }

        // Taken at the act, as Ctrl+O takes the tags before its dialog: a batch queued behind another keeps them, and its end
        // asks whether the person moved on since this act (G11, G12, F12).
        var litTags = listing.LitTags;
        var began = ImportBegan.In(listing);
        var at = clock.GetLocalNow();
        var read = await ReadOnce();
        if (!read.Succeeded)
        {
            notices.Raise(read.Failure);
        }
        else if (intoField is not null && WordsAlone(read.Value!) is { } words)
        {
            intoField(words);
        }
        else if (refused is not null)
        {
            notices.Raise(refused);
        }
        else if (read.Value is { Files.Count: > 0 } files)
        {
            await import.Paste(files.Files, litTags, began);
        }
        else if (read.Value is { Png: not null } or { Image: not null })
        {
            await import.Paste(new PastedPicture(AutoName.Screenshot(at), read.Value.Png, read.Value.Image), litTags, began);
        }
        else if (read.Value!.OnlyLink)
        {
            notices.Raise(new Failure(Action, "the link", "links are not taken and nothing is downloaded; copy the picture itself"));
        }
        else
        {
            notices.Raise(new Failure(Action, "the clipboard", "it holds no picture or file"));
        }
    }

    /// <summary>
    /// <c>Ctrl+V</c> inside an editor text field (G13): one read whatever the stash is doing,
    /// since nothing is added; its words go to <paramref name="intoText"/> when it holds words and neither files nor a
    /// picture, a web address alone among them (nothing is fetched, N2), and otherwise the notice says it holds no text to
    /// paste. A read that fails or a field that closed before taking the words gets a notice (G35).
    /// </summary>
    public async Task PasteText(Func<string, bool> intoText)
    {
        using var acting = UserAct.Begin(Action);
        var read = await ReadOnce();
        if (!read.Succeeded)
        {
            notices.Raise(read.Failure);
        }
        else if (WordsAlone(read.Value!) is { } words)
        {
            if (!intoText(words))
            {
                notices.Raise(new Failure(Action, "the editor field", "it closed before the paste could complete"));
            }
        }
        else
        {
            notices.Raise(new Failure(Action, "the clipboard", "it holds no text to paste"));
        }
    }

    /// <summary>The clipboard read once through the seam, the busy state shown meanwhile, a held clipboard retried (E3).</summary>
    private async Task<Outcome<ClipboardReading>> ReadOnce()
    {
        SetReading(+1);
        try
        {
            return await clipboard.Read(heldClipboardRetry);
        }
        finally
        {
            SetReading(-1);
        }
    }

    /// <summary>The clipboard's words when it holds words and neither files nor a picture; else null (G13).</summary>
    private static string? WordsAlone(ClipboardReading read) =>
        read is { Files.Count: 0, Png: null, Image: null, Text.Length: > 0 } ? read.Text : null;

    private void SetReading(int change)
    {
        reading += change;
        Changed?.Invoke();
    }
}
