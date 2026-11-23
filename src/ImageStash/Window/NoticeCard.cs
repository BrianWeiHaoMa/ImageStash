using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// A notice as a card, in the stash window's stack and in a notification above the tray alike (G16): its words, under
/// its heading in semi-bold when it has one (an import's counts, G12), its Undo while the notice offers it, naming the
/// key that runs it in the stash window (G7), and a close button; the pointer over it holds its time, six seconds above
/// the tray and as long as its words need in the window (Notice.cs), where a notice naming a problem stays past its time
/// with its words and its close button, its Undo button gone with the Undo's end.
/// </summary>
public static class NoticeCard
{
    /// <summary>The ×'s name, which its tooltip shows and assistive technology reads.</summary>
    private const string Close = "Close";

    private const double TextHeight = 120;

    public static Control For(Notice notice, Notices notices)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Words(notice));
        if (notice.UndoLabel is { } label)
        {
            var undo = new Button { Content = label, Focusable = false };
            undo.Click += (_, _) => _ = notices.Undo(notice);
            if (notices.UndoKey is { } key)
            {
                ToolTipRule.Set(undo, $"{label} ({key})");
            }

            row.Children.Add(undo);
            // The window keeps a card while its notice shows, past the Undo's end too: the button follows the offer.
            void Follow()
            {
                undo.IsVisible = notices.OffersUndo(notice);
                if (!notices.Shown.Contains(notice))
                {
                    notices.Changed -= Follow;
                }
            }

            notices.Changed += Follow;
        }

        row.Children.Add(CloseButton(() => notices.Close(notice)));
        var card = CardLook.Dress(new Border { Child = row, Padding = new Thickness(14, 6, 6, 6) });
        card.PointerEntered += (_, _) => notices.Hover(notice, over: true);
        card.PointerExited += (_, _) => notices.Hover(notice, over: false);
        return card;
    }

    /// <summary>
    /// The notice's words: its heading, when it has one, semi-bold and always in view, above the rest, which scroll inside
    /// the card: a batch's notice may name hundreds of files that were not added (G12), and the card stays in the window
    /// with its Undo where a click reaches it (G16).
    /// </summary>
    private static Control Words(Notice notice)
    {
        var text = new ScrollViewer
        {
            Content = new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 },
            MaxHeight = TextHeight,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        if (notice.Heading is not { } heading)
        {
            return text;
        }

        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
        if (notice.Text.Length > 0)
        {
            words.Children.Add(text);
        }

        return words;
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
