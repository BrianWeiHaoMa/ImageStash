using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ImageStash.Window;

/// <summary>
/// The search box's whole text as a summon selects it, so that typing replaces it (G1), which is not the person's
/// selection: over it <c>Ctrl+C</c> copies the selected items, and only text the person selected in the box goes as text
/// (G7). It becomes the person's once they change it in the box: a click or a drag there, Shift with an arrow,
/// Home or End, <c>Ctrl+A</c>, which may make the very range the summon made, or a change of the text, as typing over it
/// makes; and it lapses when the box loses focus, which clears its selection. Up and Down, alone or with Shift,
/// leave the box selection alone while moving or extending the grid selection.
/// </summary>
public sealed class SummonSelection
{
    private enum Origin { Default, Summon, Person }

    private Origin origin;

    /// <summary>The box's selection is still the one the summon made.</summary>
    public bool Summons => origin == Origin.Summon;

    internal bool PositionedByPerson => origin == Origin.Person;

    /// <summary>Whether the box's <paramref name="selectedText"/> is text the person selected: selected text that is not the summon's.</summary>
    public bool PersonSelected(string? selectedText) => !Summons && !string.IsNullOrEmpty(selectedText);

    /// <summary>Follows <paramref name="searchBox"/>'s keys, presses, text and focus from now on.</summary>
    public void Follow(TextBox searchBox)
    {
        // Seen whoever handles them, the window's own keys included, so the rule alone decides which change the selection.
        searchBox.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            var routed = StashKeys.Route(e.Key, e.KeyModifiers, KeyPlace.SearchBox, boxHasText: !string.IsNullOrEmpty(searchBox.Text));
            if (StashKeys.Direction(routed) is null)
            {
                KeyDown(e.Key, e.KeyModifiers);
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        // The left button's press, a click or a drag's start; a right-click inside the selected text keeps it as it is.
        searchBox.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Properties.IsLeftButtonPressed)
            {
                MovedByPerson();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        searchBox.TextChanging += (_, _) => Changed();
        searchBox.LostFocus += (_, _) => Changed();
    }

    /// <summary>The summon has just selected the box's whole text (G1).</summary>
    public void Made() => origin = Origin.Summon;

    /// <summary>The person changed the box's selection or its text, or the box lost focus: whatever it selects now is the person's.</summary>
    public void Changed() => origin = Origin.Default;

    internal void MovedByPerson() => origin = Origin.Person;

    /// <summary>A key pressed with the box focused: one that selects in the box makes its selection the person's.</summary>
    public void KeyDown(Key key, KeyModifiers modifiers)
    {
        if (SelectsInBox(key, modifiers))
        {
            MovedByPerson();
        }
    }

    /// <summary>
    /// Whether a key pressed in the box sets its selection (G7): <c>Ctrl+A</c>; Left, Right, Home and End, with or without
    /// Shift or <c>Ctrl</c>; Up and Down with <c>Ctrl+Shift</c> stay in the box as well.
    /// </summary>
    public static bool SelectsInBox(Key key, KeyModifiers modifiers) => (key, modifiers & ~KeyModifiers.Control) switch
    {
        (Key.A, KeyModifiers.None) => modifiers == KeyModifiers.Control,
        (Key.Left or Key.Right or Key.Home or Key.End, KeyModifiers.None or KeyModifiers.Shift) => true,
        (Key.Up or Key.Down, KeyModifiers.Shift) => modifiers.HasFlag(KeyModifiers.Control),
        _ => false,
    };
}
