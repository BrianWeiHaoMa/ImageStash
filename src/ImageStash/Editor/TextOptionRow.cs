using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Layers;
using ImageStash.Look;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Editor;

/// <summary>
/// Text's option row (G20, G21, F8): the presets Meme, Caption bar and Label, the selected
/// layer's checked, else the next one's; also the font button, which opens the font menu, the five fill swatches with
/// the More swatch, which opens the colour picker, and the outline swatch, which opens it with Auto and None.
/// The font and swatches show and set the selected text or caption-bar layer's values, else those the next layer of the
/// current preset takes (<see cref="EditorTool.OptionShown"/>).
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
    private Control? allFonts;
    private TextKind? refreshedKind;

    /// <param name="menu">Makes one of the editor's menus, which hands the editor its keys.</param>
    public TextOptionRow(TextTool tool, ColorChoices colors, EditorCanvas canvas, EditorPane pane, Func<PlacementMode, ContextMenu> menu, IFonts fonts)
    {
        this.tool = tool;
        this.pane = pane;
        EditorOptions.SetSpacing(this);
        presets = [.. new[] { (TextKind.Meme, "Meme"), (TextKind.CaptionBar, "Caption bar"), (TextKind.Label, "Label") }
            .Select(preset => (preset.Item1, EditorButtons.Choice(preset.Item2, preset.Item2, () => tool.Choose(preset.Item1))))];

        fontName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var arrow = EditorButtons.Glyph("\ue70d", 10);
        Grid.SetColumn(arrow, 1);
        font = new Button
        {
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { fontName, arrow } },
            Height = EditorButtons.ChoiceHeight,
            Width = 140,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(font, FontButton);
        font.Click += (_, _) => FontMenu.Open(menu(PlacementMode.TopEdgeAlignedLeft), font, tool.FontShown, tool.Choose, () =>
        {
            // It stays open as a font is chosen, as the colour picker does: a press elsewhere or Esc closes it.
            allFonts = FontMenu.AllFontsIn(fonts, () => tool.FontShown, pane.Room, tool.Choose);
            pane.Show(allFonts, font);
        });

        swatches = [.. ColorChoices.Swatches.Select(color => (color, EditorButtons.Swatch(color, () => tool.ChooseFill(color))))];
        fillPicker = new ColorPicker(colors, canvas, () => tool.FillShown, tool.ChooseFill, new ColorDrag(tool.BeginColor, tool.PreviewFill, tool.LetGoFill, tool.AbandonColor));
        fillPicker.Done += pane.Hide;
        (more, moreFill) = EditorButtons.More(DrawOptionRow.MoreColors, anchor => Open(fillPicker, anchor));

        outlinePicker = new ColorPicker(colors, canvas, OutlineColorShown, color => tool.ChooseOutline(Outline.Of(color)), new ColorDrag(tool.BeginColor, tool.PreviewOutline, tool.LetGoOutline, tool.AbandonColor),
            (() => tool.OutlineShown, tool.ChooseOutline));
        outlinePicker.Done += pane.Hide;
        outline = new OutlineSwatch(anchor => Open(outlinePicker, anchor));

        var colorsRow = EditorOptions.Group([.. swatches.Select(swatch => swatch.Button), more, outline]);
        var presetRow = EditorOptions.Group(presets.Select(preset => preset.Button));
        Children.AddRange([EditorOptions.Group([presetRow, font], EditorOptions.GroupSpacing), colorsRow]);
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh()
    {
        var kind = tool.KindShown;
        EditorButtons.Check(presets, kind);
        // Another preset is another set of values: an open picker shows, and its hex gives, the new one's colour.
        if (refreshedKind is { } before && before != kind && pane.Shown is ColorPicker picker && (ReferenceEquals(picker, fillPicker) || ReferenceEquals(picker, outlinePicker)))
        {
            picker.Reset();
        }

        refreshedKind = kind;
        var (family, color, shownOutline) = (tool.FontShown, tool.FillShown, tool.OutlineShown);
        fontName.Text = family;
        AutomationProperties.SetHelpText(font, family);
        ToolTipRule.ShowWhole(font, family);
        if (allFonts is not null && ReferenceEquals(pane.Shown, allFonts))
        {
            FontMenu.Mark(allFonts, family);
        }

        EditorButtons.Check(swatches, color);
        EditorButtons.ShowPicked(more, moreFill, color);
        outline.Show(shownOutline, color);
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
        { IsAuto: true } => LayerComposer.AutoOutline(tool.FillShown),
        _ => new Rgba(0, 0, 0, 255),
    };
}
