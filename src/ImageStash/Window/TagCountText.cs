using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ImageStash.Window;

/// <summary>
/// The number of items in the stash that carry a tag, as every list a person picks a tag from shows it (G17, G4): after
/// the tag's words, faint, at the end of the entry.
/// </summary>
internal static class TagCountText
{
    /// <summary>The room between a tag's words and its number.</summary>
    public const double Gap = 10;

    /// <summary>The number as it reads.</summary>
    public static string Of(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>A block for a number, to be given its text.</summary>
    public static TextBlock Block() => new()
    {
        TextAlignment = TextAlignment.Right,
        Opacity = 0.7,
        Margin = new Thickness(Gap, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A tag's words with its number after them, as a menu entry's or a suggestion's content.</summary>
    public static Control Beside(string tag, int count)
    {
        var number = Block();
        number.Text = Of(count);
        Grid.SetColumn(number, 1);
        return new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Auto)],
            Children = { new TextBlock { Text = tag, VerticalAlignment = VerticalAlignment.Center }, number },
        };
    }
}
