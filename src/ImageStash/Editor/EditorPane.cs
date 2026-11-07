using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The one panel the editor shows beside its content, on a card: the colour picker and the All fonts… list (G21, F8). It
/// lies inside the editor, and so inside the stash window (F4), unlike a popup, which Windows makes a window of its own
/// and whose keys never reach the window: here the window's <c>Ctrl+V</c> and the editor's <c>Ctrl+Z</c>, <c>Ctrl+Y</c>
/// and <c>Ctrl+S</c> reach it as they reach the rest of the editor. Where the editor is wide enough that the picture keeps
/// at least the card's width beside it, the card stands docked beside the picture, inside the picture's row, which the
/// picture re-fits into, so what the panel changes shows on the picture unblocked, scrolling with the product's one bar
/// when the row is shorter than the card; where it is not (the flyout's default width and the window's minimum among
/// them), the card floats over the editor above its anchor, or below it when there is no room above, wholly inside the
/// editor (G20). A resize moves it between the two as it shows. The editor closes it on a press elsewhere, on Esc and on
/// a change of tool; its content closes it when it has done its part.
/// </summary>
internal sealed class EditorPane : Canvas
{
    /// <summary>How wide what it shows stands: the colour picker and the All fonts… list.</summary>
    public const double ContentWidth = 260;

    private const double Gap = 6, CardPadding = 12;

    /// <summary>Between the docked card and the picture's room, and the window's edge.</summary>
    private const double DockGap = 10;

    private readonly Border frame;
    private readonly ScrollViewer docked;
    private Control? anchor;

    public EditorPane()
    {
        frame = CardLook.Dress(new Border { Padding = new Thickness(CardPadding), IsVisible = false });
        docked = new ScrollViewer
        {
            IsVisible = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, DockGap, 0),
        };
        // A card that grows while open (a recent colour added), or an editor resized under it, is placed again, so it
        // stays wholly inside (G20).
        frame.SizeChanged += (_, _) => Place();
        SizeChanged += (_, _) => Place();
        Children.Add(frame);
    }

    public bool IsOpen => frame.IsVisible;

    /// <summary>What it shows, while open.</summary>
    public Control? Shown => IsOpen ? frame.Child as Control : null;

    /// <summary>Whether the card stands beside the picture rather than over the editor.</summary>
    public bool IsDocked => IsOpen && ReferenceEquals(frame.Parent, docked);

    /// <summary>The height content <see cref="ContentWidth"/> wide has to stand in: the picture's row where it would dock, else the editor's.</summary>
    public double Room => Docks(ContentWidth + 2 * CardPadding) && docked.Parent is Control row ? row.Bounds.Height : Bounds.Height;

    /// <summary>
    /// The picture's row: <paramref name="picture"/>, the controls that share the picture's place, with the docked card's
    /// column beside them, empty and of no width while the card floats or is closed.
    /// </summary>
    public Grid Beside(params Control[] picture)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.AddRange(picture);
        Grid.SetColumn(docked, 1);
        row.Children.Add(docked);
        return row;
    }

    /// <summary>Shows <paramref name="content"/> for <paramref name="anchor"/>: beside the picture where the editor has room, else over the editor by the anchor.</summary>
    public void Show(Control content, Control anchor)
    {
        this.anchor = anchor;
        frame.Child = content;
        frame.IsVisible = true;
        Place();
    }

    public void Hide()
    {
        frame.IsVisible = false;
        frame.Child = null;
        docked.IsVisible = false;
        anchor = null;
    }

    /// <summary>Whether <paramref name="visual"/> is part of what it shows, its docked scroll bar included.</summary>
    public bool Holds(object? visual) =>
        IsOpen && visual is Visual inside && (ReferenceEquals(inside, frame) || frame.IsVisualAncestorOf(inside) || (docked.IsVisible && (ReferenceEquals(inside, docked) || docked.IsVisualAncestorOf(inside))));

    /// <summary>Whether a card <paramref name="width"/> wide docks: the picture keeps at least that width beside it.</summary>
    private bool Docks(double width) => width > 0 && Bounds.Width - width - DockGap >= width;

    private void Place()
    {
        if (!IsOpen || anchor is null)
        {
            return;
        }

        frame.Measure(Size.Infinity);
        var size = frame.DesiredSize;
        if (Docks(size.Width))
        {
            // Content that scrolls on its own (the font list) is fitted to the row rather than scrolled inside a second
            // scroller, which would leave its end, and the entries made only as they come into view, out of reach.
            docked.VerticalScrollBarVisibility = frame.Child is ScrollViewer ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            Move(docked);
            docked.IsVisible = true;
            return;
        }

        Move(this);
        docked.IsVisible = false;
        var at = anchor.TranslatePoint(default, this) ?? default;
        var top = at.Y - size.Height - Gap;
        if (top < Gap)
        {
            top = at.Y + anchor.Bounds.Height + Gap;
        }

        SetLeft(frame, Math.Clamp(at.X + anchor.Bounds.Width / 2 - size.Width / 2, Gap, Math.Max(Gap, Bounds.Width - size.Width - Gap)));
        SetTop(frame, Math.Clamp(top, Gap, Math.Max(Gap, Bounds.Height - size.Height - Gap)));
    }

    /// <summary>The card into <paramref name="holder"/>, out of the other, when it is not there already.</summary>
    private void Move(Control holder)
    {
        if (ReferenceEquals(frame.Parent, holder))
        {
            return;
        }

        if (ReferenceEquals(frame.Parent, docked))
        {
            docked.Content = null;
        }
        else
        {
            Children.Remove(frame);
        }

        if (ReferenceEquals(holder, docked))
        {
            docked.Content = frame;
        }
        else
        {
            Children.Add(frame);
        }
    }
}
