using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ImageStash.Look;

/// <summary>
/// The look as a control that draws itself reads it: a key's value for the theme it shows under now, read while it
/// renders. The control repaints whenever the application's take or its theme changes, so a hand-drawn surface (the
/// grid's tiles, the pictures' surround) follows a take applied at once as bound controls do (G32), not at its next
/// repaint for some other reason.
/// </summary>
public sealed class DrawnLook
{
    private readonly Control owner;

    public DrawnLook(Control owner)
    {
        this.owner = owner;
        owner.ResourcesChanged += (_, _) => owner.InvalidateVisual();
        owner.ActualThemeVariantChanged += (_, _) => owner.InvalidateVisual();
    }

    /// <summary>The brush of <paramref name="key"/>, or none before the control can see a take.</summary>
    public IBrush? Brush(string key) => Find(key) as IBrush;

    /// <summary>The colour of <paramref name="key"/>'s solid brush, or a clear one.</summary>
    public Color Color(string key) => Brush(key) is ISolidColorBrush solid ? solid.Color : default;

    /// <summary>The radius of <paramref name="key"/> for a drawn rounded rectangle, its top-left corner's; none before a take.</summary>
    public double Radius(string key) => Find(key) is CornerRadius radius ? radius.TopLeft : 0;

    /// <summary>The width of <paramref name="key"/>'s thickness for a drawn mark, its left side's; none before a take.</summary>
    public double Width(string key) => Find(key) is Thickness thickness ? thickness.Left : 0;

    /// <summary>A wash of <paramref name="brush"/>'s colour at <paramref name="part"/> of its strength: a range's ground under its mark, a text selection.</summary>
    public static IBrush? Wash(IBrush? brush, double part) => brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, solid.Opacity * part) : null;

    private object? Find(string key) => owner.TryFindResource(key, owner.ActualThemeVariant, out var value) ? value : null;
}
