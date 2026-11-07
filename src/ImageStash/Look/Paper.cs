using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ImageStash.Look;

/// <summary>
/// Paper: white surfaces with hairline grey edges and the system accent only where something answers the pointer or is
/// chosen: white scope chips with a grey border, which on hover lose the border
/// to a very light wash of the accent with words in the accent; lighter grey tag chips; a focused field keeping its grey
/// edge on every side, its bottom edge drawn in the accent; the selected tile's ring standing off the tile by a small
/// gap; the gear, + and pin on the window's own ground with the accent's wash on hover;
/// white key caps with grey edges on a grey shortcut field; radii a little larger than Fluent's. Dark is the same
/// arrangement on graphite greys, not the light inverted.
/// </summary>
public sealed class Paper : Take
{
    public override string Name => "Paper";

    protected override void Fill(ResourceDictionary into, bool dark, Color accent)
    {
        var words = dark ? Rgb(0xECEDF0) : Rgb(0x1B1C20);
        var softWords = dark ? Rgb(0xD5D7DD) : Rgb(0x3D4049);
        var faint = dark ? Rgb(0xA0A3AD) : Rgb(0x6B6E78);
        var window = dark ? Rgb(0x202125) : Rgb(0xFFFFFF);
        var card = dark ? Rgb(0x2A2B30) : Rgb(0xFFFFFF);
        var edge = dark ? Rgb(0x3A3B42) : Rgb(0xDCDEE3);
        var hairline = dark ? Rgb(0x313238) : Rgb(0xE8E9EC);
        var accentWords = AccentWords(accent, dark);
        var wash = Alpha(accent, dark ? (byte)0x33 : (byte)0x17);
        var deeperWash = Alpha(accent, dark ? (byte)0x4A : (byte)0x26);
        // Lit stays the accent itself, solid, so a lit chip is told from a hovered one at a glance; in dark a step
        // lighter, as Windows lightens its accent on dark grounds.
        var lit = dark ? Mix(accent, Rgb(0xFFFFFF), 0.18) : accent;

        into[LookKeys.FluentWindowGround] = into[LookKeys.Surface] = Solid(window);
        into[LookKeys.PageGround] = Solid(dark ? Rgb(0x1A1B1E) : Rgb(0xF4F5F7));
        into[LookKeys.Words] = Solid(words);
        into[LookKeys.FaintWords] = Solid(faint);
        into[LookKeys.Divider] = Solid(hairline);

        into[LookKeys.Card] = Solid(card);
        into[LookKeys.CardBorder] = Solid(dark ? Rgb(0x36373D) : Rgb(0xE4E5E9));
        into[LookKeys.CardShadow] = BoxShadows.Parse(dark ? "0 8 24 0 #66000000" : "0 1 2 0 #0F101828, 0 8 24 0 #1F101828");

        into[LookKeys.ScopeChipGround] = Solid(card);
        into[LookKeys.ScopeChipBorder] = Solid(edge);
        into[LookKeys.ScopeChipWords] = Solid(softWords);
        into[LookKeys.ScopeChipHoverGround] = Solid(wash);
        into[LookKeys.ScopeChipHoverBorder] = Clear;
        into[LookKeys.ScopeChipHoverWords] = Solid(accentWords);
        // Lighter than Fluent's grey, and without an edge, so they read as words to pick rather than buttons.
        into[LookKeys.TagChipGround] = Solid(dark ? Rgb(0x2E2F35) : Rgb(0xF1F2F4));
        into[LookKeys.TagChipBorder] = Clear;
        into[LookKeys.TagChipWords] = Solid(softWords);
        into[LookKeys.TagChipHoverGround] = Solid(dark ? Rgb(0x393A41) : Rgb(0xE6E8EC));
        into[LookKeys.LitChipGround] = Solid(lit);
        into[LookKeys.LitChipBorder] = Clear;
        into[LookKeys.LitChipWords] = Solid(WordsOn(lit));
        into[LookKeys.LitChipHoverGround] = Solid(Mix(lit, dark ? Rgb(0xFFFFFF) : Rgb(0x000000), 0.1));

        into[LookKeys.IconButtonGround] = Clear;
        into[LookKeys.IconButtonWords] = Solid(softWords);
        into[LookKeys.IconButtonHoverGround] = Solid(wash);
        into[LookKeys.IconButtonHoverWords] = Solid(accentWords);
        into[LookKeys.IconButtonPressedGround] = Solid(deeperWash);
        into[LookKeys.IconButtonOnGround] = Solid(wash);

        into[LookKeys.ShortcutFieldGround] = Solid(dark ? Rgb(0x2C2D32) : Rgb(0xF0F1F3));
        into[LookKeys.ShortcutFieldBorder] = Solid(dark ? Rgb(0x36373D) : Rgb(0xE2E3E7));
        into[LookKeys.KeyCapGround] = Solid(dark ? Rgb(0x3A3B42) : Rgb(0xFFFFFF));
        into[LookKeys.KeyCapBorder] = Solid(dark ? Rgb(0x4A4B53) : Rgb(0xD3D5DB));
        into[LookKeys.KeyCapWords] = Solid(words);

        into[LookKeys.AccentRing] = Solid(dark ? lit : accent);
        into[LookKeys.SelectionRing] = Solid(dark ? lit : accent);
        // Off the tile, so the ring reads larger than the tile; with the ring's width it stays inside the space
        // between tiles, so it never lies over a neighbour.
        into[LookKeys.SelectionRingGap] = new Avalonia.Thickness(1.5);
        into[LookKeys.NoteGround] = Solid(wash);

        // Neutral to the channel, so a transparent picture is never tinted (G6).
        into[LookKeys.TileGround] = Solid(dark ? Rgb(0x313131) : Rgb(0xEFEFEF));
        into[LookKeys.TileMark] = Solid(dark ? Rgb(0x8C8C8C) : Rgb(0x8A8A8A));
        into[LookKeys.FocusRing] = Solid(dark ? Rgb(0xFFFFFF) : Rgb(0x1B1C20));
        into[LookKeys.PictureSurround] = Solid(dark ? Rgb(0x18191C) : Rgb(0xF2F3F5));
        into[LookKeys.PicturePill] = Solid(Alpha(Rgb(0x1B1C20), 0xCC));

        into[LookKeys.WarningGround] = Solid(dark ? Alpha(Rgb(0xFFB900), 0x29) : Rgb(0xFFF4DA));
        into[LookKeys.WarningMark] = Solid(dark ? Rgb(0xF2B12C) : Rgb(0xC98700));
        into[LookKeys.SwatchEdge] = Solid(Alpha(Rgb(0x808080), 0x55));

        into[LookKeys.BarGround] = Solid(dark ? Rgb(0x1E1F23) : Rgb(0xF7F7F9));
        into[LookKeys.BarWords] = Solid(dark ? Rgb(0xF4F4F6) : Rgb(0x1F2024));
        into[LookKeys.BarFaintWords] = Solid(dark ? Rgb(0xAAADB6) : Rgb(0x5C5F68));
        into[LookKeys.BarHoverGround] = Solid(dark ? Rgb(0x34353B) : Rgb(0xE3E4E8));

        into[LookKeys.CardRadius] = Radius(10);
        into[LookKeys.ChipRadius] = Radius(8);
        into[LookKeys.ControlRadius] = Radius(6);
        into[LookKeys.TileRadius] = Radius(6);
        into[LookKeys.SurroundRadius] = Radius(8);
        into[LookKeys.SmallRadius] = Radius(4);
        into[LookKeys.PillRadius] = Radius(16);
        into[LookKeys.FluentControlRadius] = Radius(5);
        into[LookKeys.FluentOverlayRadius] = Radius(8);

        // Fluent's plain buttons as the scope chips are: a white ground with a grey edge, a little deeper hovered, so
        // Undo, Edit and Open folder sit lightly on the cards instead of in Fluent's darker grey.
        into[LookKeys.FluentButtonGround] = Solid(dark ? Rgb(0x2E2F35) : Rgb(0xFFFFFF));
        into[LookKeys.FluentButtonHoverGround] = Solid(dark ? Rgb(0x37383E) : Rgb(0xF4F5F7));
        into[LookKeys.FluentButtonPressedGround] = Solid(dark ? Rgb(0x29292E) : Rgb(0xECEDF0));
        into[LookKeys.FluentButtonBorder] = Solid(edge);

        // The editor's choices not chosen as the tag chips are, the chosen one in Fluent's accent.
        into[LookKeys.FluentChoiceGround] = Solid(dark ? Rgb(0x2E2F35) : Rgb(0xF1F2F4));
        into[LookKeys.FluentChoiceHoverGround] = Solid(dark ? Rgb(0x393A41) : Rgb(0xE6E8EC));
        into[LookKeys.FluentChoicePressedGround] = Solid(dark ? Rgb(0x29292E) : Rgb(0xDDDFE4));

        into[LookKeys.FluentFieldGround] = Solid(dark ? Rgb(0x2A2B30) : Rgb(0xFFFFFF));
        into[LookKeys.FluentFieldHoverGround] = Solid(dark ? Rgb(0x2F3036) : Rgb(0xFAFAFB));
        into[LookKeys.FluentFieldFocusedGround] = Solid(dark ? Rgb(0x26272B) : Rgb(0xFFFFFF));
        into[LookKeys.FluentFieldBorder] = Solid(edge);
        into[LookKeys.FluentFieldHoverBorder] = Solid(dark ? Rgb(0x4A4B53) : Rgb(0xC8CAD1));
        // The focused field keeps its grey edge on every side, so its bounds stay plain, and its bottom edge, two
        // thick, is drawn in the accent: one brush for the four sides, as Fluent's text box draws them.
        into[LookKeys.FluentFieldFocusedBorder] = AccentBottomEdge(edge, dark ? lit : accent);
        into[LookKeys.FluentFieldFocusedThickness] = new Avalonia.Thickness(1, 1, 1, 2);
    }

    /// <summary>
    /// A border brush grey on every side but the bottom two units, in <paramref name="mark"/>: a hard stop two units
    /// from the top of the box, turned upside down about the box's centre, so the band sits on the bottom edge whatever
    /// the box's height.
    /// </summary>
    private static IBrush AccentBottomEdge(Color edge, Color mark) => new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, mark), new ImmutableGradientStop(0.5, mark), new ImmutableGradientStop(0.5, edge), new ImmutableGradientStop(1, edge)],
        transform: new ImmutableTransform(Avalonia.Matrix.CreateScale(1, -1)),
        transformOrigin: Avalonia.RelativePoint.Center,
        startPoint: new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Absolute),
        endPoint: new Avalonia.RelativePoint(0, 4, Avalonia.RelativeUnit.Absolute));
}
