namespace ImageStash.Layers;

/// <summary>
/// An item's layers, ordered bottom to top (F7), as Store keeps them in its <c>layers</c> and <c>previous_layers</c>
/// columns and Media draws them. Every list that exists is one the model admits: built in code, a list it refuses is the
/// caller's defect and throws; read from the store, which may be damaged, it is a failure naming the item (G35).
/// </summary>
public sealed class LayerList : IEquatable<LayerList>
{
    public static readonly LayerList Empty = new([]);

    private const string ReadAction = "Read the item's layers";

    private readonly Layer[] layers;

    /// <exception cref="ArgumentException">The model refuses the list; the message says why.</exception>
    public LayerList(IEnumerable<Layer> layers)
    {
        this.layers = [.. layers];
        if (Problem(this.layers) is { } problem)
        {
            throw new ArgumentException(problem, nameof(layers));
        }
    }

    public IReadOnlyList<Layer> Layers => layers;

    public bool IsEmpty => layers.Length == 0;

    public CropLayer? Crop => layers.OfType<CropLayer>().FirstOrDefault();

    public TrimLayer? Trim => layers.OfType<TrimLayer>().FirstOrDefault();

    /// <summary>The shrink layer's scale; full size when there is none.</summary>
    public Shrink Shrink => layers.OfType<ShrinkLayer>().FirstOrDefault()?.Scale ?? Shrink.Full;

    /// <summary>
    /// The list a store column holds; <paramref name="itemName"/> names the item in the failure when the text is not a
    /// list the model admits, or the column holds none. Never throws.
    /// </summary>
    public static Outcome<LayerList> FromJson(string? json, string itemName)
    {
        var (read, problem) = LayerListJson.Read(json);
        problem ??= Problem(read!);
        if (problem is not null)
        {
            return new Failure(ReadAction, itemName, problem);
        }

        return Outcome<LayerList>.Of(read!.Count == 0 ? Empty : new LayerList(read));
    }

    /// <summary>The text Store keeps for this list; <see cref="Empty"/> is <c>[]</c>, the column's default.</summary>
    public string ToJson() => LayerListJson.Write(layers);

    public bool Equals(LayerList? other) => other is not null && layers.SequenceEqual(other.layers);

    public override bool Equals(object? obj) => Equals(obj as LayerList);

    public override int GetHashCode() => layers.Length == 0 ? 0 : HashCode.Combine(layers.Length, layers[0]);

    /// <summary>What the model refuses in <paramref name="list"/>, said in words, or null when it admits the list.</summary>
    private static string? Problem(IReadOnlyList<Layer> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Problem(list[i]) is { } problem)
            {
                return $"layer {i + 1} {problem}";
            }
        }

        // Crop, trim and Shrink each describe one state (G22's Reset, G24's Shrink choice), not a stack.
        foreach (var (kind, count) in new[]
        {
            ("crop", list.OfType<CropLayer>().Count()),
            ("trim", list.OfType<TrimLayer>().Count()),
            ("shrink", list.OfType<ShrinkLayer>().Count()),
        })
        {
            if (count > 1)
            {
                return $"the list has more than one {kind}";
            }
        }

        return null;
    }

    private static string? Problem(Layer? layer) => layer switch
    {
        null => "is missing",
        TextLayer text => Defined(text.Preset, "text", "preset")
            ?? Lettering(text.Text, text.Font, text.FontSize, text.Outline, "text")
            ?? Sized(text.Box, "text", "box")
            ?? (double.IsFinite(text.Rotation) ? null : "(text) has a rotation that is not a number of degrees")
            ?? Span(text.Range, "text"),
        CaptionBarLayer bar => Lettering(bar.Text, bar.Font, bar.FontSize, bar.Outline, "caption bar"),
        HideLayer hide => Defined(hide.Kind, "hide", "style") ?? Area(hide.Rect, "hide", "rectangle") ?? Span(hide.Range, "hide"),
        DrawMarkLayer mark => Defined(mark.Kind, "mark", "kind of mark")
            ?? Defined(mark.Thickness, "mark", "thickness")
            ?? Geometry(mark)
            ?? Span(mark.Range, "mark"),
        CropLayer crop => Area(crop.Rect, "crop", "rectangle"),
        TrimLayer trim => Span(trim.Range, "trim"),
        ShrinkLayer shrink => Defined(shrink.Scale, "shrink", "scale"),
        _ => $"is of a kind the model does not know, {layer.GetType().Name}",
    };

    private static string? Defined<T>(T value, string kind, string field)
        where T : struct, Enum => Enum.IsDefined(value) ? null : $"({kind}) has a {field} that is not one of its values";

    // Text that is not well-formed UTF-16 would come back from the store changed, so the model refuses it.
    private static string? Lettering(string? text, string? font, int fontSize, Outline? outline, string kind) =>
        text is null ? $"({kind}) has no text"
        : !WellFormed(text) ? $"({kind}) has text that is not valid Unicode"
        : string.IsNullOrWhiteSpace(font) ? $"({kind}) has an empty font family"
        : !WellFormed(font) ? $"({kind}) has a font family that is not valid Unicode"
        : fontSize < 1 ? $"({kind}) has a font size below 1"
        : outline is null ? $"({kind}) has no outline"
        : null;

    /// <summary>Whether <paramref name="text"/> is well-formed UTF-16, as the model requires of a layer's text and font: no half of a surrogate pair.</summary>
    internal static bool WellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Area(PixelRect rect, string kind, string field) =>
        Sized(rect, kind, field) ?? (rect.X < 0 || rect.Y < 0 ? $"({kind}) has a {field} starting left of or above the picture" : null);

    // A text's box alone may start left of or above the picture, where a turn and a move put it with its letters still on
    // the picture (G21); a hide, a box mark and a crop cover the picture, and nothing of it lies there.
    private static string? Sized(PixelRect rect, string kind, string field) =>
        rect.Width < 1 || rect.Height < 1 ? $"({kind}) has a {field} without a positive width and height" : null;

    private static string? Span(FrameRange? range, string kind) =>
        range is not { } r ? null
        : r.First < 0 ? $"({kind}) has a range starting before the first frame"
        : r.Last < r.First ? $"({kind}) has a range that ends before it starts"
        : null;

    private static string? Geometry(DrawMarkLayer mark)
    {
        var name = mark.Kind.ToString().ToLowerInvariant();
        var points = mark.Points?.Count ?? 0;
        return mark.Kind switch
        {
            MarkKind.Box when mark.Rect is null => "(box) has no rectangle",
            MarkKind.Box when points > 0 => "(box) has points; a box is a rectangle",
            MarkKind.Box => Area(mark.Rect!.Value, "box", "rectangle"),
            _ when mark.Rect is not null => $"({name}) has a rectangle; a {name} is points",
            MarkKind.Arrow when points != 2 => "(arrow) does not have exactly two points",
            _ when points < 2 => $"({name}) has fewer than two points",
            _ => null,
        };
    }
}
