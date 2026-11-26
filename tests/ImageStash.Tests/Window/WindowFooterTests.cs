using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// What the footer holds as the window draws it through its <see cref="WindowStatus"/> (G8, G10, G35): Enter's hint
/// first and clearest, with its key as a key cap and its whole words what a screen reader or a script reads; Ctrl+C's
/// hint and the count fainter; a copy's confirmation alone in the hints' place; and the busy state's words, whole as its
/// tooltip. What gives way where the footer is narrow is <c>FooterFitTests</c>'s; its pixels, the desktop suite's.
/// </summary>
[TestClass]
public sealed class WindowFooterTests
{
    [TestMethod]
    public async Task EnterNamesTheTargetForOneThreeAndNoSelectedItemsAndWhateverReadsNamesReadsEachHintWholeOnce()
    {
        using var listed = await ListedStash.Open();
        var items = await Listed(listed, "one", "two", "three");
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.App("Slack")));

        ControlThread.Run(() =>
        {
            var footer = Footer(listed, () => false);
            var parts = footer.FooterParts;
            listed.Listing.Select([], null);
            var noneSelected = Said(footer);
            listed.Listing.Focus(items[0].Id);
            var one = Said(footer);
            var oneNames = Names(footer);
            var caps = (Caps(parts.EnterHint), Caps(parts.CopyHint));
            var words = (parts.EnterHint.Words.Text, parts.CopyHint.Words.Text);
            listed.Listing.SelectAll();
            var three = Said(footer);
            listed.PasteTarget.None();
            listed.Listing.Focus(items[1].Id);
            var copying = Said(footer);
            var copyingNames = Names(footer);

            Assert.IsEmpty(noneSelected, "no hint while nothing is selected");
            CollectionAssert.AreEqual(new[] { "Enter · Paste into Slack", "Ctrl+C · Copy" }, one);
            Assert.AreEqual(1, oneNames.Count(name => name == "Enter · Paste into Slack"), string.Join(" | ", oneNames));
            Assert.AreEqual(1, oneNames.Count(name => name == "Ctrl+C · Copy"), string.Join(" | ", oneNames));
            CollectionAssert.IsSubsetOf(new[] { "Enter · Paste into Slack", "Ctrl+C · Copy" }, oneNames);
            Assert.IsFalse(oneNames.Any(name => name is "Enter" or "Ctrl" or "C" or "Copy" or "Paste into Slack"), "a cap or the words are never read again apart: " + string.Join(" | ", oneNames));
            CollectionAssert.AreEqual(new[] { "Enter" }, caps.Item1, "each key drawn as a cap");
            CollectionAssert.AreEqual(new[] { "Ctrl", "C" }, caps.Item2);
            Assert.AreEqual(("Paste into Slack", "Copy"), words, "what each key does, after its caps");
            CollectionAssert.AreEqual(new[] { "Enter · Paste 3 items into Slack", "Ctrl+C · Copy" }, three, "several are counted beside the target's app (G8)");
            CollectionAssert.AreEqual(new[] { "Enter · Copy" }, copying, "with no target, Enter copies and Ctrl+C's hint is gone (G8)");
            Assert.AreEqual(1, copyingNames.Count(name => name.StartsWith("Enter · ")));
            Assert.IsFalse(copyingNames.Any(name => name.StartsWith("Ctrl+C · ")));
        });
    }

    [TestMethod]
    public async Task TheTargetIsTheClearestTextAndCtrlCsHintAndTheCountAreFainterTheirCapsTheFootersOwn()
    {
        using var listed = await ListedStash.Open();
        var items = await Listed(listed, "one", "two");
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));

        ControlThread.Run(() =>
        {
            var parts = Footer(listed, () => false).FooterParts;
            listed.Listing.Focus(items[0].Id);

            Assert.AreEqual(1d, parts.EnterHint.Opacity * parts.EnterHint.Words.Opacity, "Enter's words at full strength");
            Assert.IsLessThan(1d, parts.CopyHint.Opacity, "Ctrl+C's hint fainter");
            Assert.IsLessThan(1d, parts.Count.Opacity, "the count fainter");
            Assert.IsGreaterThan(parts.Count.FontSize, parts.EnterHint.Words.FontSize, "Enter's words a size up from the count");
            Assert.IsGreaterThan(parts.CopyHint.Words.FontSize, parts.EnterHint.Words.FontSize, "and from Ctrl+C's");
            var heights = parts.EnterHint.Caps.Children.Concat(parts.CopyHint.Caps.Children).Cast<Border>().Select(cap => cap.Height).Distinct().ToList();
            CollectionAssert.AreEqual(new[] { KeyCaps.FooterHeight }, heights, "every cap at the footer's size, drawn by KeyCaps");
            Assert.AreEqual(36d, parts.View.Height);
        });
    }

    [TestMethod]
    public async Task TheHintsHideWhileThePreviewOrTheEditorCoversTheWindowAndReturnWhenItIsUncovered()
    {
        using var listed = await ListedStash.Open();
        var items = await Listed(listed, "one");
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));
        var covered = false;

        ControlThread.Run(() =>
        {
            var footer = Footer(listed, () => covered);
            listed.Listing.Focus(items[0].Id);
            covered = true;
            footer.ShowHints();
            var whileCovered = Said(footer);
            var namesWhileCovered = Names(footer);
            covered = false;
            footer.ShowHints();

            Assert.IsEmpty(whileCovered);
            Assert.IsFalse(namesWhileCovered.Any(name => name.StartsWith("Enter · ")), string.Join(" | ", namesWhileCovered));
            CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, Said(footer));
        });
    }

    [TestMethod]
    public async Task ACopysConfirmationStandsAloneInTheHintsPlaceAtFullStrengthWithNoKeyCap()
    {
        using var listed = await ListedStash.Open();
        var reaction = await listed.Stash.ImportFixture("the audacity", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));
        listed.Listing.Focus(reaction.Id);
        WindowStatus? shown = null;
        ControlThread.Run(() => shown = Footer(listed, () => false));
        var footer = shown!;

        var copied = await listed.Handoff.Copy(null!, [reaction]);

        ControlThread.Run(() =>
        {
            var parts = footer.FooterParts;
            var names = Names(footer);
            Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
            CollectionAssert.AreEqual(new[] { "✓ Copied “the audacity”" }, Said(footer));
            Assert.IsFalse(parts.EnterHint.IsVisible || parts.CopyHint.IsVisible, "the hints give way to it");
            Assert.IsFalse(parts.Row.CountGivesWay, "its long name cut short before the count, which stays (G10)");
            Assert.AreEqual(1d, parts.Confirmation.Opacity);
            Assert.AreEqual(Avalonia.Media.TextTrimming.CharacterEllipsis, parts.Confirmation.TextTrimming, "a long name cut short with an ellipsis");
            Assert.IsFalse(names.Any(name => name.StartsWith("Enter · ") || name.StartsWith("Ctrl+C · ")), string.Join(" | ", names));
            Assert.Contains("✓ Copied “the audacity”", names);
        });

        listed.Stash.Clock.Now += FooterHints.ConfirmedFor;
        listed.FooterHints.Sweep();

        ControlThread.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, Said(footer), "the hints back once it has gone");
            Assert.IsTrue(footer.FooterParts.Row.CountGivesWay, "and the count gives way to Enter's hint again");
        });
    }

    [TestMethod]
    public async Task OneOrTwoRunningActsShowTheirWordsCutWithAnEllipsisAndWholeAsTheTooltipBesideTheHints()
    {
        const string adding = "Adding 12 of 40 to your stash…";
        const string copying = "Copying “cat” to the clipboard…";
        using var listed = await ListedStash.Open();
        var items = await Listed(listed, "one", "two", "three");
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.App("Slack")));

        ControlThread.Run(() =>
        {
            var footer = Footer(listed, () => false);
            var parts = footer.FooterParts;
            listed.Listing.SelectAll();
            var idle = parts.Busy.IsVisible;
            footer.ShowBusy([null, adding, null]);
            var one = (parts.Busy.IsVisible, parts.BusyWords.Text, ToolTip.GetTip(parts.Busy), Said(footer));
            footer.ShowBusy([adding, copying]);
            var two = (parts.BusyWords.Text, ToolTip.GetTip(parts.Busy));
            footer.ShowBusy([null, null]);

            Assert.IsFalse(idle);
            Assert.AreEqual((true, adding, (object?)adding), (one.Item1, one.Item2, one.Item3), "the act named, its whole words its tooltip (G35)");
            CollectionAssert.AreEqual(new[] { "Enter · Paste 3 items into Slack", "Ctrl+C · Copy" }, one.Item4, "the hints stay beside it");
            Assert.AreEqual(Avalonia.Media.TextTrimming.CharacterEllipsis, parts.BusyWords.TextTrimming, "cut short with an ellipsis where the footer cannot hold it");
            Assert.AreEqual(($"{adding} · {copying}", (object?)$"{adding} · {copying}"), two, "two acts joined, both named");
            Assert.IsFalse(parts.Busy.IsVisible, "gone once nothing runs");
        });
    }

    /// <summary>Items named <paramref name="names"/> added to the stash, the listing showing All with them.</summary>
    private static async Task<List<StashItem>> Listed(ListedStash listed, params string[] names)
    {
        var items = new List<StashItem>();
        foreach (var name in names)
        {
            items.Add(await listed.Stash.Import(name));
        }

        await listed.Listing.ShowScope(Scope.All);
        return items;
    }

    /// <summary>The footer as the window wires it: drawn again when the listing, the target or the copy's confirmation changes.</summary>
    private static WindowStatus Footer(ListedStash listed, Func<bool> covered)
    {
        var status = new WindowStatus(listed.Listing, listed.Notices, listed.Settings, listed.FooterHints, new ScrollViewer(), covered, () => { }, listed.WaysIn, () => true);
        listed.Listing.Changed += () => OnControlThread(status.ShowListing);
        listed.PasteTarget.Changed += () => OnControlThread(status.ShowHints);
        listed.FooterHints.Changed += () => OnControlThread(status.ShowHints);
        status.ShowListing();
        return status;
    }

    private static void OnControlThread(Action draw)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            draw();
        }
        else
        {
            ControlThread.Run(draw);
        }
    }

    /// <summary>The footer's left words as drawn: the hints shown, or the confirmation in their place.</summary>
    private static List<string> Said(WindowStatus footer)
    {
        var parts = footer.FooterParts;
        return parts.Confirmation.IsVisible
            ? [parts.Confirmation.Text!]
            : new[] { parts.EnterHint, parts.CopyHint }.Where(hint => hint.IsVisible).Select(hint => hint.Text).ToList();
    }

    /// <summary>The names the footer gives whatever reads them, every visible piece's, in order.</summary>
    private static List<string> Names(WindowStatus footer)
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

        Walk(ControlAutomationPeer.CreatePeerForElement(footer.Footer));
        return names;
    }

    private static List<string> Caps(FooterHint hint) =>
        hint.Caps.Children.Cast<Border>().Select(cap => ((TextBlock)cap.Child!).Text!).ToList();
}
