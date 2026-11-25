using Avalonia.Input;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class StashWindowSelectionKeysTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DelWhileThePreviewEditOpensKeepsThePicture(bool editButton)
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("cat.png"), shown.Picture("dog.png")))[0];
        var store = shown.Listing.Store!;
        var opened = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            shown.Editing.Changed += RecordOpening;
        });

        try
        {
            using (shown.HoldStore())
            {
                TrayDesktop.Run(() =>
                {
                    if (editButton)
                    {
                        shown.Preview.EditName();
                        shown.NameBox.Text = "renamed cat";
                        Assert.IsTrue(shown.Preview.CommitDraft() is { IsCompleted: false });
                        RaiseClick(shown.EditButton);
                    }
                    else
                    {
                        Tap(shown.PreviewPicture, Key.E, KeyModifiers.Control);
                    }

                    Tap(shown.PreviewPicture, Key.Delete);
                });
            }

            var kept = (await store.Item(item.Id)).Value;
            Assert.IsNotNull(kept, "Del while Edit was opening removed cat from the stash");
            Assert.AreEqual(item.Id, await opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            kept = (await store.Item(item.Id)).Value;
            Assert.IsNotNull(kept);
            Assert.IsTrue(File.Exists(kept.FilePath));
            if (editButton)
            {
                Assert.AreEqual("renamed cat", kept.Name);
            }
        }
        finally
        {
            TrayDesktop.Run(() =>
            {
                shown.Editing.Changed -= RecordOpening;
                shown.Editing.Cancel();
            });
        }

        void RecordOpening()
        {
            if (shown.Editing.Session is { } session)
            {
                opened.TrySetResult(session.Item.Id);
            }
        }
    }
}
