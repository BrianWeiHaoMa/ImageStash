using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ImageStash.Look;

namespace ImageStash.Windows.Tests.Support;

/// <summary>The look as a control sees it now: a key's colour in the take applied, for the theme the control shows under.</summary>
public static class AppliedLook
{
    /// <summary>The colour <paramref name="key"/>'s solid brush has for <paramref name="at"/> now. UI thread.</summary>
    public static Color Of(StyledElement at, string key) =>
        at.TryFindResource(key, at.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : throw new AssertFailedException($"{key} is no brush of the take applied, under {at.ActualThemeVariant}");

    /// <summary>The selection's mark, the editor's handles' colour (F10), as the stash window shows it now, opaque, as ARGB in a <c>uint</c>.</summary>
    public static uint SelectionMark(ShownWindow shown) => TrayDesktop.Get(() =>
    {
        var mark = Of(shown.Window, LookKeys.SelectionRing);
        return 0xFF000000u | ((uint)mark.R << 16) | ((uint)mark.G << 8) | mark.B;
    });
}
