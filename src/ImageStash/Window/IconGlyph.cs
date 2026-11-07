using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ImageStash.Window;

/// <summary>A Segoe Fluent Icons glyph as the stash window shows it in its head, on its pin and in its states, and every card its × (F10), centred in its place.</summary>
public static class IconGlyph
{
    private static readonly FontFamily Icons = new("Segoe Fluent Icons");

    public static TextBlock Of(string glyph, double size, Thickness margin = default) =>
        new() { Text = glyph, FontFamily = Icons, FontSize = size, Margin = margin, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
}
