using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// Avalonia's tray icon, which opens its menu on a right-click and updates its icon and tooltip live (S10). Avalonia
/// 12.1.4's tray popup is built afresh at every right-click and binds each entry's header, gesture, toggle and check
/// mark into its menu items, so a header, a gesture or a check mark changed here shows at the menu's next opening. The icon with
/// the red dot is the plain one's generated sibling, both resources of this assembly.
/// </summary>
public sealed class WindowsTray : ITray, IDisposable
{
    // NOTIFYICONDATA's szTip holds 128 characters including the terminating null (learn.microsoft.com, NOTIFYICONDATAW).
    internal const int ToolTipLimit = 127;

    private readonly Dictionary<string, NativeMenuItem> items = [];
    // Loaded at Show, once Avalonia's platform is up: the seam is made before it starts.
    private (WindowIcon Plain, WindowIcon Recording)? icons;
    private TrayIcon? icon;
    private bool recording;

    internal TrayIcon? Icon => icon;

    public int ToolTipLength => ToolTipLimit;

    public void Show(IReadOnlyList<TrayMenuEntry> entries, string toolTip, Action clicked)
    {
        var menu = new NativeMenu();
        foreach (var entry in entries)
        {
            var item = new NativeMenuItem(entry.Header) { Gesture = entry.Gesture };
            if (entry.Checked is { } isChecked)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = isChecked;
            }

            item.Click += (_, _) =>
            {
                entry.Chosen();
                // Avalonia closes the menu's window only after this click's handlers have run.
                if (entry.MenuClosed is { } closed)
                {
                    Dispatcher.UIThread.Post(closed, DispatcherPriority.Background);
                }
            };
            menu.Add(item);
            items[entry.Header] = item;
        }

        icons = (Resource("ImageStash.ico"), Resource("ImageStash-recording.ico"));
        icon = new TrayIcon { Icon = IconShown(), Menu = menu, ToolTipText = Cut(toolTip) };
        icon.Clicked += (_, _) => clicked();
        // Registered with the application so the icon leaves the notification area when ImageStash exits.
        TrayIcon.SetIcons(Application.Current!, [icon]);
    }

    public void SetToolTip(string toolTip)
    {
        if (icon is not null)
        {
            icon.ToolTipText = Cut(toolTip);
        }
    }

    public void SetChecked(string header, bool isChecked)
    {
        if (items.TryGetValue(header, out var item))
        {
            item.IsChecked = isChecked;
        }
    }

    public void SetHeader(string header, string text)
    {
        if (items.TryGetValue(header, out var item))
        {
            item.Header = text;
        }
    }

    public void SetGesture(string header, KeyGesture? gesture)
    {
        if (items.TryGetValue(header, out var item))
        {
            item.Gesture = gesture;
        }
    }

    public void SetRecording(bool recording)
    {
        this.recording = recording;
        if (icon is not null)
        {
            icon.Icon = IconShown();
        }
    }

    private WindowIcon IconShown() => recording ? icons!.Value.Recording : icons!.Value.Plain;

    private static WindowIcon Resource(string name)
    {
        using var picture = typeof(WindowsTray).Assembly.GetManifestResourceStream(name)!;
        return new WindowIcon(picture);
    }

    internal static string Cut(string text)
    {
        if (text.Length <= ToolTipLimit)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[ToolTipLimit - 1]) ? ToolTipLimit - 1 : ToolTipLimit;
        return text[..length];
    }

    public void Dispose()
    {
        icon?.Dispose();
        icon = null;
    }
}
