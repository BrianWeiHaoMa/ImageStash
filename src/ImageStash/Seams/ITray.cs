using Avalonia.Input;

namespace ImageStash.Seams;

/// <summary>The icon in the notification area, its menu and its tooltip.</summary>
public interface ITray
{
    /// <summary>
    /// Shows the icon with these menu entries, in order, opened by a right-click, and this tooltip; a left-click on
    /// the icon runs <paramref name="clicked"/> on the UI thread (G34).
    /// </summary>
    void Show(IReadOnlyList<TrayMenuEntry> entries, string toolTip, Action clicked);

    /// <summary>Replaces the tooltip; text longer than <see cref="ToolTipLength"/> is cut there.</summary>
    void SetToolTip(string toolTip);

    /// <summary>The most characters the platform shows in the tooltip.</summary>
    int ToolTipLength { get; }

    /// <summary>Checks or clears the toggle entry with this header; the menu shows it at its next opening.</summary>
    void SetChecked(string header, bool isChecked);

    /// <summary>
    /// The entry shown with <paramref name="header"/> reads <paramref name="text"/> from the menu's next opening, and is
    /// still found by <paramref name="header"/> afterwards.
    /// </summary>
    void SetHeader(string header, string text);

    /// <summary>
    /// The entry shown with <paramref name="header"/> shows <paramref name="gesture"/> beside it from the menu's next
    /// opening, or no shortcut when it is null (G34).
    /// </summary>
    void SetGesture(string header, KeyGesture? gesture);

    /// <summary>The icon with a red dot while <paramref name="recording"/>, and the plain icon otherwise (G15).</summary>
    void SetRecording(bool recording);
}

/// <summary>
/// A tray menu entry; <paramref name="Chosen"/> runs on the UI thread when the user picks it. <paramref name="Gesture"/>
/// is the shortcut shown beside it (G34); <paramref name="Checked"/> makes it a toggle shown checked or not, and is
/// null for a plain entry; <paramref name="MenuClosed"/>, when given, runs on the UI thread after <paramref name="Chosen"/>,
/// once the menu has closed.
/// </summary>
public sealed record TrayMenuEntry(string Header, Action Chosen, KeyGesture? Gesture = null, bool? Checked = null, Action? MenuClosed = null);
