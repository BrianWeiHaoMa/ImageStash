using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>A pointer's press on a capture card, as the checks give one with no platform to deliver it.</summary>
public static class CaptureCardPresses
{
    /// <summary>A left press on the card's body, outside its field and buttons.</summary>
    public static Avalonia.Input.PointerPressedEventArgs Body(CaptureCard card)
    {
        var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true);
        var properties = new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed);
        return new Avalonia.Input.PointerPressedEventArgs(card, pointer, card, default, 0, properties, Avalonia.Input.KeyModifiers.None, 1);
    }
}
