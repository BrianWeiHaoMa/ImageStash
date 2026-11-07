using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Layers;
using Thickness = Avalonia.Thickness;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The buttons the editor's rows are made of, in Windows 11's look (F10): a button of an icon and words with the key
/// that does the same faint after them, a choice among others of its row, a colour swatch, each named for a screen
/// reader by its words.
/// </summary>
internal static class EditorButtons
{
    private static readonly FontFamily Icons = new("Segoe Fluent Icons");

    /// <summary>The More swatch's fill while it holds no picked colour.</summary>
    private static readonly IBrush Rainbow = new ConicGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Colors.Red, 0), new GradientStop(Colors.Yellow, 1 / 6d), new GradientStop(Colors.Lime, 2 / 6d), new GradientStop(Colors.Cyan, 3 / 6d),
            new GradientStop(Colors.Blue, 4 / 6d), new GradientStop(Colors.Magenta, 5 / 6d), new GradientStop(Colors.Red, 1),
        },
    };

    /// <summary>A Segoe Fluent Icons glyph, centred in its place.</summary>
    public static TextBlock Glyph(string glyph, double size) =>
        new() { Text = glyph, FontFamily = Icons, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>A button of an optional icon, its words and, faint after them, the key that does the same.</summary>
    public static Button Labelled(string? glyph, string words, string? key = null, bool accent = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (glyph is not null)
        {
            content.Children.Add(Glyph(glyph, 14));
        }

        content.Children.Add(new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center });
        if (key is not null)
        {
            content.Children.Add(new TextBlock { Text = key, FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        }

        var button = new Button { Content = content, Height = 34 };
        if (accent)
        {
            button.Classes.Add("accent");
        }

        AutomationProperties.SetName(button, words);
        return button;
    }

    /// <summary>A button of an icon alone, named and explained by <paramref name="words"/>.</summary>
    public static Button Icon(string glyph, string words, string? tip = null)
    {
        var button = new Button
        {
            Content = Glyph(glyph, 16),
            Width = 36,
            Height = 36,
            Padding = default,
            BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(button, words);
        ToolTipRule.Set(button, tip ?? words);
        return button;
    }

    /// <summary>
    /// One choice of a row of choices: checked while it is the one shown, choosing it when clicked; a tip naming it only
    /// when <paramref name="content"/> shows no words of its own (a dot, a square).
    /// </summary>
    public static ToggleButton Choice(object content, string words, Action chosen)
    {
        var choice = new ChoiceButton { Content = content, Height = 32, MinWidth = 32, Padding = new Thickness(10, 0), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(choice, words);
        ToolTipRule.Set(choice, words);
        choice.Click += (_, _) => chosen();
        return choice;
    }

    /// <summary>A swatch: a square of <paramref name="color"/>, a choice named by its hex.</summary>
    public static ToggleButton Swatch(Rgba color, Action chosen) =>
        Choice(Square(new SolidColorBrush(ToColor(color))), ColorChoices.Hex(color), chosen);

    /// <summary>A square of a fill, the inside of a swatch.</summary>
    public static Border Square(IBrush fill)
    {
        var square = new Border { Width = 18, Height = 18, Background = fill, BorderThickness = new Thickness(1) };
        square.Bind(Border.BorderBrushProperty, square.GetResourceObservable(LookKeys.SwatchEdge));
        square.Bind(Border.CornerRadiusProperty, square.GetResourceObservable(LookKeys.SmallRadius));
        return square;
    }

    public static Color ToColor(Rgba color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>The More swatch, named <paramref name="words"/>, which opens the colour picker beside itself; its square is <see cref="ShowPicked"/>'s to fill.</summary>
    public static (ToggleButton Button, Border Fill) More(string words, Action<Control> clicked)
    {
        var fill = Square(Rainbow);
        var more = new ChoiceButton { Content = fill, Height = 32, MinWidth = 32, Padding = new Thickness(6, 0) };
        AutomationProperties.SetName(more, words);
        ToolTipRule.Set(more, words);
        more.Click += (_, _) => clicked(more);
        return (more, fill);
    }

    /// <summary>A colour that is no swatch shows inside the More swatch, which is then the one checked (G21); else the rainbow.</summary>
    public static void ShowPicked(ToggleButton more, Border fill, Rgba shown)
    {
        var picked = !ColorChoices.Swatches.Contains(shown);
        more.IsChecked = picked;
        fill.Background = picked ? new SolidColorBrush(ToColor(shown)) : Rainbow;
    }

    /// <summary>Sets every choice's check to whether it is the one shown.</summary>
    public static void Check<T>(IEnumerable<(T Value, ToggleButton Button)> choices, T shown)
    {
        foreach (var (value, button) in choices)
        {
            button.IsChecked = EqualityComparer<T>.Default.Equals(value, shown);
        }
    }

    /// <summary>
    /// A choice whose check shows only its row's answer: a click never flips it, the row checks what is shown once the
    /// click has chosen (Refresh, <see cref="Check"/>, <see cref="ShowPicked"/>). A second click on the current choice so
    /// leaves it checked, while a true toggle (Crop's 1 : 1) still turns off, because its row's answer does.
    /// </summary>
    private sealed class ChoiceButton : ToggleButton
    {
        // Dressed as Fluent dresses every toggle button.
        protected override Type StyleKeyOverride => typeof(ToggleButton);

        protected override void Toggle()
        {
        }
    }
}
