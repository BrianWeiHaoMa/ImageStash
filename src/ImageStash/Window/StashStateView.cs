using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// What replaces the stash window's grid, and the footer's count (G2, G5, G35): the opening, a stash that did not
/// open, the empty stash listing every way in as rows a click runs, no match with Clear, no favorites and an empty Recent,
/// each distinct from the others and keyed, so the window builds a view again only when the state it shows changes.
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

    /// <summary>
    /// The view of the state <see cref="Key"/> names; <paramref name="clearSearch"/> is no match's Clear, and
    /// <paramref name="waysIn"/> the acts the empty stash's rows run, and <paramref name="unavailable"/> whether an act's
    /// shortcut is unavailable now, as the policy judges it.
    /// </summary>
    public static Control For(Listing listing, SettingsKeeper settings, Action clearSearch, WaysInActs waysIn, Func<ShortcutAct, bool> unavailable)
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
            // G2: every way in, each with its shortcut, as rows a click runs; then how ImageStash opens again (G3).
            var shortcuts = settings.Current.Shortcuts;
            view.Children.Add(Line(EmptyStashWaysIn.Heading, 20, FontWeight.SemiBold));
            view.Children.Add(Line(EmptyStashWaysIn.DropLine, 13));
            var rows = new StackPanel();
            rows.Children.AddRange(EmptyStashWaysIn.Of(shortcuts, waysIn, unavailable).Select(Row));
            view.Children.Add(rows);
            view.Children.Add(Line(EmptyStashWaysIn.OpenLine(shortcuts, unavailable(ShortcutAct.Open)), 12.5));
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

    /// <summary>
    /// A way in as a row: its icon, then its words, left, and its shortcut's key caps right, followed by the word
    /// unavailable while it is, or the tray menu's words while its setting is no shortcut. A click runs its act;
    /// unfocusable, as the <c>+</c> button is, so the keyboard's focus stays where it was (G7).
    /// </summary>
    private static Button Row(WayIn way)
    {
        var icon = IconGlyph.Of(way.Glyph, 16, new Thickness(0, 0, 10, 0));
        var words = new TextBlock { Text = way.Words, FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(words, 1);
        Control keys = way.Shortcut is { } text
            ? KeyCaps.Of(text)
            : new TextBlock { Text = EmptyStashWaysIn.FromTrayMenu, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        if (way.ShortcutUnavailable && keys is StackPanel caps)
        {
            caps.Children.Add(new TextBlock { Text = ShortcutWords.UnavailableBesideKeys, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        }
        keys.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(keys, 2);
        var row = new Button
        {
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { icon, words, keys } },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 3),
            Focusable = false,
        };
        row.Classes.Add(LookStyles.FlatRow);
        AutomationProperties.SetName(row, way.Words);
        row.Click += (_, _) => way.Act();
        return row;
    }

    private static TextBlock Line(string text, double size, FontWeight weight = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
}
