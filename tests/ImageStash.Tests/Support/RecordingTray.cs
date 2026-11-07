using Avalonia.Input;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>A tray that remembers what it was shown, for checks of the parts that drive the Tray seam.</summary>
public sealed class RecordingTray(int toolTipLength = 127) : ITray
{
    public int ToolTipLength { get; } = toolTipLength;

    public IReadOnlyList<TrayMenuEntry> Entries { get; private set; } = [];

    public string? ToolTip { get; private set; }

    /// <summary>What a left-click on the icon runs; a check calls it to click.</summary>
    public Action? Clicked { get; private set; }

    public void Show(IReadOnlyList<TrayMenuEntry> entries, string toolTip, Action clicked)
    {
        Entries = entries;
        ToolTip = toolTip;
        Clicked = clicked;
    }

    public void SetToolTip(string toolTip) => ToolTip = toolTip;

    /// <summary>
    /// Every change of an entry's words asked of the seam, in order, by the header the entry was shown with; the platform
    /// changes nothing for a header no entry was shown with, so a check also finds that entry in <see cref="Entries"/>.
    /// </summary>
    public List<(string Header, string Text)> HeaderChanges { get; } = [];

    /// <summary>Whether the icon shows its red dot.</summary>
    public bool Recording { get; private set; }

    /// <summary>Keeps the toggle's state on the entry, as the menu shows it at its next opening.</summary>
    public void SetChecked(string header, bool isChecked) =>
        Entries = Entries.Select(entry => entry.Header == header ? entry with { Checked = isChecked } : entry).ToList();

    public void SetHeader(string header, string text) => HeaderChanges.Add((header, text));

    /// <summary>Keeps the shortcut on the entry, as the menu shows it at its next opening.</summary>
    public void SetGesture(string header, KeyGesture? gesture) =>
        Entries = Entries.Select(entry => entry.Header == header ? entry with { Gesture = gesture } : entry).ToList();

    public void SetRecording(bool recording) => Recording = recording;
}
