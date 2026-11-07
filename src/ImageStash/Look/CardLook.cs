using Avalonia;
using Avalonia.Controls;

namespace ImageStash.Look;

/// <summary>
/// A card as every take draws one, bound to the take's keys: its ground, its hairline edge, its corners and, for a card
/// that floats over others, its shadow. The notices, the notifications' cards, the lists, the tag field's card, the
/// drop card, the editor's popovers and the Settings page's groups are all cards, so a take changes them together.
/// </summary>
public static class CardLook
{
    /// <summary>Dresses <paramref name="card"/> as a card, with the take's shadow when it <paramref name="floats"/>.</summary>
    public static T Dress<T>(T card, bool floats = true) where T : Border
    {
        card.BorderThickness = new Thickness(1);
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable(LookKeys.Card));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable(LookKeys.CardBorder));
        card.Bind(Border.CornerRadiusProperty, card.GetResourceObservable(LookKeys.CardRadius));
        if (floats)
        {
            card.Bind(Border.BoxShadowProperty, card.GetResourceObservable(LookKeys.CardShadow));
        }

        return card;
    }
}
