using Avalonia.Controls;

namespace ImageStash.Window;

internal static class SearchBoxQuery
{
    public static void Wire(TextBox search, Listing listing)
    {
        listing.QueryChangedByWindow += () =>
        {
            search.Text = listing.Query;
            search.SelectionStart = search.SelectionEnd = search.CaretIndex = listing.Query.Length;
        };

        // TextChanging runs within the edit. By the posted TextChanged event an import may have reset the query.
        search.TextChanging += (_, _) =>
        {
            var text = search.Text ?? "";
            if (text != listing.Query)
            {
                _ = listing.SetQuery(text);
            }
        };
    }
}
