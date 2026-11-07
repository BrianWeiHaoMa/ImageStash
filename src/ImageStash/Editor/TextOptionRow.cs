using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ImageStash.Layers;
using ImageStash.Look;
using ImageStash.Media;
using ImageStash.Seams;
using Thickness = Avalonia.Thickness;

namespace ImageStash.Editor;

/// <summary>
/// Text's option row (G20, G21, F8): the presets Meme, Caption bar and Label, the selected
/// layer's checked, else the next one's; with a text or caption-bar layer selected, also the font button, which opens
/// the font menu, the five fill swatches with the More swatch, which opens the colour picker, and the outline swatch,
/// which opens it with Auto and None; and what the mouse does, with nothing selected "Click the picture to add text".
/// It wraps onto further lines when the window is narrower, so nothing is clipped or hidden.
/// </summary>
internal sealed class TextOptionRow : WrapPanel, IOptionRow
{
    public const string FontButton = "Font";

    private readonly TextTool tool;
    private readonly EditorPane pane;
    private readonly List<(TextKind Kind, ToggleButton Button)> presets;
    private readonly Button font;
    private readonly TextBlock fontName;
    private readonly List<(Rgba Color, ToggleButton Button)> swatches;
    private readonly ToggleButton more;
    private readonly Border moreFill;
    private readonly OutlineSwatch outline;
    private readonly ColorPicker fillPicker;
    private readonly ColorPicker outlinePicker;
    private readonly Control[] ofTheSelected;
    private readonly TextBlock hint;
    private Control? allFonts;

    /// <param name="menu">Makes one of the editor's menus, which hands the editor its keys.</param>
    public TextOptionRow(TextTool tool, ColorChoices colors, EditorCanvas canvas, EditorPane pane, Func<PlacementMode, ContextMenu> menu, IFonts fonts)
    {
        this.tool = tool;
        this.pane = pane;
        Orientation = Orientation.Horizontal;
        ItemSpacing = 12;
        LineSpacing = 6;
        presets = [.. new[] { (TextKind.Meme, "Meme"), (TextKind.CaptionBar, "Caption bar"), (TextKind.Label, "Label") }
            .Select(preset => (preset.Item1, EditorButtons.Choice(preset.Item2, preset.Item2, () => tool.Choose(preset.Item1))))];

        fontName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, MaxWidth = 160 };
        font = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { fontName, EditorButtons.Glyph("", 10) } },
            Height = 32,
            MinWidth = 120,
        };
        AutomationProperties.SetName(font, FontButton);
        ToolTipRule.Set(font, FontButton);
        font.Click += (_, _) => FontMenu.Open(menu(PlacementMode.TopEdgeAlignedLeft), font, tool.FontShown, tool.Choose, () =>
        {
            // It stays open as a font is chosen, as the colour picker does: a press elsewhere or Esc closes it.
            allFonts = FontMenu.AllFontsIn(fonts, () => tool.FontShown, pane.Room, tool.Choose);
            pane.Show(allFonts, font);
        });

        swatches = [.. ColorChoices.Swatches.Select(color => (color, EditorButtons.Swatch(color, () => tool.ChooseFill(color))))];
        fillPicker = new ColorPicker(colors, canvas, () => tool.FillShown ?? ColorChoices.Swatches[0], tool.ChooseFill, new ColorDrag(tool.BeginColor, tool.PreviewFill, tool.LetGoFill, tool.AbandonColor));
        fillPicker.Done += pane.Hide;
        (more, moreFill) = EditorButtons.More(DrawOptionRow.MoreColors, anchor => Open(fillPicker, anchor));

        outlinePicker = new ColorPicker(colors, canvas, OutlineColorShown, color => tool.ChooseOutline(Outline.Of(color)), new ColorDrag(tool.BeginColor, tool.PreviewOutline, tool.LetGoOutline, tool.AbandonColor),
            (() => tool.OutlineShown, tool.ChooseOutline));
        outlinePicker.Done += pane.Hide;
        outline = new OutlineSwatch(anchor => Open(outlinePicker, anchor));

        // Text's sentence is long: it wraps within the row rather than run past the window (G21).
        hint = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var colorRow = Group([.. swatches.Select(swatch => swatch.Button), more]);
        ofTheSelected = [font, colorRow, outline];
        var presetRow = Group(presets.Select(preset => preset.Button));
        Children.AddRange([presetRow, font, colorRow, outline, hint]);
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh()
    {
        EditorButtons.Check(presets, tool.KindShown);
        hint.Text = tool.Hint;
        var (family, fill, shownOutline) = (tool.FontShown, tool.FillShown, tool.OutlineShown);
        foreach (var control in ofTheSelected)
        {
            control.IsVisible = family is not null;
        }

        if (family is null || fill is not { } color || shownOutline is null)
        {
            // What the pane shows was for the layer no longer selected.
            if (pane.Shown is { } shown && (ReferenceEquals(shown, fillPicker) || ReferenceEquals(shown, outlinePicker) || ReferenceEquals(shown, allFonts)))
            {
                pane.Hide();
            }

            return;
        }

        fontName.Text = family;
        if (allFonts is not null && ReferenceEquals(pane.Shown, allFonts))
        {
            FontMenu.Mark(allFonts, family);
        }

        EditorButtons.Check(swatches, color);
        EditorButtons.ShowPicked(more, moreFill, color);
        outline.Show(shownOutline, color);
    }

    /// <summary>Choices kept close together, which still wrap among themselves when the row is narrower than they are (G21).</summary>
    private static WrapPanel Group(IEnumerable<Control> choices)
    {
        var group = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 2 };
        group.Children.AddRange(choices);
        return group;
    }

    private void Open(ColorPicker picker, Control anchor)
    {
        picker.Reset();
        pane.Show(picker, anchor);
        Refresh();
    }

    /// <summary>The colour the outline picker opens on: the outline's own, Auto's for the fill, else black.</summary>
    private Rgba OutlineColorShown() => tool.OutlineShown switch
    {
        { Color: { } color } => color,
        { IsAuto: true } when tool.FillShown is { } fill => LayerComposer.AutoOutline(fill),
        _ => new Rgba(0, 0, 0, 255),
    };
}
