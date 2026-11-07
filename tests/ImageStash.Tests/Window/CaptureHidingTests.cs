using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The stash window's hiding for a capture (G3), without a window: a showing flyout hidden and a showing pinned window
/// minimised whatever it has open, a window already out of the way or a flyout kept up by its own dialog or drag (G2)
/// left as it is; what was done kept in one slot that the capture's end brings back and empties, and that a summon forgets.
/// </summary>
[TestClass]
public sealed class CaptureHidingTests
{
    [TestMethod]
    [DataRow(true, false, false, false, false, CaptureHide.Flyout, DisplayName = "a showing flyout is hidden")]
    [DataRow(true, true, false, false, false, CaptureHide.Pinned, DisplayName = "a showing pinned window is minimised")]
    [DataRow(false, false, false, false, false, CaptureHide.Nothing, DisplayName = "a hidden flyout stays hidden")]
    [DataRow(false, true, false, false, false, CaptureHide.Nothing, DisplayName = "a pinned window closed stays closed")]
    [DataRow(true, true, true, false, false, CaptureHide.Nothing, DisplayName = "a minimised pinned window stays minimised")]
    [DataRow(true, false, false, true, false, CaptureHide.Nothing, DisplayName = "a flyout with its dialog up stays")]
    [DataRow(true, false, false, false, true, CaptureHide.Nothing, DisplayName = "a flyout with its drag in progress stays")]
    [DataRow(true, true, false, true, false, CaptureHide.Pinned, DisplayName = "a pinned window with its dialog up is minimised")]
    [DataRow(true, true, false, false, true, CaptureHide.Pinned, DisplayName = "a pinned window with its drag in progress is minimised")]
    public void TheWindowIsHiddenOrMinimisedAsItIsNow(bool visible, bool pinned, bool minimised, bool dialogUp, bool dragInProgress, CaptureHide expected) =>
        Assert.AreEqual(expected, CaptureHiding.Decide(visible, pinned, minimised, dialogUp, dragInProgress));

    [TestMethod]
    [DataRow(CaptureHide.Flyout)]
    [DataRow(CaptureHide.Pinned)]
    public void TheCapturesEndBringsBackWhatItsStartDidOnce(CaptureHide done)
    {
        var window = new RecordedWindow(done);

        window.Hiding.HideForCapture();
        window.Hiding.ShowAfterCapture();
        window.Hiding.ShowAfterCapture();

        CollectionAssert.AreEqual(new[] { "hide", $"restore {done}" }, window.Steps, "brought back once, the slot then empty");
    }

    [TestMethod]
    public void AWindowLeftAsItWasIsNotShownAtTheEnd()
    {
        var window = new RecordedWindow(CaptureHide.Nothing);

        window.Hiding.HideForCapture();
        window.Hiding.ShowAfterCapture();

        CollectionAssert.AreEqual(new[] { "hide" }, window.Steps, "a window already out of the way is not shown by the capture");
    }

    [TestMethod]
    public void AStartThatDoesNothingKeepsWhatAnEarlierStartRemembered()
    {
        var window = new RecordedWindow(CaptureHide.Pinned);
        window.Hiding.HideForCapture();
        window.Does = CaptureHide.Nothing;

        window.Hiding.HideForCapture();
        window.Hiding.ShowAfterCapture();

        CollectionAssert.AreEqual(new[] { "hide", "hide", "restore Pinned" }, window.Steps, "the window it minimised comes back at the end");
    }

    [TestMethod]
    public void ASummonForgetsTheSlotSoTheEndShowsNothing()
    {
        var window = new RecordedWindow(CaptureHide.Flyout);
        window.Hiding.HideForCapture();

        window.Hiding.Forget();
        window.Hiding.ShowAfterCapture();

        CollectionAssert.AreEqual(new[] { "hide" }, window.Steps);
    }

    /// <summary>A window whose hide answers <see cref="Does"/>, recording its hides and restores in order.</summary>
    private sealed class RecordedWindow
    {
        public RecordedWindow(CaptureHide does)
        {
            Does = does;
            Hiding = new CaptureHiding(
                () =>
                {
                    Steps.Add("hide");
                    return Does;
                },
                hidden => Steps.Add($"restore {hidden}"));
        }

        public CaptureHide Does { get; set; }

        public List<string> Steps { get; } = [];

        public CaptureHiding Hiding { get; }
    }
}
