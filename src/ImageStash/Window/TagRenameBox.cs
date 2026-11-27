using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>The temporary text input in a tag chip or Every tag row, with its own commit and cancel keys.</summary>
internal sealed class TagRenameBox : TextBox
{
    private readonly PressOnce? heldKeys;
    private readonly ClipboardPaste? paste;
    private readonly Action<TagRenameBox, string?> end;
    private bool ended;

    public TagRenameBox(string tag, PressOnce? heldKeys, ClipboardPaste? paste, Action<TagRenameBox, string?> end)
    {
        this.heldKeys = heldKeys;
        this.paste = paste;
        this.end = end;
        Text = tag;
        MinWidth = MinHeight = 0;
        Padding = new Thickness(3, 0);
        VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, ItemTags.RenameAction);
        // TextBox has other ways to invoke its native paste. Only the window's explicit action reads the clipboard.
        PastingFromClipboard += (_, e) => e.Handled = true;
        ContextFlyout = null;
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public void Cancel() => Finish(null);

    /// <summary>Ends the draft as Enter does: the list of every tag it sits in closing for a press elsewhere in the window (G17).</summary>
    public void Keep() => Finish(Text ?? "");

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            if (e.KeyModifiers == KeyModifiers.None && (heldKeys?.PressedInMenu(e.Key) ?? true))
            {
                Finish(e.Key == Key.Enter ? Text ?? "" : null);
            }
        }
        else if ((e.Key, e.KeyModifiers) == (Key.V, KeyModifiers.Control))
        {
            e.Handled = true;
            if (!ended && (heldKeys?.PressedInMenu(e.Key) ?? true) && paste is not null)
            {
                _ = paste.Paste(words =>
                {
                    if (!ended)
                    {
                        SelectedText = Regex.Replace(words, @"\s+", " ");
                    }
                });
            }
        }
        else
        {
            if (e.Key == Key.Z)
            {
                // A repeat after focus leaves the draft must not undo a stash change.
                heldKeys?.Pressed(e.Key);
            }

            base.OnKeyDown(e);
        }
    }

    /// <summary>
    /// Focus moving to another part of the window keeps the typed name, as Enter does, by the window's one rule for a
    /// field left (<see cref="FieldLeaving"/>, G17). Focus going with no element taking it (another app, the flyout's
    /// hide, the list closing) ends the draft with nothing written; Esc and Enter have already ended it before the focus
    /// they give back moves, and a press elsewhere in the window has kept it before closing the list
    /// (<see cref="TagPicker"/>).
    /// </summary>
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        var movedInWindow = e.NewFocusedElement is Visual and not TopLevel;
        var intoBox = e.NewFocusedElement is Visual into && (ReferenceEquals(into, this) || this.IsVisualAncestorOf(into));
        Finish(movedInWindow && FieldLeaving.Commits(holdsEdit: !ended, intoField: intoBox) ? Text ?? "" : null);
    }

    private void Finish(string? text)
    {
        if (!ended)
        {
            ended = true;
            end(this, text);
        }
    }
}
