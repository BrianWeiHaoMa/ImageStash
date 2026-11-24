using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// What the drop card says before release (G11): how many of the files are supported and how many will be skipped as
/// unsupported, judged by their names alone, a folder by its own files; or that what is dragged is a link, which is
/// not taken (N2), or holds no file; and, while tag chips are lit, that every picture dropped gets their tags, those
/// already in the stash too (G11, G12). Whether a supported file is a duplicate or unreadable is known only after release.
/// </summary>
public sealed record DropCard(string Title, IReadOnlyList<string> Lines, int Supported, int Skipped)
{
    public static DropCard Of(DropContents contents, IReadOnlyList<string>? litTags = null)
    {
        if (contents.Items.Count == 0)
        {
            return contents.OnlyLink
                ? new DropCard("Links are not taken", ["Nothing is downloaded. Drag the picture itself in."], 0, 0)
                : new DropCard("Nothing here can be added", [contents.FilesUnavailable ? "The files being dragged cannot be read." : "Only files and pictures can be dropped here."], 0, 0);
        }

        var (files, _) = Import.Expand(contents.Items);
        var supported = files.Count(file => FormatByName.Judge(file.Name).Supported);
        var skipped = files.Count - supported;
        var counts = (supported == 1 ? "1 supported file" : $"{supported} supported files") + (skipped > 0 ? $" · {skipped} will be skipped as unsupported" : "");
        // The tags reach only a picture that may come in: with none among the files the line would promise nothing.
        IReadOnlyList<string> lines = litTags is null or { Count: 0 } || supported == 0
            ? [counts, "Copies are made; the originals stay where they are."]
            : [counts, $"Each picture gets the {(litTags.Count == 1 ? "tag" : "tags")} {Import.Tagged(litTags)}, those already in your stash too.", "Copies are made; the originals stay where they are."];
        return new DropCard("Add to your stash", lines, supported, skipped);
    }
}
