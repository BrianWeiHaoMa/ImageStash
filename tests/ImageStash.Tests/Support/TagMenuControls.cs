using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

internal static class TagMenuControls
{
    internal static async Task<Button> OpenOrigin(ListedStash listed, string origin, StashItem item, string tag = "shared")
    {
        if (origin is "chip" or "chip picker")
        {
            var row = Chips(listed);
            if (origin == "chip")
            {
                return Chip(row, tag);
            }

            Click(EveryTag(row));
            return PickerRow(row.Picker, tag);
        }

        TagField field;
        if (origin == "selection picker")
        {
            var host = new TagFieldOverlay(listed.Listing, listed.Organising, listed.Notices, listed.Keys, listed.Paste);
            host.Open(listed.Listing.SelectedItems, new Button());
            field = host.Field;
        }
        else
        {
            var preview = new Preview(listed.Listing, listed.Handoff, listed.Organising, listed.Deletion,
                listed.Renaming, listed.Editing, listed.ItemMenu, listed.Notices, listed.Stash.Clock, listed.PasteTarget, _ => { }, new Button(), listed.Keys, listed.Paste);
            preview.Open(item);
            field = preview.GetVisualDescendants().OfType<PreviewTags>().Single().Field;
        }

        var opener = EveryTag(field);
        if (!opener.IsVisible)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == Visual.IsVisibleProperty && opener.IsVisible)
                {
                    ready.TrySetResult();
                }
            }

            opener.PropertyChanged += Changed;
            try
            {
                await ready.Task;
            }
            finally
            {
                opener.PropertyChanged -= Changed;
            }
        }

        Click(opener);
        return PickerRow(field.Picker, tag);
    }

    internal static ChipRow Chips(ListedStash listed)
    {
        var row = new ChipRow(listed.Listing, listed.Organising, () => null, listed.Keys, listed.Paste);
        row.Measure(new Size(600, 40));
        Dispatcher.UIThread.RunJobs();
        return row;
    }

    internal static Button Chip(ChipRow row, string tag) => row.Children.OfType<Button>().Single(button => Equals(button.Content, "#" + tag));

    internal static Button EveryTag(Control host) => host.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == "Every tag");

    internal static Button PickerRow(TagPicker picker, string tag)
    {
        Assert.IsTrue(picker.IsOpen);
        var items = (ItemsControl)((ScrollViewer)((Border)picker.Child!).Child!).Content!;
        var index = picker.Entries.ToList().FindIndex(entry => entry.Tag == tag);
        return (Button)items.ItemTemplate!.Build(items.Items[index])!;
    }

    internal static ContextRequestedEventArgs RequestMenu(Control source)
    {
        var request = new ContextRequestedEventArgs { RoutedEvent = InputElement.ContextRequestedEvent };
        source.RaiseEvent(request);
        return request;
    }

    internal static TextBox OpenRename(Button source)
    {
        Assert.IsTrue(RequestMenu(source).Handled);
        Click(source.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Rename tag…")));
        return source.Content as TextBox ?? throw new AssertFailedException("The tag's place did not become a text box.");
    }

    internal static void Press(TextBox box, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
        box.RaiseEvent(args);
        Assert.IsTrue(args.Handled);
    }

    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
}
