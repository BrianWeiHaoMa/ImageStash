using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ImageStash.Seams;
using ImageStash.Look;
using Avalonia.Reactive;

namespace ImageStash.Window;

/// <summary>
/// The whole stash window as a drop target, the empty state included (G2, G11): while something is dragged over it,
/// the drop card says what release will add (<see cref="DropCard"/>); release hands what was dropped to the import.
/// What a drag carries is read once per drag, through the Clipboard-and-drops seam, by name only. The window's own
/// drag-out is not taken back: it is not a drop out of the window, so it adds nothing and records no use (G10).
/// </summary>
public sealed class DropTarget
{
    private readonly IClipboardAndDrops drops;
    private readonly Import import;
    private readonly Func<bool> draggingOut;
    private readonly Func<IReadOnlyList<string>> litTags;
    private readonly TextBlock title;
    private readonly StackPanel lines;
    private IDataTransfer? draggedData;
    private DropContents? dragged;
    private bool leaving;

    /// <param name="litTags">The tags of the lit chips, which the drop card names, since the new items will get them (G11).</param>
    public DropTarget(Interactive window, IClipboardAndDrops drops, Import import, Func<bool> draggingOut, Func<IReadOnlyList<string>> litTags)
    {
        this.drops = drops;
        this.import = import;
        this.draggingOut = draggingOut;
        this.litTags = litTags;
        title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        lines = new StackPanel { Spacing = 4 };
        var content = new StackPanel { Spacing = 6, Children = { title, lines } };
        content.Children.Insert(0, new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var card = CardLook.Dress(new Border
        {
            Child = content,
            Padding = new Thickness(22, 16),
            MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var outline = new Rectangle { StrokeThickness = 2, StrokeDashArray = [4, 3], Margin = new Thickness(6) };
        outline.Bind(Shape.StrokeProperty, outline.GetResourceObservable(LookKeys.AccentRing));
        // The outline's corners are a card's, as the card inside it.
        outline.GetResourceObservable(LookKeys.CardRadius).Subscribe(new AnonymousObserver<object?>(radius =>
        {
            if (radius is CornerRadius corner)
            {
                outline.RadiusX = outline.RadiusY = corner.TopLeft;
            }
        }));
        // Shown over the grid or the empty state while a drag is in, and never in the way of the drag's own hit tests.
        Card = new Panel { Children = { outline, card }, IsVisible = false, IsHitTestVisible = false };

        DragDrop.SetAllowDrop(window, true);
        window.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        window.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        window.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        window.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>The drop card, for the window to lay over its grid.</summary>
    public Control Card { get; }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (draggingOut())
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        // Moving between the window's own controls leaves one and enters the next with the same drag: one drag, one read.
        leaving = false;
        if (!ReferenceEquals(e.DataTransfer, draggedData))
        {
            EndDrag();
            draggedData = e.DataTransfer;
            dragged = drops.ReadDrop(e.DataTransfer);
            Show(DropCard.Of(dragged, litTags()));
        }

        e.DragEffects = Accepting(e.DragEffects);
    }

    private void OnDragOver(object? sender, DragEventArgs e) => e.DragEffects = draggingOut() ? DragDropEffects.None : Accepting(e.DragEffects);

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // A leave followed at once by an enter is a move between the window's controls; only a leave alone ends the drag.
        leaving = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (leaving)
            {
                EndDrag();
            }
        }, DispatcherPriority.Background);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        EndDrag();
        if (draggingOut())
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = Accepting(e.DragEffects);
        _ = import.Drop(drops.ReadDrop(e.DataTransfer));
    }

    /// <summary>
    /// A drop is taken as a copy (G11: the stash always makes copies), and every drop is taken, so a link or a file
    /// ImageStash cannot add is named after release rather than met with a forbidden cursor.
    /// </summary>
    private static DragDropEffects Accepting(DragDropEffects offered) =>
        offered.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : offered & DragDropEffects.Link;

    private void Show(DropCard card)
    {
        title.Text = card.Title;
        lines.Children.Clear();
        foreach (var line in card.Lines)
        {
            lines.Children.Add(new TextBlock { Text = line, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        }

        Card.IsVisible = true;
    }

    private void EndDrag()
    {
        leaving = false;
        Card.IsVisible = false;
        draggedData = null;
        dragged?.Dispose();
        dragged = null;
    }
}
