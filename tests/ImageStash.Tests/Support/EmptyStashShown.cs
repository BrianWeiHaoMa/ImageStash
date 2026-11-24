using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// One row of the empty stash as a person reads it, left to right: the icon glyph leading it, or null when none does, its
/// words, and its key caps in order, or the words shown in their place.
/// </summary>
/// <param name="Beside">The words after the key caps, saying the shortcut is unavailable; null when none stand there.</param>
public sealed record ShownWay(string? Icon, string Words, IReadOnlyList<string> Caps, string? Instead, string? Beside = null);

/// <summary>
/// The empty stash as the window draws it: the window's status over a listed stash, made as <c>StashWindow</c> makes it,
/// built and read on the control thread. Its view is read as a person reads it (the lines, each row's words and caps)
/// and a row is clicked as a click raises it; nothing here calls the rule the view draws from.
/// </summary>
public sealed class EmptyStashShown
{
    /// <summary>The look's icon font (F10), the only font a row's icon is read in.</summary>
    private const string IconFont = "Segoe Fluent Icons";

    private EmptyStashShown(WindowStatus status) => Status = status;

    public WindowStatus Status { get; }

    /// <summary>
    /// The status over <paramref name="listed"/>'s listing and settings, its rows running <paramref name="acts"/>, the
    /// acts the window hands, <see cref="ListedStash.WaysIn"/>, unless a check gives its own; shown as the window shows it,
    /// following the listed stash's shortcuts as the window follows the app's, and told of each change of them as the app
    /// tells the window.
    /// </summary>
    public static EmptyStashShown Over(ListedStash listed, WaysInActs? acts = null)
    {
        WindowStatus? status = null;
        ControlThread.Run(() =>
        {
            status = new WindowStatus(listed.Listing, listed.Notices, listed.Settings, listed.FooterHints, new ScrollViewer(), () => false, () => { }, acts ?? listed.WaysIn);
            status.FollowShortcuts(listed.Shortcuts);
        });
        listed.Shortcuts.Changed += () =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                status!.ShortcutsChanged();
            }
            else
            {
                ControlThread.Run(status!.ShortcutsChanged);
            }
        };
        return new EmptyStashShown(status!);
    }

    /// <summary>The paused words in the normal footer, when shown.</summary>
    public string? ReadPausedShortcuts() => Get(() =>
        PauseLine() is { IsVisible: true } line ? line.Children.OfType<TextBlock>().First().Text : null);

    public void ResumeShortcuts() => ControlThread.Run(() =>
        PauseLine().Resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    private PausedShortcutsLine PauseLine()
    {
        var footer = (Grid)Status.Footer.Child!;
        var onLeft = footer.Children.OfType<DockPanel>().Single();
        return (PausedShortcutsLine)onLeft.Children.OfType<FooterPauseSlot>().Single().Child!;
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
        // What shows, in the order the row's columns lay it out, as the eye reads it.
        var parts = ((Grid)row.Content!).Children.Where(part => part.IsVisible).OrderBy(Grid.GetColumn).ToList();
        if (parts.Select(Grid.GetColumn).Distinct().Count() != parts.Count)
        {
            throw new AssertFailedException($"a row lays two of its parts in one column: {string.Join(", ", parts.Select(part => $"{part.GetType().Name} in {Grid.GetColumn(part)}"))}");
        }

        var icon = parts[0] is TextBlock { FontFamily.Name: IconFont } glyph ? glyph.Text : null;
        var rest = icon is null ? parts : parts.Skip(1).ToList();
        var words = ((TextBlock)rest[0]).Text ?? "";
        return rest[1] switch
        {
            StackPanel caps => new ShownWay(icon, words, caps.Children.OfType<Border>().Select(cap => ((TextBlock)cap.Child!).Text ?? "").ToList(), null,
                caps.Children.OfType<TextBlock>().SingleOrDefault()?.Text),
            TextBlock instead => new ShownWay(icon, words, [], instead.Text),
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
