using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Media;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.CaptureDesk;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The capture card's words and layout as Windows shows them, in the light and the dark look (G16, G24, G13, F10): for
/// each kind, its title naming what was captured on one line, its facts on one line with every part whole, a stashed
/// clipboard image's sentence on lines of its own, a name longer than the column cut with an ellipsis inside its box, and
/// Edit or Trim, Undo and × whole inside the card and its window, the card no taller than before its words went on one line;
/// the card showing takes no activation, the chat keeping the keystrokes. Run at 100 % and again at 150 %
/// (<c>tools/display-scale.ps1</c>). These checks show the notification and a chat window of their own on the primary
/// display.
/// </summary>
[TestClass]
public sealed class CaptureCardLookTests
{
    /// <summary>A name far longer than the card's column can hold.</summary>
    private const string LongName = "the reaction picture with a name far longer than any card could hold whole";

    /// <summary>
    /// The card before its facts went on one line: five rows beside the picture (the title, the sentences, the size, the
    /// name, the buttons with the hint), about 155 units tall; with a stashed clipboard image's sentence wrapped over three
    /// lines, about 195.
    /// </summary>
    private static double TallestBefore(CaptureKind kind) => kind == CaptureKind.StashedClipboardImage ? 195 : 155;

    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task EachKindsCardSaysItsTitleAndLineOnOneLineCutsALongNameAndKeepsItsButtonsWhole(string theme)
    {
        using var desk = await CaptureDesk.Open([LongName + ".png"]);
        var item = desk.Items().Single();
        var requested = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark);
        try
        {
            // Every part of the line present: the longest length a recording can have (the longest cap Settings offers) and the clipboard.
            foreach (var (kind, onClipboard, lengthMs) in new (CaptureKind, bool, long?)[]
                { (CaptureKind.Recording, true, SettingsPage.Lengths.Max() * 1000L), (CaptureKind.Screenshot, true, null), (CaptureKind.StashedClipboardImage, false, null) })
            {
                var card = await desk.Raise(item, kind, onClipboard, lengthMs);
                desk.ChatKeepsTheKeystrokes($"after the {kind} card appeared (G16: never take focus on their own)");
                TrayDesktop.Run(() =>
                {
                    card.UpdateLayout();
                    var size = card.Size!;
                    var words = new List<string?> { lengthMs is { } length ? ClipLength.Words(length) : null }
                        .Concat(onClipboard ? [CaptureNotification.OnClipboard, CaptureNotification.PastesIt] : [])
                        .Append(size).OfType<string>().ToList();
                    Assert.AreEqual(string.Join(" · ", words), card.Words.Line, $"{kind}: the line, a length first and the size last (G16, G24)");

                    var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !text.GetVisualAncestors().OfType<Button>().Any()).ToList();
                    var title = texts.Single(text => text.Text == CaptureNotification.Title(kind));
                    var line = texts.Single(text => text.Text == card.Words.Line);
                    foreach (var (text, what) in new[] { (title, "title"), (line, "line") })
                    {
                        Assert.HasCount(1, text.TextLayout.TextLines, $"{kind}'s {what} on one line");
                        Assert.IsFalse(text.TextLayout.TextLines[0].HasCollapsed, $"{kind}'s {what} whole, nothing cut");
                    }

                    if (kind == CaptureKind.StashedClipboardImage)
                    {
                        var aside = texts.Single(text => text.Text == Import.StillOnly);
                        Assert.IsFalse(aside.TextLayout.TextLines.Any(each => each.HasCollapsed), "G13's sentence whole, on lines of its own");
                        Assert.IsGreaterThan(Bottom(line, card), Top(aside, card) + 1e-9, "beneath the line");
                    }

                    var shownIn = TopLevel.GetTopLevel(card)!;
                    var name = NameButton(card).GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == LongName);
                    Assert.IsTrue(name.TextLayout.TextLines.Any(each => each.HasCollapsed), "the long name cut with an ellipsis, never the buttons");
                    foreach (var control in new Control[] { NameButton(card), EditButton(card), UndoButton(card), CloseButton(card) })
                    {
                        var what = $"{kind}: {Avalonia.Automation.AutomationProperties.GetName(control)} {control.GetType().Name}";
                        Assert.IsTrue(new Rect(card.Bounds.Size).Contains(Box(control, card)), $"{what} whole inside the card");
                        // And inside the notification's window, which is sized to its cards when they show.
                        Assert.IsTrue(new Rect(shownIn.ClientSize).Contains(Box(control, shownIn)), $"{what} whole inside the notification");
                    }

                    Assert.AreEqual(Top(EditButton(card), card), Top(UndoButton(card), card), 1e-9, "Edit or Trim and Undo on one row");
                    Assert.IsGreaterThan(Bottom(NameButton(card), card), Top(EditButton(card), card) + 1e-9, "their row beneath the name, theirs alone");
                    Assert.IsLessThanOrEqualTo(TallestBefore(kind), card.Bounds.Height, $"{kind}: no taller than the card before");

                    var ground = Solid(card.Background);
                    Assert.IsGreaterThanOrEqualTo(4.5, Take.Contrast(Solid(title.Foreground), ground), $"{kind}'s title reads on the card in the {theme} look (F10)");
                    Assert.IsGreaterThanOrEqualTo(3, Take.Contrast(Faded(Solid(line.Foreground), line.Opacity, ground), ground), $"{kind}'s line reads on the card in the {theme} look (F10)");
                    desk.Notifications.Close(desk.Notifications.Shown.Single());
                });
                // Each card fades over the look's exit before its window closes.
                await desk.FinishNoticeExit(card);
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is null));
            }
        }
        finally
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = requested);
        }
    }

    private static Rect Box(Visual control, Visual card) => new(control.TranslatePoint(default, card)!.Value, control.Bounds.Size);

    private static double Top(Visual control, Visual card) => control.TranslatePoint(default, card)!.Value.Y;

    private static double Bottom(Visual control, Visual card) => Top(control, card) + control.Bounds.Height;

    private static Color Solid(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    /// <summary>Words drawn at <paramref name="opacity"/> over <paramref name="ground"/>, as the eye meets them.</summary>
    private static Color Faded(Color words, double opacity, Color ground) =>
        Color.FromRgb(Mix(words.R, ground.R, opacity), Mix(words.G, ground.G, opacity), Mix(words.B, ground.B, opacity));

    private static byte Mix(byte words, byte ground, double opacity) => (byte)Math.Round(words * opacity + ground * (1 - opacity));
}
