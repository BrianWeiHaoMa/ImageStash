using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using ImageStash.Seams;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The notifications above the tray (G16, F4): ImageStash's own small window, shown through the Quiet-windows seam so
/// it never takes focus, topmost and without a taskbar button, above the tray icon on the primary display, or above
/// that display's bottom-right corner, wholly on screen and sized to its cards. Each card is a notice of
/// <paramref name="notifications"/>, drawn as the card it carries (a capture's) or as the notice card; the notices keep
/// the six seconds, the hover and a field's focus (Notice.cs), whether or not the stash
/// window shows; the window closes once the last notice has left and is made afresh for the next, each window, and each
/// popup it opens (a tooltip, a field's menu), left out of every capture before it is first shown (G15, S4), a refused
/// mark named through <paramref name="named"/> and the window shown anyway (G35). Its caption is the newest notice's
/// words, so another program finds it by what it says.
/// </summary>
public sealed class Notification
{
    private readonly Notices notifications;
    private readonly IQuietWindows quietWindows;
    private readonly IScreen screen;
    private readonly Action<Failure> named;
    private readonly DispatcherTimer sweep;
    private readonly Dictionary<Notice, Control> cards = [];
    private Avalonia.Controls.Window? window;
    private StackPanel? stack;
    private ScrollViewer? scroller;

    public Notification(Notices notifications, IQuietWindows quietWindows, IScreen screen, Action<Failure> named)
    {
        this.notifications = notifications;
        this.quietWindows = quietWindows;
        this.screen = screen;
        this.named = named;
        sweep = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => notifications.Sweep());
        notifications.Changed += Show;
        // A popup is a window of its own, made afresh at each opening; its template is applied before it is first shown
        // (WindowBase.Show), so it is marked then.
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<PopupRoot>((popup, _) =>
        {
            if (window is not null && Opens(window, popup))
            {
                Exclude(popup);
            }
        });
    }

    /// <summary>The window while a notification shows; null while none does.</summary>
    public Avalonia.Controls.Window? Shown => window;

    /// <summary>
    /// Raised when the window loses the activation, which a click into a card's field gave it (G16), so the stash
    /// window judges again whether focus has gone to another app (G2).
    /// </summary>
    public event Action? Deactivated;

    /// <summary>
    /// Keeps the notifications above <paramref name="other"/>, the stash window, whose topmost flyout stands above the tray
    /// where they do (G1): each time it shows or comes forward, a card's Edit opening it included, the notifications'
    /// window is put back on top of it, still without the activation, so a card stays in sight and takes its clicks (G16).
    /// </summary>
    public void StayAbove(Avalonia.Controls.Window other)
    {
        other.Activated += (_, _) => RaiseAfterwards(other);
        other.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && other.IsVisible)
            {
                RaiseAfterwards(other);
            }
        };
    }

    /// <summary>
    /// Once the showing or activation under way has placed <paramref name="other"/>, puts the notifications' window on top
    /// of every topmost one, while <paramref name="other"/> is still on the screen: a window hidden or minimised since, as a
    /// capture's start does, leaves the capture's overlay above the notifications (G14).
    /// </summary>
    private void RaiseAfterwards(Avalonia.Controls.Window other) => Dispatcher.UIThread.Post(() =>
    {
        if (window is { IsVisible: true } shown && other is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            // Avalonia applies a change of Topmost through SetWindowPos without activating, and set again, the window goes
            // to the top of the topmost windows; a value set unchanged does nothing.
            shown.Topmost = false;
            shown.Topmost = true;
        }
    }, DispatcherPriority.Background);

    private void Show()
    {
        if (notifications.Shown.Count == 0)
        {
            Close();
            return;
        }

        if (window is null || stack is null || scroller is null)
        {
            (window, stack, scroller) = Create();
            window.Deactivated += (_, _) => Deactivated?.Invoke();
        }

        foreach (var gone in cards.Keys.Except(notifications.Shown).ToList())
        {
            stack.Children.Remove(cards[gone]);
            cards.Remove(gone);
        }

        foreach (var notice in notifications.Shown.Where(n => !cards.ContainsKey(n)))
        {
            cards[notice] = (notice.Card ?? NoticeCard.For)(notice, notifications);
            stack.Children.Add(cards[notice]);
        }

        window.Title = notifications.Shown[^1].Text;
        Place(window, stack);
        // More than the working area holds: the newest stays in sight at the bottom, the oldest scroll away (G16).
        Dispatcher.UIThread.Post(scroller.ScrollToEnd, DispatcherPriority.Background);
        if (!window.IsVisible)
        {
            // Every window is new (Create), so each is marked here, after its caption and before its first frame.
            Exclude(window);
            quietWindows.RoundCorners(window);
            quietWindows.ShowWithoutActivating(window);
            // Placed again once shown, as the stash window is: only now does it size itself by its display's scaling.
            var shown = window;
            Dispatcher.UIThread.Post(() => Place(shown, stack), DispatcherPriority.Background);
        }

        sweep.Start();
    }

    private void Exclude(TopLevel shown)
    {
        if (screen.ExcludeFromCapture(shown).Failure is { } refused)
        {
            named(refused);
        }
    }

    /// <summary>Whether <paramref name="popup"/> was opened from <paramref name="owner"/>, or from a popup of it.</summary>
    private static bool Opens(TopLevel owner, PopupRoot popup) =>
        ReferenceEquals(popup.ParentTopLevel, owner) || popup.ParentTopLevel is PopupRoot parent && Opens(owner, parent);

    private static (Avalonia.Controls.Window, StackPanel, ScrollViewer) Create()
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(6) };
        var scroller = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Avalonia.Controls.Window
        {
            Content = scroller,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            Topmost = true,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
        };
        window.Bind(Avalonia.Controls.Window.BackgroundProperty, window.GetResourceObservable(LookKeys.Surface));
        KeyReleases.IgnoreUnpressed(window);
        return (window, stack, scroller);
    }

    /// <summary>Puts the window, at the size its cards ask for, above the tray icon on the primary display (G16).</summary>
    private void Place(Avalonia.Controls.Window shown, StackPanel content)
    {
        if (!ReferenceEquals(shown, window))
        {
            return;
        }

        content.Measure(Size.Infinity);
        var wanted = content.DesiredSize;
        var place = Summon.NotificationPlace(quietWindows.TrayIconRectangle(), Summon.DisplaysOf(shown.Screens), new Store.WindowSize(wanted.Width, wanted.Height));
        shown.Position = place.Position;
        shown.Width = place.Width / shown.RenderScaling;
        shown.Height = place.Height / shown.RenderScaling;
    }

    private void Close()
    {
        sweep.Stop();
        cards.Clear();
        // The cards leave before the window closes, so a card holding the activation gives it back first.
        stack?.Children.Clear();
        window?.Close();
        window = null;
        stack = null;
        scroller = null;
    }
}
