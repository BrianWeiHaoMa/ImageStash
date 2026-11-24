using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ImageStash.Look;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Settings, F4's one separate page: a normal window titled "ImageStash settings" that draws its
/// <see cref="SettingsPage"/> as one scrolling page of grouped rows (L2), each control named by its row's
/// title, a shortcut row's Reset beside its field (<see cref="ShortcutResetButton"/>), a failed act's sentence under
/// its row, and the start's read failure and a refused write at the top (G35); while
/// a Move is asked, its busy state under the stash folder's path, and while an export or an import is, its busy state
/// under its row, every control disabled until it has ended; an import's offer inside the Import row (F4,
/// <see cref="ImportOfferPanel"/>); and at its foot, the running build as one quiet line of text that can be selected and
/// copied (<see cref="SettingsPage.VersionLine"/>). One
/// is open at a time, brought forward when asked again (<see cref="Open"/>), on the row of a shortcut that is
/// unavailable when asked so (G33); it opens centred on the primary display, wholly inside its working area at any
/// scaling (E1), its size and place not remembered; Esc and Close close it, and it is then gone, Esc ending a shortcut
/// field's recording first. A recording also ends when the window is deactivated or closed, so ImageStash's shortcuts
/// are never left released. It is never hidden for a capture: a recorded region holds it like any other window (G3,
/// G15).
/// </summary>
public sealed class SettingsWindow : Avalonia.Controls.Window
{
    public const string WindowTitle = "ImageStash settings";

    /// <summary>The page's client size where the display has room for it, within 660 × 924 (L2).</summary>
    public static readonly WindowSize DefaultSize = new(620, 760);

    /// <summary>The smallest the user can make it (L2); the rows wrap and the page scrolls above it.</summary>
    public static readonly WindowSize MinimumSize = new(480, 360);

    private const string OpenFolderWords = "Open folder";

    private readonly SettingsPage page;
    private readonly ToggleSwitch alsoCopy;
    private readonly ComboBox lengths;
    private readonly ToggleSwitch pointer;
    private readonly TextBox folder;
    private readonly TextBlock moveLine = Line();
    private readonly TextBlock exportLine = Line();
    private readonly TextBlock importLine = Line();
    private readonly ImportOfferPanel offer;
    private readonly StackPanel content;
    private readonly ToggleSwitch startWithWindows;
    private readonly ComboBox theme;
    private readonly ComboBox thumbnailSize;
    private readonly Dictionary<string, (Border Row, TextBlock Words)> warnings = [];
    private readonly Dictionary<string, ShortcutField> shortcutFields = [];
    private readonly List<ShortcutResetButton> shortcutResets = [];
    private readonly Border readFailure;
    private readonly TextBlock readFailureWords = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border writeFailure;
    private readonly TextBlock writeFailureWords = new() { TextWrapping = TextWrapping.Wrap };
    private bool refreshing;
    private bool closed;

    public SettingsWindow(SettingsPage page)
    {
        this.page = page;
        Title = WindowTitle;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Width = DefaultSize.Width;
        Height = DefaultSize.Height;
        MinWidth = MinimumSize.Width;
        MinHeight = MinimumSize.Height;
        Bind(BackgroundProperty, this.GetResourceObservable(LookKeys.PageGround));
        KeyReleases.IgnoreUnpressed(this);

        alsoCopy = Toggle(SettingsPage.AlsoCopyTitle, on => page.SetAlsoCopyCapture(on));
        lengths = Choices(SettingsPage.MaximumLengthTitle, [], value => page.SetRecordingMaxSeconds((int)value));
        pointer = Toggle(SettingsPage.RecordPointerTitle, on => page.SetRecordPointer(on));
        // The path in the page's own family, as every word of it (F10).
        folder = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetName(folder, SettingsPage.StashFolderTitle);
        startWithWindows = Toggle(SettingsPage.StartWithWindowsTitle, on => page.SetStartWithWindows(on));
        theme = Choices(SettingsPage.ThemeTitle, SettingsPage.Themes.Select(choice => ((object)choice.Theme, choice.Words)), value => page.SetTheme((Theme)value));
        thumbnailSize = Choices(SettingsPage.ThumbnailSizeTitle, SettingsPage.ThumbnailSizes.Select(choice => ((object)choice.Size, choice.Words)), value => page.SetThumbnailSize((ThumbnailSize)value));
        offer = new ImportOfferPanel(page, this);

        readFailure = TopWarning(readFailureWords);
        writeFailure = TopWarning(writeFailureWords);
        content = new StackPanel { Margin = new Thickness(20, 4, 20, 20) };
        content.Children.Add(readFailure);
        content.Children.Add(writeFailure);
        string? group = null;
        StackPanel? card = null;
        foreach (var row in page.Rows)
        {
            if (row.Group != group)
            {
                group = row.Group;
                card = new StackPanel();
                content.Children.Add(new TextBlock { Text = group, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
                content.Children.Add(Card(card));
            }

            card!.Children.Add(Row(row, separated: card.Children.Count > 0));
            var words = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var warning = WarningRow(words);
            warnings[row.Title] = (warning, words);
            card.Children.Add(warning);
        }

        content.Children.Add(new SelectableTextBlock { Text = SettingsPage.VersionLine, FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 20, 0, 0) });
        Content = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        // Bubbling and unhandled only, so Esc in an open list closes the list first: one thing at a time, as in the
        // stash window (G2).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Close();
            }
        });
        // The press a field waits for will not come once the user has gone elsewhere.
        Deactivated += (_, _) => page.CancelRecording();
        page.Changed += Refresh;
        Refresh();
    }

    /// <summary>The model it draws.</summary>
    public SettingsPage Page => page;

    /// <summary>Whether the window has closed; a closed one is never shown again, and the next opening makes another.</summary>
    public bool HasClosed => closed;

    /// <summary>
    /// The one Settings window (F4): <paramref name="open"/> brought forward when it is still open, else a new window on
    /// a new <paramref name="page"/>, shown centred on the primary display and wholly inside its working area (E1); with
    /// <paramref name="row"/>, on that shortcut's field, whose row shows why it is unavailable (G33).
    /// </summary>
    public static SettingsWindow Open(SettingsWindow? open, Func<SettingsPage> page, ShortcutAct? row = null)
    {
        SettingsWindow window;
        if (open is { HasClosed: false })
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            window = open;
        }
        else
        {
            window = new SettingsWindow(page());
            window.ShowCentred();
        }

        if (row is { } act)
        {
            // Once shown and placed, which the new window finishes at this priority.
            Dispatcher.UIThread.Post(() => window.FocusShortcutRow(act), DispatcherPriority.Background);
        }

        return window;
    }

    /// <summary>Once the tray menu that asked for the page has closed, a window of its own whose going may take the activation back: the page comes forward again (G32).</summary>
    public void KeepForward()
    {
        if (!closed && !IsActive)
        {
            Activate();
        }
    }

    /// <summary>Brings <paramref name="act"/>'s shortcut field into view and focuses it, without beginning a recording.</summary>
    public void FocusShortcutRow(ShortcutAct act)
    {
        var field = shortcutFields[page.ShortcutField(act).Title];
        field.BringIntoView();
        field.Focus(NavigationMethod.Tab);
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        page.Changed -= Refresh;
        page.Close();
        base.OnClosed(e);
    }

    /// <summary>
    /// Placed before it shows, then once more after the pending layout, because the frame round the client (its title
    /// bar and borders) is known only once shown, and the whole window, frame included, must lie inside the working area.
    /// </summary>
    private void ShowCentred()
    {
        Place(default);
        Show();
        Dispatcher.UIThread.Post(() => Place(FrameSize is { } outer ? outer - ClientSize : default), DispatcherPriority.Background);
    }

    /// <summary>
    /// The whole window centred on the primary display at its default size, as a pinned window with no remembered place
    /// is, sized by that display's scaling and shrunk to its working area, the page scrolling within.
    /// </summary>
    private void Place(Size frame)
    {
        var displays = Summon.DisplaysOf(Screens);
        var outer = Summon.Pinned(null, displays, new WindowSize(DefaultSize.Width + frame.Width, DefaultSize.Height + frame.Height));
        var scaling = Summon.DisplayAt(outer.Center, displays).Scaling;
        var width = Math.Max(1, outer.Width / scaling - frame.Width);
        var height = Math.Max(1, outer.Height / scaling - frame.Height);
        // On a display too small for the minimum, the minimum yields, so the window stays wholly on screen (E1).
        MinWidth = Math.Min(MinimumSize.Width, width);
        MinHeight = Math.Min(MinimumSize.Height, height);
        Position = outer.Position;
        Width = width;
        Height = height;
    }

    /// <summary>Every control shows the page's values as they are now, without any of them acting.</summary>
    private void Refresh()
    {
        refreshing = true;
        try
        {
            alsoCopy.IsChecked = page.AlsoCopyCapture;
            ShowChoices(lengths, page.RecordingLengths.Select(seconds => ((object)seconds, SettingsPage.LengthWords(seconds))));
            Select(lengths, page.RecordingMaxSeconds);
            pointer.IsChecked = page.RecordPointer;
            folder.Text = page.StashFolder;
            Show(moveLine, page.MoveLine);
            Show(exportLine, page.ExportLine);
            Show(importLine, page.ImportLine);
            offer.Show();
            // The window's own Close stays: a Move, an export or an import goes on without the page, its outcome said in
            // the stash window's notices.
            content.IsEnabled = !page.WholeStashActAsked;
            startWithWindows.IsChecked = page.StartWithWindows;
            Select(theme, page.Theme);
            Select(thumbnailSize, page.ThumbnailSize);
            foreach (var field in shortcutFields.Values)
            {
                field.Show();
            }

            foreach (var reset in shortcutResets)
            {
                reset.Show();
            }

            foreach (var (title, (row, words)) in warnings)
            {
                var warning = page.WarningOf(title);
                words.Text = warning;
                row.IsVisible = warning is not null;
            }

            readFailureWords.Text = page.ReadFailure is { } failure ? $"{failure.Sentence}. {SettingsPage.NotKept}" : null;
            readFailure.IsVisible = page.ReadFailure is not null;
            writeFailureWords.Text = page.WriteFailure?.Sentence;
            writeFailure.IsVisible = page.WriteFailure is not null;
        }
        finally
        {
            refreshing = false;
        }
    }

    /// <summary>One row: its title and the line under it on the left, its control on the right, the stash folder's path and the Move's line under its words.</summary>
    private Border Row(SettingRow row, bool separated)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = row.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (row.Description is { } description)
        {
            words.Children.Add(new TextBlock { Text = description, FontSize = 12.5, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        }

        var control = row.Title switch
        {
            SettingsPage.AlsoCopyTitle => alsoCopy,
            SettingsPage.MaximumLengthTitle => lengths,
            SettingsPage.RecordPointerTitle => pointer,
            SettingsPage.StashFolderTitle => StashFolderButtons(),
            SettingsPage.ExportTitle => NamedButton.Of(SettingsPage.ExportWords, () => _ = page.Export(this)),
            SettingsPage.ImportTitle => NamedButton.Of(SettingsPage.ImportWords, () => _ = page.Import(this)),
            SettingsPage.StartWithWindowsTitle => startWithWindows,
            SettingsPage.ThemeTitle => (Control)theme,
            SettingsPage.ThumbnailSizeTitle => thumbnailSize,
            _ when page.ShortcutField(row.Title) is { } recording => ShortcutControls(recording),
            _ => throw new InvalidOperationException($"No control draws the row “{row.Title}”."),
        };
        if (row.Title == SettingsPage.StashFolderTitle)
        {
            words.Children.Add(folder);
            words.Children.Add(moveLine);
        }
        else if (row.Title == SettingsPage.ExportTitle)
        {
            words.Children.Add(exportLine);
        }
        else if (row.Title == SettingsPage.ImportTitle)
        {
            words.Children.Add(importLine);
            words.Children.Add(offer);
        }

        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(control, 1);
        grid.Children.Add(words);
        grid.Children.Add(control);
        // A line between rows of one card.
        var drawn = new Border { Child = grid, Padding = new Thickness(16, 12), BorderThickness = new Thickness(0, separated ? 1 : 0, 0, 0) };
        drawn.Bind(Border.BorderBrushProperty, drawn.GetResourceObservable(LookKeys.Divider));
        return drawn;
    }

    /// <summary>A shortcut row's controls: its Reset, before the field so the field keeps its place as Reset comes and goes, then the field (G32).</summary>
    private StackPanel ShortcutControls(ShortcutRecording recording)
    {
        var field = shortcutFields[recording.Title] = new ShortcutField(recording);
        var reset = new ShortcutResetButton(recording, field) { VerticalAlignment = VerticalAlignment.Center };
        shortcutResets.Add(reset);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { reset, field } };
    }

    /// <summary>Open folder, and Move… beside it (G32).</summary>
    private StackPanel StashFolderButtons()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(OpenFolderButton());
        buttons.Children.Add(NamedButton.Of(SettingsPage.MoveWords, () => _ = page.Move(this)));
        return buttons;
    }

    /// <summary>A line under a row's words, hidden while it has none.</summary>
    private static TextBlock Line() => new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), IsVisible = false };

    private static void Show(TextBlock line, string? words)
    {
        line.Text = words;
        line.IsVisible = words is not null;
    }

    private Button OpenFolderButton()
    {
        var words = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        words.Children.Add(IconGlyph.Of("", 14));
        words.Children.Add(new TextBlock { Text = OpenFolderWords, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = words };
        AutomationProperties.SetName(button, OpenFolderWords);
        button.Click += (_, _) => page.OpenFolder();
        return button;
    }

    private ToggleSwitch Toggle(string title, Action<bool> changed)
    {
        // No On and Off words beside the switch; the row's title says what it switches.
        var toggle = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0 };
        AutomationProperties.SetName(toggle, title);
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (!refreshing)
            {
                changed(toggle.IsChecked == true);
            }
        };
        return toggle;
    }

    private ComboBox Choices(string title, IEnumerable<(object Value, string Words)> choices, Action<object> chosen)
    {
        var box = new ComboBox { MinWidth = 180 };
        AutomationProperties.SetName(box, title);
        ShowChoices(box, choices);
        box.SelectionChanged += (_, _) =>
        {
            if (!refreshing && box.SelectedItem is ComboBoxItem { Tag: { } value })
            {
                chosen(value);
            }
        };
        return box;
    }

    /// <summary>
    /// The list holds exactly <paramref name="choices"/>, in order: an item no longer offered leaves and a new one is
    /// added in its place, while the items that stay are kept, so the one just picked is never replaced under the pick.
    /// </summary>
    private static void ShowChoices(ComboBox box, IEnumerable<(object Value, string Words)> choices)
    {
        var wanted = choices.ToList();
        foreach (var item in box.Items.OfType<ComboBoxItem>().Where(item => !wanted.Any(choice => choice.Value.Equals(item.Tag))).ToList())
        {
            box.Items.Remove(item);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (box.Items.Count <= i || !wanted[i].Value.Equals(((ComboBoxItem)box.Items[i]!).Tag))
            {
                box.Items.Insert(i, new ComboBoxItem { Content = wanted[i].Words, Tag = wanted[i].Value });
            }
        }
    }

    private static void Select(ComboBox box, object value) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().Single(item => value.Equals(item.Tag));

    private static Border Card(StackPanel rows)
    {
        return CardLook.Dress(new Border { Child = rows, ClipToBounds = true }, floats: false);
    }

    /// <summary>A warning row of the page's own, above the groups.</summary>
    private static Border TopWarning(TextBlock words)
    {
        var warning = WarningRow(words);
        warning.Bind(Border.CornerRadiusProperty, warning.GetResourceObservable(LookKeys.CardRadius));
        warning.Margin = new Thickness(0, 8, 0, 4);
        return warning;
    }

    /// <summary>The warning row: the warning glyph and the failure's words on an amber wash, hidden until it has words.</summary>
    private static Border WarningRow(TextBlock words)
    {
        var glyph = IconGlyph.Of("", 16);
        glyph.Bind(TextBlock.ForegroundProperty, glyph.GetResourceObservable(LookKeys.WarningMark));
        glyph.VerticalAlignment = VerticalAlignment.Top;
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(10, 0, 0, 0);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(words, 1);
        line.Children.Add(glyph);
        line.Children.Add(words);
        var warning = new Border { Child = line, Padding = new Thickness(16, 10), IsVisible = false };
        warning.Bind(Border.BackgroundProperty, warning.GetResourceObservable(LookKeys.WarningGround));
        return warning;
    }
}
