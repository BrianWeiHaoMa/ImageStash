using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// A notice as a card, in the stash window's stack and in a notification above the tray alike (G16): its words, its
/// Undo when it has one, and a close button; the pointer over it holds its six seconds.
/// </summary>
public static class NoticeCard
{
    /// <summary>The ×'s name, which its tooltip shows and assistive technology reads.</summary>
    private const string Close = "Close";

    private const double TextHeight = 120;

    public static Control For(Notice notice, Notices notices)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        // A batch's notice names every file (G12), hundreds for a folder: its words scroll inside the card, so the card
        // stays in the window and its Undo where a click reaches it (G16).
        row.Children.Add(new ScrollViewer
        {
            Content = new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 },
            MaxHeight = TextHeight,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        if (notice.UndoLabel is { } label)
        {
            var undo = new Button { Content = label, Focusable = false };
            undo.Click += (_, _) => _ = notices.Undo(notice);
            row.Children.Add(undo);
        }

        row.Children.Add(CloseButton(() => notices.Close(notice)));
        var card = CardLook.Dress(new Border { Child = row, Padding = new Thickness(14, 6, 6, 6) });
        card.PointerEntered += (_, _) => notices.Hover(notice, over: true);
        card.PointerExited += (_, _) => notices.Hover(notice, over: false);
        return card;
    }

    /// <summary>
    /// The × every card carries, the plain one's and the capture notification's alike (G16): the close glyph of Segoe
    /// Fluent Icons (F10) in the theme's text colour, never focused, so a click on it leaves the keystrokes where they were.
    /// </summary>
    public static Button CloseButton(Action close)
    {
        var button = new Button
        {
            Content = IconGlyph.Of("", 12),
            Padding = new Thickness(6),
            Focusable = false,
        };
        button.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(button, Close);
        ToolTipRule.Set(button, Close);
        button.Click += (_, _) => close();
        return button;
    }
}
