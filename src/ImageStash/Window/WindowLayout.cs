using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The stash window's controls that only hold and arrange others (F4): the head with the search box, the Settings
/// button, the <c>+</c> menu and the pin; the grid's scroller, which hands a press on the grid's margin to the grid, and the notices' scroller;
/// the footer with the paste hints, the busy state and the count; and the rows that hold them, the preview over all but
/// the footer and the editor over all of them.
/// </summary>
public static class WindowLayout
{
    /// <summary>The search box, where the query is typed (G5, G7).</summary>
    public static TextBox SearchBox()
    {
        var search = new TextBox
        {
            PlaceholderText = "Search your stash",
            Height = 36,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            InnerLeftContent = IconGlyph.Of("", 14, new Thickness(10, 0, 2, 0)),
        };
        AutomationProperties.SetName(search, "Search your stash");
        return search;
    }

    /// <summary>
    /// The Settings button (G32), shaped as the <c>+</c> button beside it: never focused, so Tab still goes from the box
    /// to the grid (G7), and named and tipped for a screen reader.
    /// </summary>
    public static Button SettingsButton(Action clicked)
    {
        var settings = new Button { Content = IconGlyph.Of("", 16), Width = 36, Height = 36, Padding = default, Focusable = false };
        settings.Classes.Add(LookStyles.IconButton);
        settings.Click += (_, _) => clicked();
        ToolTipRule.Set(settings, "Settings");
        AutomationProperties.SetName(settings, "Settings");
        return settings;
    }

    /// <summary>The head: the search box, the Settings button, the <c>+</c> menu of ways in and the pin button.</summary>
    public static Grid Head(TextBox search, Button settings, Button add, ToggleButton pin)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(10, 10, 10, 6) };
        Grid.SetColumn(settings, 1);
        Grid.SetColumn(add, 2);
        Grid.SetColumn(pin, 3);
        settings.Margin = new Thickness(6, 0, 0, 0);
        add.Margin = new Thickness(6, 0, 0, 0);
        pin.Margin = new Thickness(6, 0, 0, 0);
        head.Children.Add(search);
        head.Children.Add(settings);
        head.Children.Add(add);
        head.Children.Add(pin);
        return head;
    }

    /// <summary>The grid's scroller: up and down only, its rows justified to its width (G6).</summary>
    public static ScrollViewer GridScroller(ThumbnailGrid grid)
    {
        var scroller = new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        // A press on the grid's margin lands on the scroller's presenter, not the grid, yet a person sees the grid's
        // blank space there (G7); the scroll bar keeps its own presses.
        scroller.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (ReferenceEquals(e.Source, scroller.Presenter))
            {
                grid.TakeFocusFrom(e);
            }
        });
        return scroller;
    }

    /// <summary>Notices taller together than the window scroll within it, so none is pushed out of reach (G16).</summary>
    public static ScrollViewer NoticeArea(StackPanel noticeStack) => new()
    {
        Content = noticeStack,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(12, 0, 12, 12),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>The footer: the paste hints and the busy state on the left, the count on the right.</summary>
    public static Border Footer(Control hints, StackPanel busy, TextBlock busyText, TextBlock count)
    {
        busy.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 48, MinWidth = 48, VerticalAlignment = VerticalAlignment.Center });
        busy.Children.Add(busyText);
        var footer = new Border
        {
            Height = 36,
            Padding = new Thickness(12, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Panel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { hints, busy } }, count } },
        };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable(LookKeys.Divider));
        return footer;
    }

    /// <summary>
    /// The rows: the head, the chips, the body and the footer, with the preview over all but the footer, the editor over
    /// all four with a footer of its own, and <paramref name="overBody"/> over the body, above both.
    /// </summary>
    public static Grid Rows(Control head, Control scopes, Control body, Control foot, Control preview, Control editor, Control overBody)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        Grid.SetRow(scopes, 1);
        Grid.SetRow(body, 2);
        Grid.SetRow(foot, 3);
        layout.Children.Add(head);
        layout.Children.Add(scopes);
        layout.Children.Add(body);
        layout.Children.Add(foot);
        Grid.SetRowSpan(preview, 3);
        layout.Children.Add(preview);
        Grid.SetRowSpan(editor, 4);
        layout.Children.Add(editor);
        Grid.SetRow(overBody, 2);
        layout.Children.Add(overBody);
        return layout;
    }
}
