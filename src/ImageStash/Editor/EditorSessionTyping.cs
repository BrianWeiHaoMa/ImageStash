using System.Drawing;
using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>
/// Typing into a text or caption-bar layer (G21, G25), and the pointer's double-click and Shift: the view hosts the
/// field and hands each change of its text to the session, which shows it at once, each text measured by Media, and
/// makes the whole run one step when the field closes. Every other operation of the session closes an open field first,
/// so <c>Ctrl+Z</c> while typing undoes what was typed and Save saves it; but for one of Text's options chosen while the
/// field's layer holds no letter, which changes that layer and leaves the field open (<see cref="OptionsKeepField"/>).
/// </summary>
public sealed partial class EditorSession
{
    private Field? textField;

    /// <summary>Whether a field is open for typing into the selected text or caption-bar layer.</summary>
    public bool Typing => textField is not null;

    /// <summary>Whether Shift is held, as the view reads it from each pointer event: turning a text layer then snaps to 15° steps (G21).</summary>
    public bool ShiftHeld { get; set; }

    /// <summary>
    /// A press whose click count is two, instead of a press: on the selected text layer's round handle it turns the layer
    /// back to 0° as one step; on a text or caption-bar layer, the topmost under it, it selects that layer and opens its
    /// field (G21); elsewhere it does nothing. No release follows it.
    /// </summary>
    public void DoubleClick(PointF point)
    {
        if (!Accepts)
        {
            return;
        }

        EndTyping();
        tool.Abandon();
        if (Selected is { } selected && SelectedLayer is TextLayer turned && LayerGeometry.HandleAt(Layers, selected, point, HitTolerance, OriginalSize, HandleRoom) == Handle.Rotate)
        {
            if (turned.Rotation != 0)
            {
                Change(Replaced(selected, turned with { Rotation = 0 }), selected);
            }

            return;
        }

        if (LayerGeometry.TopmostAt(Layers, point, HitTolerance, OriginalSize) is { } index && Layers.Layers[index] is TextLayer or CaptionBarLayer)
        {
            Select(index);
            BeginTyping();
        }
    }

    /// <summary>Opens the field on the selected text or caption-bar layer; nothing without one, or while a field is open.</summary>
    public void BeginTyping()
    {
        if (Accepts && textField is null && SelectedLayer is TextLayer or CaptionBarLayer)
        {
            // A drag in progress is put back first, or its end would later put back the list the field started from.
            tool.Abandon();
            textField = new Field(Now, Added: false, SelectedLayer!);
            RaiseChanged();
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the open field's layer's text, no step of its own, a text layer's box following
    /// its letters as measured from where the box was when the field opened, so letters typed and taken back leave it
    /// where it was; a Meme keeps the uppercase letters it shows (G21: "Meme is uppercase"), so a field laid out on its
    /// text puts the caret on the letters drawn, and as a Label later it keeps them. A text that is not well-formed
    /// UTF-16, as a field typed one code unit at a time holds for a moment halfway through an emoji, is not shown: the
    /// layer keeps the last text that was.
    /// </summary>
    public void SetTypedText(string? text)
    {
        text ??= "";
        if (!Accepts || textField is not { } open || Selected is not { } index || !LayerList.WellFormed(text))
        {
            return;
        }

        // A colour dragged on the picker meanwhile is put back first: it was shown on the layer as it was before this text.
        tool.Abandon();
        var layer = Layers.Layers[index];
        Layer typed = (layer, open.Start) switch
        {
            (TextLayer, TextLayer lettered) => LayerGeometry.Measured(lettered with { Text = lettered.Preset == TextPreset.Meme ? Uppercase.Of(text) : text }, Layers, OriginalSize),
            (CaptionBarLayer bar, _) => bar with { Text = text },
            _ => layer,
        };
        if (!typed.Equals(layer))
        {
            Show(Replaced(index, typed), index);
        }
    }

    /// <summary>
    /// Closes the open field. What was typed becomes one step, the click that added the layer included; a layer left with
    /// no text but white space is removed, as part of that step for one that was there before, and for one the click had
    /// just added by putting the list back as it was before the click, no step at all.
    /// </summary>
    public void EndTyping() => CloseField();

    /// <summary>Shows <paramref name="layers"/>, which add the layer at <paramref name="index"/>, and opens its field; the add becomes a step with what is typed.</summary>
    internal void TypeIntoAdded(LayerList layers, int index)
    {
        textField = new Field(Now, Added: true, layers.Layers[index]);
        Show(layers, index);
    }

    /// <summary>
    /// Whether an option chosen now changes the open field's layer and leaves the field open, rather than closing it first:
    /// the layer holds no letter, so closing the field would take away the layer the option was chosen for (G21).
    /// </summary>
    internal bool OptionsKeepField => textField is not null && SelectedLayer is { } layer && Blank(layer);

    /// <summary>
    /// Shows <paramref name="layer"/>, an option's change, as the open field's layer, no step of its own: the field's step
    /// takes it when the field closes, and what is typed next is laid out from it.
    /// </summary>
    internal void ShowTyped(Layer layer)
    {
        if (textField is { } open && Selected is { } index)
        {
            textField = open with { Start = layer };
            Show(Replaced(index, layer), index);
        }
    }

    /// <summary>Closes the open field as <see cref="EndTyping"/> says; true when that took back the layer the click had added, a change of its own to the user.</summary>
    private bool CloseField()
    {
        if (textField is not { } open)
        {
            return false;
        }

        // A colour still dragged on the picker for the field's layer is put back first: it ends with the field.
        tool.Abandon();
        textField = null;
        if (Selected is { } index && Blank(Layers.Layers[index]))
        {
            if (open.Added)
            {
                Show(open.Before.Layers, open.Before.Selected);
                return true;
            }

            Commit(open.Before, new LayerList(Layers.Layers.Where((_, i) => i != index)), null);
            return false;
        }

        Commit(open.Before, Layers, Selected);
        return false;
    }

    /// <summary>Whether a text or caption-bar layer holds nothing but white space, so it would draw no letter.</summary>
    private static bool Blank(Layer layer) =>
        layer is TextLayer text ? string.IsNullOrWhiteSpace(text.Text) : layer is CaptionBarLayer bar && string.IsNullOrWhiteSpace(bar.Text);

    private LayerList Replaced(int index, Layer layer) => new(Layers.Layers.Select((each, i) => i == index ? layer : each));

    /// <summary>
    /// An open field: the layers and selection before it opened (before the click, for a layer the click added), whether
    /// the click added its layer, and its layer as the field opened on it.
    /// </summary>
    private readonly record struct Field(Snapshot Before, bool Added, Layer Start);
}
