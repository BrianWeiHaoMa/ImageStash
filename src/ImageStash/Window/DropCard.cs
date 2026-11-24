using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// What the drop card says before release (G11): how many of the files are supported, how many are to check (a name
/// with no extension, whose content decides) and how many will be skipped as unsupported, judged by their names alone,
/// a folder by its own files, nothing opened; or that what is dragged is a link, which is not taken (N2), or holds no
/// file; and, while tag chips are lit, that every picture dropped gets their tags, those already in the stash too (G11,
/// G12). Whether a supported file is a duplicate or unreadable, and whether one to check is a picture, is known only
/// after release.
/// </summary>
public sealed record DropCard(string Title, IReadOnlyList<string> Lines, int Supported, int Skipped, int ToCheck)
{
    public static DropCard Of(DropContents contents, IReadOnlyList<string>? litTags = null)
    {
        if (contents.Items.Count == 0)
        {
            return contents.OnlyLink
                ? new DropCard("Links are not taken", ["Nothing is downloaded. Drag the picture itself in."], 0, 0, 0)
                : new DropCard("Nothing here can be added", [contents.FilesUnavailable ? "The files being dragged cannot be read." : "Only files and pictures can be dropped here."], 0, 0, 0);
        }

        var (files, _) = Import.Expand(contents.Items);
        var verdicts = files.Select(file => FormatByName.Judge(file.Name)).ToList();
        var supported = verdicts.Count(verdict => verdict.Supported);
        var toCheck = verdicts.Count(verdict => verdict.ToCheck);
        var skipped = files.Count - supported - toCheck;
        List<string> counted = [supported == 1 ? "1 supported file" : $"{supported} supported files"];
        if (toCheck > 0)
        {
            counted.Add($"{toCheck} to check");
        }

        if (skipped > 0)
        {
            counted.Add($"{skipped} will be skipped as unsupported");
        }

        var counts = string.Join(" · ", counted);
        // The tags reach only a picture that may come in, a file to check included: with none the line would promise nothing.
        IReadOnlyList<string> lines = litTags is null or { Count: 0 } || supported + toCheck == 0
            ? [counts, "Copies are made; the originals stay where they are."]
            : [counts, $"Each picture gets the {(litTags.Count == 1 ? "tag" : "tags")} {Import.Tagged(litTags)}, those already in your stash too.", "Copies are made; the originals stay where they are."];
        return new DropCard("Add to your stash", lines, supported, skipped, toCheck);
    }
}
