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
/// that display's bottom-right corner, wholly on screen and sized to its cards; or, made for the notifications that
/// stand <paramref name="on"/> another display (G8's paste that could not be made), the same window above that
/// display's bottom-right working-area corner. Each card is a notice of
/// <paramref name="notifications"/>, drawn as the card it carries (a capture's) or as the notice card, arriving and leaving
/// with the stack's motion (<see cref="NoticeStack"/>), unless the system's animation effects are off; the notices keep
/// their time by their words, the hover and a field's focus (Notice.cs), whether or not the stash window shows, the pointer
/// anywhere on the window holding every card, and a refusal or a failure stays until its × or the next notification of its
/// action; the window is sized to its cards as they take or give up their room, never while one
/// fades, and closes once the last has left and faded, made afresh for the next, each window, and each
/// popup it opens (a tooltip, a field's menu), left out of every capture before it is first shown (G15, S4), a refused
/// mark named through <paramref name="named"/> and the window shown anyway (G35). Its caption is the newest notice's
/// first line, so another program finds it by what it says.
/// </summary>
public sealed class Notification
{
    private readonly Notices notifications;
    private readonly IQuietWindows quietWindows;
    private readonly IScreen screen;
    private readonly Action<Failure> named;
    private readonly Func<Display?>? on;
    private readonly DispatcherTimer sweep;
    private readonly NoticeStack stack;
    private Avalonia.Controls.Window? window;
    private ScrollViewer? scroller;

    /// <param name="on">The display its notifications stand on, read at each placing; null, or none given, for above the tray.</param>
    public Notification(Notices notifications, IQuietWindows quietWindows, IScreen screen, Action<Failure> named, Func<Display?>? on = null)
    {
        this.notifications = notifications;
        this.quietWindows = quietWindows;
        this.screen = screen;
        this.named = named;
        this.on = on;
        stack = new NoticeStack(notifications, notice => (notice.Card ?? NoticeCard.For)(notice, notifications), quietWindows.AnimationEffectsShown, inWindow: false);
        stack.View.Margin = new Thickness(NoticeStack.Gap);
        stack.RoomChanged += RoomChanged;
        // The sweep steps the motion too, should a frame not come.
        sweep = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            notifications.Sweep();
            stack.Step();
        });
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

    /// <summary>The window while a notification shows, or a card still fades out; null while none does.</summary>
    public Avalonia.Controls.Window? Shown => window;

    /// <summary>The cards, as the window holds them.</summary>
    public NoticeStack Stack => stack;

    /// <summary>
    /// Raised when the window loses the activation, which a click into a card's field gave it (G16), so the stash
    /// window judges again whether focus has gone to another app (G2).
    /// </summary>
    public event Action? Deactivated;

    /// <summary>
    /// Keeps the notifications above <paramref name="other"/>, the stash window, whose topmost flyout stands above the tray
    /// where they do (G1): each time it shows or comes forward, another card's Edit opening it included, the notifications'
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
        if (window is null && notifications.Shown.Count == 0)
        {
            return;
        }

        if (window is null || scroller is null)
        {
            (window, scroller) = Create(stack.View);
            window.Deactivated += (_, _) => Deactivated?.Invoke();
            window.PointerEntered += (_, _) => stack.Held(over: true);
            window.PointerExited += (_, _) => stack.Held(over: false);
        }

        if (notifications.Shown.Count > 0)
        {
            // The heading leads a notice's words, "Copied · press Ctrl+V" above why (G8), and names the window as it does.
            window.Title = notifications.Shown[^1].Heading ?? notifications.Shown[^1].Text;
        }

        // A card taking its room or giving it up places the window again (RoomChanged); one fading leaves it as it is.
        stack.Show();
        if (window is not { IsVisible: false } hidden)
        {
            return;
        }

        // Every window is new (Create), so each is marked here, after its caption and before its first frame.
        Exclude(hidden);
        quietWindows.RoundCorners(hidden);
        quietWindows.ShowWithoutActivating(hidden);
        // Placed again once shown, as the stash window is: only now does it size itself by its display's scaling.
        Dispatcher.UIThread.Post(() => Place(hidden, stack.View), DispatcherPriority.Background);
        sweep.Start();
    }

    /// <summary>
    /// A card took its room or gave it up whole: the window placed again at the size its cards ask for, the newest in sight
    /// (G16); with no card left, it closes.
    /// </summary>
    private void RoomChanged()
    {
        if (window is null || scroller is null)
        {
            return;
        }

        if (!stack.HoldsRoom)
        {
            Close();
            return;
        }

        Place(window, stack.View);
        // More than the working area holds: the newest stays in sight at the bottom, the oldest scroll away (G16).
        Dispatcher.UIThread.Post(scroller.ScrollToEnd, DispatcherPriority.Background);
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

    private static (Avalonia.Controls.Window, ScrollViewer) Create(StackPanel stack)
    {
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
        return (window, scroller);
    }

    /// <summary>Puts the window, at the size its cards ask for, above the tray icon on the primary display, or on its own display (G16).</summary>
    private void Place(Avalonia.Controls.Window shown, StackPanel content)
    {
        if (!ReferenceEquals(shown, window))
        {
            return;
        }

        content.Measure(Size.Infinity);
        var wanted = content.DesiredSize;
        var place = Summon.NotificationPlace(quietWindows.TrayIconRectangle(), Summon.DisplaysOf(shown.Screens), new Store.WindowSize(wanted.Width, wanted.Height), on?.Invoke());
        if (shown.IsVisible)
        {
            // Never past its old place or its new one, both on its display: moved first at its old size, a window
            // shrinking towards a display to its right stood on it for a moment and took its scaling, the × of the card
            // left then falling outside the window. So the smaller of the two sizes where it stands, applied now, before
            // the move; Avalonia applies a size at the next layout pass, a position at once.
            shown.Width = Math.Min(shown.ClientSize.Width, place.Width / shown.RenderScaling);
            shown.Height = Math.Min(shown.ClientSize.Height, place.Height / shown.RenderScaling);
            shown.UpdateLayout();
        }

        shown.Position = place.Position;
        shown.Width = place.Width / shown.RenderScaling;
        shown.Height = place.Height / shown.RenderScaling;
    }

    private void Close()
    {
        sweep.Stop();
        // No pointer is on a window that has gone, so no hold outlives it.
        stack.Held(over: false);
        // The cards leave before the window closes, so a card holding the activation gives it back first.
        if (scroller is not null)
        {
            scroller.Content = null;
        }

        window?.Close();
        window = null;
        scroller = null;
    }
}
