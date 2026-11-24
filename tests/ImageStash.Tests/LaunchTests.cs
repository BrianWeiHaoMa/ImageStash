using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests;

/// <summary>
/// G3, as the technical specification arranges it: "A start whose command line carries the Run entry's argument (State:
/// Settings) is the start at sign-in (G3): finding the mutex held, it exits without signalling." Every other start is a
/// launch by hand, which asks the running instance to show its window and starts nothing else.
/// </summary>
[TestClass]
public sealed class LaunchTests
{
    private const string Executable = @"C:\Program Files\ImageStash\ImageStash.Windows.exe";

    /// <summary>The argument the Run entry's rule registers beside the executable, as the start at sign-in is then given it.</summary>
    private static string Registered()
    {
        var run = new RecordingStartWithWindows();
        Assert.IsTrue(StartWithWindowsRule.Apply(true, run, Executable).Succeeded);
        return run.Argument!;
    }

    [TestMethod]
    public void TheArgumentTheRunEntryRegistersMarksTheStartAtSignInAloneOrBesideAnother()
    {
        var registered = Registered();

        Assert.IsTrue(Launch.IsAtSignIn([registered]), "alone");
        Assert.IsTrue(Launch.IsAtSignIn(["--other", registered]), "after another");
        Assert.IsTrue(Launch.IsAtSignIn([registered, "--other"]), "before another");
    }

    [TestMethod]
    public void AStartWithoutThatArgumentAsWrittenIsALaunchByHand()
    {
        var registered = Registered();

        Assert.IsFalse(Launch.IsAtSignIn([]), "none, as the Start menu, an earlier build's Run entry and the installer start it (G31)");
        Assert.IsFalse(Launch.IsAtSignIn(["--other"]), "another alone");
        Assert.IsFalse(Launch.IsAtSignIn([registered.ToUpperInvariant()]), "another case");
        Assert.IsFalse(Launch.IsAtSignIn([registered + "-not"]), "a longer word");
        Assert.IsFalse(Launch.IsAtSignIn([registered[..^1]]), "a shorter one");
    }

    [TestMethod]
    public void WhileAnotherHoldsTheInstanceALaunchByHandAsksItToShowItsWindowAndStartsNothingElse()
    {
        var instance = new RecordingOneInstance { Free = false };

        Assert.IsFalse(Launch.HoldInstance(instance, ["--other"]));
        CollectionAssert.AreEqual(new[] { "hold", "signal" }, instance.Calls, "the running instance's window opens as a tray click would (G3)");
    }

    [TestMethod]
    public void WhileAnotherHoldsTheInstanceTheStartAtSignInExitsWithoutSignalling()
    {
        var instance = new RecordingOneInstance { Free = false };

        Assert.IsFalse(Launch.HoldInstance(instance, [Registered()]));
        CollectionAssert.AreEqual(new[] { "hold" }, instance.Calls, "it opens nothing in the running instance (G3)");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WithTheInstanceFreeEitherStartHoldsItAndSignalsNothing(bool atSignIn)
    {
        var instance = new RecordingOneInstance();

        Assert.IsTrue(Launch.HoldInstance(instance, atSignIn ? [Registered()] : []));
        CollectionAssert.AreEqual(new[] { "hold" }, instance.Calls);
    }
}
