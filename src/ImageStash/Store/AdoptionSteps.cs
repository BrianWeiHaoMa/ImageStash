namespace ImageStash.Store;

/// <summary>
/// How every reader of a row sees an Open with… adoption, or its Undo, whose steps are pending (State: A live failure,
/// Files; G12). An adoption: the previous picture until it leaves the root, nothing listed while the adopted file is in
/// <c>.work\</c>, and the adopted file once it has landed, its facts and SHA-256 those its place step carries until the
/// adoption's last transaction writes them on the row (<see cref="ItemAdoption"/>). Its Undo: the adopted picture until
/// it is set aside, nothing listed until the previous picture is back, and the previous picture after, described by the
/// steps that bring it back until the Undo's last transaction (<see cref="ItemAdoptionUndo"/>).
/// </summary>
internal static class AdoptionSteps
{
    /// <summary>A row carrying an adoption's steps, whatever else it carries: its recovery is the adoption's.</summary>
    public static bool IsAdoption(IReadOnlyList<PendingStep> steps) => steps.Any(step => step.Step == PendingStep.PlaceAdopted);

    /// <summary>A row carrying the steps of an adoption's Undo, from its commit to its last transaction: its recovery is the Undo's.</summary>
    public static bool IsUndo(IReadOnlyList<PendingStep> steps) => steps.Any(step => step.Step == PendingStep.SetAsideAdopted);

    /// <summary>
    /// Whether a row waiting on an adoption's steps, or its Undo's, is in the stash: while the picture it replaces is
    /// still in the root, and once its own has landed; not between, when the item's picture is in <c>.work\</c>, where
    /// nothing is an item. Null for a row with neither pending.
    /// </summary>
    public static bool? ListsWhilePending(StashFolder folder, IReadOnlyList<PendingStep> steps)
    {
        if (steps.FirstOrDefault(step => step.Step == PendingStep.PlaceAdopted) is { } place)
        {
            return place.IsDone(folder) || steps.Any(step => step.Step is PendingStep.HoldInWork or PendingStep.RemoveRender && !step.IsDone(folder));
        }

        return steps.FirstOrDefault(step => step.Step == PendingStep.SetAsideAdopted) is { } aside
            ? !aside.IsDone(folder, steps) || Restored(folder, steps) is not null
            : null;
    }

    /// <summary>
    /// The adoption's place step once its file has landed: the item is then that file, its original and finished picture,
    /// described by the step's facts and SHA-256 until the last transaction writes them on the row. Null otherwise.
    /// </summary>
    public static PendingStep? Landed(StashFolder folder, IReadOnlyList<PendingStep> steps) =>
        steps.FirstOrDefault(step => step.Step == PendingStep.PlaceAdopted) is { } place && place.IsDone(folder) ? place : null;

    /// <summary>
    /// An Undo's restore or place step once the previous picture has landed in the root: the item is then that picture,
    /// described by the step's facts, at the layer revision. Null otherwise.
    /// </summary>
    public static PendingStep? Restored(StashFolder folder, IReadOnlyList<PendingStep> steps) =>
        PendingStep.Restoring(steps) is { } restoring && restoring.IsDone(folder) ? restoring : null;

    /// <summary>
    /// The step that says where the original is, with its record and SHA-256, while the row's columns lag behind its
    /// picture on disk: an adoption's landed place step; or, once an Undo's previous picture has landed, the restore of
    /// an unedited original, the one file, or the rename in <c>originals\</c> of an edited one. Null otherwise.
    /// </summary>
    public static PendingStep? OriginalOnDisk(StashFolder folder, IReadOnlyList<PendingStep> steps) =>
        Landed(folder, steps)
        ?? (Restored(folder, steps) is null ? null : steps.First(step => step.Step is PendingStep.RestoreHeld or PendingStep.RenameInOriginals));

    /// <summary>Where the original <see cref="OriginalOnDisk"/> names is: in <c>originals\</c> where its rename has it, else at its step's name in the root.</summary>
    public static string PathOf(StashFolder folder, PendingStep original) =>
        original.Step == PendingStep.RenameInOriginals ? original.Located(folder) : original.ToPath(folder);

    /// <summary>The SHA-256 a step carries, as the row's column holds a hash.</summary>
    public static byte[] Sha256Of(PendingStep step) => Convert.FromHexString(step.Sha256!);

    /// <summary>
    /// Whether these steps make bytes of <paramref name="sha256"/> the original before the row's column says so: a
    /// pending adoption adopting them, or an Undo whose previous picture has landed restoring them. A candidate for the
    /// duplicate check (G12).
    /// </summary>
    public static bool MakeOriginal(StashFolder folder, IReadOnlyList<PendingStep> steps, byte[] sha256)
    {
        var hex = Convert.ToHexString(sha256);
        return steps.Any(step => step.Step == PendingStep.PlaceAdopted && step.Sha256 == hex) || Restored(folder, steps)?.Sha256 == hex;
    }
}
