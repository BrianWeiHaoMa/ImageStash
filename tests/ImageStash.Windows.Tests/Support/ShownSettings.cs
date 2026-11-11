using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The Settings page as the desktop checks make and read it: a settings folder of the check's own to make it over, its
/// rows' titles in order, and readers of its controls, their names and the warning under a row.
/// </summary>
public static class ShownSettings
{
    /// <summary>The page's row titles and buttons that carry G32's row names, in the order the page shows them.</summary>
    public static readonly string[] Titles =
    [
        SettingsPage.OpenTitle, SettingsPage.TakeScreenshotTitle, SettingsPage.RecordClipTitle, SettingsPage.StashClipboardImageTitle,
        SettingsPage.AlsoCopyTitle, SettingsPage.MaximumLengthTitle, SettingsPage.ShowPointerTitle, SettingsPage.StashFolderTitle,
        "Open folder", SettingsPage.MoveWords, SettingsPage.ExportWords, SettingsPage.ImportWords, SettingsPage.StartWithWindowsTitle, SettingsPage.ThemeTitle,
        SettingsPage.ThumbnailSizeTitle,
    ];

    /// <summary>The names among <see cref="Titles"/> that the page's controls carry, in the order the page shows them.</summary>
    public static List<string> Named(SettingsWindow window) =>
        window.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetName).Where(name => Titles.Contains(name)).OfType<string>().ToList();

    /// <summary>The one control on the page whose automation name is <paramref name="name"/>.</summary>
    public static Control Control(SettingsWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().Single(control => AutomationProperties.GetName(control) == name);

    /// <summary>The words of the warning drawn under the row whose control carries <paramref name="title"/>, or null while none shows.</summary>
    public static string? WarningUnder(SettingsWindow window, string title)
    {
        var row = Control(window, title).GetVisualAncestors().OfType<Border>().First(border => border.GetVisualParent() is StackPanel rows && rows.GetVisualParent() is Border);
        var rows = (StackPanel)row.GetVisualParent()!;
        var warning = (Border)rows.Children[rows.Children.IndexOf(row) + 1];
        return warning.IsVisible ? warning.GetVisualDescendants().OfType<TextBlock>().Last().Text : null;
    }

    /// <summary>Whether the page's outer frame lies wholly inside the primary display's working area (E1).</summary>
    public static bool InsideWorkingArea(SettingsWindow window) =>
        window.Screens.Primary!.WorkingArea.Contains(CaptureDesk.Outer(window));

    /// <summary>
    /// A settings folder of the check's own under %TEMP%, its keeper, and the seams a page is made over: F5's four
    /// shortcuts bound over the real seam as ImageStash binds them, registered only once a check starts them, each press
    /// counted where its act runs and each unavailable one named as the tooltip would name it.
    /// </summary>
    public sealed class SettingsFolder : IDisposable
    {
        public const string Executable = @"C:\Apps\ImageStash\ImageStash.Windows.exe";

        private readonly string folder = Path.Combine(Path.GetTempPath(), "imagestash-settings-check-" + Guid.NewGuid().ToString("N"));
        private readonly SettingsLoad load;
        private readonly WindowsShortcuts seam = new();
        private readonly int[] pressed = new int[4];

        public SettingsFolder(string? file = null)
        {
            Directory.CreateDirectory(folder);
            if (file is not null)
            {
                File.WriteAllText(Path.Combine(folder, Store.Settings.FileName), file);
            }

            load = Store.Settings.Load(folder, HeldFileRetry.Duration);
            Keeper = new SettingsKeeper(folder, load, HeldFileRetry.Duration, SettingsKeeper.Quiet, _ => { });
            Shortcuts = new GlobalShortcuts(Keeper,
                GlobalShortcuts.Bindings(() => Interlocked.Increment(ref pressed[0]), () => Interlocked.Increment(ref pressed[1]), () => Interlocked.Increment(ref pressed[2]), () => Interlocked.Increment(ref pressed[3])),
                seam, Named.Add, action => Dispatcher.UIThread.Post(action));
        }

        public SettingsKeeper Keeper { get; }

        public GlobalShortcuts Shortcuts { get; }

        /// <summary>Each shortcut named unavailable, on the UI thread.</summary>
        public List<Failure> Named { get; } = [];

        /// <summary>How often each act has run, by <see cref="ShortcutAct"/>, the open shortcut's at the press.</summary>
        public int Pressed(ShortcutAct act) => Volatile.Read(ref pressed[(int)act]);

        public PresetStartWithWindows Run { get; } = new();

        public PresetDialogs Dialogs { get; } = new();

        public string DefaultStash => Path.Combine(folder, "stash");

        /// <param name="moving">Move… as a check's own stash window asks it; by default, over a stash that never opens.</param>
        /// <param name="transferring">Export stash… and Import stash… as a check's own stash window asks them; by default, over a stash that never opens.</param>
        /// <param name="thumbnails">The thumbnail size as a check's own stash window holds it; by default, one over this folder's settings.</param>
        public SettingsPage Page(Action<Store.Theme>? applyTheme = null, Moving? moving = null, Transferring? transferring = null, ThumbnailSizing? thumbnails = null)
        {
            moving ??= NeverOpened();
            return new(Keeper, load, Shortcuts, new WindowsLocationsAt(DefaultStash, folder), Run, Dialogs, moving, transferring ?? NeverOpenedTransfers(moving),
                thumbnails ?? new ThumbnailSizing(Keeper), applyTheme ?? (_ => { }), Executable);
        }

        /// <summary>Move… over a stash still opening, as the page's other rows are checked: it refuses before anything else.</summary>
        private Moving NeverOpened()
        {
            var listing = new Listing(_ => { });
            var notices = new Notices(TimeProvider.System);
            var handoff = new Handoff(listing, notices, notices, null!, null!, TimeProvider.System, TimeSpan.Zero, TimeSpan.Zero);
            return new Moving(listing, notices, Keeper, Dialogs, handoff, _ => throw new InvalidOperationException("No stash opens here."), () => Task.CompletedTask);
        }

        /// <summary>Export stash… and Import stash… over a stash still opening, beside <paramref name="moving"/>: each refuses before its picker.</summary>
        private Transferring NeverOpenedTransfers(Moving moving)
        {
            var listing = new Listing(_ => { });
            var notices = new Notices(TimeProvider.System);
            var handoff = new Handoff(listing, notices, notices, null!, null!, TimeProvider.System, TimeSpan.Zero, TimeSpan.Zero);
            var editing = new Editing(listing, notices, handoff, ImageStash.Media.FinishedPicture.Render, new ImageStash.Editor.ColorChoices(), TimeSpan.Zero);
            var openWith = new OpenWith(listing, notices, editing, Dialogs, TimeProvider.System, action => action());
            return new Transferring(listing, notices, Keeper, Dialogs, null!, handoff, openWith, moving,
                _ => throw new InvalidOperationException("No stash is imported here."), TimeProvider.System, TimeSpan.Zero);
        }

        public void Dispose()
        {
            seam.Dispose();
            Keeper.Flush().Wait();
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed record WindowsLocationsAt(string DefaultStashFolder, string SettingsFolder) : ILocations;
}
