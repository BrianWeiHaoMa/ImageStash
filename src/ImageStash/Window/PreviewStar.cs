using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ImageStash.Look;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The preview's star (G19, G17): a glyph button in its head before ⋯, filled in the accent while the item it shows is a
/// favorite and outlined while it is not; a click toggles the flag as <c>Ctrl+D</c> does there. It shows only the item
/// <see cref="Show"/> hands it, the one the preview opened with or last read, so it never flips ahead of the store (R9,
/// G28) and follows the item at every read and every move to another. One of the preview's buttons, it is in its Tab
/// order, where Space presses it and focus stays after the flag changes, with a name saying the state (G7).
/// </summary>
public sealed class PreviewStar
{
    private readonly TextBlock glyph;
    private IDisposable? accent;

    /// <param name="toggle">The preview's toggle of its item's flag, the one <c>Ctrl+D</c> runs there.</param>
    public PreviewStar(Action toggle)
    {
        glyph = IconGlyph.Of(FavoriteStar.Outlined, 16);
        Button = new Button
        {
            Content = glyph,
            Width = 36,
            Height = 36,
            Padding = default,
            BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Button.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetAutomationId(Button, "Favorite");
        Button.Click += (_, _) => toggle();
        Show(null);
    }

    public Button Button { get; }

    /// <summary>Whether it shows the star filled.</summary>
    public bool Filled { get; private set; }

    /// <summary>The flag of <paramref name="item"/> as handed, outlined for none; only the content changes, so focus stays on it.</summary>
    public void Show(StashItem? item)
    {
        var favorite = item?.Favorite == true;
        Filled = favorite;
        glyph.Text = FavoriteStar.Glyph(favorite);
        accent?.Dispose();
        accent = favorite ? glyph.Bind(TextBlock.ForegroundProperty, glyph.GetResourceObservable(LookKeys.FavoriteStar)) : null;
        AutomationProperties.SetName(Button, Name(favorite));
        ToolTipRule.Set(Button, Tip(favorite));
    }

    /// <summary>What a screen reader reads for it: the state.</summary>
    public static string Name(bool favorite) => favorite ? "In Favorites" : "Not in Favorites";

    /// <summary>Its tooltip: what a click does, and the key that does the same.</summary>
    public static string Tip(bool favorite) => favorite ? "Remove from Favorites (Ctrl+D)" : "Add to Favorites (Ctrl+D)";
}
