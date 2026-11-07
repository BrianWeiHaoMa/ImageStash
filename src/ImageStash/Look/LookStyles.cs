using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace ImageStash.Look;

/// <summary>
/// The rules that dress Fluent's buttons in the take's keys by class: a control takes its look by carrying the class,
/// never by brushes of its own (G32, the one home for the look). Each rule reads a key of <see cref="LookKeys"/>
/// through a dynamic resource, so a take applied later restyles every control at once. Added once, by
/// <see cref="Looks.Apply(Application, Take, Avalonia.Media.Color)"/>; Fluent's own rule for a state applies where none here does.
/// </summary>
public sealed class LookStyles : Styles
{
    /// <summary>Recent, Favorites and All in the chip row.</summary>
    public const string ScopeChip = "scopeChip";

    /// <summary>A tag's chip in the chip row, and a suggestion in the tag field.</summary>
    public const string TagChip = "tagChip";

    /// <summary>A chip while lit, scope or tag: Fluent's accent class, which these rules dress in the take's keys.</summary>
    public const string Lit = "accent";

    /// <summary>A button of an icon alone, without a ground until hovered: the head's gear, + and pin, the editor's icons.</summary>
    public const string IconButton = "iconButton";

    private const string Presenter = "PART_ContentPresenter";

    /// <summary>The thin thumb: six pixels of Fluent's sixteen-pixel strip, two in from its edge, as Fluent's own two-pixel thumb stands.</summary>
    private const double ThumbThickness = 6, ThumbInset = 2;

    /// <summary>The thumb's resting colour, Fluent's own, which Fluent swaps for a fainter one while it expands the bar.</summary>
    private const string ThumbGround = "ScrollBarPanningThumbBackground";

    public LookStyles()
    {
        Chip(ScopeChip, LookKeys.ScopeChipGround, LookKeys.ScopeChipBorder, LookKeys.ScopeChipWords);
        Chip(TagChip, LookKeys.TagChipGround, LookKeys.TagChipBorder, LookKeys.TagChipWords);
        Hovered(ScopeChip, LookKeys.ScopeChipHoverGround, LookKeys.ScopeChipHoverBorder, LookKeys.ScopeChipHoverWords);
        Hovered(TagChip, LookKeys.TagChipHoverGround, LookKeys.TagChipBorder, LookKeys.TagChipWords);
        foreach (var chip in new[] { ScopeChip, TagChip })
        {
            // After the unlit rules, so a lit chip's own keys win, hovered or not; on the template's presenter, where
            // Fluent's own rule for its accent class sets them.
            Add(Rule(x => x.OfType<Button>().Class(chip).Class(Lit).Template().OfType<ContentPresenter>().Name(Presenter),
                (ContentPresenter.BackgroundProperty, LookKeys.LitChipGround),
                (ContentPresenter.BorderBrushProperty, LookKeys.LitChipBorder),
                (ContentPresenter.ForegroundProperty, LookKeys.LitChipWords)));
            foreach (var state in new[] { ":pointerover", ":pressed" })
            {
                Add(Rule(x => x.OfType<Button>().Class(chip).Class(Lit).Class(state).Template().OfType<ContentPresenter>().Name(Presenter),
                    (ContentPresenter.BackgroundProperty, LookKeys.LitChipHoverGround),
                    (ContentPresenter.BorderBrushProperty, LookKeys.LitChipBorder),
                    (ContentPresenter.ForegroundProperty, LookKeys.LitChipWords)));
            }
        }

        // A toggle button is a button: the pin takes these rules too, and its checked ground while pinned. An icon
        // button has no edge, whatever edge a take gives Fluent's plain buttons.
        var icon = Rule(x => x.Is<Button>().Class(IconButton),
            (TemplatedControl.BackgroundProperty, LookKeys.IconButtonGround),
            (TemplatedControl.ForegroundProperty, LookKeys.IconButtonWords));
        icon.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, default(Thickness)));
        Add(icon);
        Add(Rule(x => x.Is<Button>().Class(IconButton).Class(":checked").Template().OfType<ContentPresenter>().Name(Presenter),
            (ContentPresenter.BackgroundProperty, LookKeys.IconButtonOnGround),
            (ContentPresenter.ForegroundProperty, LookKeys.IconButtonWords)));
        Add(Rule(x => x.Is<Button>().Class(IconButton).Class(":pointerover").Template().OfType<ContentPresenter>().Name(Presenter),
            (ContentPresenter.BackgroundProperty, LookKeys.IconButtonHoverGround),
            (ContentPresenter.ForegroundProperty, LookKeys.IconButtonHoverWords)));
        Add(Rule(x => x.Is<Button>().Class(IconButton).Class(":pressed").Template().OfType<ContentPresenter>().Name(Presenter),
            (ContentPresenter.BackgroundProperty, LookKeys.IconButtonPressedGround),
            (ContentPresenter.ForegroundProperty, LookKeys.IconButtonHoverWords)));
        ThinScrollBar(":vertical", vertical: true);
        ThinScrollBar(":horizontal", vertical: false);
    }

    /// <summary>
    /// Every scroll bar of the product, in both orientations: a thin thumb with semicircular ends, a little wider than
    /// Fluent's own two pixels and standing where Fluent's stands, that never widens into the full bar with its ground,
    /// track and arrows, nor fades, when Fluent expands it under the pointer. The thumb keeps Fluent's own colours, hover
    /// and pressed included, and the bar keeps Fluent's strip, so a press there pages as before (G7); a thinner
    /// <c>ScrollBarSize</c> would narrow that strip, and a scroller that never hides its bar would show the full bar always.
    /// </summary>
    private void ThinScrollBar(string orientation, bool vertical)
    {
        var thumb = new Style(x => x.OfType<ScrollBar>().Class(orientation).Template().OfType<Thumb>());
        // Laid out thin, not Fluent's full-width thumb scaled down, which would squash its round ends into ellipses.
        thumb.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Identity));
        thumb.Setters.Add(new Setter(vertical ? Layoutable.WidthProperty : Layoutable.HeightProperty, ThumbThickness));
        thumb.Setters.Add(vertical
            ? new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Right)
            : new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Bottom));
        thumb.Setters.Add(new Setter(Layoutable.MarginProperty, vertical ? new Thickness(0, 0, ThumbInset, 0) : new Thickness(0, 0, 0, ThumbInset)));
        thumb.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(ThumbThickness / 2)));
        Add(thumb);
        Add(Constant(x => x.OfType<ScrollBar>().Class(orientation).PropertyEquals(ScrollBar.IsExpandedProperty, true).Template().OfType<Thumb>(),
            TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ThumbGround)));
        Add(Constant(x => x.OfType<ScrollBar>().Class(orientation).PropertyEquals(ScrollBar.IsExpandedProperty, true).Template().OfType<Rectangle>().Name("TrackRect"),
            Visual.OpacityProperty, 0d));
        Add(Constant(x => x.OfType<ScrollBar>().Class(orientation).PropertyEquals(ScrollBar.IsExpandedProperty, true).Template().OfType<RepeatButton>(),
            Visual.OpacityProperty, 0d));
        Add(Constant(x => x.OfType<ScrollBar>().Class(orientation).PropertyEquals(ScrollBar.IsExpandedProperty, true).Template().OfType<Grid>().Name("Root"),
            Panel.BackgroundProperty, Brushes.Transparent));
    }

    private static Style Constant(Func<Selector?, Selector> selector, AvaloniaProperty property, object value)
    {
        var style = new Style(selector);
        style.Setters.Add(new Setter(property, value));
        return style;
    }

    /// <summary>A chip at rest: its ground, edge and words, and its corners.</summary>
    private void Chip(string chip, string ground, string border, string words) =>
        Add(Rule(x => x.OfType<Button>().Class(chip),
            (TemplatedControl.BackgroundProperty, ground),
            (TemplatedControl.BorderBrushProperty, border),
            (TemplatedControl.ForegroundProperty, words),
            (TemplatedControl.CornerRadiusProperty, LookKeys.ChipRadius)));

    /// <summary>A chip hovered or pressed: what its template's presenter shows in place of Fluent's.</summary>
    private void Hovered(string chip, string ground, string border, string words)
    {
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            Add(Rule(x => x.OfType<Button>().Class(chip).Class(state).Template().OfType<ContentPresenter>().Name(Presenter),
                (ContentPresenter.BackgroundProperty, ground),
                (ContentPresenter.BorderBrushProperty, border),
                (ContentPresenter.ForegroundProperty, words)));
        }
    }

    private static Style Rule(Func<Selector?, Selector> selector, params (AvaloniaProperty Property, string Key)[] setters)
    {
        var style = new Style(selector);
        foreach (var (property, key) in setters)
        {
            style.Setters.Add(new Setter(property, new DynamicResourceExtension(key)));
        }

        return style;
    }
}
