using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Thickness = Avalonia.Thickness;

namespace ImageStash.Editor;

/// <summary>The active tool's choices and mouse hint, with the same room reserved for every tool and selection.</summary>
internal sealed class EditorOptions : Grid
{
    public const int GroupSpacing = 8;

    private const int LineSpacing = 6;
    private readonly ChoiceArea choices = new();
    private readonly TextBlock hint = new() { Height = 20, FontSize = 13, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) };

    public EditorOptions()
    {
        Margin = new Thickness(12, 8, 12, 0);
        // The trailing column holds room for a 36-unit layer action and its gap.
        ColumnDefinitions = new ColumnDefinitions("*,44");
        RowDefinitions = new RowDefinitions("Auto,Auto");
        SetColumnSpan(hint, 2);
        SetRow(hint, 1);
        Children.AddRange([choices, hint]);
    }

    public Control? Row
    {
        get => choices.Content as Control;
        set => choices.Content = value;
    }

    public string Hint { set => hint.Text = value; }

    public static void SetSpacing(WrapPanel row)
    {
        row.Orientation = Orientation.Horizontal;
        row.ItemSpacing = GroupSpacing;
        row.LineSpacing = LineSpacing;
    }

    public static StackPanel Group(IEnumerable<Control> choices, double spacing = 2)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        group.Children.AddRange(choices);
        return group;
    }

    private sealed class ChoiceArea : ContentControl
    {
        // At this width, the complete Text, Draw and Trim groups fit in one line, including a long Trim readout.
        private const double SingleLineWidth = 680;

        protected override Type StyleKeyOverride => typeof(ContentControl);

        protected override Size MeasureOverride(Size availableSize)
        {
            var content = base.MeasureOverride(availableSize);
            var reserved = availableSize.Width >= SingleLineWidth
                ? EditorButtons.ChoiceHeight
                : 2 * EditorButtons.ChoiceHeight + LineSpacing;
            return new Size(content.Width, Math.Max(reserved, content.Height));
        }
    }
}
