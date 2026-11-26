using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ImageStash.Look;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The footer's fit in pixels, the real words measured (G8, G35, G2): Enter's hint whole, the busy state keeping its bar
/// and the words naming its act, cut with an ellipsis and whole as its tooltip, nothing over anything else or outside the
/// footer, at the window's minimum width and the pinned default, in the light and the dark take. Shown in an off-screen
/// window of its own, so it runs without the stash window or a signed-in desktop.
/// </summary>
[TestClass]
public sealed class FooterFitTests
{
    private const string Adding = "Adding 12 of 40 to your stash…";
    private const string Moving = @"Moving your stash to D:\Shared\Pictures\Reactions and stickers kept for later\ImageStash…";
    private const string Copying = "Copying “the audacity” to the clipboard…";

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void AtTheMinimumWidthPasteThreeItemsIntoSlackReadsWholeBesideTheImportsBarAndOpening(string take) => InFooter(take, WindowSize.Minimum.Width, footer =>
    {
        Show(footer, "Slack", Adding);

        AssertLaidOut(footer);
        Assert.IsFalse(Cut(footer.EnterHint.Words), "\"Paste 3 items into Slack\" whole");
        Assert.IsTrue(footer.EnterHint.Caps.Children.Count == 1 && footer.EnterHint.Caps.Children.All(cap => cap.Bounds.Width >= cap.DesiredSize.Width - 0.01), "Enter's cap whole");
        AssertTheActIsNamed(footer, "Adding 12");
        Assert.AreEqual(Adding, ToolTip.GetTip(footer.Busy), "the whole words to a pointer resting on them");
        var names = Names(footer.View);
        Assert.AreEqual(1, names.Count(name => name == "Enter · Paste 3 items into Slack"), string.Join(" | ", names));
        Assert.AreEqual(footer.Row.Given.Copy > 0 ? 1 : 0, names.Count(name => name == "Ctrl+C · Copy"), "a hint given no room is not read either");
        AssertTheLook(footer);
    });

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void ALongerTargetALongMoveAndTwoActsJoinedCutTheBusyWordsBeforeEntersHintAndNothingOverlaps(string take) => InFooter(take, WindowSize.Minimum.Width, footer =>
    {
        foreach (var (app, running) in new[] { ("Microsoft Teams", Moving), ("Microsoft Teams", $"{Copying} · {Adding}"), ("Microsoft Teams (work or school)", Moving) })
        {
            Show(footer, app, running);

            AssertLaidOut(footer);
            Assert.IsTrue(Cut(footer.BusyWords), $"the busy words cut with an ellipsis: {running}");
            AssertTheActIsNamed(footer, running.Split(' ')[0] + " " + running.Split(' ')[1]);
            var room = footer.Row.Bounds.Width;
            var whole = Whole(footer.EnterHint);
            if (whole + FooterFit.Gap + FooterFit.BusyOpening <= room)
            {
                Assert.IsFalse(Cut(footer.EnterHint.Words), $"Enter's hint whole beside the busy state's opening, for {app}");
            }
            else
            {
                Assert.IsTrue(Cut(footer.EnterHint.Words), $"Enter's hint cut with an ellipsis only past the busy state's opening, for {app}");
                Assert.IsGreaterThanOrEqualTo(FooterFit.BusyOpening - 0.5, footer.Row.Given.Busy);
            }
        }
    });

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void AtThePinnedDefaultWidthTheSlackCaseReadsWholeHintsBusyWordsAndCount(string take) => InFooter(take, WindowSize.PinnedDefault.Width, footer =>
    {
        Show(footer, "Slack", Adding);

        AssertLaidOut(footer);
        Assert.IsFalse(Cut(footer.EnterHint.Words) || Cut(footer.CopyHint.Words) || Cut(footer.BusyWords) || Cut(footer.Count));
        Assert.IsTrue(footer.Row.Given.Copy > 0 && footer.Row.Given.Count > 0, "Ctrl+C's hint and the count shown too");
        var names = Names(footer.View);
        Assert.AreEqual(1, names.Count(name => name == "Enter · Paste 3 items into Slack"), string.Join(" | ", names));
        Assert.AreEqual(1, names.Count(name => name == "Ctrl+C · Copy"), string.Join(" | ", names));
        AssertTheLook(footer);
    });

    [TestMethod]
    public void APieceThatComesToShowAfterTheFooterSettledWithoutItIsLaidOutAndShown() => InFooter("Light", WindowSize.PinnedDefault.Width, footer =>
    {
        var paused = (Control)footer.Row.Pieces[2];
        paused.IsVisible = false;
        footer.ShowHints([], confirming: false, shown: false);
        footer.Count.Text = StashStateView.CountText(40, 0);
        footer.ShowBusy([]);
        TopLevel.GetTopLevel(footer.View)!.UpdateLayout();

        footer.ShowBusy([Adding]);
        footer.ShowHints(["Enter · Copy"], confirming: false, shown: true);
        paused.IsVisible = true;
        TopLevel.GetTopLevel(footer.View)!.UpdateLayout();

        Assert.IsTrue(footer.Row.Given.Busy > 0 && footer.Busy.IsEffectivelyVisible, "the busy state started after the footer settled shows (G35)");
        Assert.IsTrue(footer.Row.Given.Lead > 0 && footer.EnterHint.IsEffectivelyVisible, "Enter's hint for a selection made after it shows (G8)");
        Assert.IsTrue(footer.Row.Given.Paused > 0 && paused.IsEffectivelyVisible, "the paused line, with room for it, shows (G33)");
    });

    /// <summary>Runs <paramref name="check"/> on a footer in a window of its own, off every display, <paramref name="width"/> wide inside, in <paramref name="take"/>.</summary>
    private static void InFooter(string take, double width, Action<WindowFooter> check) => TrayDesktop.Run(() =>
    {
        Application.Current!.RequestedThemeVariant = take == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var footer = new WindowFooter(new PausedShortcutsLine(() => { }, focusable: false));
        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(footer.View, Dock.Bottom);
        body.Children.Add(footer.View);
        body.Children.Add(new Border());
        var place = new PixelPoint(-8000, 100);
        var window = new Avalonia.Controls.Window { Width = width, Height = 160, ShowActivated = false, Position = place, Content = body };
        try
        {
            window.Show();
            // Avalonia's showing moves a window placed off every display onto one; once shown, it stays where it is moved.
            window.Position = place;
            Assert.AreEqual(nint.Zero, MonitorFromWindow(window.TryGetPlatformHandle()!.Handle, 0), $"off every display, at {window.Position}");
            check(footer);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    });

    private static void Show(WindowFooter footer, string app, string running)
    {
        footer.ShowHints([$"Enter · Paste 3 items into {app}", "Ctrl+C · Copy"], confirming: false, shown: true);
        footer.Count.Text = StashStateView.CountText(40, 3);
        footer.ShowBusy([running]);
        TopLevel.GetTopLevel(footer.View)!.UpdateLayout();
    }

    /// <summary>Every piece shown lies inside the footer's row and over no other; the footer 36 high under its divider.</summary>
    private static void AssertLaidOut(WindowFooter footer)
    {
        var row = new Rect(footer.Row.Bounds.Size);
        var shown = footer.Row.Pieces.Select(piece => (Control)piece.Parent!).Where(slot => slot.IsVisible).Select(slot => slot.Bounds).ToList();
        Assert.IsTrue(shown.All(place => place.X >= -0.01 && place.Right <= row.Right + 0.01), $"inside the row {row}: {string.Join(", ", shown)}");
        for (var i = 0; i < shown.Count; i++)
        {
            for (var j = i + 1; j < shown.Count; j++)
            {
                Assert.IsFalse(shown[i].Intersects(shown[j]), $"{shown[i]} and {shown[j]} apart");
            }
        }

        Assert.AreEqual(36d, footer.View.Bounds.Height);
        Assert.AreEqual(new Thickness(0, 1, 0, 0), footer.View.BorderThickness);
        Assert.AreEqual(footer.Row.Bounds.Width, ((Control)footer.Busy.Parent!).Bounds.Right, 0.01, "the busy state against the row's right edge");
    }

    /// <summary>The busy state shown with its whole bar and at least <paramref name="act"/> of its words, before any ellipsis.</summary>
    private static void AssertTheActIsNamed(WindowFooter footer, string act)
    {
        Assert.IsGreaterThan(0d, footer.Row.Given.Busy, "the busy state shown");
        var bar = footer.Busy.Children.OfType<ProgressBar>().Single();
        Assert.AreEqual(48d, bar.Bounds.Width, "its bar whole");
        var opening = new TextBlock { Text = act + "…", FontSize = footer.BusyWords.FontSize, FontFamily = footer.BusyWords.FontFamily };
        opening.Measure(Size.Infinity);
        Assert.IsGreaterThanOrEqualTo(opening.DesiredSize.Width, footer.BusyWords.Bounds.Width, $"\"{act}\" shown before the ellipsis");
    }

    /// <summary>Enter's words in the look's full text colour, Ctrl+C's hint and the count fainter, each cap in the key-cap keys.</summary>
    private static void AssertTheLook(WindowFooter footer)
    {
        Assert.AreEqual(AppliedLook.Of(footer.EnterHint, LookKeys.Words), ((ISolidColorBrush)footer.EnterHint.Words.Foreground!).Color);
        Assert.AreEqual(1d, footer.EnterHint.Opacity);
        Assert.IsLessThan(1d, footer.CopyHint.Opacity);
        Assert.IsLessThan(1d, footer.Count.Opacity);
        foreach (var cap in footer.EnterHint.Caps.Children.Concat(footer.CopyHint.Caps.Children).Cast<Border>())
        {
            Assert.AreEqual(AppliedLook.Of(cap, LookKeys.KeyCapGround), ((ISolidColorBrush)cap.Background!).Color);
            Assert.AreEqual(AppliedLook.Of(cap, LookKeys.KeyCapBorder), ((ISolidColorBrush)cap.BorderBrush!).Color);
            Assert.AreEqual(AppliedLook.Of(cap, LookKeys.KeyCapWords), ((ISolidColorBrush)((TextBlock)cap.Child!).Foreground!).Color);
        }
    }

    private static bool Cut(TextBlock text) => text.TextLayout.TextLines.Any(line => line.HasCollapsed);

    private static double Whole(Control piece)
    {
        piece.Measure(Size.Infinity);
        var whole = piece.DesiredSize.Width;
        piece.InvalidateMeasure();
        TopLevel.GetTopLevel(piece)!.UpdateLayout();
        return whole;
    }

    private static List<string> Names(Control control)
    {
        var names = new List<string>();
        void Walk(AutomationPeer peer)
        {
            if (peer.GetName() is { Length: > 0 } name)
            {
                names.Add(name);
            }

            foreach (var child in peer.GetChildren())
            {
                Walk(child);
            }
        }

        Walk(ControlAutomationPeer.CreatePeerForElement(control));
        return names;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
}
