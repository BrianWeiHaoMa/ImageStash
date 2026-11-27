using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The pinned stash window and the Settings page draw their title without the icon, each asking the Quiet windows seam
/// at every change to the titled frame and for each page opened, and nothing clears a window's own icon, which the
/// taskbar button and the task switcher show. Read from the product's calls, since the portable checks build no window;
/// the desktop suite asks on real windows.
/// </summary>
[TestClass]
public sealed class PlainTitleBarTests
{
    [TestMethod]
    public void TheFrameChangeAndEachPageOpeningAskForAPlainTitleBar()
    {
        var callers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType == typeof(IQuietWindows) && called.Name == nameof(IQuietWindows.PlainTitleBar)))
            .Select(method => $"{ProductCalls.Outermost(method.DeclaringType!).FullName}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(new[] { $"{typeof(SettingsWindow).FullName}.{nameof(SettingsWindow.Open)}", $"{typeof(WindowChrome).FullName}.{nameof(WindowChrome.Apply)}" }, callers,
            "asked where the stash window's frame is set, every pin included, not once at its construction, and for each page opened");
    }

    [TestMethod]
    public void NoWindowsOwnIconIsSet()
    {
        var setters = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.Name == "set_Icon" && typeof(Avalonia.Controls.Window).IsAssignableFrom(called.DeclaringType)))
            .Select(method => $"{ProductCalls.Outermost(method.DeclaringType!).FullName}.{method.Name}")
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), setters, "the taskbar button and the task switcher keep the executable's icon");
    }
}
