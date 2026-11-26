using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The stash window's footer, 36 high under its divider: Enter's hint, which says where Enter goes in the footer's
/// clearest words, or a copy's confirmation in the hints' place (G8, G10); Ctrl+C's hint; the paused line (G33); the
/// count; and the busy state naming what runs (G35), its words cut short with an ellipsis where the footer cannot hold
/// them and shown whole to a pointer resting on them. <see cref="FooterFit"/> says what gives way.
/// </summary>
internal sealed class WindowFooter
{
    public WindowFooter(Control pausedShortcuts)
    {
        EnterHint = new FooterHint(leads: true);
        CopyHint = new FooterHint(leads: false);
        // The confirmation at full strength, so it reads at a glance, alone in the hints' place (G10).
        Confirmation = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = false };
        Confirmation.Bind(TextBlock.ForegroundProperty, Confirmation.GetResourceObservable(LookKeys.Words));
        var lead = new Panel { Children = { EnterHint, Confirmation } };
        Count = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        BusyWords = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var bar = new ProgressBar { IsIndeterminate = true, Width = 48, MinWidth = 48, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(bar, Dock.Left);
        Busy = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Background = Brushes.Transparent, Children = { bar, BusyWords } };
        Row = new FooterFit(lead, CopyHint, pausedShortcuts, Count, Busy);
        // New words may fit where a piece had given way, which no layout below a hidden piece would notice.
        Count.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty)
            {
                Row.InvalidateMeasure();
            }
        };
        View = new Border
        {
            Height = 36,
            Padding = new Thickness(12, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = Row,
        };
        View.Bind(Border.BorderBrushProperty, View.GetResourceObservable(LookKeys.Divider));
    }

    /// <summary>The footer as the window places it.</summary>
    public Border View { get; }

    /// <summary>The row whose pieces give way as <see cref="FooterFit"/> says.</summary>
    public FooterFit Row { get; }

    /// <summary>Enter's hint: Paste into the target's app, or Copy (G8).</summary>
    public FooterHint EnterHint { get; }

    /// <summary>Ctrl+C's hint, beside Enter's while there is a target (G9).</summary>
    public FooterHint CopyHint { get; }

    /// <summary>A copy's confirmation, in the hints' place for two seconds (G10).</summary>
    public TextBlock Confirmation { get; }

    /// <summary>How many items the window lists, and how many are selected.</summary>
    public TextBlock Count { get; }

    /// <summary>The busy state: its bar and its words, whose whole text is its tooltip (G35).</summary>
    public DockPanel Busy { get; }

    /// <summary>What the running acts are doing, joined.</summary>
    public TextBlock BusyWords { get; }

    /// <summary>
    /// The footer's left words while they are <paramref name="shown"/>: <paramref name="texts"/>, a copy's confirmation
    /// alone when <paramref name="confirming"/>, else Enter's hint and, when there is one, Ctrl+C's.
    /// </summary>
    public void ShowHints(IReadOnlyList<string> texts, bool confirming, bool shown)
    {
        Confirmation.IsVisible = shown && confirming;
        EnterHint.IsVisible = shown && !confirming && texts.Count > 0;
        CopyHint.IsVisible = shown && !confirming && texts.Count > 1;
        Row.CountGivesWay = !confirming;
        if (confirming)
        {
            Confirmation.Text = texts[0];
        }
        else
        {
            if (texts.Count > 0)
            {
                EnterHint.Show(texts[0]);
            }

            if (texts.Count > 1)
            {
                CopyHint.Show(texts[1]);
            }
        }

        Row.InvalidateMeasure();
    }

    /// <summary>The busy states of the acts <paramref name="running"/>, joined, or none shown while nothing runs (G35).</summary>
    public void ShowBusy(IReadOnlyList<string> running)
    {
        Busy.IsVisible = running.Count > 0;
        BusyWords.Text = string.Join(" · ", running);
        ToolTipRule.ShowWhole(Busy, BusyWords.Text);
        Row.InvalidateMeasure();
    }
}
