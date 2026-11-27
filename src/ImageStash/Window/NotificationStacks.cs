using Avalonia;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// Which stack a notification stands in (G16, F4), the one home every raiser asks: the tray's, for the primary display or
/// none given; else that display's own, opened the first time one is asked for and kept for the run, so a display has one
/// stack and one notifications window whoever raised into it, the paste fallback and the captures alike. Each stack opened
/// here is made by <see cref="Notices.Beside"/>, lasting as the tray's do and sharing its history of acts, so the next
/// notification of an action takes a failure's place from any stack (<see cref="SameActionStacks"/>). Used on the UI thread.
/// </summary>
public sealed class NotificationStacks
{
    private readonly Dictionary<PixelRect, Notices> elsewhere = [];
    private readonly List<Notices> all;

    /// <param name="tray">The notifications above the tray.</param>
    public NotificationStacks(Notices tray)
    {
        Tray = tray;
        all = [tray];
        tray.Changed += RaiseChanged;
    }

    /// <summary>The notifications above the tray, on the primary display.</summary>
    public Notices Tray { get; }

    /// <summary>Every stack, the tray's first, then each other display's in the order it was opened.</summary>
    public IReadOnlyList<Notices> All => all;

    /// <summary>
    /// Raised, on the raising thread, with a stack just opened for a display other than the primary and that display,
    /// before anything is raised into it: drawn in a window of its own and followed by the stash window, as the tray's is.
    /// </summary>
    public event Action<Notices, Display>? Opened;

    /// <summary>Raised after every change of the notifications showing in any stack, one opened since included.</summary>
    public event Action? Changed;

    /// <summary>The tray's stack, or, when <paramref name="display"/> is another display, that display's own, found by its bounds.</summary>
    public Notices On(Display? display)
    {
        if (display is not { IsPrimary: false } other)
        {
            return Tray;
        }

        if (!elsewhere.TryGetValue(other.Bounds, out var stack))
        {
            stack = elsewhere[other.Bounds] = Tray.Beside();
            all.Add(stack);
            stack.Changed += RaiseChanged;
            Opened?.Invoke(stack, other);
        }

        return stack;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
