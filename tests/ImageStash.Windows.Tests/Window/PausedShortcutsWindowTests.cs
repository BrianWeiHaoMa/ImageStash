using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>Run at 100% and 150% on the signed-in desktop. The footer uses the flyout's default width of 460 DIP.</summary>
[TestClass]
public sealed class PausedShortcutsWindowTests
{
    private const string Paused = "Shortcuts are paused";
    private const string Resume = "Resume";
    private const ushort VkTab = 0x09;
    private const ushort VkShift = 0x10;
    private const ushort VkSpace = 0x20;

    [TestMethod]
    public async Task TheFlyoutFooterKeepsItsHintsBusyWordsAndCountAndResumeKeepsSearchFocus()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var settings = new SettingsFolder();
        using var shown = ShownWindow.Open();
        var cat = (await shown.OpenStash(shown.Picture("cat.png"))).Single();
        TrayDesktop.Run(() =>
        {
            settings.Shortcuts.Start();
            shown.Window.FollowShortcuts(settings.Shortcuts);
            shown.Window.SummonFromTray();
            shown.Search.Focus();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Focused == shown.Search && shown.FooterHints().Count == 1));
        var ordinary = TrayDesktop.Get(() => FooterWords(shown));
        var line = TrayDesktop.Get(() => LineOf(shown.Window));
        var footer = TrayDesktop.Get(() => line.GetVisualAncestors().OfType<Border>().First());

        TrayDesktop.Run(() =>
        {
            Assert.IsLessThanOrEqualTo(1 / shown.Window.RenderScaling, Math.Abs(shown.Window.ClientSize.Width - 460));
            settings.Shortcuts.TogglePause();
            shown.Window.UpdateLayout();
            CollectionAssert.AreEqual(new[] { Paused, "·" }, line.Children.OfType<TextBlock>().Select(block => block.Text).ToList());
            Assert.AreEqual(Resume, AutomationProperties.GetName(line.Resume));
            Assert.IsFalse(line.Resume.Focusable);
            CollectionAssert.AreEqual(ordinary, FooterWords(shown));
            Assert.IsGreaterThan(0d, line.Bounds.Width);
            Assert.IsTrue(OnScreen(footer).Contains(OnScreen(line.Resume)));
        });

        // With a wider paste hint and an import still accessing a held input, compare the primary words in both states.
        var path = shown.Picture("dog.png");
        Task<IReadOnlyList<ImportedFile>> adding;
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            adding = TrayDesktop.Get(() =>
            {
                shown.Target.Before = PresetTarget.App("Example chat");
                shown.Window.SummonFromTray();
                return shown.Import.Drop(new DropContents([new DroppedItem("dog.png", path, null, false)], false, false, []));
            });
            TrayDesktop.Run(() =>
            {
                var pausedWithBusy = FooterWords(shown);
                Assert.IsTrue(pausedWithBusy.Any(entry => entry.Text.StartsWith("Adding")), "the footer is visibly busy before comparing");
                Assert.IsTrue(pausedWithBusy.Any(entry => entry.Text.StartsWith("Enter · Paste into Example chat")));
                Assert.IsTrue(pausedWithBusy.Any(entry => entry.Text == "Ctrl+C · Copy"));
                Assert.IsTrue(pausedWithBusy.Any(entry => entry.Text == "1 item"));
                settings.Shortcuts.TogglePause();
                CollectionAssert.AreEqual(pausedWithBusy, FooterWords(shown));
                settings.Shortcuts.TogglePause();
                shown.Target.Before = null;
                shown.Window.SummonFromTray();
                shown.Search.Focus();
            });
        }

        await adding;
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
            line.Resume.IsEffectivelyVisible && line.Resume.Bounds.Width > 0 && shown.Focused == shown.Search
            && line.Resume.TranslatePoint(new Point(line.Resume.Bounds.Width / 2, line.Resume.Bounds.Height / 2), shown.Window) is { } center
            && shown.Window.InputHitTest(center) is Visual hit
            && hit.GetSelfAndVisualAncestors().Contains(line.Resume)));
        TestMouse.Click(TestMouse.Centre(line.Resume));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !settings.Shortcuts.Paused));
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(line.IsVisible);
            Assert.AreSame(shown.Search, shown.Focused, "the click leaves focus where the person was typing");
            shown.Window.SummonPinned();
            shown.OpenPreview(cat);
            settings.Shortcuts.TogglePause();
            shown.Window.UpdateLayout();
            Assert.IsTrue(shown.Preview.IsOpen && line.IsEffectivelyVisible && line.Resume.Bounds.Width > 0,
                "the paused line is still available under the pinned preview");
        });
    }

    [TestMethod]
    public async Task SettingsShowsThePausedLineWithAReadFailureAndResumeWorksByTabAndSpace()
    {
        using var settings = new SettingsFolder(file: "{");
        var window = TrayDesktop.Get(() =>
        {
            settings.Shortcuts.Start();
            settings.Shortcuts.TogglePause();
            return SettingsWindow.Open(null, () => settings.Page());
        });
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsActive && window.FrameSize is not null));
            var line = TrayDesktop.Get(() => LineOf(window));
            var first = TrayDesktop.Get(() => Control(window, SettingsPage.OpenTitle));
            TrayDesktop.Run(() =>
            {
                Assert.IsNotNull(window.Page.ReadFailure);
                var text = window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).ToList();
                Assert.IsTrue(text.Any(block => block.Text?.Contains(SettingsPage.NotKept, StringComparison.Ordinal) == true));
                var note = text.Single(block => block.Text == Paused);
                var heading = text.Single(block => block.Text == "Shortcuts");
                Assert.IsLessThan(OnScreen(heading).Y, OnScreen(note).Y);
                Assert.AreEqual(Resume, AutomationProperties.GetName(line.Resume));
                first.Focus(NavigationMethod.Tab);
            });

            TestHotKeys.Tap(VkSpace);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.Page.ShortcutField(ShortcutAct.Open).IsRecording));
            using (TestHotKeys.Hold(VkShift))
            {
                TestHotKeys.Tap(VkTab);
            }

            await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
                line.Resume.IsFocused && !window.Page.ShortcutField(ShortcutAct.Open).IsRecording));
            TestHotKeys.Tap(VkSpace);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !line.IsVisible && first.IsFocused));
            Assert.IsFalse(TrayDesktop.Get(() => settings.Shortcuts.Paused));
            Assert.IsFalse(TrayDesktop.Get(() => window.Page.ShortcutFields.Any(field => field.IsRecording)));
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    private static PausedShortcutsLine LineOf(Control window) =>
        window.GetVisualDescendants().OfType<PausedShortcutsLine>().Single();

    private static PixelRect OnScreen(Control control) =>
        new(control.PointToScreen(default), control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height)));

    private static List<(string Text, PixelRect Place)> FooterWords(ShownWindow shown)
    {
        shown.Window.UpdateLayout();
        var left = (DockPanel)LineOf(shown.Window).GetVisualParent()!.GetVisualParent()!;
        var footer = left.GetVisualParent()!;
        return footer.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Text is { Length: > 0 } && !block.GetVisualAncestors().OfType<PausedShortcutsLine>().Any())
            .Select(block => (block.Text!, OnScreen(block))).ToList();
    }
}
