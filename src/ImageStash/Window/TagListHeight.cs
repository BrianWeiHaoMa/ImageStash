using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;

namespace ImageStash.Window;

/// <summary>
/// How tall a list of tags stands (G4, G17: "A list longer than fits scrolls"): the chip row's and the tag field's list
/// of every tag, through <see cref="TagPicker"/>, and the item menu's Add tag ▸ and Remove tag ▸, through
/// <see cref="ItemMenuLists"/>. At most <see cref="Tallest"/>, the chip row's list as it stands in the window, and
/// inside the display's working area less a margin; a list shorter than that stands as tall as its entries.
/// </summary>
public static class TagListHeight
{
    /// <summary>The class the item menu's two tag lists carry, which <see cref="ItemMenuLists"/> dresses.</summary>
    public const string ItemMenuList = "tagList";

    /// <summary>The tallest a list stands, in device-independent units, on a display with room for that.</summary>
    public const double Tallest = 360;

    /// <summary>The most a list stands on a display whose working area is <paramref name="workingAreaHeight"/> device-independent units high.</summary>
    public static double For(double workingAreaHeight) => Math.Clamp(workingAreaHeight - 40, 40, Tallest);

    /// <summary>The most a list beside <paramref name="anchor"/> stands, on the display it is on; <see cref="Tallest"/> while that is unknown.</summary>
    public static double For(Visual anchor) =>
        TopLevel.GetTopLevel(anchor)?.Screens?.ScreenFromVisual(anchor) is { } screen ? For(screen.WorkingArea.Height / screen.Scaling) : Tallest;

    /// <summary>
    /// The style that caps the item menu's tag lists at <paramref name="height"/> and gives their scroller a scroll bar:
    /// Fluent's submenu scroller has only arrows, so the list's scroller takes the default <see cref="ScrollViewer"/>
    /// theme, whose bar the look draws as every other (G17).
    /// </summary>
    public static Style ItemMenuLists(double height)
    {
        var style = new Style(x => x.OfType<MenuItem>().Class(ItemMenuList).Template().OfType<ScrollViewer>());
        style.Setters.Add(new Setter(Layoutable.MaxHeightProperty, height));
        style.Setters.Add(new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension(typeof(ScrollViewer))));
        return style;
    }
}
