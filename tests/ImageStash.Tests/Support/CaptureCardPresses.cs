using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>A press or a click on a capture card, as the checks give one with no platform to deliver it, and the card to give it to.</summary>
public static class CaptureCardPresses
{
    /// <summary>A left press on the card's body, outside its field and buttons.</summary>
    public static Avalonia.Input.PointerPressedEventArgs Body(CaptureCard card)
    {
        var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true);
        var properties = new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed);
        return new Avalonia.Input.PointerPressedEventArgs(card, pointer, card, default, 0, properties, Avalonia.Input.KeyModifiers.None, 1);
    }

    /// <summary>The card the notifications' window draws for the newest notice of <paramref name="stack"/>.</summary>
    public static CaptureCard Newest(Notices stack)
    {
        var notice = stack.Shown.Last();
        return (CaptureCard)notice.Card!(notice, stack);
    }

    /// <summary>A click on the card's button reading <paramref name="words"/>: Edit, Trim or Undo.</summary>
    public static void Click(CaptureCard card, string words) =>
        card.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, words)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
