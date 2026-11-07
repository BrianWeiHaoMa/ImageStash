using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Layers;
using ImageStash.Media;
using Thickness = Avalonia.Thickness;
using ImageStash.Look;
using Avalonia.Reactive;

namespace ImageStash.Editor;

/// <summary>
/// The field a text or caption-bar layer is typed in (G21, G25, R4): a real text input, so what the Windows emoji panel
/// inserts lands in the layer, laid over the layer's box on the canvas and turned with it, in the layer's font at its
/// size on screen, and transparent but for its caret and the selection's highlight: every change of its text goes to
/// the session at once and the canvas draws the layer through Media, so no letter the user reads is the field's (R4).
/// It shows while the session types into a layer. Enter adds a line, stored as Media breaks lines; it keeps cut and
/// copy of its text on their keys and has no paste of its own: <c>Ctrl+V</c> is the window's (N9, R5). On a Meme it holds
/// the uppercase letters Media draws, what is typed uppercased as it lands, so its caret, selection and hits fall on
/// the letters the canvas shows and the caret goes after every letter a character became ("ß" is "SS"; G21, G25).
/// </summary>
internal sealed class TextField : Canvas
{
    /// <summary>What a screen reader calls it.</summary>
    public const string Called = "Text";

    // The theme's own fills and borders for the box, which its states set on a part of its template a local value cannot reach.
    private static readonly string[] ThemeBrushes =
    [
        "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
        "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
    ];

    private readonly EditorCanvas canvas;
    private readonly TextBox box;
    private EditorSession? session;
    private bool open;

    public TextField(EditorCanvas canvas)
    {
        this.canvas = canvas;
        box = new TextBox
        {
            AcceptsReturn = true,
            NewLine = "\n",
            Padding = default,
            MinWidth = 0,
            MinHeight = 0,
            BorderThickness = default,
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            SelectionForegroundBrush = Brushes.Transparent,
            ContextFlyout = null,
            IsVisible = false,
            RenderTransformOrigin = RelativePoint.TopLeft,
        };
        // The caret and the selection in the selection's mark, the canvas's handles' colour, the system's accent (F10).
        box.GetResourceObservable(LookKeys.SelectionRing).Subscribe(new AnonymousObserver<object?>(mark =>
        {
            box.CaretBrush = mark as IBrush;
            box.SelectionBrush = DrawnLook.Wash(mark as IBrush, 0.33);
        }));
        foreach (var key in ThemeBrushes)
        {
            box.Resources[key] = Brushes.Transparent;
        }

        AutomationProperties.SetName(box, Called);
        // Its own paste would read the clipboard's text (N9, R5); its menu would offer a Paste that does nothing.
        box.PastingFromClipboard += (_, e) => e.Handled = true;
        // Before the box inserts it, so the box places its own caret after the letters a Meme shows for it.
        box.AddHandler(TextInputEvent, (_, e) =>
        {
            if (open && e.Text is { } typed && session?.SelectedLayer is TextLayer { Preset: TextPreset.Meme })
            {
                e.Text = Uppercase.Of(typed);
            }
        }, RoutingStrategies.Tunnel);
        // As each change lands, not on TextChanged, which follows later: a Ctrl+Enter right after a key keeps the key.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && open)
            {
                session?.SetTypedText(box.Text);
            }
        };
        Children.Add(box);
        canvas.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                Place();
            }
        };
    }

    /// <summary>Whether <paramref name="source"/>, where focus went, is the field.</summary>
    public bool Holds(object? source) => source is Visual visual && (ReferenceEquals(visual, box) || box.IsVisualAncestorOf(visual));

    /// <summary>Gives the open field the keyboard focus, so what is typed lands in its layer; nothing while it is closed.</summary>
    public void TakeFocus()
    {
        if (open)
        {
            box.Focus();
        }
    }

    /// <summary>Follows <paramref name="next"/>, or nothing.</summary>
    public void Show(EditorSession? next)
    {
        if (session is not null)
        {
            session.Changed -= OnSessionChanged;
        }

        session = next;
        if (next is not null)
        {
            next.Changed += OnSessionChanged;
        }

        Refresh();
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

    /// <summary>Opens the field on the layer the session types into, or closes it, and lays it over the layer as it is now.</summary>
    private void Refresh()
    {
        var typing = session is { Typing: true, SelectedLayer: TextLayer or CaptionBarLayer };
        if (typing && !open)
        {
            box.Text = session!.SelectedLayer switch
            {
                TextLayer { Preset: TextPreset.Meme } meme => Uppercase.Of(meme.Text),
                TextLayer text => text.Text,
                CaptionBarLayer bar => bar.Text,
                _ => "",
            };
            box.CaretIndex = box.Text?.Length ?? 0;
            open = true;
            box.IsVisible = true;
            Place();
            box.Focus();
        }
        else if (!typing && open)
        {
            open = false;
            // Focus stays where it went, a swatch pressed while typing among them, whose press would end if focus left it;
            // with the editor closing, it is the window's to place.
            var focused = Holds(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement());
            box.IsVisible = false;
            box.Text = "";
            if (focused && session is not null)
            {
                canvas.Focus();
            }
        }
        else if (open)
        {
            Place();
        }
    }

    /// <summary>Lays the field over the layer's box on the canvas, its text at the layer's size there, turned about the box's centre.</summary>
    private void Place()
    {
        if (!open || session is not { Selected: { } index } shown || canvas.Mapping() is not { } mapping
            || LayerGeometry.Bounds(shown.Layers, index, shown.OriginalSize) is not { } bounds || bounds.Width <= 0)
        {
            return;
        }

        var layer = shown.Layers.Layers[index];
        var (fontSize, family, wraps, centred) = layer switch
        {
            TextLayer text => (text.FontSize, text.Font, text.Preset == TextPreset.Meme, text.Preset == TextPreset.Meme),
            CaptionBarLayer bar => (bar.FontSize, bar.Font, true, true),
            _ => (1, "", false, false),
        };
        var place = mapping.ToCanvas(bounds);
        var scale = place.Width / bounds.Width;
        SetLeft(box, place.X);
        SetTop(box, place.Y);
        box.Width = place.Width;
        box.Height = place.Height;
        box.FontSize = Math.Max(1, fontSize * scale);
        box.LineHeight = box.FontSize * 1.2;
        FontFace.Of(family).ApplyTo(box);
        box.TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        box.TextAlignment = centred ? TextAlignment.Center : TextAlignment.Left;
        // A caption's text starts below the band's padding, as Media draws it.
        box.Padding = layer is CaptionBarLayer ? new Thickness(0, fontSize * 0.4 * scale, 0, 0) : default;
        box.RenderTransform = new RotateTransform(LayerGeometry.Rotation(layer), place.Width / 2, place.Height / 2);
    }
}
