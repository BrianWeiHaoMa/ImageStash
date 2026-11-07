using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ImageStash.Window;

/// <summary>
/// The item menu's interaction handler, which keeps a drag begun on the scroll bar of its Add tag ▸ or Remove tag ▸ list
/// until the button is let go (G17: "A list longer than fits scrolls", and a click on a listed tag is a click begun on
/// it). Avalonia's own handler, on every move with the left button down, compares the bounds of the menu item holding the
/// pressed part, taken in the menu's popup, with the pointer's position, taken in the submenu's popup, and drops the
/// pointer's capture when the one does not hold the other: so the thumb's drag stopped mid-way, and the let-go reached the
/// entry then under the pointer as a click. While a part of a scroll bar (its thumb, or the strip either side of it)
/// holds the capture, this handler leaves the move to that part; every other move, and everything else, is Avalonia's.
/// </summary>
public sealed class TagListDrag() : DefaultMenuInteractionHandler(isContextMenu: true)
{
    protected override void PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured is Visual captured && captured.FindAncestorOfType<ScrollBar>(includeSelf: true) is not null)
        {
            return;
        }

        base.PointerMoved(sender, e);
    }
}
