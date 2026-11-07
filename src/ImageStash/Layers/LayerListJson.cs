using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ImageStash.Layers;

/// <summary>
/// The one reader and writer of the layer list's JSON, the format Store keeps in <c>layers</c> and
/// <c>previous_layers</c>: an array of objects, each with a <c>kind</c> and that kind's fields by name; rectangles as
/// <c>[x, y, width, height]</c>, points as <c>[x, y]</c>, ranges as <c>[first, last]</c> (omitted for the whole clip),
/// colors as <c>#rrggbbaa</c>. Later builds read what this one wrote (F11), so a name here never changes; a property a
/// kind does not define is ignored, so a field a later build adds leaves its lists readable here.
/// </summary>
internal static class LayerListJson
{
    private static readonly Names<TextPreset> Presets = new((TextPreset.Meme, "meme"), (TextPreset.Label, "label"));

    private static readonly Names<HideKind> HideStyles = new((HideKind.Blur, "blur"), (HideKind.Black, "black"));

    private static readonly Names<MarkKind> Marks =
        new((MarkKind.Pen, "pen"), (MarkKind.Marker, "marker"), (MarkKind.Arrow, "arrow"), (MarkKind.Box, "box"));

    private static readonly Names<Thickness> Thicknesses =
        new((Thickness.Thin, "thin"), (Thickness.Medium, "medium"), (Thickness.Thick, "thick"));

    private static readonly Names<Shrink> Scales = new((Shrink.Full, "1"), (Shrink.ThreeQuarter, "3/4"), (Shrink.Half, "1/2"));

    public static string Write(IReadOnlyList<Layer> layers)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var layer in layers)
            {
                json.WriteStartObject();
                Write(json, layer);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The layers <paramref name="json"/> holds, or what makes it unreadable, in words; never throws.</summary>
    public static (List<Layer>? Layers, string? Problem) Read(string? json)
    {
        if (json is null)
        {
            return (null, "its layers are missing");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (null, "its layers are not a list");
            }

            var layers = new List<Layer>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                layers.Add(Read(element, layers.Count + 1));
            }

            return (layers, null);
        }
        catch (Malformed malformed)
        {
            return (null, malformed.Message);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            // ArgumentException: text that is not valid UTF-16 cannot be parsed at all; InvalidOperationException: a
            // string escaping half a surrogate pair parses but cannot be read as text.
            return (null, "its layers are not readable JSON");
        }
    }

    private static void Write(Utf8JsonWriter json, Layer layer)
    {
        switch (layer)
        {
            case TextLayer text:
                json.WriteString("kind", "text");
                json.WriteString("preset", Presets.Of(text.Preset));
                WriteLettering(json, text.Text, text.Font, text.FontSize, text.Fill, text.Outline);
                WriteNumbers(json, "box", text.Box.X, text.Box.Y, text.Box.Width, text.Box.Height);
                json.WriteNumber("rotation", text.Rotation);
                WriteRange(json, text.Range);
                break;
            case CaptionBarLayer bar:
                json.WriteString("kind", "captionBar");
                WriteLettering(json, bar.Text, bar.Font, bar.FontSize, bar.Fill, bar.Outline);
                break;
            case HideLayer hide:
                json.WriteString("kind", "hide");
                json.WriteString("style", HideStyles.Of(hide.Kind));
                WriteNumbers(json, "rect", hide.Rect.X, hide.Rect.Y, hide.Rect.Width, hide.Rect.Height);
                WriteRange(json, hide.Range);
                break;
            case DrawMarkLayer mark:
                json.WriteString("kind", "mark");
                json.WriteString("mark", Marks.Of(mark.Kind));
                if (mark.Rect is { } box)
                {
                    WriteNumbers(json, "rect", box.X, box.Y, box.Width, box.Height);
                }
                else
                {
                    json.WriteStartArray("points");
                    foreach (var point in mark.Points)
                    {
                        json.WriteStartArray();
                        json.WriteNumberValue(point.X);
                        json.WriteNumberValue(point.Y);
                        json.WriteEndArray();
                    }

                    json.WriteEndArray();
                }

                json.WriteString("color", Hex(mark.Color));
                json.WriteString("thickness", Thicknesses.Of(mark.Thickness));
                WriteRange(json, mark.Range);
                break;
            case CropLayer crop:
                json.WriteString("kind", "crop");
                WriteNumbers(json, "rect", crop.Rect.X, crop.Rect.Y, crop.Rect.Width, crop.Rect.Height);
                break;
            case TrimLayer trim:
                json.WriteString("kind", "trim");
                WriteNumbers(json, "range", trim.Range.First, trim.Range.Last);
                break;
            case ShrinkLayer shrink:
                json.WriteString("kind", "shrink");
                json.WriteString("scale", Scales.Of(shrink.Scale));
                break;
        }
    }

    private static void WriteLettering(Utf8JsonWriter json, string text, string font, int fontSize, Rgba fill, Outline outline)
    {
        json.WriteString("text", text);
        json.WriteString("font", font);
        json.WriteNumber("fontSize", fontSize);
        json.WriteString("fill", Hex(fill));
        json.WriteString("outline", outline.IsAuto ? "auto" : outline.Color is { } color ? Hex(color) : "none");
    }

    private static void WriteRange(Utf8JsonWriter json, FrameRange? range)
    {
        if (range is { } r)
        {
            WriteNumbers(json, "range", r.First, r.Last);
        }
    }

    private static void WriteNumbers(Utf8JsonWriter json, string name, params int[] numbers)
    {
        json.WriteStartArray(name);
        foreach (var number in numbers)
        {
            json.WriteNumberValue(number);
        }

        json.WriteEndArray();
    }

    private static string Hex(Rgba color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}";

    private static Layer Read(JsonElement element, int number)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new Malformed($"layer {number} is not an object");
        }

        if (!element.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
        {
            throw new Malformed($"layer {number} has no \"kind\"");
        }

        var fields = new Fields(element, $"layer {number} ({kind.GetString()})");
        switch (kind.GetString())
        {
            case "text":
                return new TextLayer(fields.Named("preset", Presets), fields.Text("text"), fields.Text("font"), fields.Int("fontSize"),
                    fields.Color("fill"), fields.Outline("outline"), fields.Rect("box"), fields.Number("rotation"), fields.Range("range", optional: true));
            case "captionBar":
                return new CaptionBarLayer(fields.Text("text"), fields.Text("font"), fields.Int("fontSize"), fields.Color("fill"), fields.Outline("outline"));
            case "hide":
                return new HideLayer(fields.Rect("rect"), fields.Named("style", HideStyles), fields.Range("range", optional: true));
            case "mark":
                var mark = fields.Named("mark", Marks);
                return mark == MarkKind.Box
                    ? new DrawMarkLayer(mark, [], fields.Rect("rect"), fields.Color("color"), fields.Named("thickness", Thicknesses), fields.Range("range", optional: true))
                    : new DrawMarkLayer(mark, fields.Points("points"), null, fields.Color("color"), fields.Named("thickness", Thicknesses), fields.Range("range", optional: true));
            case "crop":
                return new CropLayer(fields.Rect("rect"));
            case "trim":
                return new TrimLayer(fields.Range("range", optional: false)!.Value);
            case "shrink":
                return new ShrinkLayer(fields.Named("scale", Scales));
            default:
                throw new Malformed($"layer {number} is of an unknown kind, \"{kind.GetString()}\"");
        }
    }

    /// <summary>A layer object's fields, each read with its type checked; <see cref="Malformed"/> names the first that is wrong.</summary>
    private readonly struct Fields(JsonElement layer, string label)
    {
        public string Text(string name) => Get(name, JsonValueKind.String, "text").GetString()!;

        public int Int(string name) => Get(name, JsonValueKind.Number, "a whole number").TryGetInt32(out var value)
            ? value
            : throw Wrong(name, "a whole number");

        public double Number(string name) => Get(name, JsonValueKind.Number, "a number").TryGetDouble(out var value)
            ? value
            : throw Wrong(name, "a number");

        public T Named<T>(string name, Names<T> names)
            where T : struct, Enum => names.TryRead(Get(name, JsonValueKind.String, names.Listed).GetString(), out var value)
            ? value
            : throw Wrong(name, names.Listed);

        public Rgba Color(string name)
        {
            var text = Get(name, JsonValueKind.String, "a color #rrggbbaa").GetString()!;
            return Rgba(text) ?? throw Wrong(name, "a color #rrggbbaa");
        }

        public Outline Outline(string name)
        {
            var text = Get(name, JsonValueKind.String, "auto, none or a color #rrggbbaa").GetString()!;
            return text switch
            {
                "auto" => Layers.Outline.Auto,
                "none" => Layers.Outline.None,
                _ => Rgba(text) is { } color ? Layers.Outline.Of(color) : throw Wrong(name, "auto, none or a color #rrggbbaa"),
            };
        }

        public PixelRect Rect(string name)
        {
            var n = Ints(Get(name, JsonValueKind.Array, "four whole numbers"), 4) ?? throw Wrong(name, "four whole numbers");
            return new PixelRect(n[0], n[1], n[2], n[3]);
        }

        public FrameRange? Range(string name, bool optional)
        {
            if (optional && (!layer.TryGetProperty(name, out var present) || present.ValueKind == JsonValueKind.Null))
            {
                return null;
            }

            var n = Ints(Get(name, JsonValueKind.Array, "two whole numbers"), 2) ?? throw Wrong(name, "two whole numbers");
            return new FrameRange(n[0], n[1]);
        }

        public IReadOnlyList<PixelPoint> Points(string name)
        {
            var points = new List<PixelPoint>();
            foreach (var point in Get(name, JsonValueKind.Array, "a list of points [x, y]").EnumerateArray())
            {
                var n = Ints(point, 2) ?? throw Wrong(name, "a list of points [x, y]");
                points.Add(new PixelPoint(n[0], n[1]));
            }

            return points;
        }

        private JsonElement Get(string name, JsonValueKind kind, string expected) =>
            !layer.TryGetProperty(name, out var value) ? throw new Malformed($"{label} has no \"{name}\"")
            : value.ValueKind != kind ? throw Wrong(name, expected)
            : value;

        private Malformed Wrong(string name, string expected) => new($"{label} has a \"{name}\" that is not {expected}");

        private static int[]? Ints(JsonElement array, int count)
        {
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != count)
            {
                return null;
            }

            var numbers = new int[count];
            var i = 0;
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out numbers[i++]))
                {
                    return null;
                }
            }

            return numbers;
        }

        private static Rgba? Rgba(string text) =>
            text.Length == 9 && text[0] == '#' && uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)
                ? new Rgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
                : null;
    }

    /// <summary>An enum's names in the format, both ways.</summary>
    private sealed class Names<T>(params (T Value, string Name)[] pairs)
        where T : struct, Enum
    {
        public string Listed { get; } = "one of " + string.Join(", ", pairs.Select(pair => pair.Name));

        public string Of(T value) => pairs.First(pair => EqualityComparer<T>.Default.Equals(pair.Value, value)).Name;

        public bool TryRead(string? name, out T value)
        {
            foreach (var pair in pairs)
            {
                if (pair.Name == name)
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }

    /// <summary>What makes a stored list unreadable, caught at <see cref="Read(string)"/> and never thrown past it.</summary>
    private sealed class Malformed(string problem) : Exception(problem);
}
