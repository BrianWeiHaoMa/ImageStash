using System.Text.Json;

namespace ImageStash.Store;

/// <summary>
/// A copy of one of a deleted item's files, held in <c>.work\</c> while the delete can still be undone (State: the
/// Delete bullet): which of the row's files it copies, by the column of <c>items</c> that names that file
/// (<see cref="StoredFile.Column"/>), and the copy's name in <c>.work\</c>. A marked row keeps its copies as a JSON
/// list in <c>held_copies</c> until the delete is finished or undone; where each file belongs is
/// <see cref="StoredFiles.Files"/>'s answer, never recorded twice.
/// </summary>
internal sealed record HeldCopy(string File, string Copy)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Encode(IReadOnlyList<HeldCopy> copies) => JsonSerializer.Serialize(copies, Json);

    public static IReadOnlyList<HeldCopy> Decode(string json) => JsonSerializer.Deserialize<List<HeldCopy>>(json, Json) ?? [];

    /// <summary>The copy <paramref name="copies"/> holds of <paramref name="file"/>, or null when none was made.</summary>
    public static HeldCopy? Of(IReadOnlyList<HeldCopy> copies, StoredFile file) => copies.FirstOrDefault(c => c.File == file.Column);
}
