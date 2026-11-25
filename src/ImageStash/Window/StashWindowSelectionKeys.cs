using Avalonia.Input;
using Avalonia.Threading;

namespace ImageStash.Window;

public sealed partial class StashWindow
{
    private bool RunSelectionKey(KeyAction action, KeyPlace place)
    {
        if (place is not (KeyPlace.Grid or KeyPlace.SearchBox))
        {
            return RunOverlaySelectionKey(action, place);
        }

        switch (action)
        {
            case KeyAction.Handoff:
                var target = activation.TargetNow();
                var hide = activation.ForPaste.ForEnterPress();
                _ = keys.ActOnSelection(selection => preview.IsOpen
                    ? preview.HandOver(this, target, hide) : handoff.HandOver(this, selection.Items, target, hide), allowExplicitTile: true);
                break;
            case KeyAction.Copy:
                _ = keys.ActOnSelection(selection => preview.IsOpen
                    ? preview.Copy(this) : handoff.Copy(this, selection.Items), allowExplicitTile: true);
                break;
            case KeyAction.Edit:
                _ = keys.ActOnSelection(selection => preview.IsOpen ? preview.Edit() : editing.OpenOnSelection(selection.Items));
                break;
            case KeyAction.Delete:
                _ = keys.ActOnSelection(selection => preview.IsOpen ? preview.Delete() : deletion.Delete(selection.Items));
                break;
            case KeyAction.ToggleFavorite:
                _ = keys.ActOnSelection(selection => preview.IsOpen ? preview.ToggleFavorite() : organising.ToggleFavorite(selection.Items));
                break;
            case KeyAction.TogglePreview:
            case KeyAction.Rename:
                _ = keys.WithSelection(selection => ShowKeyPreview(action, selection));
                break;
            case KeyAction.SelectAll:
                _ = keys.WithCurrentView(input =>
                {
                    if (!preview.IsOpen)
                    {
                        listing.SelectAll(input);
                    }
                });
                break;
            case KeyAction.AddTags:
                _ = keys.WithSelection(selection => tagKey.PressOnSelection(selection.Items));
                break;
            default:
                return false;
        }

        return true;
    }

    private bool RunOverlaySelectionKey(KeyAction action, KeyPlace place)
    {
        switch (action)
        {
            case KeyAction.Handoff:
                _ = preview.HandOver(this, activation.TargetNow(), activation.ForPaste.ForEnterPress());
                break;
            case KeyAction.Copy:
                // The editor copies the last saved picture, without rendering the live edit.
                _ = place == KeyPlace.Editor && editing.Session is { } edited ? handoff.Copy(this, [edited.Item]) : preview.Copy(this);
                break;
            case KeyAction.Edit:
                _ = preview.Edit();
                break;
            case KeyAction.Delete:
                _ = preview.Delete();
                break;
            case KeyAction.ToggleFavorite:
                _ = preview.ToggleFavorite();
                break;
            case KeyAction.TogglePreview:
                preview.Dismiss();
                break;
            case KeyAction.Rename:
                preview.EditName();
                break;
            case KeyAction.AddTags:
                tagKey.Press();
                break;
            default:
                return false;
        }

        return true;
    }

    private void ShowKeyPreview(KeyAction action, KeySelection selection)
    {
        if (preview.IsOpen)
        {
            if (action == KeyAction.TogglePreview)
            {
                preview.Dismiss();
            }
            else
            {
                preview.EditName();
            }
        }
        else if (selection.Focused is { } item)
        {
            preview.Open(item, editingName: action == KeyAction.Rename, input: selection.Input);
        }
    }

    private bool MoveInKeyboardPreview(Direction direction, long input)
    {
        // A Space earlier in the same queue can have opened the preview since this arrow was pressed.
        if (!preview.IsOpen)
        {
            return false;
        }

        var way = direction is Direction.Left or Direction.Up ? StepWay.Back : StepWay.On;
        if (!preview.NameFocused && FocusManager?.GetFocusedElement() is not Avalonia.Controls.TextBox &&
            OpenTagLists().Count == 0 && previewSteps.Next(way) is { } item)
        {
            preview.Open(item, input: input);
        }

        return true;
    }

    private void OpenAfterMenu(Action open)
    {
        // The menu choice wins at the click, before the popup releases focus and the overlay can open.
        listing.SupersedeSelectionKeys();
        Dispatcher.UIThread.Post(open, DispatcherPriority.Background);
    }
}
