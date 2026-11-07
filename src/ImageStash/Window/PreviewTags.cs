using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ImageStash.Window;

/// <summary>
/// The preview's tags row (G17, G19): "Tags", then the item's tags each with its ×, and the box with its list of every
/// tag and its suggestions from the existing tags: the one <see cref="TagField"/> Type a tag… shows over the grid, placed in the preview. It
/// shows the tags it is given, the item's as the preview last read them, never a copy kept across changes. Enter
/// commits and Esc cancels, either emptying the box before it raises its event, and focus moving elsewhere in the
/// window empties it as Esc would; a hide moves no focus, so what is typed outlasts it. However many tags the
/// item carries, the row scrolls within a bounded height, so the picture and the handoff button below it stay in the
/// window (G19, G7, E4). It never reaches the store itself: the preview acts on its item through the window's one
/// organising entry. Never a window of its own (F4).
/// </summary>
public sealed class PreviewTags : Grid
{
    private readonly TagFieldScroller scroller;

    public PreviewTags()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        var label = new TextBlock { Text = "Tags", FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 14, 0) };
        Field = new TagField();
        // The box and its suggestions end the row: while focus is in it, they stay in view as the row grows.
        scroller = new TagFieldScroller(Field, () => HadFocus);
        SetColumn(scroller, 1);
        Children.Add(label);
        Children.Add(scroller);
        Field.Committed += text =>
        {
            Field.Clear();
            Committed?.Invoke(text);
        };
        Field.Cancelled += () =>
        {
            Field.Clear();
            Cancelled?.Invoke();
        };
        Field.Removed += tag => Removed?.Invoke(tag);
        Field.Picked += tag => Picked?.Invoke(tag);
        AttachedToVisualTree += (_, e) => (e.RootVisual as InputElement)?.AddHandler(GotFocusEvent, FocusMoved, handledEventsToo: true);
        DetachedFromVisualTree += (_, e) => (e.RootVisual as InputElement)?.RemoveHandler(GotFocusEvent, FocusMoved);
    }

    /// <summary>Enter, or a click on a suggestion: the words to commit, as typed, one tag per word.</summary>
    public event Action<string>? Committed;

    /// <summary>A pick in the list of every tag: that tag is to be given to the item, or taken off it when it carries it (G17).</summary>
    public event Action<string>? Picked;

    /// <summary>Esc: nothing is committed.</summary>
    public event Action? Cancelled;

    /// <summary>A tag's ×: that tag is to be taken off the item.</summary>
    public event Action<string>? Removed;

    public TagField Field { get; }

    /// <summary>
    /// Whether focus was last inside the box, as a summon gives it back (G1); hiding the window moves no focus inside it,
    /// so this outlasts the hide.
    /// </summary>
    public bool HadFocus { get; private set; }

    /// <summary>
    /// Shows <paramref name="tags"/>, the item's, each with its ×, and takes the existing tags the suggestions and the list
    /// come from; the list marks the item's tags, since its one item carries every one of them (G17).
    /// </summary>
    public void Show(IReadOnlyList<string> tags, IReadOnlyList<string> existingTags) => Field.Show(tags, existingTags, tags);

    /// <summary>Empties the box and closes the list, as the preview opening on another item or closing does.</summary>
    public void Clear()
    {
        Field.CloseList();
        Field.Clear();
    }

    public void FocusBox() => Field.FocusBox();

    private void FocusMoved(object? sender, FocusChangedEventArgs e)
    {
        var inField = e.Source is Visual focused && Field.IsVisualAncestorOf(focused);
        if (HadFocus && !inField)
        {
            // Focus moving elsewhere in the window ends the edit as Esc would, leaving focus where it went (G17); a hide
            // moves no focus, so a summon brings the edit back as it was left (G1).
            Clear();
        }

        HadFocus = inField;
        scroller.KeepBoxInView();
    }
}
