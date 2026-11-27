namespace ImageStash.Window;

/// <summary>
/// A list under the pointer holds its order (G4, G17): while it stays in view, the entries it held keep their places
/// in the order they had, and an entry that has come since joins the end, in the order it comes, so nothing a person
/// is about to click moves under the pointer. Recent's order, the tag chips' places and every open list of tags take
/// their order through this rule; what a held entry shows meanwhile (its count, its mark, whether a gone one keeps its
/// place) is each list's own, given here as the entries already brought up to date.
/// </summary>
internal static class HeldOrder
{
    /// <summary>
    /// <paramref name="held"/>, each entry once, in its order; then each of <paramref name="now"/> whose key none of
    /// them has, in the order of <paramref name="now"/>.
    /// </summary>
    public static List<T> Kept<T, TKey>(IEnumerable<T> held, IEnumerable<T> now, Func<T, TKey> keyOf, IEqualityComparer<TKey>? same = null)
        where TKey : notnull
    {
        var places = new HashSet<TKey>(same);
        var kept = new List<T>();
        foreach (var entry in held.Concat(now))
        {
            if (places.Add(keyOf(entry)))
            {
                kept.Add(entry);
            }
        }

        return kept;
    }
}
