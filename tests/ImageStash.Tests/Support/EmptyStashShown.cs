using Avalonia.Controls;
using Avalonia.Interactivity;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>One row of the empty stash as a person reads it: its words, its key caps in order, or the words shown in their place.</summary>
public sealed record ShownWay(string Words, IReadOnlyList<string> Caps, string? Instead);

/// <summary>
/// The empty stash as the window draws it: the window's status over a listed stash, made as <c>StashWindow</c> makes it,
/// built and read on the control thread. Its view is read as a person reads it (the lines, each row's words and caps)
/// and a row is clicked as a click raises it; nothing here calls the rule the view draws from.
/// </summary>
public sealed class EmptyStashShown
{
    private EmptyStashShown(WindowStatus status) => Status = status;

    public WindowStatus Status { get; }

    /// <summary>
    /// The status over <paramref name="listed"/>'s listing and settings, its rows running <paramref name="acts"/>, the
    /// acts the window hands, <see cref="ListedStash.WaysIn"/>, unless a check gives its own; shown as the window shows it,
    /// and told of a shortcut rebound as the app tells the window.
    /// </summary>
    public static EmptyStashShown Over(ListedStash listed, WaysInActs? acts = null)
    {
        WindowStatus? status = null;
        ControlThread.Run(() =>
        {
            status = new WindowStatus(listed.Listing, listed.Notices, listed.Settings, listed.FooterHints, new ScrollViewer(), () => false, () => { }, acts ?? listed.WaysIn);
            status.ShowListing();
        });
        listed.Shortcuts.Changed += () => ControlThread.Run(status!.ShortcutsChanged);
        return new EmptyStashShown(status!);
    }

    /// <summary>The listing shown again, as the window shows it after each change.</summary>
    public void ShowListing() => ControlThread.Run(Status.ShowListing);

    /// <summary>A shortcut remapped in Settings, as the window is told.</summary>
    public void ShortcutsChanged() => ControlThread.Run(Status.ShortcutsChanged);

    /// <summary>Whether the empty stash shows in the grid's place: its view visible, headed "Your stash is empty".</summary>
    public bool Showing => Get(() => Status.StateView.IsVisible && Lines().FirstOrDefault() == EmptyStashWaysIn.Heading);

    /// <summary>The view's lines of words, outside its rows, in order.</summary>
    public IReadOnlyList<string> ReadLines() => Get(Lines);

    /// <summary>The view's rows, in order.</summary>
    public IReadOnlyList<ShownWay> ReadRows() => Get(() => Rows().Select(Read).ToList());

    /// <summary>Whether any row, or any control between a row and the view, takes the keyboard's focus.</summary>
    public bool AnyRowFocusable => Get(() => Rows().Any(row => row.Focusable || Between(row).Any(control => control.Focusable)));

    /// <summary>The rows' key caps, each a cap's border.</summary>
    public IReadOnlyList<Border> Caps() => Get(() => Rows().SelectMany(row => ((Grid)row.Content!).Children.OfType<StackPanel>().SelectMany(caps => caps.Children.OfType<Border>())).ToList());

    /// <summary>A click on the row reading <paramref name="words"/>.</summary>
    public void Click(string words) =>
        ControlThread.Run(() => Rows().Single(row => Read(row).Words == words).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    private Panel View() => (Panel)((ContentControl)Status.StateView).Content!;

    private List<string> Lines() =>
        ((ContentControl)Status.StateView).Content is Panel view ? view.Children.OfType<TextBlock>().Select(line => line.Text ?? "").ToList() : [];

    private List<Button> Rows() => View().Children.OfType<Panel>().SelectMany(rows => rows.Children.OfType<Button>()).ToList();

    private IEnumerable<Control> Between(Control row)
    {
        for (var parent = row.Parent as Control; parent is not null && !ReferenceEquals(parent, Status.StateView); parent = parent.Parent as Control)
        {
            yield return parent;
        }
    }

    private static ShownWay Read(Button row)
    {
        var parts = ((Grid)row.Content!).Children;
        var words = ((TextBlock)parts[0]).Text ?? "";
        return parts[1] switch
        {
            StackPanel caps => new ShownWay(words, caps.Children.OfType<Border>().Select(cap => ((TextBlock)cap.Child!).Text ?? "").ToList(), null),
            TextBlock instead => new ShownWay(words, [], instead.Text),
            var other => throw new AssertFailedException($"{words}: a row's right part is {other.GetType().Name}"),
        };
    }

    private static T Get<T>(Func<T> read)
    {
        T value = default!;
        ControlThread.Run(() => value = read());
        return value;
    }
}
