using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ImageStash.Window;

public sealed partial class StashWindow
{
    private Task keyboardHandoff = Task.CompletedTask;

    private bool BrowsingOverlayOpen => preview.IsOpen || editing.IsOpen || tagField.IsVisible;

    private TagField? TagFieldTakingKeys => tagField.IsVisible
        ? tagField.Field
        : (FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<TagField>();

    // A scope hint is a popup too. The shortcut is available while the hint is visible.
    private bool CanUseBrowsingKeys() => !BrowsingOverlayOpen &&
        !OpenedPopups.Any(popup => popup.Child is not ToolTip);

    private bool RunBrowsingKey(KeyAction action)
    {
        if (!(search.IsFocused || grid.IsFocused) || !CanUseBrowsingKeys())
        {
            return false;
        }

        if (action == KeyAction.Settings)
        {
            var earlierHandoff = keyboardHandoff;
            keys.Then(() => _ = OpenSettingsAfter(earlierHandoff));
        }
        else if (StashKeys.ScopeChoice(action) is { } scope)
        {
            _ = keys.ChooseScope(scope, CanUseBrowsingKeys);
        }

        return true;
    }

    private async Task OpenSettingsAfter(Task earlierHandoff)
    {
        // The handoff still needs the Enter release in the stash window before another window takes focus.
        await earlierHandoff;
        if (CanUseBrowsingKeys())
        {
            SettingsRequested?.Invoke();
        }
    }

    private void AfterScopeChoice(KeyPlace place, Action act)
    {
        if (place is (KeyPlace.SearchBox or KeyPlace.Grid) && !keys.PendingScopeChoice.IsCompleted)
        {
            keys.AfterScopeChoice(act);
        }
        else
        {
            act();
        }
    }

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
                keyboardHandoff = keys.ActOnSelection(selection => preview.IsOpen
                    ? preview.HandOver(this, target, hide) : handoff.HandOver(this, selection.Items, target, hide),
                    allowExplicitTile: true, viewUnavailable: () => handoff.UnreadSelection(target is null ? Handoff.Action : Handoff.HandOverAct));
                break;
            case KeyAction.Copy:
                _ = keys.ActOnSelection(selection => preview.IsOpen
                    ? preview.Copy(this) : handoff.Copy(this, selection.Items),
                    allowExplicitTile: true, viewUnavailable: () => handoff.UnreadSelection(Handoff.Action));
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
                keyboardHandoff = preview.HandOver(this, activation.TargetNow(), activation.ForPaste.ForEnterPress());
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

    private bool RunMovementKey(Direction direction)
    {
        var gridOnly = StashKeys.RequiresGridFocus(direction);
        if (gridOnly && (!grid.IsFocused || !CanUseBrowsingKeys()))
        {
            return false;
        }

        _ = keys.Move(direction, gridOnly ? grid.BringFocusedIntoView : null);
        return true;
    }

    private bool HandleMovementOutsideGrid(Direction direction, long input)
    {
        if (StashKeys.RequiresGridFocus(direction))
        {
            // A popup opened after the press leaves the accepted move in order with its following action.
            return BrowsingOverlayOpen;
        }

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
