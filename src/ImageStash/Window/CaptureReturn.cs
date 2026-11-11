using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The stash window out of the way for a capture and back as it was afterwards (G3): at the start a flyout whose focus
/// has already gone to another app hidden for good (G2), else the flyout hidden, or the pinned window minimised from
/// hidden, with its place and whether it was active remembered, as the window says and the platform agrees; at the end
/// shown again where it was, never through a summon, activated only when it was, as the window's own showing, so the
/// paste target stands (G8). It owns that memory and the <see cref="CaptureHiding"/> holder the captures call, and
/// follows the user's own restore of the window, after which a minimise is theirs. UI thread only.
/// </summary>
public sealed class CaptureReturn
{
    private readonly Avalonia.Controls.Window window;
    private readonly WindowChrome chrome;
    private readonly WindowActivation activation;
    private readonly ITarget targets;
    private readonly Func<bool> dialogUp;
    private readonly Func<bool> dragInProgress;
    private readonly Action focusContent;
    private BeforeCapture? beforeCapture;

    /// <summary>
    /// For <paramref name="window"/>, whose dialogs and drags <paramref name="dialogUp"/> and <paramref name="dragInProgress"/>
    /// report, and whose focus <paramref name="focusContent"/> puts where a summon puts it; <paramref name="targets"/> says
    /// what is in front.
    /// </summary>
    public CaptureReturn(Avalonia.Controls.Window window, WindowChrome chrome, WindowActivation activation, ITarget targets, Func<bool> dialogUp, Func<bool> dragInProgress, Action focusContent)
    {
        this.window = window;
        this.chrome = chrome;
        this.activation = activation;
        this.targets = targets;
        this.dialogUp = dialogUp;
        this.dragInProgress = dragInProgress;
        this.focusContent = focusContent;
        Hiding = new CaptureHiding(HideForCapture, ShowAfterCapture);
        // Restored by the user meanwhile (its taskbar button, Alt+Tab), the pinned window is theirs again: a minimise they
        // make afterwards is not the capture's, which then leaves it (G3). The capture's own restore empties the slot first.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.WindowStateProperty && window.WindowState != WindowState.Minimized)
            {
                Hiding.Forget();
            }
        };
    }

    /// <summary>The window out of the way while a capture is in progress and back as it was afterwards (G3).</summary>
    public CaptureHiding Hiding { get; }

    /// <summary>
    /// A capture's start, as <see cref="CaptureHiding.Decide"/> judges the window now (G3): the flyout hidden, or the
    /// pinned window minimised, with its place and whether it was active remembered; its content, an open preview
    /// included, stays as it was.
    /// </summary>
    private CaptureHide HideForCapture()
    {
        // The capture's windows are ImageStash's own: no late report reads the target again across them (G8, G3).
        activation.CaptureStarted();
        activation.HideIfFocusAlreadyLeft();
        var hide = CaptureHiding.Decide(window.IsVisible, chrome.Pinned, window.WindowState == WindowState.Minimized, dialogUp(), dragInProgress());
        if (hide == CaptureHide.Nothing)
        {
            return hide;
        }

        var outer = window.FrameSize ?? window.ClientSize;
        var place = new PixelRect(window.Position, new PixelSize((int)Math.Round(outer.Width * window.RenderScaling), (int)Math.Round(outer.Height * window.RenderScaling)));
        // Active only while a window of ImageStash's is in front too: the window's own flag lags another app's activation
        // until the UI thread handles it, so a screenshot asked at once after a click into the chat would read it active
        // and give the chat's keystrokes to the window at the end (G3).
        beforeCapture = new BeforeCapture(place, window.IsActive && targets.Foreground().IsMine);
        // A tooltip is a window of its own, which the hide leaves up until Avalonia has handled the pointer leaving, after
        // the grab that follows at once would take it in; closed now, as Avalonia's tooltip service closes one.
        foreach (var tipped in window.GetVisualDescendants().OfType<Control>().Where(ToolTip.GetIsOpen).ToList())
        {
            ToolTip.SetIsOpen(tipped, false);
        }

        activation.HideWindow();
        if (hide == CaptureHide.Pinned)
        {
            // Minimised from hidden, since Windows animates a showing window's minimise for half a second, which the grab
            // that follows at once would take in (measured on Windows 11 build 26300).
            window.WindowState = WindowState.Minimized;
            ShowActivatedOrNot(false, window.Show);
        }

        return hide;
    }

    /// <summary>
    /// A capture's end (G3): the flyout shown again where it was, at its size, or the pinned window restored, never
    /// through a summon, which would select the box's text; activated, with focus where it was, only when it was active, so
    /// the app that was in front otherwise keeps the keystrokes; and as the window's own showing, so the paste target
    /// stands as it was (G8). A window the user put otherwise meanwhile, closing it or restoring it, stays as they put
    /// it (G2).
    /// </summary>
    private void ShowAfterCapture(CaptureHide hidden)
    {
        if (hidden == CaptureHide.Flyout ? window.IsVisible : !(chrome.Pinned && window.IsVisible && window.WindowState == WindowState.Minimized))
        {
            return;
        }

        activation.WithOwnActivation(() => ShowAsBeforeCapture(hidden), beforeCapture!.Active);
    }

    private void ShowAsBeforeCapture(CaptureHide hidden)
    {
        var before = beforeCapture!;
        if (hidden == CaptureHide.Pinned && before.Active)
        {
            window.WindowState = WindowState.Normal;
        }
        else if (hidden == CaptureHide.Flyout)
        {
            ShowActivatedOrNot(before.Active, () => chrome.ShowAt(before.Place));
        }
        else
        {
            // Avalonia restores a minimised window activated; shown afresh from hidden, it is shown as asked.
            window.Hide();
            window.WindowState = WindowState.Normal;
            ShowActivatedOrNot(false, window.Show);
        }

        if (before.Active)
        {
            // Avalonia gives the focus back to the control that had it at the activation; else it goes as a summon puts it.
            window.Activate();
            if (window.FocusManager?.GetFocusedElement() is null)
            {
                focusContent();
            }
        }
    }

    /// <summary>Runs <paramref name="show"/>, a showing of the hidden window, activating it only when <paramref name="activated"/>.</summary>
    private void ShowActivatedOrNot(bool activated, Action show)
    {
        var showActivated = window.ShowActivated;
        window.ShowActivated = activated;
        try
        {
            show();
        }
        finally
        {
            window.ShowActivated = showActivated;
        }
    }

    /// <summary>The stash window as a capture's start found it: its whole rectangle in physical pixels, and whether it was active.</summary>
    private sealed record BeforeCapture(PixelRect Place, bool Active);
}
