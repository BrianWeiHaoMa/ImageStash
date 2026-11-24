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
/// back into the name and raises its event. Focus moving elsewhere in the window raises <see cref="Left"/>, which the
/// preview commits as Enter would, focus staying where it went (<see cref="FieldLeaving"/>, G19); a hide moves no focus,
/// so the edit outlasts it (G1). The capture card's name is one too, drawn boxed (G16), which wires its own ends. It shows
/// the name it is given and never the typed text, so no new name shows before the store has it (G28, R9); it never
/// reaches the store itself. Inside the box the keys it uses are its own, and it has no paste of its own and no menu
/// offering one, as the search box has none: ImageStash reads the clipboard only for its three actions (N9, R5). Never
/// a window or a popup of its own (F4).
/// </summary>
public sealed class NameField : Panel
{
    private readonly Button name;
    private readonly Control nameShown;
    private readonly TextBlock nameText;
    private readonly TextBox box;

    /// <param name="boxed">
    /// Drawn as the field it is, at the words' size in a hairline box across its column with the rename glyph at its end,
    /// as the capture card shows it (G16); otherwise the preview's name, a heading with the glyph beside it.
    /// </param>
    public NameField(bool boxed = false)
    {
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        nameText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(glyph, 1);
        // Not focusable, so Tab's round in the preview is unchanged: the keyboard's way in is F2 (G7).
        name = new Button
        {
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { nameText, glyph } },
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = boxed ? new Thickness(8, 4) : new Thickness(6, 4),
            HorizontalAlignment = boxed ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = boxed ? VerticalAlignment.Stretch : VerticalAlignment.Center,
            HorizontalContentAlignment = boxed ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(name, "Rename");
        ToolTipRule.Set(name, "Rename (F2)");
        name.Click += (_, _) => Edit();
        box = new TextBox { PlaceholderText = "Name", Height = 32, VerticalContentAlignment = VerticalAlignment.Center, IsVisible = false };
        if (!boxed)
        {
            (nameText.FontFamily, nameText.FontSize, nameText.FontWeight) = (font, 15, FontWeight.SemiBold);
            (box.FontFamily, box.FontSize, box.FontWeight) = (font, 15, FontWeight.SemiBold);
        }

        AutomationProperties.SetName(box, "Name");
        // The text box's own Paste (Ctrl+V, Shift+Insert, its menu) would read the clipboard's text (N9, R5); its menu
        // would then offer a Paste that does nothing (G35). Cut and Copy of its text stay on their keys.
        box.PastingFromClipboard += (_, e) => e.Handled = true;
        box.ContextFlyout = null;
        nameShown = boxed ? Boxed(name, box.Height) : name;
        Children.Add(nameShown);
        Children.Add(box);
        // Tunnelling, so Enter and Esc reach the field before the text box treats them as its own.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, e) => (e.RootVisual as InputElement)?.AddHandler(GotFocusEvent, FocusMoved, handledEventsToo: true);
        DetachedFromVisualTree += (_, e) => (e.RootVisual as InputElement)?.RemoveHandler(GotFocusEvent, FocusMoved);
    }

    /// <summary>Enter: the text to commit as the name, exactly as typed; the field shows the name it was given meanwhile.</summary>
    public event Action<string>? Committed;

    /// <summary>
    /// Focus moved to another part of the window: the text to commit as the name, as Enter's <see cref="Committed"/>
    /// gives it; focus stays where it went (G19, G7).
    /// </summary>
    public event Action<string>? Left;

    /// <summary>Esc: nothing is committed.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// The edit ended, whatever ended it: Enter and Esc, raised before <see cref="Committed"/> or <see cref="Cancelled"/>;
    /// focus moving elsewhere in the window, raised before <see cref="Left"/>; <see cref="StopEditing"/>.
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
        nameShown.IsVisible = false;
        box.IsVisible = true;
        box.Focus();
        box.SelectAll();
    }

    /// <summary>Gives the text box focus again while the name is being edited, as a summon brings it back (G1).</summary>
    public void FocusBox() => box.Focus();

    /// <summary>Whether its text box has focus.</summary>
    public bool BoxFocused => box.IsFocused;

    /// <summary>
    /// Enter's commit: the edit ended and what the box shows raised as <see cref="Committed"/>; also a click on the
    /// preview's ‹ or ›, which takes no focus, so its commit is Enter's (G19).
    /// </summary>
    public void CommitAsEnter()
    {
        var typed = Text;
        StopEditing();
        Committed?.Invoke(typed);
    }

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
        nameShown.IsVisible = true;
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
                CommitAsEnter();
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
        var intoBox = e.Source is Visual focused && (ReferenceEquals(focused, box) || box.IsVisualAncestorOf(focused));
        if (FieldLeaving.Commits(IsEditing, intoBox))
        {
            var typed = Text;
            StopEditing();
            Left?.Invoke(typed);
        }
    }

    /// <summary>The name in a hairline box as tall as its text box, in the look's field colours, so it reads as the field it is.</summary>
    private static Border Boxed(Button name, double height)
    {
        var field = new Border { Child = name, BorderThickness = new Thickness(1), Height = height };
        field.Bind(Border.BackgroundProperty, field.GetResourceObservable(LookKeys.ShortcutFieldGround));
        field.Bind(Border.BorderBrushProperty, field.GetResourceObservable(LookKeys.ShortcutFieldBorder));
        field.Bind(Border.CornerRadiusProperty, field.GetResourceObservable(LookKeys.ControlRadius));
        return field;
    }
}
