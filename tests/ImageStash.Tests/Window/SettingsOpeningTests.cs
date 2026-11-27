using System.Reflection;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Where the app opens a new Settings page from: the stash window's gear and <c>Ctrl+,</c> hand the page the window's
/// rectangle, which it opens beside; the tray menu and a notification's Choose another never do, so their page is centred
/// on the primary display whether or not a stash window shows. Read from the app's composition, which the portable checks
/// cannot start; <see cref="Summon.SettingsPlace"/>'s checks pin the places.
/// </summary>
[TestClass]
public sealed class SettingsOpeningTests
{
    private const BindingFlags Declared = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [TestMethod]
    public void OnlyTheStashWindowsOwnRequestReadsWhereTheWindowStands()
    {
        var readers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType == typeof(StashWindow) && called.Name == $"get_{nameof(StashWindow.Outer)}"))
            .Select(method => $"{ProductCalls.Outermost(method.DeclaringType!).Name}.{method.Name}")
            .ToList();

        CollectionAssert.AreEqual(new[] { "App.OpenSettingsFromWindow" }, readers, "the tray menu's and a notification's page take no stash window for the asker");
    }

    [TestMethod]
    public void TheStashWindowsRequestIsWiredToItsOwnOpening()
    {
        var createWindow = typeof(App).GetMethod("CreateWindow", Declared)!;
        var fromWindow = typeof(App).GetMethod("OpenSettingsFromWindow", Declared)!;
        var trayOpening = typeof(App).GetMethod("OpenSettings", Declared)!;

        Assert.IsTrue(MakesDelegateOf(createWindow, fromWindow), "the gear's and Ctrl+,'s request opens the page beside the window");
        Assert.IsFalse(MakesDelegateOf(createWindow, trayOpening), "not the tray menu's opening, which centres it on the primary display");
    }

    /// <summary>Whether <paramref name="method"/>'s IL takes <paramref name="target"/>'s address for a delegate (<c>ldftn</c>, 0xFE 0x06, then its token).</summary>
    private static bool MakesDelegateOf(MethodInfo method, MethodInfo target)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var token = BitConverter.GetBytes(target.MetadataToken);
        return Enumerable.Range(0, il.Length - 5).Any(i => il[i] == 0xFE && il[i + 1] == 0x06 && il.AsSpan(i + 2, 4).SequenceEqual(token));
    }
}
