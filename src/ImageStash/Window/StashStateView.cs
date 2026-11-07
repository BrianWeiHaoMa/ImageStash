using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// What replaces the stash window's grid, and the footer's count (G2, G5, G35): the opening, a stash that did not
/// open, the empty stash listing every way in, no match with Clear, no favorites and an empty Recent, each distinct from
/// the others and keyed, so the window builds a view again only when the state it shows changes.
/// </summary>
public static class StashStateView
{
    /// <summary>What replaces the grid while an import replaces the stash, nothing of which shows meanwhile (G35, G36).</summary>
    public const string ReplacingWords = "Importing your stash…";

    /// <summary>The footer's count of the items shown and, with several selected, of the selection.</summary>
    public static string CountText(int items, int selected) =>
        (items == 1 ? "1 item" : $"{items} items") + (selected > 1 ? $" · {selected} selected" : "");

    /// <summary>
    /// Which state replaces the grid, if any: the opening, a stash that did not open, an import replacing it, the empty
    /// stash, no match, no favorites, an empty Recent.
    /// </summary>
    public static string? Key(Listing listing) => listing.State switch
    {
        StashState.Opening => "opening",
        StashState.Failed => "failed",
        StashState.Replacing => "replacing",
        _ when listing.IsEmptyStash => "empty",
        _ when listing.NoMatch => "no match " + listing.Query,
        _ when listing.Settled && !listing.IsSearch && listing.Items.Count == 0 && listing.Scope == Scope.Favorites => "no favorites",
        _ when listing.Settled && !listing.IsSearch && listing.Items.Count == 0 => "nothing recent",
        _ => null,
    };

    /// <summary>The view of the state <see cref="Key"/> names; <paramref name="clearSearch"/> is no match's Clear.</summary>
    public static Control For(Listing listing, SettingsKeeper settings, Action clearSearch)
    {
        var view = new StackPanel { Spacing = 8, MaxWidth = 340 };
        if (listing.State == StashState.Opening)
        {
            // G35: a busy state names its action and shows nothing of the stash until it has opened.
            view.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 160, HorizontalAlignment = HorizontalAlignment.Center });
            view.Children.Add(Line("Opening your stash…", 14));
        }
        else if (listing.State == StashState.Replacing)
        {
            view.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 160, HorizontalAlignment = HorizontalAlignment.Center });
            view.Children.Add(Line(ReplacingWords, 14));
        }
        else if (listing.State == StashState.Failed)
        {
            view.Children.Add(Line("Your stash could not be opened", 16, FontWeight.SemiBold));
            view.Children.Add(Line(listing.OpenFailure!.Sentence, 12.5));
        }
        else if (listing.IsEmptyStash)
        {
            // G2: every way in, each with its shortcut.
            view.Children.Add(Line("Your stash is empty", 20, FontWeight.SemiBold));
            view.Children.Add(Line("Drop images and GIFs here, from a browser, a chat or File Explorer", 13));
            view.Children.Add(Line("Paste a copied image    Ctrl+V", 13));
            view.Children.Add(Line($"Take a screenshot    {ShortcutOrTray(settings.Current.Shortcuts.Screenshot)}", 13));
            view.Children.Add(Line($"Record a clip    {ShortcutOrTray(settings.Current.Shortcuts.Record)}", 13));
            view.Children.Add(Line($"Stash the clipboard image    {ShortcutOrTray(settings.Current.Shortcuts.StashClipboardImage)}", 13));
            view.Children.Add(Line("Import files    Ctrl+O", 13));
        }
        else if (listing.NoMatch)
        {
            var clear = new Button { Content = "Clear search", HorizontalAlignment = HorizontalAlignment.Center, Focusable = false };
            clear.Click += (_, _) => clearSearch();
            view.Children.Add(IconGlyph.Of("", 28));
            view.Children.Add(Line($"Nothing matches “{listing.Query}”", 16, FontWeight.SemiBold));
            view.Children.Add(Line("Every word has to appear in a name or a tag. Try fewer words.", 12.5));
            view.Children.Add(clear);
        }
        else if (listing.Scope == Scope.Favorites)
        {
            // Distinct from the empty stash and from no match (G2, G5): the stash holds items, none of them a favorite.
            view.Children.Add(Line("No favorites yet", 16, FontWeight.SemiBold));
            view.Children.Add(Line("Press Ctrl+D on a picture, or drag it onto Favorites.", 12.5));
        }
        else
        {
            view.Children.Add(Line("Nothing used yet", 14));
        }

        return view;
    }

    /// <summary>A global shortcut as the settings read it, as the tray menu shows it; the tray's entry while that is not a shortcut.</summary>
    private static string ShortcutOrTray(string setting) => Shortcut.Parse(setting).Value?.Text ?? "from the tray menu";

    private static TextBlock Line(string text, double size, FontWeight weight = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
}
