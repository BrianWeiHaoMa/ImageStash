using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Seams;
using Thickness = Avalonia.Thickness;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The editor as the stash window shows it (F4, G20): an overlay over the whole of the window's content,
/// never a window of its own. Its head has the back arrow, the item's name with "editing", Compare, which shows the
/// original while held, and a ⋯ menu with Save as new item, Revert to original on an edited item, and Discard changes;
/// under it, while the picture draws a font this PC lacks, a line saying so (G21); then the canvas, with the field a
/// text is typed in over it while typing; on an animated item the filmstrip (G22); the tool row; the current tool's
/// option row, which states what the mouse does; and its own footer
/// with Cancel, the output's size and estimated file size with the Shrink choice behind it (G24), and Save, which shows
/// the busy state naming the item while it runs (G35). Its keys are G25's (<see cref="EditorKeys"/>), so Enter never
/// pastes (G20). How a session ends is the window's: it asks with <see cref="SaveRequested"/>,
/// <see cref="SaveAsNewItemRequested"/> and <see cref="CancelRequested"/>.
/// </summary>
public sealed class EditorOverlay : Border
{
    public const string Editing = "editing";
    public const string SaveAsNewItem = "Save as new item";
    public const string RevertToOriginal = "Revert to original";
    public const string DiscardChanges = "Discard changes";
    public const string OutputSize = "Output size";

    private static readonly IReadOnlyList<(Shrink Shrink, string Words)> Shrinks = [(Shrink.Full, "Full size"), (Shrink.ThreeQuarter, "Three-quarter size"), (Shrink.Half, "Half size")];

    private readonly EditorCanvas canvas;
    private readonly Filmstrip filmstrip;
    private readonly TextField field;
    private readonly FontFallbackNote note = new();
    private readonly IFonts fonts;
    private readonly EditorPane pane = new();
    private readonly TextBlock name;
    private readonly Button compare;
    private readonly Button more;
    private readonly StackPanel tools;
    private readonly ContentControl options;
    private readonly Button cancel;
    private readonly Button size;
    private readonly TextBlock sizeText;
    private readonly DockPanel busy;
    private readonly TextBlock busyText;
    private readonly Button save;
    private EditorSession? session;
    private (EditorTool? Tool, int? Selected) refreshedFor;
    private IInputElement? focusLeft;
    private bool focusByKeys;
    private bool spaceTaken;

    /// <param name="failed">Told of a picture the screen could not draw (G35).</param>
    /// <param name="fonts">The installed fonts, which the Text tool's font menu lists (F8, F13).</param>
    public EditorOverlay(Action<Failure> failed, IFonts fonts)
    {
        this.fonts = fonts;
        canvas = new EditorCanvas(failed) { Margin = new Thickness(10, 0) };
        field = new TextField(canvas) { Margin = canvas.Margin };
        filmstrip = new Filmstrip(failed) { Margin = new Thickness(10, 8, 10, 0) };
        canvas.FallbacksChanged += () => note.Show(canvas.Fallbacks);
        name = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        compare = EditorButtons.Labelled("\ue8ab", "Compare");
        more = EditorButtons.Icon("\ue712", "More options");
        tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10, 8, 10, 0) };
        options = new ContentControl { Margin = new Thickness(12, 8, 12, 0) };
        cancel = EditorButtons.Labelled(null, "Cancel", "Esc");
        busyText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        var progress = new ProgressBar { IsIndeterminate = true, Width = 48, MinWidth = 48, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(progress, Dock.Left);
        busy = new DockPanel { IsVisible = false, Margin = new Thickness(0, 0, 0, 8), Children = { progress, busyText } };
        sizeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        size = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sizeText, EditorButtons.Glyph("\ue70d", 10) } },
            Height = 34,
        };
        save = EditorButtons.Labelled("\ue74e", "Save", "Ctrl S", accent: true);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto") };
        var (head, foot) = (Head(), Foot());
        Grid.SetRow(note, 1);
        // The field lies over the canvas, in its place, so the two share the canvas's units; the pane may stand beside them.
        var picture = pane.Beside(canvas, field);
        Grid.SetRow(picture, 2);
        Grid.SetRow(filmstrip, 3);
        Grid.SetRow(tools, 4);
        Grid.SetRow(options, 5);
        Grid.SetRow(foot, 6);
        layout.Children.AddRange([head, note, picture, filmstrip, tools, options, foot]);
        Child = new Panel { Children = { layout, pane } };
        // Tab goes round the editor's own controls and never reaches what it covers (G7).
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        this.Bind(BackgroundProperty, this.GetResourceObservable(LookKeys.Surface));
        IsVisible = false;
        WireInput();
    }

    /// <summary>Save, <c>Ctrl+S</c>: the window saves the session (G26).</summary>
    public event Action? SaveRequested;

    /// <summary>The ⋯ menu's Save as new item (G26).</summary>
    public event Action? SaveAsNewItemRequested;

    /// <summary>Cancel, the back arrow, Esc with nothing selected and Discard changes: the window leaves the item as it was (G20).</summary>
    public event Action? CancelRequested;

    /// <summary>The session shown, or null while the editor is closed.</summary>
    public EditorSession? Session => session;

    public EditorCanvas Canvas => canvas;

    public Filmstrip Filmstrip => filmstrip;

    /// <summary>Shows <paramref name="next"/> with focus on the canvas when it is a new one; null closes the overlay.</summary>
    public void Show(EditorSession? next)
    {
        if (session is not null)
        {
            session.Changed -= OnSessionChanged;
        }

        var fresh = !ReferenceEquals(session, next);
        session = next;
        pane.Hide();
        canvas.Show(next);
        filmstrip.Show(next);
        field.Show(next);
        IsVisible = next is not null;
        if (next is null)
        {
            options.Content = null;
            tools.Children.Clear();
            focusLeft = null;
            return;
        }

        next.Changed += OnSessionChanged;
        if (fresh)
        {
            focusLeft = null;
            ShowTools(next);
        }

        Refresh();
    }

    /// <summary>The head: the back arrow, the item's name with "editing", Compare held, and the ⋯ menu (G20).</summary>
    private Grid Head()
    {
        var back = EditorButtons.Icon("\ue72b", "Back", "Back (Esc)");
        back.Click += (_, _) => CancelRequested?.Invoke();
        var editing = new TextBlock { Text = Editing, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        // A star column, unlike a horizontal StackPanel, bounds the name, so a long one trims; aligned left, the column is
        // only as wide as a short name, with "editing" beside it (G20, G21).
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0), Children = { name, editing } };
        Grid.SetColumn(editing, 1);
        ToolTipRule.Set(compare, "Hold to see the original (C)");
        // Held, not clicked: the original shows while the button is down (G20).
        compare.AddHandler(PointerPressedEvent, (_, _) => SetCompare(true), RoutingStrategies.Tunnel, handledEventsToo: true);
        compare.AddHandler(PointerReleasedEvent, (_, _) => SetCompare(false), RoutingStrategies.Tunnel, handledEventsToo: true);
        compare.AddHandler(PointerCaptureLostEvent, (_, _) => SetCompare(false), RoutingStrategies.Bubble, handledEventsToo: true);
        more.Click += (_, _) => ShowMenu();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(10, 10, 10, 8) };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(compare, 2);
        Grid.SetColumn(more, 3);
        more.Margin = new Thickness(6, 0, 0, 0);
        head.Children.AddRange([back, title, compare, more]);
        return head;
    }

    /// <summary>The footer: the busy state on a line of its own while it runs; Cancel, the output's size with the Shrink choice behind it, and Save (G24, G35).</summary>
    private Grid Foot()
    {
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        size.Click += (_, _) => ShowShrinkMenu();
        AutomationProperties.SetName(size, OutputSize);
        ToolTipRule.Set(size, "The saved picture's size; Shrink it here");
        save.Click += (_, _) => SaveRequested?.Invoke();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { size, save } };
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(12, 10, 12, 12) };
        // On a line of its own: words as long as an item's name, beside the size and Save, would push them past the
        // window's edge at its minimum width (G20, G35).
        Grid.SetColumnSpan(busy, 2);
        Grid.SetRow(cancel, 1);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        foot.Children.AddRange([busy, cancel, right]);
        return foot;
    }

    /// <summary>The editor's keys, where focus was left in it, Compare's end, and the pane's light dismiss.</summary>
    private void WireInput()
    {
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        {
            if (e.Key == Key.C)
            {
                SetCompare(false);
            }
            else if (e.Key == Key.Space && spaceTaken)
            {
                // A button clicks on Space's release even when its press was handled: the play took this Space.
                spaceTaken = false;
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            focusLeft = e.Source as IInputElement;
            // How the user last moved focus, by Tab or the arrows or else by the pointer; the code's own moves say nothing of it.
            if (e.NavigationMethod != NavigationMethod.Unspecified)
            {
                focusByKeys = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
            }

            // Focus moving to another of the editor's controls ends the typing; the window losing the foreground does not,
            // nor focus moving to Text's options while the field's layer holds no letter, which ending it would take away.
            if (session is { Typing: true } open && !field.Holds(e.Source) && !(open.OptionsKeepField && AmongTheOptions(e.Source)))
            {
                open.EndTyping();
            }
        }, RoutingStrategies.Bubble);
        // A C let go in another app is never seen here: Compare ends when the window loses the foreground.
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is WindowBase window)
            {
                window.Deactivated += (_, _) => SetCompare(false);
            }
        };
        // A press anywhere else closes what the pane shows, as a flyout's light dismiss does, and does nothing more.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (pane.IsOpen && !pane.Holds(e.Source))
            {
                pane.Hide();
                TypingGoesOn(paneClosed: true);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>Whether <paramref name="source"/>, where focus went, is among the current tool's options: its row, the pane, or the canvas while it picks a colour for them.</summary>
    private bool AmongTheOptions(object? source) =>
        source is Visual visual && (options.IsVisualAncestorOf(visual) || pane.Holds(visual) || (canvas.Picking && ReferenceEquals(visual, canvas)));

    /// <summary>
    /// Gives a field still open, after its layer's options were chosen among (<see cref="EditorSession.OptionsKeepField"/>),
    /// the focus back from the editor's control that holds it, so what is typed next lands in its layer; false while none
    /// is open. Until the pane has just closed (<paramref name="paneClosed"/>), a button still pressed keeps it, as its
    /// press would end if focus left it, and so does the picker's hex, which is typed in, and so does a control the user
    /// reached by Tab or the arrows: a choice made there by key, or any other change of the session's, leaves focus there,
    /// as a key's choice does in Windows. Focus outside the editor (a menu open, another window) is never taken.
    /// </summary>
    private bool TypingGoesOn(bool paneClosed = false)
    {
        if (session is not { Typing: true })
        {
            return false;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (!field.Holds(focused) && (paneClosed || (focused is Visual inside && this.IsVisualAncestorOf(inside) && !focusByKeys && focused is not (Button { IsPressed: true } or TextBox))))
        {
            field.TakeFocus();
        }

        return true;
    }

    /// <summary>Focus where it was left in the editor, as a summon gives it back (G1), else on the canvas.</summary>
    public void FocusAsLeft()
    {
        if (focusLeft is Visual { IsEffectivelyVisible: true } left && this.IsVisualAncestorOf(left) && left is IInputElement { IsEffectivelyEnabled: true })
        {
            focusLeft.Focus();
        }
        else
        {
            canvas.Focus();
        }
    }

    /// <summary>The ⋯ menu's entries for the session: Revert to original only on an item the store holds layers for (G20).</summary>
    private IReadOnlyList<string> MenuEntries() =>
        session is { Edit.Layers.IsEmpty: false } ? [SaveAsNewItem, RevertToOriginal, DiscardChanges] : [SaveAsNewItem, DiscardChanges];

    private void ShowMenu()
    {
        if (session is null)
        {
            return;
        }

        var menu = Menu(PlacementMode.BottomEdgeAlignedRight);
        foreach (var entry in MenuEntries())
        {
            if (entry == DiscardChanges)
            {
                menu.Items.Add(new Separator());
            }

            var item = new MenuItem { Header = entry };
            item.Click += (_, _) => FromMenu(entry);
            menu.Items.Add(item);
        }

        menu.Open(more);
    }

    /// <summary>The Shrink choice behind the size chip (G24): full, three-quarter or half size, the one in effect marked.</summary>
    private void ShowShrinkMenu()
    {
        if (session is not { } shown)
        {
            return;
        }

        var menu = Menu(PlacementMode.TopEdgeAlignedRight);
        foreach (var (shrink, words) in Shrinks)
        {
            var item = new MenuItem { Header = words, ToggleType = MenuItemToggleType.Radio, GroupName = "Shrink", IsChecked = shrink == shown.Shrink };
            item.Click += (_, _) => ChooseShrink(shrink);
            menu.Items.Add(item);
        }

        menu.Open(size);
    }

    /// <summary>
    /// A menu of the editor's. It is a popup, whose keys never reach the editor, so it hands them the editor's own keys:
    /// <c>Ctrl+Z</c>, <c>Ctrl+Y</c> and <c>Ctrl+S</c> act while it is open (G25), every other key is the menu's.
    /// </summary>
    private ContextMenu Menu(PlacementMode placement)
    {
        var menu = new ContextMenu { Placement = placement };
        menu.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        // A field still open as the menu closes (the font menu opened before a letter was typed) takes the focus back from
        // the button the menu returns it to, once the menu has.
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => TypingGoesOn());
        return menu;
    }

    private void FromMenu(string entry)
    {
        switch (entry)
        {
            case SaveAsNewItem:
                SaveAsNewItemRequested?.Invoke();
                break;
            case RevertToOriginal:
                // Inside the editor it empties the list, and the item changes only at Save (G26).
                session?.RevertToOriginal();
                break;
            default:
                CancelRequested?.Invoke();
                break;
        }
    }

    private void ShowTools(EditorSession shown)
    {
        tools.Children.Clear();
        foreach (var tool in shown.Tools)
        {
            var glyph = tool switch
            {
                TextTool => "",
                CropTool => "",
                TrimTool => "",
                HideTool => "",
                _ => "",
            };
            var choice = EditorButtons.Choice(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { EditorButtons.Glyph(glyph, 14), new TextBlock { Text = tool.Name } } }, tool.Name, () =>
            {
                if (session is { } open)
                {
                    open.Tool = tool;
                }
            });
            choice.Tag = tool;
            tools.Children.Add(choice);
        }
    }

    private void OnSessionChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    /// <summary>Shows the session as it is now: its name, the current tool and its options, the output's size, the busy state.</summary>
    private void Refresh()
    {
        if (session is not { } shown)
        {
            return;
        }

        // A colour being picked is for the tool and the layer its picker was opened on: either replaced ends it, so the next
        // press is the tools' (G21, G23); the picker's own closing as the pick starts changes neither.
        if (!ReferenceEquals(refreshedFor.Tool, shown.Tool) || refreshedFor.Selected != shown.Selected)
        {
            canvas.StopPicking();
        }

        refreshedFor = (shown.Tool, shown.Selected);
        name.Text = shown.Subject;
        ToolTipRule.ShowWhole(name, shown.Subject);
        foreach (var choice in tools.Children.OfType<ToggleButton>())
        {
            choice.IsChecked = ReferenceEquals(choice.Tag, shown.Tool);
        }

        if (options.Content is IOptionRow row && ReferenceEquals(row.Tool, shown.Tool))
        {
            row.Refresh();
        }
        else
        {
            pane.Hide();
            options.Content = OptionRows.For(shown.Tool, shown, canvas, pane, Menu, fonts);
        }

        // Focus the pointer left among the options, choosing one while the field stays open, goes back to the field (G21).
        TypingGoesOn();

        // G24: the size for the live layers; the estimate only once it is known for them, never a stale one as certain.
        var output = shown.OutputSize;
        sizeText.Text = $"{output.Width} × {output.Height}" + (shown.EstimatedBytes is { } bytes ? " · " + FileSize.Words(bytes) : shown.Estimating ? " · …" : "");
        // G35: the running Save named, and nothing else asked of the session meanwhile.
        busy.IsVisible = shown.Busy is not null;
        busyText.Text = shown.Busy ?? "";
        save.IsEnabled = cancel.IsEnabled = size.IsEnabled = more.IsEnabled = shown.Busy is null && shown.IsOpen;
    }

    private void ChooseShrink(Shrink shrink)
    {
        if (session is { } open)
        {
            open.Shrink = shrink;
        }

        Refresh();
    }

    private void SetCompare(bool held)
    {
        if (session is { IsOpen: true } open)
        {
            open.Compare = held;
        }
    }

    /// <summary>G25's keys, before the focused control: <c>Ctrl+Z</c>, <c>Ctrl+Y</c> and <c>Ctrl+S</c> from anywhere in the editor, the rest outside the hex.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            spaceTaken = false;
        }

        if (session is not { IsOpen: true } open || e.Handled)
        {
            return;
        }

        // A menu or the picker open over the editor keeps every key but Ctrl+Z, Ctrl+Y and Ctrl+S, as a text field does.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var inField = sender is ContextMenu || focused is TextBox || focused is not Visual inside || !this.IsVisualAncestorOf(inside);
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && (canvas.Picking || pane.IsOpen))
        {
            // Esc closes the picker, its hex included, or ends the colour being picked, before it reaches the layers; a field
            // still open keeps its layer, and the focus.
            canvas.StopPicking();
            pane.Hide();
            if (!TypingGoesOn(paneClosed: true))
            {
                canvas.Focus();
            }

            e.Handled = true;
            return;
        }

        switch (EditorKeys.Route(e.Key, e.KeyModifiers, inField, open.Selected is not null))
        {
            case EditorKeyAction.Undo:
                open.Undo();
                break;
            case EditorKeyAction.Redo:
                open.Redo();
                break;
            case EditorKeyAction.Save:
                SaveRequested?.Invoke();
                break;
            case EditorKeyAction.DeleteSelected:
                open.DeleteSelected();
                break;
            case EditorKeyAction.Deselect:
                open.Deselect();
                break;
            case EditorKeyAction.Cancel:
                CancelRequested?.Invoke();
                break;
            case EditorKeyAction.Compare:
                open.Compare = true;
                break;
            // Esc in a menu opened while a field stays open (the font menu before a letter is typed) is the menu's, never the field's.
            case EditorKeyAction.LeaveText when open.Typing && !(sender is ContextMenu):
                open.EndTyping();
                canvas.Focus();
                break;
            // On a still there is nothing to play, and the key is the focused control's.
            case EditorKeyAction.PlayPause when open.Animated:
                open.TogglePlaying();
                spaceTaken = true;
                break;
            case EditorKeyAction.StepForward when open.Animated:
                open.Step(1);
                break;
            case EditorKeyAction.StepBack when open.Animated:
                open.Step(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
        // A menu that handed the key over has done its part.
        (sender as ContextMenu)?.Close();
    }
}
