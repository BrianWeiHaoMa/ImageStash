using Avalonia;
using Avalonia.Controls;

namespace ImageStash.Look;

/// <summary>
/// Whether a control carries a tooltip, and the only way one is set (F10, the look's one home): a control whose visible
/// words are its action carries none, since a tip would repeat them; a control shown as a glyph alone carries one naming
/// its action; a tip that adds what the words do not say (a key, the role of a word that changes, the whole of a text
/// trimmed to fit) stays. Where a tip opens is <c>Window/ToolTips.cs</c>'s, for every tip set here.
/// </summary>
public static class ToolTipRule
{
    /// <summary>
    /// Gives <paramref name="control"/> <paramref name="tip"/>, or no tip when it repeats words the control shows, read
    /// from its content as it is now: set the content first.
    /// </summary>
    public static void Set(Control control, string? tip) => ToolTip.SetTip(control, Decide(Shown(control), tip));

    /// <summary>Gives <paramref name="control"/>, which shows a text that may be trimmed or wrapped to fit, that whole text as its tip; none for none.</summary>
    public static void ShowWhole(Control control, string? whole) => ToolTip.SetTip(control, string.IsNullOrWhiteSpace(whole) ? null : whole);

    /// <summary>
    /// Gives <paramref name="control"/>, whose words <paramref name="shown"/> are its action and may be trimmed to fit,
    /// <paramref name="whole"/> as its tip only while they are cut short: shown whole, a tip would repeat them; cut short,
    /// it adds the tail they hide. Decided again whenever the words are laid out anew.
    /// </summary>
    public static void ShowWhenTrimmed(Control control, TextBlock shown, string? whole)
    {
        void Decide() => ToolTip.SetTip(control, !string.IsNullOrWhiteSpace(whole) && shown.TextLayout.TextLines.Any(line => line.HasCollapsed) ? whole : null);
        shown.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty || e.Property == TextBlock.TextProperty)
            {
                Decide();
            }
        };
        Decide();
    }

    /// <summary>
    /// The tip a control showing <paramref name="shown"/> carries for <paramref name="tip"/>: none when the tip is empty or
    /// repeats one of them, letter for letter whatever the case and the spacing (a button showing "1 : 1" named "1:1").
    /// </summary>
    public static string? Decide(IEnumerable<string> shown, string? tip) =>
        string.IsNullOrWhiteSpace(tip) || shown.Any(words => string.Equals(Letters(words), Letters(tip), StringComparison.OrdinalIgnoreCase)) ? null : tip;

    private static string Letters(string words) => string.Concat(words.Where(letter => !char.IsWhiteSpace(letter)));

    /// <summary>The words a control or its content shows: a string, a text block's text, and those of the children it holds.</summary>
    private static IEnumerable<string> Shown(object? content) => content switch
    {
        string words => [words],
        TextBlock text => text.Text is { } words ? [words] : [],
        ContentControl control => Shown(control.Content),
        Decorator decorator => Shown(decorator.Child),
        Panel panel => panel.Children.SelectMany(child => Shown(child)),
        _ => [],
    };
}
