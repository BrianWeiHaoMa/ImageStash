using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using TheLook = ImageStash.Look;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The Settings page's look on the desktop: Theme changes the page and the stash window at once (F10), a take of the
/// look reaches the page, the stash window and a notification at once, a shortcut field and its key caps show the
/// take's keys, the stash folder's field focused keeps the edges every focused field keeps, and every word on the page,
/// the folder's path included, is drawn in Segoe UI (F10).
/// </summary>
[TestClass]
public sealed class SettingsWindowLookTests
{
    [TestMethod]
    public async Task ThemeChangesThePageAndTheStashWindowAtOnce()
    {
        using var settings = new SettingsFolder();
        using var shown = ShownWindow.Open();
        var requested = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page(theme => Application.Current!.RequestedThemeVariant = App.ThemeVariantOf(theme))));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            TrayDesktop.Run(() =>
            {
                var theme = (ComboBox)Control(window, SettingsPage.ThemeTitle);
                CollectionAssert.AreEqual(new[] { "Use system setting", "Light", "Dark" }, theme.Items.OfType<ComboBoxItem>().Select(item => item.Content).ToList());
                theme.SelectedIndex = 2;
                Assert.AreEqual((ThemeVariant.Dark, ThemeVariant.Dark), (window.ActualThemeVariant, shown.Window.ActualThemeVariant), "Dark on the page and the stash window at once (F10)");
                Assert.AreEqual(Store.Theme.Dark, settings.Keeper.Current.Theme);
                theme.SelectedIndex = 1;
                Assert.AreEqual((ThemeVariant.Light, ThemeVariant.Light), (window.ActualThemeVariant, shown.Window.ActualThemeVariant));
                theme.SelectedIndex = 0;
                var system = Application.Current!.PlatformSettings!.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Assert.AreEqual((system, system), (window.ActualThemeVariant, shown.Window.ActualThemeVariant), "the system's again");
                Assert.AreEqual(Store.Theme.System, settings.Keeper.Current.Theme);
            });
        }
        finally
        {
            TrayDesktop.Run(() =>
            {
                window.Close();
                Application.Current!.RequestedThemeVariant = requested;
            });
        }
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task ATakeOfTheLookReachesThePageTheStashWindowAndANotificationAtOnce(string theme)
    {
        using var settings = new SettingsFolder();
        using var shown = ShownWindow.Open();
        using var tray = new WindowsTray();
        var notifications = new Notices(new StoppedClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
        var requested = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            TrayDesktop.Run(() =>
            {
                Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                notifications.Raise("Saved to your stash");
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible && notification.Shown is { IsVisible: true }));
            var variant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var grounds = () => TrayDesktop.Get(() => (Solid(shown.Window.Background), Solid(window.Background), Solid(notification.Shown!.Background), Solid(shown.Chip("Favorites").Background)));

            TrayDesktop.Run(() => TheLook.Looks.Apply(Application.Current!, new CheckTake(), TheLook.Looks.Accent(Application.Current!)));
            Assert.AreEqual(
                (CheckTake.ColorOf(TheLook.LookKeys.FluentWindowGround, variant), CheckTake.ColorOf(TheLook.LookKeys.PageGround, variant), CheckTake.ColorOf(TheLook.LookKeys.Surface, variant),
                    CheckTake.ColorOf(TheLook.LookKeys.ScopeChipGround, variant)),
                grounds(), "the stash window's ground, the page's, the notification's and a chip's: the take's, at once, each window at once (F10)");

            var accent = TrayDesktop.Get(() => TheLook.Looks.Accent(Application.Current!));
            TrayDesktop.Run(() => TheLook.Looks.Apply(Application.Current!, accent));
            var paper = TheLook.Looks.All[0].Resources(variant, accent);
            Assert.AreEqual(
                (ColorOf(paper[TheLook.LookKeys.FluentWindowGround]), ColorOf(paper[TheLook.LookKeys.PageGround]), ColorOf(paper[TheLook.LookKeys.Surface]), ColorOf(paper[TheLook.LookKeys.ScopeChipGround])),
                grounds(), "Paper back on every window at once, Theme's variant kept (F10)");
            TrayDesktop.Run(() => Assert.AreEqual(variant, window.ActualThemeVariant, "Theme keeps its meaning under the take"));
        }
        finally
        {
            TrayDesktop.Run(() =>
            {
                window.Close();
                notifications.Close(notifications.Shown.Single());
                Application.Current!.RequestedThemeVariant = requested;
            });
            TrayDesktop.UseProductTheme();
        }
    }

    [TestMethod]
    public async Task AShortcutFieldAndItsKeyCapsShowTheTakesKeys()
    {
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            TrayDesktop.Run(() =>
            {
                var field = window.GetVisualDescendants().OfType<ShortcutField>().First();
                var caps = field.GetVisualDescendants().OfType<Border>().Where(border => border.Child is TextBlock { Text: "Ctrl" or "Alt" or "Space" }).ToList();
                Assert.HasCount(3, caps, "Ctrl, Alt and Space, each a key cap (F5)");
                Assert.AreEqual(AppliedLook.Of(field, TheLook.LookKeys.ShortcutFieldGround), Solid(field.Background), "the field's own ground (G32: the binding grey)");
                foreach (var cap in caps)
                {
                    Assert.AreEqual(AppliedLook.Of(cap, TheLook.LookKeys.KeyCapGround), Solid(cap.Background), "a key's own ground (G32)");
                    Assert.AreEqual(AppliedLook.Of(cap, TheLook.LookKeys.KeyCapBorder), Solid(cap.BorderBrush), "a key's grey edge");
                    Assert.IsTrue(cap.TryFindResource(TheLook.LookKeys.ControlRadius, cap.ActualThemeVariant, out var radius));
                    Assert.AreEqual(radius, cap.CornerRadius);
                }
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task TheStashFoldersFieldFocusedKeepsTheEdgesEveryFocusedFieldKeeps()
    {
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            TrayDesktop.Run(() => ((TextBox)Control(window, SettingsPage.StashFolderTitle)).Focus());
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => Control(window, SettingsPage.StashFolderTitle).IsFocused));
            TrayDesktop.Run(() =>
            {
                var field = Control(window, SettingsPage.StashFolderTitle);
                field.UpdateLayout();
                var border = field.GetVisualDescendants().OfType<Border>().First(each => each.Name == "PART_BorderElement");
                Assert.IsTrue(field.TryFindResource(TheLook.LookKeys.FluentFieldFocusedBorder, field.ActualThemeVariant, out var brush));
                Assert.IsTrue(field.TryFindResource(TheLook.LookKeys.FluentFieldFocusedThickness, field.ActualThemeVariant, out var thickness));
                Assert.AreSame(brush, border.BorderBrush, "the one focused border: grey sides, the bottom in the accent");
                Assert.AreEqual(thickness, border.BorderThickness, "an edge on every side");
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task EveryWordOnThePageTheStashFoldersIncludedIsDrawnInSegoeUi()
    {
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            TrayDesktop.Run(() =>
            {
                window.UpdateLayout();
                var folder = Control(window, SettingsPage.StashFolderTitle).GetVisualDescendants().OfType<TextPresenter>().Single();
                Assert.IsFalse(string.IsNullOrEmpty(folder.Text), "the stash folder's field shows its path");
                var words = window.GetVisualDescendants()
                    .Select(part => part switch
                    {
                        TextBlock text when !string.IsNullOrEmpty(text.Text) && text.FontFamily.Name != "Segoe Fluent Icons" => (Words: text.Text, Layout: text.TextLayout),
                        TextPresenter presenter when !string.IsNullOrEmpty(presenter.Text) => (Words: presenter.Text, Layout: presenter.TextLayout),
                        _ => default,
                    })
                    .Where(each => each.Layout is not null)
                    .ToList();
                Assert.IsTrue(words.Any(each => each.Words == folder.Text), "the folder's path is among the words read");
                foreach (var (text, layout) in words)
                {
                    CollectionAssert.AreEqual(new[] { RenderedFamilies.SegoeUi }, RenderedFamilies.Of(layout).ToList(), $"“{text}” is drawn in F10's Segoe UI");
                }
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    private static Color Solid(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    private static Color ColorOf(object? brush) => Solid(brush as IBrush);
}
