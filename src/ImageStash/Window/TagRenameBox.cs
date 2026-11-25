using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
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

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Cancel();
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
