using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// A shortcut's keys drawn as key caps, one cap per key of its text, in the look's key-cap keys (F10): Settings' shortcut
/// fields, the empty stash's rows and the footer's hints draw them here alone, so they read alike in every take; the
/// footer's caps are smaller, to sit in its row.
/// </summary>
public static class KeyCaps
{
    /// <summary>The keys of <paramref name="text"/>, a shortcut as <c>Shortcut.Text</c> writes it, in order.</summary>
    public static IReadOnlyList<string> Keys(string text) => text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The height of a cap in the footer's row, which is 36 high.</summary>
    public const double FooterHeight = 20;

    /// <summary>A row of the caps of <paramref name="text"/>.</summary>
    public static StackPanel Of(string text)
    {
        var row = Row();
        Show(row, text);
        return row;
    }

    /// <summary>An empty row for caps, which <see cref="Show"/> fills.</summary>
    public static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The caps of <paramref name="text"/> in <paramref name="row"/>, in place of what it held.</summary>
    public static void Show(StackPanel row, string text) => Fill(row, text, Size.Field);

    /// <summary>The caps of <paramref name="text"/>, at the footer's size, in <paramref name="row"/>, in place of what it held.</summary>
    public static void ShowInFooter(StackPanel row, string text) => Fill(row, text, Size.Footer);

    private static void Fill(StackPanel row, string text, Size size)
    {
        row.Children.Clear();
        foreach (var key in Keys(text))
        {
            row.Children.Add(Cap(key, size));
        }
    }

    private enum Size
    {
        Field,
        Footer,
    }

    /// <summary>One key of a shortcut, drawn as a key cap.</summary>
    private static Border Cap(string name, Size size)
    {
        var footer = size == Size.Footer;
        var cap = new Border
        {
            Child = new TextBlock { Text = name, FontSize = footer ? 11 : 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            MinWidth = footer ? 22 : 30,
            Height = footer ? FooterHeight : 24,
            Padding = new Thickness(footer ? 6 : 8, 0),
            BorderThickness = new Thickness(1),
        };
        ((TextBlock)cap.Child).HorizontalAlignment = HorizontalAlignment.Center;
        cap.Bind(Border.BackgroundProperty, cap.GetResourceObservable(LookKeys.KeyCapGround));
        cap.Bind(Border.BorderBrushProperty, cap.GetResourceObservable(LookKeys.KeyCapBorder));
        cap.Bind(Border.CornerRadiusProperty, cap.GetResourceObservable(LookKeys.ControlRadius));
        cap.Child.Bind(TextBlock.ForegroundProperty, cap.GetResourceObservable(LookKeys.KeyCapWords));
        return cap;
    }
}
