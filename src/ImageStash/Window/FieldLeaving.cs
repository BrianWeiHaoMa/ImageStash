using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The one rule for the preview's name in edit, the preview's tag box and the "Tags of …" field as focus moves (G17,
/// G19): focus going to another part of the window commits the edit as Enter would, Esc staying the only cancel. Each
/// field asks <see cref="Commits"/> from its focus handler and commits through its Enter's own path, on the items it
/// was opened on, taken there before any wait, without Enter's giving focus back, so focus stays where it went (G7).
/// Whoever moves focus, the edit is kept: a click, Tab, a click on the preview's name putting focus in its box, a summon
/// putting it in the search box. Only focus moving inside the window reaches a field: a hide, or another app taking
/// focus, moves none, so the edit outlasts it (G1). Esc, and the window closing a field itself (the preview closing,
/// an import replacing the stash), end the edit before focus moves, so that move meets no edit and commits nothing;
/// the preview closing to show an import's result commits its edit first, as its ‹ and › do
/// (<see cref="Preview.CommitDraft"/>).
/// </summary>
public static class FieldLeaving
{
    /// <summary>
    /// Whether focus landing on an element commits a field's edit: <paramref name="holdsEdit"/>, whether the field holds
    /// one (a name in edit, a tag box with focus last in it, the "Tags of …" field open); <paramref name="intoField"/>,
    /// whether the element is inside the field, its box or its own parts, where the edit goes on.
    /// </summary>
    public static bool Commits(bool holdsEdit, bool intoField) => holdsEdit && !intoField;

    /// <summary>
    /// A tag field's commit, Enter's and focus leaving's alike: every one of <paramref name="items"/> given the tags
    /// <paramref name="text"/> holds, one per word, through the window's one organising entry, which names each refusal
    /// (G35); nothing typed, or only <c>#</c>s, changes nothing (G17: nothing requires a tag). While no other tag or
    /// favorite change runs, the store's change is asked before this returns, so an act asked after it reaches the store
    /// after it.
    /// </summary>
    public static Task<Outcome> AddTags(Organising organising, IReadOnlyList<StashItem> items, string text) =>
        ItemTags.Words(text).Count > 0 ? organising.AddTag(items, text) : Task.FromResult(Outcome.Done);
}
