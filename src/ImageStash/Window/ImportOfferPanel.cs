using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ImageStash.Window;

/// <summary>
/// The import's offer under the Import row's words, inside the Settings page, since F4 admits no other window (G36): the
/// offer's words, then Export current stash first…, which runs the row's Export stash… and leaves the offer standing,
/// Replace and Cancel, each named for automation by its words and wrapping when the row is narrow. No button carries the
/// accent and none is the default, so neither the look nor Enter invites the click that replaces the stash (F10). Shown
/// only while a chosen file waits for confirmation.
/// </summary>
public sealed class ImportOfferPanel : StackPanel
{
    private readonly SettingsPage page;
    private readonly TextBlock words = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };

    /// <param name="owner">The page's window, which owns the export's save picker.</param>
    public ImportOfferPanel(SettingsPage page, TopLevel owner)
    {
        this.page = page;
        Spacing = 8;
        Margin = new Thickness(0, 8, 0, 0);
        Buttons =
        [
            NamedButton.Of(SettingsPage.ExportFirstWords, () => _ = page.Export(owner)),
            NamedButton.Of(SettingsPage.ConfirmImportWords, () => _ = page.ConfirmImport()),
            NamedButton.Of(SettingsPage.CancelImportWords, page.CancelImport),
        ];
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        foreach (var button in Buttons)
        {
            row.Children.Add(button);
        }

        Children.Add(words);
        Children.Add(row);
        Show();
    }

    /// <summary>Export current stash first…, Replace and Cancel, in that order.</summary>
    public IReadOnlyList<Button> Buttons { get; }

    /// <summary>The offer's words as they are now, shown while there is an offer.</summary>
    public void Show()
    {
        words.Text = page.ImportOffer;
        IsVisible = page.ImportOffer is not null;
    }
}
