using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The preview's name, edited in place (G17, G19): the item's name with its rename glyph, which a click, or
/// <see cref="Edit"/>, turns into a text box in the same place holding the name selected, so typing replaces it. Enter
/// commits what the box shows, any text, none included (G17: nothing requires a name), and Esc cancels; either turns it
/// back into the name and raises its event, and focus moving elsewhere in the window cancels it as Esc would. It shows
/// the name it is given and never the typed text, so no new name shows before the store has it (G28, R9); it never
/// reaches the store itself. Inside the box the keys it uses are its own, and it has no paste of its own and no menu
/// offering one, as the search box has none: ImageStash reads the clipboard only for its three actions (N9, R5). Never
/// a window or a popup of its own (F4).
/// </summary>
public sealed class NameField : Panel
{
    private readonly Button name;
    private readonly TextBlock nameText;
    private readonly TextBox box;

    public NameField()
    {
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        nameText = new TextBlock { FontFamily = font, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(glyph, 1);
        // Not focusable, so Tab's round in the preview is unchanged: the keyboard's way in is F2 (G7).
        name = new Button
        {
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { nameText, glyph } },
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(6, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(name, "Rename");
        ToolTipRule.Set(name, "Rename (F2)");
        name.Click += (_, _) => Edit();
        box = new TextBox { PlaceholderText = "Name", FontFamily = font, FontSize = 15, FontWeight = FontWeight.SemiBold, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, IsVisible = false };
        AutomationProperties.SetName(box, "Name");
        // The text box's own Paste (Ctrl+V, Shift+Insert, its menu) would read the clipboard's text (N9, R5); its menu
        // would then offer a Paste that does nothing (G35). Cut and Copy of its text stay on their keys.
        box.PastingFromClipboard += (_, e) => e.Handled = true;
        box.ContextFlyout = null;
        Children.Add(name);
        Children.Add(box);
        // Tunnelling, so Enter and Esc reach the field before the text box treats them as its own.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, e) => (e.RootVisual as InputElement)?.AddHandler(GotFocusEvent, FocusMoved, handledEventsToo: true);
        DetachedFromVisualTree += (_, e) => (e.RootVisual as InputElement)?.RemoveHandler(GotFocusEvent, FocusMoved);
    }

    /// <summary>Enter: the text to commit as the name, exactly as typed; the field shows the name it was given meanwhile.</summary>
    public event Action<string>? Committed;

    /// <summary>Esc: nothing is committed.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// The edit ended, whatever ended it: Enter and Esc, raised before <see cref="Committed"/> or <see cref="Cancelled"/>;
    /// focus moving elsewhere in the window; <see cref="StopEditing"/>.
    /// </summary>
    public event Action? Stopped;

    /// <summary>Whether the name is being edited, its text box showing in its place.</summary>
    public bool IsEditing { get; private set; }

    /// <summary>The name shown, as given; an empty name shows the item's file name, faint, as notices name such an item.</summary>
    public string Shown { get; private set; } = "";

    /// <summary>The text typed in the box.</summary>
    public string Text => box.Text ?? "";

    /// <summary>Shows <paramref name="item"/>'s name; while it is being edited, the box keeps what was typed.</summary>
    public void Show(StashItem item)
    {
        Shown = item.Name;
        nameText.Text = item.Name.Length > 0 ? item.Name : item.FileName;
        nameText.Opacity = item.Name.Length > 0 ? 1 : 0.6;
    }

    /// <summary>Turns the name into the text box holding it selected, with focus in the box.</summary>
    public void Edit()
    {
        box.Text = Shown;
        IsEditing = true;
        name.IsVisible = false;
        box.IsVisible = true;
        box.Focus();
        box.SelectAll();
    }

    /// <summary>Gives the text box focus again while the name is being edited, as a summon brings it back (G1).</summary>
    public void FocusBox() => box.Focus();

    /// <summary>Turns it back into the name with nothing committed; of its events only <see cref="Stopped"/> is raised.</summary>
    public void StopEditing()
    {
        if (!IsEditing)
        {
            return;
        }

        // Back to the name first, so focus given back is not taken for focus moving away from an edit.
        IsEditing = false;
        box.IsVisible = false;
        name.IsVisible = true;
        Stopped?.Invoke();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsEditing)
        {
            return;
        }

        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Enter, KeyModifiers.None):
                var typed = Text;
                StopEditing();
                Committed?.Invoke(typed);
                break;
            case (Key.Escape, KeyModifiers.None):
                StopEditing();
                Cancelled?.Invoke();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void FocusMoved(object? sender, FocusChangedEventArgs e)
    {
        if (IsEditing && e.Source is Visual focused && !ReferenceEquals(focused, box) && !box.IsVisualAncestorOf(focused))
        {
            StopEditing();
        }
    }
}
