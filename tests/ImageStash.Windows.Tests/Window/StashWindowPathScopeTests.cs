using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

public sealed partial class StashWindowPathTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ScopeKeysClearTheActualSearchSelectTheFirstPictureAndKeepFocus(bool pinned)
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("cat recent.png"), shown.Picture("cat favorite.png"), shown.Picture("cat all.png"));
        var store = shown.Listing.Store!;
        await store.RecordUses([items[0]], DateTimeOffset.UnixEpoch);
        await store.SetFavorite([items[1]], true);

        foreach (var (key, scope, index, hint) in new[]
        {
            (Key.D1, Scope.Recent, 0, "Ctrl+1"), (Key.D2, Scope.Favorites, 1, "Ctrl+2"), (Key.D3, Scope.All, 2, "Ctrl+3"),
        })
        {
            foreach (var fromGrid in new[] { false, true })
            {
                Control origin = TrayDesktop.Get(() =>
                {
                    if (pinned)
                    {
                        shown.Window.SummonPinned();
                    }
                    else
                    {
                        shown.Window.SummonFromTray();
                    }

                    shown.Search.Text = "cat";
                    Assert.IsTrue(shown.Search.IsFocused);
                    if (fromGrid)
                    {
                        Tap(shown.Search, Key.Tab);
                        Assert.IsTrue(shown.Grid.IsFocused);
                    }

                    Control focused = fromGrid ? shown.Grid : shown.Search;
                    var chip = shown.Chip(scope.ToString());
                    ToolTip.SetIsOpen(chip, true);
                    try
                    {
                        Assert.IsTrue(shown.Window.OpenedPopups.Any(popup => popup.Child is ToolTip), "the scope's key hint is open");
                        Tap(focused, key, KeyModifiers.Control);
                    }
                    finally
                    {
                        ToolTip.SetIsOpen(chip, false);
                    }

                    return focused;
                });
                await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.LitChip == scope && shown.Search.Text == ""));
                TrayDesktop.Run(() =>
                {
                    Assert.AreSame(origin, shown.Focused);
                    Assert.AreEqual(items[index].Id, shown.Listing.Focused);
                    var chip = shown.Chip(scope.ToString());
                    Assert.IsTrue(shown.Lit(scope.ToString()));
                    Assert.AreEqual($"{scope} ({hint})", ToolTip.GetTip(chip));
                });
            }
        }

        TrayDesktop.Run(() => Assert.AreEqual("Settings (Ctrl+,)", ToolTip.GetTip(shown.SettingsButton)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DelayedChipAfterPinnedResummonKeepsTheOriginAndRangeOfSearchSelection(bool personal)
    {
        using var shown = ShownWindow.Open();
        var carrier = (await shown.OpenStash(shown.Picture("one.png"))).Single();
        await shown.Listing.Store!.AddTag([carrier], "cat dog");
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(shown.Window.SummonPinned);

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                RaiseClick(shown.Chip("#cat"));
                Assert.IsFalse(shown.Listing.Settled);
                RaiseClick(shown.Chip("#dog"));
                shown.Window.SummonFromTray();
                Assert.AreEqual("cat", shown.Search.SelectedText);
                if (personal)
                {
                    Tap(shown.Search, Key.Home);
                    Tap(shown.Search, Key.Right, KeyModifiers.Shift);
                    Assert.AreEqual("c", shown.Search.SelectedText);
                }
            });
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Search.Text == "cat dog"));
        if (personal)
        {
            TestClipboard.PutText("before the copy");
        }

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(personal ? "c" : "cat dog", shown.Search.SelectedText);
            Tap(shown.Search, Key.C, KeyModifiers.Control);
        });

        if (personal)
        {
            await Eventually(() => TestClipboard.Text() == "c");
            Assert.AreEqual(0L, shown.Uses());
        }
        else
        {
            await Eventually(() => shown.Uses() == 1);
            CollectionAssert.AreEqual(new[] { carrier.FilePath }, TestClipboard.Read().Files.ToArray());
            TrayDesktop.Run(() =>
            {
                Type(shown.Search, "owl");
                Assert.AreEqual("owl", shown.Search.Text);
            });
        }
    }

    [TestMethod]
    public async Task ScopeAndSettingsKeysLeaveAnOpenMenuAndTagFieldInChargeOfTheirInput()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat.png"));
        TrayDesktop.Run(() =>
        {
            shown.Window.SummonPinned();
            shown.Search.Text = "cat";
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));

        TrayDesktop.Run(() =>
        {
            var settingsAsked = 0;
            shown.Window.SettingsRequested += () => settingsAsked++;
            var focused = shown.Listing.Focused;
            var menu = shown.AddButton.Flyout!;
            menu.ShowAt(shown.AddButton);
            Assert.IsTrue(shown.Window.OpenedPopups.Any());
            Tap(shown.Search, Key.D2, KeyModifiers.Control);
            Tap(shown.Search, Key.OemComma, KeyModifiers.Control);
            Assert.AreEqual(("cat", focused, 0), (shown.Search.Text, shown.Listing.Focused, settingsAsked));
            menu.Hide();

            shown.Search.Focus();
            Tap(shown.Search, Key.T, KeyModifiers.Control);
            Assert.IsTrue(shown.TagBox.IsFocused);
            Tap(shown.TagBox, Key.D2, KeyModifiers.Control);
            Tap(shown.TagBox, Key.OemComma, KeyModifiers.Control);
            Assert.IsTrue(shown.TagBox.IsFocused);
            Assert.AreEqual(("cat", focused, 0), (shown.Search.Text, shown.Listing.Focused, settingsAsked));
        });
    }

    [TestMethod]
    public async Task TheFocusedGridCanLeaveAnEmptyScopeByKey()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("only.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.SummonPinned();
            Tap(shown.Search, Key.Tab);
            Assert.IsTrue(shown.Grid.IsFocused);
            Tap(shown.Grid, Key.D2, KeyModifiers.Control);
        });

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.Favorites));
        TrayDesktop.Run(() =>
        {
            Assert.IsEmpty(shown.Listing.Items);
            Assert.IsTrue(shown.Grid.IsEffectivelyVisible);
            Assert.IsTrue(shown.Grid.IsFocused);
            Tap(shown.Grid, Key.D3, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.All));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(item.Id, shown.Listing.Focused);
            Assert.IsTrue(shown.Grid.IsFocused);
        });
    }

    [TestMethod]
    [DataRow(Key.O, false)]
    [DataRow(Key.V, false)]
    [DataRow(Key.O, true)]
    [DataRow(Key.V, true)]
    public async Task KeyboardImportAfterAWaitingScopeDoesNotGiveItsOldLitTag(Key importKey, bool editSearch)
    {
        var dialogs = new PresetDialogs();
        using var shown = ShownWindow.Open(dialogs: dialogs);
        var carrier = (await shown.OpenStash(shown.Picture("carrier.png"))).Single();
        await shown.Listing.Store!.AddTag([carrier], "cat");
        var fresh = shown.Picture("fresh.png");
        dialogs.PickedImports = [fresh];
        if (importKey == Key.V)
        {
            TestClipboard.Put(TestClipboard.FileList(fresh));
        }

        TrayDesktop.Run(() =>
        {
            shown.Window.SummonPinned();
            shown.Search.Text = "cat";
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Lit("#cat")));
        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                _ = shown.Listing.Refresh();
                Assert.IsFalse(shown.Listing.Settled);
                Tap(shown.Search, Key.Down);
                Tap(shown.Search, Key.D2, KeyModifiers.Control);
                Tap(shown.Search, importKey, KeyModifiers.Control);
                Assert.AreEqual(0, dialogs.OpenedImportPickers);
                if (editSearch)
                {
                    shown.Search.Text = "owl";
                }
            });
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Any(item => item.Name == "fresh")));
        TrayDesktop.Run(() => Assert.IsEmpty(shown.Listing.Items.Single(item => item.Name == "fresh").Tags));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SettingsKeyWaitsForAnEarlierHandoffAndItsEnterRelease(bool fromPreview)
    {
        var relay = PresetTarget.App("Relay");
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat one.png"), shown.Picture("cat two.png"));
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        TrayDesktop.Run(() =>
        {
            shown.Target.DuringPaste = () => order.Add("Paste");
            shown.Window.SettingsRequested += () =>
            {
                order.Add("Settings");
                asked.TrySetResult();
            };
            shown.Window.SummonFromShortcut(new ForegroundReading(false, relay), new SummonAnchor(null));
        });
        if (fromPreview)
        {
            await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));
            TrayDesktop.Run(() => shown.OpenPreview(shown.Listing.Items[0]));
        }

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                if (fromPreview)
                {
                    Press(shown.PreviewPicture, Key.Enter);
                    Tap(shown.PreviewPicture, Key.Escape);
                    Assert.IsFalse(shown.Preview.IsOpen);
                }
                else
                {
                    shown.Search.Text = "cat";
                    Assert.IsFalse(shown.Listing.Settled);
                    Tap(shown.Search, Key.Down);
                    Press(shown.Search, Key.Enter);
                }

                Tap((Control)shown.Focused!, Key.OemComma, KeyModifiers.Control);
                Assert.IsFalse(asked.Task.IsCompleted);
            });
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));
        TrayDesktop.Run(() => Release(shown.Search, Key.Enter));
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { relay }, shown.Target.Pasted);
        CollectionAssert.AreEqual(new[] { "Paste", "Settings" }, order);
    }
}
