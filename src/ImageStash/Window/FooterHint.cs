using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// One of the footer's hints (G8, G9), "Enter · Paste into Slack": its key drawn as key caps, then what the key does, cut
/// short with an ellipsis when the footer cannot hold it. Whatever reads names (a screen reader, the published scripts)
/// reads the hint whole, as the footer says it, once, and never its cap or its words apart.
/// </summary>
public sealed class FooterHint : DockPanel
{
    private readonly StackPanel caps = KeyCaps.Row();

    /// <summary>
    /// A hint that <paramref name="leads"/> names where Enter goes, in the look's full text colour and a size up from the
    /// rest of the footer, so it reads first (G8); one that does not is drawn faint, as standing advice.
    /// </summary>
    public FooterHint(bool leads)
    {
        LastChildFill = true;
        VerticalAlignment = VerticalAlignment.Center;
        Words = new TextBlock
        {
            FontSize = leads ? 12.5 : 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0, 0, 0),
        };
        if (leads)
        {
            Words.Bind(TextBlock.ForegroundProperty, Words.GetResourceObservable(LookKeys.Words));
        }
        else
        {
            Opacity = 0.7;
        }

        SetDock(caps, Dock.Left);
        Children.Add(caps);
        Children.Add(Words);
        // Cut short in a narrow footer, the words are still read whole by a pointer resting on them (G8).
        ToolTipRule.ShowWhenTrimmed(this, Words);
    }

    /// <summary>The hint whole, as whatever reads names reads it.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The caps of the hint's key, as <see cref="KeyCaps"/> draws them.</summary>
    public StackPanel Caps => caps;

    /// <summary>What the key does, as drawn after its caps.</summary>
    public TextBlock Words { get; }

    /// <summary>Shows <paramref name="hint"/>, its key before the first <see cref="PasteTargetHolder.HintSeparator"/> and what it does after.</summary>
    public void Show(string hint)
    {
        if (hint == Text)
        {
            return;
        }

        var was = Text;
        Text = hint;
        var split = hint.IndexOf(PasteTargetHolder.HintSeparator, StringComparison.Ordinal);
        KeyCaps.ShowInFooter(caps, split < 0 ? "" : hint[..split]);
        Words.Text = split < 0 ? hint : hint[(split + PasteTargetHolder.HintSeparator.Length)..];
        AutomationProperties.SetName(this, hint);
        // A reader following the footer hears the new words, as it would a text's (G8), while the hint shows.
        if (IsEffectivelyVisible)
        {
            ControlAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, was, hint);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HintPeer(this);

    /// <summary>The hint as one piece of text, named by its whole words, with no parts of its own to read again.</summary>
    private sealed class HintPeer(FooterHint hint) : ControlAutomationPeer(hint)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetNameCore() => hint.Text;

        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => [];

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
