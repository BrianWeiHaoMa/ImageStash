using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Window;
using DesktopWindow = Avalonia.Controls.Window;

namespace ImageStash.Windows.Tests.Support;

/// <summary>Steps a desktop check's stopped notice clock through the product look's motion, on the UI thread.</summary>
internal static class NoticeMotion
{
    public static TimeSpan Entrance => Duration(LookKeys.CardEntrance);

    public static TimeSpan Exit => Duration(LookKeys.CardExit);

    public static void Advance(Notices notices, NoticeStack stack, TimeSpan by)
    {
        ((StoppedClock)notices.Clock).Advance(by);
        stack.Step();
    }

    public static void Advance(ShownWindow shown, TimeSpan by) => Advance(shown.Notices, StashStack(shown.Window), by);

    public static bool Hits(DesktopWindow window, PixelPoint at, Control control) =>
        window.InputHitTest(window.PointToClient(at)) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(control);

    public static async Task FinishExit(Notices notices, NoticeStack stack, Control card)
    {
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => notices.Shown.All(notice => stack.CardOf(notice) != card)));
        TrayDesktop.Run(() => Advance(notices, stack, Exit));
        if (TrayDesktop.Get(() => TopLevel.GetTopLevel(card) is not null))
        {
            TestMouse.MoveTo(TestMouse.Away);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => TopLevel.GetTopLevel(card) is null));
        }
    }

    public static NoticeStack StashStack(StashWindow window) => window.NoticeCards;

    private static TimeSpan Duration(string key)
    {
        var app = Application.Current!;
        Assert.IsTrue(app.TryGetResource(key, app.ActualThemeVariant, out var value), $"the product look provides {key}");
        return (TimeSpan)value!;
    }
}
