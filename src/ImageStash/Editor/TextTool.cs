using System.Drawing;
using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>The Text tool's presets, as its option row offers them (G21); a text layer keeps Meme or Label as its <see cref="TextPreset"/> (F7).</summary>
public enum TextKind
{
    Meme,
    CaptionBar,
    Label,
}

/// <summary>
/// Text (G21): a click on the picture adds a layer of the current preset and opens its field for typing, Meme (uppercase
/// Impact with an automatic outline, at the top or the bottom of the picture by the click's half), Caption bar (a white
/// band above the picture with near-black text and no outline) or Label (Segoe UI with an automatic outline where the
/// click was). A drag moves a text layer, a corner resizes a text layer or a caption bar by its font size, and the round
/// handle turns a text layer; dragging never draws one. The font, the fill and the outline, chosen with such a layer
/// selected, change that layer; a new one takes its preset's own (F8). Its letters are measured by Media alone.
/// </summary>
public sealed class TextTool : EditorTool
{
    // L2: a new layer's font size, a share of the shown picture's width (a Meme at a tenth of it, a caption at
    // 6.5% and a label at 8%), and a Meme's margin from the picture's edges, a share of its font size (4%).
    private const double MemeSizeOfWidth = 0.10, CaptionSizeOfWidth = 0.065, LabelSizeOfWidth = 0.08, MarginOfSize = 0.4;

    private const string MemeFont = "Impact", OtherFont = "Segoe UI";

    internal TextTool(EditorSession session)
        : base(session)
    {
    }

    public override string Name => "Text";

    public override string Hint => Session.SelectedLayer switch
    {
        TextLayer => "Drag to move it, a corner to resize it, the round handle to turn it; double-click to edit its text",
        CaptionBarLayer => "Drag a corner to resize it; double-click to edit its text",
        _ => "Click the picture to add text",
    };

    /// <summary>The preset the next click adds.</summary>
    public TextKind Kind { get; private set; } = TextKind.Meme;

    /// <summary>The preset the option row shows: the selected text or caption-bar layer's, else <see cref="Kind"/>.</summary>
    public TextKind KindShown => Session.SelectedLayer switch
    {
        TextLayer { Preset: TextPreset.Meme } => TextKind.Meme,
        TextLayer => TextKind.Label,
        CaptionBarLayer => TextKind.CaptionBar,
        _ => Kind,
    };

    /// <summary>The selected text or caption-bar layer's font family; null while none is selected.</summary>
    public string? FontShown => Session.SelectedLayer switch
    {
        TextLayer text => text.Font,
        CaptionBarLayer bar => bar.Font,
        _ => null,
    };

    /// <summary>The selected text or caption-bar layer's fill, the text's color; null while none is selected.</summary>
    public Rgba? FillShown => Session.SelectedLayer switch
    {
        TextLayer text => text.Fill,
        CaptionBarLayer bar => bar.Fill,
        _ => null,
    };

    /// <summary>The selected text or caption-bar layer's outline; null while none is selected.</summary>
    public Outline? OutlineShown => Session.SelectedLayer switch
    {
        TextLayer text => text.Outline,
        CaptionBarLayer bar => bar.Outline,
        _ => null,
    };

    /// <summary>The fill a new caption bar's text takes: the near-black swatch, since its band is always white (G21).</summary>
    private static Rgba CaptionFill => ColorChoices.Swatches[1];

    private static Rgba White => ColorChoices.Swatches[0];

    /// <summary>
    /// Chooses the preset of the next layer, and turns the selected text or caption-bar layer into it as one step: its
    /// text, font and size kept; between Meme and Label its colors, origin and rotation kept too; made a caption bar, the
    /// caption's colors (its text sits on white); made text from a caption bar, the preset's colors, placed as a new one.
    /// </summary>
    public void Choose(TextKind kind)
    {
        Kind = kind;
        ApplyToSelected(layer => Converted(layer, kind));
    }

    /// <summary>Gives the selected text or caption-bar layer the font <paramref name="family"/>, as one step; a name no layer can carry changes nothing.</summary>
    public void Choose(string family)
    {
        if (string.IsNullOrWhiteSpace(family) || !LayerList.WellFormed(family))
        {
            return;
        }

        ApplyToSelected(layer => layer switch
        {
            TextLayer text => LayerGeometry.Measured(text with { Font = family }, Session.Layers, Session.OriginalSize),
            CaptionBarLayer bar => bar with { Font = family },
            _ => layer,
        });
    }

    /// <summary>Gives the selected text or caption-bar layer's text the fill <paramref name="color"/>, as one step; a color that is not a swatch becomes a recent one (G21).</summary>
    public void ChooseFill(Rgba color) => ApplyToSelected(layer =>
    {
        // Recorded as the choice applies, before the change is shown, so the picker shows it among the recent ones.
        Session.Colors.Use(color);
        return WithFill(layer, color);
    });

    /// <summary>Shows the selected text or caption-bar layer's text in the fill <paramref name="color"/> the picker is dragged to, as no step (G21, G25); false when the drag was ended meanwhile.</summary>
    public bool PreviewFill(Rgba color) => PreviewOnSelected(layer => WithFill(layer, color));

    /// <summary>The picker's drag let go on the fill <paramref name="color"/>: one step and one recent colour; false, applying nothing, when the drag was abandoned meanwhile.</summary>
    public bool LetGoFill(Rgba color) => LetGo(color, layer => WithFill(layer, color));

    /// <summary>Gives the selected text or caption-bar layer the outline Auto, None or a color, as one step; a color that is not a swatch becomes a recent one (G21).</summary>
    public void ChooseOutline(Outline outline)
    {
        if (outline is null)
        {
            return;
        }

        ApplyToSelected(layer =>
        {
            if (outline.Color is { } color)
            {
                Session.Colors.Use(color);
            }

            return WithOutline(layer, outline);
        });
    }

    /// <summary>Shows the selected text or caption-bar layer with the outline <paramref name="color"/> the picker is dragged to, as no step (G21, G25); false when the drag was ended meanwhile.</summary>
    public bool PreviewOutline(Rgba color) => PreviewOnSelected(layer => WithOutline(layer, Outline.Of(color)));

    /// <summary>The picker's drag let go on the outline <paramref name="color"/>: one step and one recent colour; false, applying nothing, when the drag was abandoned meanwhile.</summary>
    public bool LetGoOutline(Rgba color) => LetGo(color, layer => WithOutline(layer, Outline.Of(color)));

    public override bool Owns(Layer layer) => layer is TextLayer or CaptionBarLayer;

    private static Layer WithFill(Layer layer, Rgba color) => layer switch
    {
        TextLayer text => text with { Fill = color },
        CaptionBarLayer bar => bar with { Fill = color },
        _ => layer,
    };

    private static Layer WithOutline(Layer layer, Outline outline) => layer switch
    {
        TextLayer text => text with { Outline = outline },
        CaptionBarLayer bar => bar with { Outline = outline },
        _ => layer,
    };

    /// <summary>
    /// A colour drag let go on <paramref name="color"/>: one step made by <paramref name="change"/>, the colour recorded
    /// once as a recent one when a layer took it (G21); false, applying nothing, when the drag was ended meanwhile.
    /// </summary>
    private bool LetGo(Rgba color, Func<Layer, Layer> change)
    {
        var onLayer = ColorDragShowsOnLayer;
        if (!CommitOnSelected(change))
        {
            return false;
        }

        if (onLayer)
        {
            Session.Colors.Use(color);
        }

        return true;
    }

    /// <summary>On a text layer a drag moves it, selected or not (G21); a caption bar is never moved.</summary>
    private protected override bool MovesWhenPressed(Layer layer) => layer is TextLayer;

    /// <summary>
    /// A click on the shown picture adds a layer of the preset the row shows as current (<see cref="KindShown"/>, which
    /// becomes <see cref="Kind"/>) with empty text, selected, its field open; a click beside the picture adds nothing.
    /// </summary>
    private protected override void Clicked(PointF point)
    {
        var (layers, original) = (Session.Layers, Session.OriginalSize);
        if (!Session.Accepts || LayerGeometry.Shown(layers, original) is not { } shown
            || point.X < shown.X || point.X >= shown.X + shown.Width || point.Y < shown.Y || point.Y >= shown.Y + shown.Height)
        {
            return;
        }

        Kind = KindShown;
        Layer added = Kind switch
        {
            TextKind.CaptionBar => new CaptionBarLayer("", OtherFont, SizeOf(shown, CaptionSizeOfWidth), CaptionFill, Outline.None),
            TextKind.Label => Label("", OtherFont, SizeOf(shown, LabelSizeOfWidth), LayerGeometry.PixelUnder(point, shown)),
            _ => Meme("", MemeFont, SizeOf(shown, MemeSizeOfWidth), shown, atTop: point.Y < shown.Y + shown.Height / 2f),
        };
        Session.TypeIntoAdded(new LayerList([.. layers.Layers, added]), layers.Layers.Count);
    }

    /// <summary>A new layer's font size: its share of the shown picture's width, at least 1.</summary>
    private static int SizeOf(PixelRect shown, double share) => (int)Math.Clamp(Math.Round(shown.Width * share, MidpointRounding.AwayFromZero), 1, int.MaxValue);

    private static int MarginOf(int size) => (int)Math.Min(Math.Round(size * MarginOfSize, MidpointRounding.AwayFromZero), int.MaxValue);

    private Layer Converted(Layer layer, TextKind kind) => (layer, kind) switch
    {
        (TextLayer { Preset: TextPreset.Meme } meme, TextKind.Label) => LayerGeometry.Measured(meme with { Preset = TextPreset.Label }, Session.Layers, Session.OriginalSize),
        (TextLayer { Preset: TextPreset.Label } label, TextKind.Meme) => LayerGeometry.Measured(label with { Preset = TextPreset.Meme, Box = label.Box with { Width = MemeWidth(label) } }, Session.Layers, Session.OriginalSize),
        (TextLayer text, TextKind.CaptionBar) => new CaptionBarLayer(text.Text, text.Font, text.FontSize, CaptionFill, Outline.None),
        (CaptionBarLayer bar, TextKind.Meme) when LayerGeometry.Shown(Session.Layers, Session.OriginalSize) is { } shown => Meme(bar.Text, bar.Font, bar.FontSize, shown, atTop: true),
        (CaptionBarLayer bar, TextKind.Label) when LayerGeometry.Shown(Session.Layers, Session.OriginalSize) is { } shown =>
            Label(bar.Text, bar.Font, bar.FontSize, new PixelPoint((int)Math.Min((long)shown.X + MarginOf(bar.FontSize), int.MaxValue), (int)Math.Min((long)shown.Y + MarginOf(bar.FontSize), int.MaxValue))),
        _ => layer,
    };

    /// <summary>
    /// A Label's width as a Meme: its letters' in uppercase on one line, as far as the shown picture's right edge (a crop's,
    /// past which letters would not show), so turning it into a Meme does not break its words at the narrower width of its
    /// mixed-case letters; with no letter yet, its field still open (<see cref="EditorSession.OptionsKeepField"/>), all of
    /// that room, or every letter typed would wrap.
    /// </summary>
    private int MemeWidth(TextLayer label)
    {
        var picture = LayerGeometry.Picture(Session.Layers, Session.OriginalSize);
        var room = (int)Math.Clamp((long)picture.X + picture.Width - label.Box.X, 1, int.MaxValue);
        if (string.IsNullOrWhiteSpace(label.Text))
        {
            return room;
        }

        var unwrapped = LayerComposer.TextSize(label with { Preset = TextPreset.Meme, Box = label.Box with { Width = int.MaxValue } });
        return Math.Clamp(unwrapped.Width, 1, room);
    }

    /// <summary>
    /// A Meme spanning the shown picture less a margin on each side, at its top or with its bottom edge at the margin above
    /// its bottom (G21); above and below, no more margin than the picture has room for beside the box, so on a picture
    /// cropped shorter than that it still lies at the edge it was put at and shows there.
    /// </summary>
    private TextLayer Meme(string text, string font, int size, PixelRect shown, bool atTop)
    {
        var margin = MarginOf(size);
        var width = (int)Math.Clamp((long)shown.Width - 2L * margin, 1, int.MaxValue);
        var origin = new PixelRect((int)Math.Min((long)shown.X + margin, int.MaxValue), shown.Y, width, 1);
        var meme = LayerGeometry.Measured(new TextLayer(TextPreset.Meme, text, font, size, White, Outline.Auto, origin, 0, null), Session.Layers, Session.OriginalSize);
        var room = Math.Clamp((long)shown.Height - meme.Box.Height, 0, margin);
        var y = atTop ? (long)shown.Y + room : (long)shown.Y + shown.Height - room - meme.Box.Height;
        return LayerGeometry.Measured(meme with { Box = meme.Box with { Y = (int)Math.Clamp(y, 0, int.MaxValue) } }, Session.Layers, Session.OriginalSize);
    }

    /// <summary>A Label whose box's top-left is at <paramref name="at"/>, moved only as far as keeps it inside the original.</summary>
    private TextLayer Label(string text, string font, int size, PixelPoint at) =>
        LayerGeometry.Measured(new TextLayer(TextPreset.Label, text, font, size, White, Outline.Auto, new PixelRect(at.X, at.Y, 1, 1), 0, null), Session.Layers, Session.OriginalSize);
}
