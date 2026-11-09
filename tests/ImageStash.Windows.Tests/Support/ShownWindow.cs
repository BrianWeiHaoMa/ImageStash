using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A stash window shown off every display at the flyout's default size, its notices and notifications on a stopped
/// clock, with a paste target whose answers the check presets.
/// </summary>
public sealed class ShownWindow : IDisposable
{
    private readonly string settingsFolder;
    private readonly Avalonia.Input.Pointer mouse = new(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
    private StashStore? store;
    private ContextMenu? openMenu;
    private IDisposable? menus;

    private ShownWindow(StashWindow window, Notices notices, Notices notifications, PresetTarget target, Listing listing, Import import, Handoff handoff, Editing editing, OpenWith openWith, Moving moving, SettingsKeeper settings, string settingsFolder)
    {
        Window = window;
        Moving = moving;
        // The store a Move opened is the one the check closes at its end.
        moving.Moved += opened => store = opened;
        Editing = editing;
        OpenWith = openWith;
        Handoff = handoff;
        Notices = notices;
        Notifications = notifications;
        Target = target;
        Listing = listing;
        Import = import;
        Settings = settings;
        this.settingsFolder = settingsFolder;
    }

    public StashWindow Window { get; }

    public Notices Notices { get; }

    /// <summary>The notifications above the tray the window's handoff raises; none is shown as a window here.</summary>
    public Notices Notifications { get; }

    public PresetTarget Target { get; }

    public Listing Listing { get; }

    public Import Import { get; }

    /// <summary>The window's handoff, whose one write at a time a capture's copy shares.</summary>
    public Handoff Handoff { get; }

    /// <summary>The window's editing, whose Saves render through Media as the product's do.</summary>
    public Editing Editing { get; }

    /// <summary>The window's Open with…, watching each copy it opened the dialog on until the check ends.</summary>
    public OpenWith OpenWith { get; }

    /// <summary>Move…, waiting for the window's parts as the product's does, opening the folder it moves to as <see cref="OpenStash"/> opens the first.</summary>
    public Moving Moving { get; }

    /// <summary>The settings the window reads and remembers its sizes in, as the keeper holds them.</summary>
    public SettingsKeeper Settings { get; }

    public TextBox Search => Window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Search your stash");

    /// <summary>The tag field Type a tag… opens over the grid, open or not.</summary>
    public TagFieldOverlay TagField => Window.GetVisualDescendants().OfType<TagFieldOverlay>().Single();

    /// <summary>The tag field's text box.</summary>
    public TextBox TagBox => TagField.GetVisualDescendants().OfType<TextBox>().Single();

    public ThumbnailGrid Grid => Window.GetVisualDescendants().OfType<ThumbnailGrid>().Single();

    /// <summary>The <c>+</c> button, whose menu holds the ways in.</summary>
    public Button AddButton => Window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Add to your stash");

    /// <summary>The Settings button beside the <c>+</c> button.</summary>
    public Button SettingsButton => Window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings");

    /// <summary>The entries of the <c>+</c> button's menu of ways in, as its flyout holds them.</summary>
    public List<MenuItem> AddMenu() => ((MenuFlyout)AddButton.Flyout!).Items.OfType<MenuItem>().ToList();

    public ChipRow ChipRow => Window.GetVisualDescendants().OfType<ChipRow>().Single();

    /// <summary>The preview, open or not, and its picture and back arrow.</summary>
    public Preview Preview => Window.GetVisualDescendants().OfType<Preview>().Single();

    public PreviewPicture PreviewPicture => Preview.GetVisualDescendants().OfType<PreviewPicture>().Single();

    public Button BackArrow => Preview.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Back");

    /// <summary>The preview's handoff button, its Copy, shown or not, and its ⋯ button.</summary>
    public Button HandoffButton => Preview.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Handoff");

    public Button CopyButton => Preview.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Copy");

    public Button EditButton => Preview.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Edit");

    public Button MoreButton => Preview.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "More options");

    /// <summary>The preview's name, edited in place; its text box, shown or not; and the name a click on it edits.</summary>
    public NameField NameField => Preview.GetVisualDescendants().OfType<NameField>().Single();

    public TextBox NameBox => NameField.GetVisualDescendants().OfType<TextBox>().Single();

    public Button NameButton => NameField.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Rename");

    /// <summary>The preview's tags row, and its tag field's text box.</summary>
    public PreviewTags PreviewTags => Preview.GetVisualDescendants().OfType<PreviewTags>().Single();

    public TextBox PreviewTagBox => PreviewTags.GetVisualDescendants().OfType<TextBox>().Single();

    /// <summary>The menu open on the grid or on the preview's ⋯ button, or null.</summary>
    public ContextMenu? OpenMenu => openMenu;

    /// <summary>The words a button shows, in order: its icon's glyph left out.</summary>
    public static List<string> Words(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().Where(text => text.FontFamily.Name != "Segoe Fluent Icons").Select(text => text.Text ?? "").ToList();

    /// <summary>The element of the window that has keyboard focus, or null.</summary>
    public object? Focused => Window.FocusManager?.GetFocusedElement();

    /// <summary>Space with the grid focused on <paramref name="item"/>: the preview opened as a person opens it. Run on the UI thread.</summary>
    public void OpenPreview(StashItem item)
    {
        Listing.Focus(item.Id);
        Grid.Focus();
        Press(Grid, Key.Space);
        Assert.AreEqual(item.Id, Preview.Item?.Id, "the preview opened");
    }

    /// <summary>Whether something holds the file open, as a playing animation does: it will not open with no sharing.</summary>
    public static bool Held(string path)
    {
        try
        {
            using var alone = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>The entry of an open tag list as the user sees it, once its entries are laid out.</summary>
    public static Button Entry(TagPicker picker, string tag)
    {
        var card = (Control)picker.Child!;
        Dispatcher.UIThread.RunJobs();
        card.UpdateLayout();
        return card.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == tag);
    }

    /// <summary>The button of <paramref name="field"/> that lists every tag.</summary>
    public static Button EveryTagButton(TagField field) =>
        field.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Every tag");

    /// <summary>The chip with these words, shown or not.</summary>
    public Button Chip(string label) => ChipRow.Children.OfType<Button>().Single(chip => chip.Content as string == label);

    /// <summary>The words of the chips the row shows, in order, once laid out: those whole inside it and reachable.</summary>
    public List<string> ShownChips()
    {
        Window.UpdateLayout();
        return ChipRow.Children.OfType<Button>().Where(chip => chip.IsHitTestVisible && chip.Bounds.X >= 0 && chip.Bounds.Right <= ChipRow.Bounds.Width).Select(chip => (string)chip.Content!).ToList();
    }

    /// <summary>Whether the chip shows as lit.</summary>
    public bool Lit(string label) => Chip(label).Classes.Contains("accent");

    /// <summary>
    /// The element a drag over the chip's centre is raised on, as Avalonia's drag-and-drop device finds it: the first
    /// element the window's hit test finds there, provided it allows drops (DragDropDevice.GetTarget, Avalonia 12.1.4).
    /// The hit test reads the rendered scene, so it waits until the window has rendered the chip there.
    /// </summary>
    public async Task<Interactive> DropTargetAt(string label)
    {
        Interactive? found = null;
        await Eventually(() => TrayDesktop.Get(() =>
        {
            var chip = Chip(label);
            var centre = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), Window)!.Value;
            found = (Window.InputHitTest(centre) as Visual)?.GetSelfAndVisualAncestors().OfType<Interactive>().FirstOrDefault();
            return found is Visual hit && (ReferenceEquals(hit, chip) || hit.GetVisualAncestors().Contains(chip));
        }));
        Assert.IsTrue(TrayDesktop.Get(() => DragDrop.GetAllowDrop(found!)), $"the window allows drops on {label}");
        return found!;
    }

    /// <summary>A drag event raised on <paramref name="target"/> as the platform raises it; returns the effects its handlers left.</summary>
    public static DragDropEffects RaiseDrag(Interactive target, RoutedEvent<DragEventArgs> routed, IDataTransfer data)
    {
        var dragged = new DragEventArgs(routed, data, target, new Point(1, 1), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        target.RaiseEvent(dragged);
        return dragged.DragEffects;
    }

    /// <summary>A drag carrying the given files, as the window's own drag-out or another program's would hand it over.</summary>
    public async Task<IDataTransfer> Files(params string[] paths) => await Dispatcher.UIThread.InvokeAsync(async () =>
    {
        var transfer = new DataTransfer();
        foreach (var path in paths)
        {
            transfer.Add(DataTransferItem.CreateFile((await Window.StorageProvider.TryGetFileFromPathAsync(path))!));
        }

        return (IDataTransfer)transfer;
    });

    /// <summary>Waits up to ten seconds for <paramref name="condition"/>, failing the check if it never comes true.</summary>
    public static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, "the condition did not come true");
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Opens a stash in the check's folder for the window to show, keeping its Open with… copies there too, and imports
    /// <paramref name="pictures"/> into it.
    /// </summary>
    public async Task<IReadOnlyList<StashItem>> OpenStash(params string[] pictures)
    {
        store = (await OpenStore(settingsFolder, Path.Combine(settingsFolder, "stash"))).Store!;
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Listing.StashOpened(store);
            var results = await Import.Drop(new DropContents(pictures.Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)).ToList(), false, false, []));
            return (IReadOnlyList<StashItem>)results.Select(file => file.Item!).ToList();
        });
    }

    /// <summary>One new picture for each of <paramref name="names"/>, each of its own colour, dropped on the open stash; their items.</summary>
    public async Task<IReadOnlyList<StashItem>> AddTo(params string[] names)
    {
        var dropped = names.Select(name => new DroppedItem(name, Picture(name, 6, 4, (_, _) => new SKColor((uint)name.GetHashCode() | 0xFF000000)), null, false)).ToList();
        var added = await TrayDesktop.Get(() => Import.Drop(new DropContents(dropped, false, false, [])));
        return added.Select(file => file.Item!).ToList();
    }

    /// <summary>A small PNG with a transparent corner, its colour from its name so no two are duplicates, written into the check's folder outside the stash.</summary>
    public string Picture(string name)
    {
        var shade = (byte)name.Sum(character => character);
        var pixels = Enumerable.Range(0, 6 * 4).SelectMany(i => new byte[] { shade, 80, (byte)(i * 10), (byte)(i == 0 ? 0 : 255) }).ToArray();
        using var image = SKImage.FromPixelCopy(new SKImageInfo(6, 4, SKColorType.Bgra8888, SKAlphaType.Unpremul), pixels);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(settingsFolder, name);
        Directory.CreateDirectory(settingsFolder);
        File.WriteAllBytes(path, png.ToArray());
        return path;
    }

    /// <summary>A copy of one of the portable checks' media fixtures, under <paramref name="name"/>, in the check's folder outside the stash.</summary>
    public string Fixture(string fixture, string name)
    {
        var path = Path.Combine(settingsFolder, name);
        Directory.CreateDirectory(settingsFolder);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path);
        return path;
    }

    /// <summary>A PNG of the given size, each pixel coloured by <paramref name="colorAt"/>, written into the check's folder outside the stash.</summary>
    public string Picture(string name, int width, int height, Func<int, int, SKColor> colorAt)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, colorAt(x, y));
            }
        }

        var path = Path.Combine(settingsFolder, name);
        Directory.CreateDirectory(settingsFolder);
        File.WriteAllBytes(path, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        return path;
    }

    /// <summary>An exclusive lock on the store from a second connection, which holds back its reads until disposed.</summary>
    public SqliteConnection HoldStore()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(settingsFolder, "stash", StashFolder.StoreFileName), Pooling = false }.ToString());
        connection.Open();
        using var exclusive = connection.CreateCommand();
        exclusive.CommandText = "BEGIN EXCLUSIVE";
        exclusive.ExecuteNonQuery();
        return connection;
    }

    /// <summary>The uses the stash has recorded, read through a second connection between the store's transactions.</summary>
    public long Uses()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(settingsFolder, "stash", StashFolder.StoreFileName), Pooling = false }.ToString());
        connection.Open();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM uses";
        return (long)count.ExecuteScalar()!;
    }

    /// <summary>Gives the items the tags <paramref name="text"/> commits through the store and shows the stash again.</summary>
    public async Task Tag(IReadOnlyList<StashItem> items, string text)
    {
        var outcomes = await Listing.Store!.AddTag(items, text);
        Assert.IsTrue(outcomes.All(outcome => outcome.Succeeded));
        await Dispatcher.UIThread.InvokeAsync(() => Listing.Refresh());
    }

    /// <summary>The item's tags as the store holds them now, in spelling order ignoring case.</summary>
    public List<string> TagsOf(long id)
    {
        var all = Listing.Store!.All().GetAwaiter().GetResult().Value!;
        return all.SingleOrDefault(item => item.Id == id)?.Tags.Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    /// <summary>
    /// Adds <paramref name="count"/> small pictures to the open stash, named "added 1" onwards, each of the one flat
    /// <see cref="AddedColour"/>, in place and carrying the tag <paramref name="tagOf"/> gives it, in one transaction
    /// through a second connection, as a stash this size would hold them; the window shows them at its next load.
    /// </summary>
    public void AddPictures(int count, Func<int, string> tagOf)
    {
        var root = Path.Combine(settingsFolder, "stash");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, StashFolder.StoreFileName), Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var item = connection.CreateCommand();
        item.Transaction = transaction;
        item.CommandText =
            """
            INSERT INTO items (name, added_at, format, width, height, animated, frame_count, duration_ms, original_file, finished_file, original_sha256)
            VALUES ($name, $added, 'Png', 8, 6, 0, 1, 0, $file, $file, $sha256) RETURNING id
            """;
        using var tag = connection.CreateCommand();
        tag.Transaction = transaction;
        tag.CommandText = "INSERT INTO tags (item_id, tag) VALUES ($id, $tag)";
        // Added before the stash's own items, so those stay first in All.
        var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (count + 1) * 1000L;
        for (var i = 1; i <= count; i++)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(8, 6, SKColorType.Bgra8888, SKAlphaType.Opaque));
            bitmap.Erase(AddedColour(i));
            var bytes = bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray();
            var name = $"added {i}";
            File.WriteAllBytes(Path.Combine(root, name + ".png"), bytes);
            item.Parameters.Clear();
            item.Parameters.AddWithValue("$name", name);
            item.Parameters.AddWithValue("$added", start + i * 1000L);
            item.Parameters.AddWithValue("$file", name + ".png");
            item.Parameters.AddWithValue("$sha256", System.Security.Cryptography.SHA256.HashData(bytes));
            var id = (long)item.ExecuteScalar()!;
            tag.Parameters.Clear();
            tag.Parameters.AddWithValue("$id", id);
            tag.Parameters.AddWithValue("$tag", tagOf(i));
            tag.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>The one colour of the <paramref name="i"/>th picture <see cref="AddPictures"/> adds, opaque, unlike every other's.</summary>
    public static SKColor AddedColour(int i) => new((byte)i, (byte)(i >> 8), 7);

    /// <summary>A key pressed with focus on <paramref name="target"/>, as the platform raises it: tunnelling from the window down.</summary>
    public static void Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target });

    /// <summary>Types <paramref name="text"/> into <paramref name="target"/> one character at a time, as the platform raises text input.</summary>
    public static void Type(Control target, string text)
    {
        foreach (var character in text)
        {
            target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = character.ToString(), Source = target });
        }
    }

    /// <summary>A key released on <paramref name="target"/>, as the platform raises it after its press.</summary>
    public static void Release(Control target, Key key) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = target });

    /// <summary>The second press of a double-click with the left button at the centre of the item's tile, with <paramref name="modifiers"/> held.</summary>
    public void DoubleClick(long id, KeyModifiers modifiers = KeyModifiers.None)
    {
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        Grid.RaiseEvent(new PointerPressedEventArgs(Grid, mouse, Window, Centre(id), 0, properties, modifiers, 2));
    }

    /// <summary>A click of the left button at the centre of <paramref name="target"/>, its press and its release.</summary>
    public void Click(Control target)
    {
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)!.Value;
        target.RaiseEvent(new PointerPressedEventArgs(target, mouse, Window, centre, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        target.RaiseEvent(new PointerReleasedEventArgs(target, mouse, Window, centre, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
    }

    /// <summary>The button's Click event raised on it, with no pointer press or release, unlike <see cref="Click"/>.</summary>
    public static void RaiseClick(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>A press of the left button, without a key held, at the centre of the item's tile.</summary>
    public void Press(long id)
    {
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        Grid.RaiseEvent(new PointerPressedEventArgs(Grid, mouse, Window, Centre(id), 0, properties, KeyModifiers.None, 1));
    }

    /// <summary>The pointer moved with the left button still down to the given distance from the centre of the item's tile.</summary>
    public void MovePointer(long id, double dx, double dy)
    {
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
        Grid.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, Grid, mouse, Window, Centre(id) + new Point(dx, dy), 0, properties, KeyModifiers.None));
    }

    /// <summary>The left button released at the centre of the item's tile.</summary>
    public void Release(long id)
    {
        var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        Grid.RaiseEvent(new PointerReleasedEventArgs(Grid, mouse, Window, Centre(id), 0, properties, KeyModifiers.None, MouseButton.Left));
    }

    /// <summary>A right-click at the centre of the item's tile, as the platform asks for a context menu; the labels the menu it opened shows.</summary>
    public List<string> RightClick(long id)
    {
        CloseMenu();
        var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased);
        var release = new PointerReleasedEventArgs(Grid, mouse, Window, Centre(id), 0, properties, KeyModifiers.None, MouseButton.Right);
        Grid.RaiseEvent(new ContextRequestedEventArgs(release) { Source = Grid });
        Assert.IsNotNull(openMenu, "a menu opened");
        return openMenu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(Label).ToList();
    }

    /// <summary>A click on the preview's ⋯ button; the open menu's entries as they show, a separator as "—".</summary>
    public List<string> ClickMore()
    {
        CloseMenu();
        MoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsNotNull(openMenu, "a menu opened");
        return MenuShown();
    }

    /// <summary>The open menu's entries as they show, a separator as "—".</summary>
    public List<string> MenuShown() => openMenu!.Items.Where(entry => entry is not MenuItem { IsVisible: false }).Select(entry => entry is Separator ? "—" : Label((MenuItem)entry!)).ToList();

    /// <summary>
    /// A key pressed and released as Windows hands a key to the stash window, the window that has the keyboard (WM_KEYDOWN
    /// and WM_KEYUP): Avalonia raises it on whatever holds keyboard focus, inside the window or inside a menu it opened.
    /// </summary>
    public void SendKey(Key key)
    {
        var virtualKey = key switch
        {
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Up => 0x26,
            Key.Down => 0x28,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "not a key the checks send"),
        };
        var handle = Window.TryGetPlatformHandle()!.Handle;
        var scan = (nint)(MapVirtualKey((uint)virtualKey, 0) << 16);
        SendMessage(handle, 0x0100, virtualKey, 1 | scan);
        SendMessage(handle, 0x0101, virtualKey, unchecked((nint)0xC0000001) | scan);
    }

    /// <summary>Opens the open menu's submenu with these words; the words of its entries as they are now.</summary>
    public List<string> Submenu(string label)
    {
        var submenu = Entry(label);
        submenu.IsSubMenuOpen = true;
        return submenu.Items.OfType<MenuItem>().Select(Label).ToList();
    }

    /// <summary>Clicks the entry with the given words in the open menu's submenu with these words.</summary>
    public void ClickSubmenuEntry(string submenu, string label) =>
        Entry(submenu).Items.OfType<MenuItem>().Single(item => Label(item) == label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    /// <summary>The window told it has lost the foreground to another app (WM_ACTIVATE, WA_INACTIVE), as Windows tells it.</summary>
    public void Deactivate() => SendMessage(Window.TryGetPlatformHandle()!.Handle, 0x0006, 0, 0);

    /// <summary>The window told it has come to the foreground (WM_ACTIVATE, WA_ACTIVE), as Windows tells it after a click on it.</summary>
    public void Activate() => SendMessage(Window.TryGetPlatformHandle()!.Handle, 0x0006, 1, 0);

    /// <summary>The words of the footer's hints, in order.</summary>
    public List<string> FooterHints() =>
        Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Text is { } words && (words.StartsWith("Enter · ") || words.StartsWith("Ctrl+C · ")))
            .Select(text => text.Text!).ToList();

    /// <summary>What the notices showing say, in order, joined, after the import's own notice that opened the stash.</summary>
    public string Said() => string.Join(" | ", Notices.Shown.Where(notice => !notice.Text.StartsWith("Added ")).Select(notice => notice.Text));

    /// <summary>Clicks the open menu's entry with the given words.</summary>
    public void ClickMenuEntry(string label) => Entry(label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    public void CloseMenu()
    {
        openMenu?.Close();
        openMenu = null;
    }

    /// <summary>The open menu's entry with <paramref name="label"/>, for a real click on it.</summary>
    public MenuItem MenuEntry(string label) => Entry(label);

    private MenuItem Entry(string label) => openMenu!.Items.OfType<MenuItem>().Single(item => item.IsVisible && Label(item) == label);

    /// <summary>A menu entry's words as they show: a doubled underscore shows as one.</summary>
    private static string Label(MenuItem item) => ((string)item.Header!).Replace("__", "_");

    /// <summary>The centre of the item's tile on the desktop, in physical pixels, where a real click reaches it.</summary>
    public PixelPoint TileOnScreen(long id) => Window.PointToScreen(Centre(id));

    private Point Centre(long id)
    {
        var tile = Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == id);
        return Grid.TranslatePoint(new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2), Window)!.Value;
    }

    /// <param name="platformTarget">The real paste target seam the window reads instead of <paramref name="target"/>'s presets, when a check needs the windows in front read.</param>
    /// <param name="fonts">The fonts seam the window reads instead of the real one.</param>
    public static ShownWindow Open(PresetDrags? drags = null, PresetTarget? target = null, PresetDialogs? dialogs = null, ITarget? platformTarget = null, IFonts? fonts = null)
    {
        var settingsFolder = Path.Combine(Path.GetTempPath(), "imagestash-window-check-" + Guid.NewGuid().ToString("N"));
        ShownWindow? shown = null;
        TrayDesktop.UseProductTheme();
        TrayDesktop.Run(() =>
        {
            var notices = new Notices(new StoppedClock());
            var notifications = new Notices(new StoppedClock());
            var targets = target ?? new PresetTarget();
            var listing = new Listing(_ => { });
            var keeper = new SettingsKeeper(settingsFolder, Store.Settings.Load(settingsFolder, HeldFileRetry.Duration), HeldFileRetry.Duration, SettingsKeeper.Quiet, _ => { });
            var seams = new PlatformSeams(null!, null!, null!, null!, null!, new NoTrayIcon(), drags ?? (IClipboardAndDrops)new WindowsClipboardAndDrops(), dialogs ?? (IDialogsAndExplorer)new WindowsDialogsAndExplorer(), null!, platformTarget ?? targets, new WindowsScreen(), fonts ?? new WindowsFonts(), new WindowsBorderDrag());
            var handoff = new Handoff(listing, notices, notifications, seams.ClipboardAndDrops, seams.Target, new StoppedClock(), HeldFileRetry.Duration, HeldClipboard.Retry);
            var import = new Import(listing, notices, HeldFileRetry.Duration);
            var editing = new Editing(listing, notices, handoff, FinishedPicture.Render, new ColorChoices(), HeldFileRetry.Duration);
            var openWith = new OpenWith(listing, notifications, editing, seams.DialogsAndExplorer, LocalClock.Instance, action => Dispatcher.UIThread.Post(action));
            var moving = new Moving(listing, notices, keeper, seams.DialogsAndExplorer, handoff, folder => OpenStore(settingsFolder, folder), NextTurn);
            var window = new StashWindow(listing, notices, keeper, import, handoff, editing, openWith, moving, seams)
            {
                ShowActivated = false,
                // Where Windows would put it on showing, at the primary display's left edge: a window created off every
                // display takes that display's scaling only in part (its size, and PointToScreen, stay at the nearest
                // display's), which a primary display at another scale shows.
                Position = DisplayPlaces.At(DisplayPlaces.Primary(), 0, 100),
                Width = WindowSize.FlyoutDefault.Width,
                Height = WindowSize.FlyoutDefault.Height,
            };
            window.Show();
            shown = new ShownWindow(window, notices, notifications, targets, listing, import, handoff, editing, openWith, moving, keeper, settingsFolder);
            // The item menu is a popup of its own, outside the window's tree: it is found as it opens on the grid, and the
            // preview's ⋯ menu as it opens on its button.
            var opening = shown;
            shown.menus = MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) =>
            {
                var on = menu.PlacementTarget ?? (menu.Parent as Avalonia.Controls.Primitives.Popup)?.PlacementTarget;
                if (ReferenceEquals(on, opening.Grid) || ReferenceEquals(on, opening.MoreButton))
                {
                    opening.openMenu = menu;
                }
            }, RoutingStrategies.Bubble | RoutingStrategies.Direct, handledEventsToo: true);
        });
        return shown!;
    }

    /// <summary>A stash folder opened as the start opens one, its Open with… copies kept in the check's folder.</summary>
    private static Task<StoreOpening> OpenStore(string settingsFolder, string folder) =>
        StashStore.Open(folder, new WindowsRecycleBin(), HeldFileRetry.Duration, TimeProvider.System, openWithRoot: Path.Combine(settingsFolder, "open-with"));

    /// <summary>A later turn of the UI thread, as the product's Move waits for one.</summary>
    private static Task NextTurn()
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() => turn.SetResult(), DispatcherPriority.Background);
        return turn.Task;
    }

    /// <summary>Whether a click at the centre of the notice's Undo, inside the window's client area, lands on that button.</summary>
    public bool Reachable(Notice notice)
    {
        var content = (Control)Window.Content!;
        var undo = Window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == notice.UndoLabel
            && Texts((Control)button.GetVisualParent()!).Contains(notice.Text));
        if (undo.TranslatePoint(new Point(undo.Bounds.Width / 2, undo.Bounds.Height / 2), content) is not { } centre
            || centre.X < 0 || centre.Y < 0 || centre.X > content.Bounds.Width || centre.Y > content.Bounds.Height)
        {
            return false;
        }

        return content.InputHitTest(centre) is Visual hit && (ReferenceEquals(hit, undo) || hit.GetVisualAncestors().Contains(undo));
    }

    public void Dispose()
    {
        TrayDesktop.Run(() =>
        {
            // Close only hides the stash window (G3); hidden and off every display, it stays until the process ends.
            CloseMenu();
            menus?.Dispose();
            Window.Close();
            OpenWith.Dispose();
        });
        store?.Dispose();
        // A change still waiting for quiet would write settings.json after the folder is gone, leaving it behind.
        Settings.Flush().GetAwaiter().GetResult();
        // A thumbnail may still be reading a picture of the stash for a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(settingsFolder, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static IEnumerable<string> Texts(Control control) =>
        control.GetSelfAndVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "");

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MapVirtualKeyW", ExactSpelling = true)]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
