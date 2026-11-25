using Avalonia.Input;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class SearchBoxQueryTests
{
    [TestMethod]
    [DataRow(Key.None, "cat dog", KeyAction.Copy)]
    [DataRow(Key.Left, "at", KeyAction.None)]
    [DataRow(Key.End, "", KeyAction.Copy)]
    public async Task ADelayedChipUpdateKeepsTheSummonOrThePersonsLaterSelection(Key personalMove, string selected, KeyAction copy)
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("one");
        await listed.Stash.Store!.AddTag([carrier], "cat dog");
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var search = WindowLayout.SearchBox();
            var selection = new SummonSelection();
            SearchBoxQuery.Wire(search, listing, selection);
            Task cat, dog, summoned;
            using (listed.Stash.HoldStore())
            {
                cat = listing.ChooseTag("cat");
                dog = listing.ChooseTag("dog");
                Assert.IsFalse(dog.IsCompleted);
                summoned = listing.Summoned(flyout: false);
                search.SelectAll();
                selection.Made();
                Assert.AreEqual("cat", search.SelectedText);
                if (personalMove == Key.Left)
                {
                    selection.KeyDown(Key.Left, KeyModifiers.Shift);
                    search.SelectionStart = 3;
                    search.SelectionEnd = 1;
                }
                else if (personalMove == Key.End)
                {
                    selection.KeyDown(Key.End, KeyModifiers.None);
                    search.SelectionStart = search.SelectionEnd = search.CaretIndex = 3;
                }
            }

            await Task.WhenAll(cat, dog, summoned);
            Assert.AreEqual("cat dog", search.Text);
            Assert.AreEqual(selected, search.SelectedText);
            Assert.AreEqual(copy, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.SearchBox, selection.PersonSelected(search.SelectedText)));
            if (personalMove == Key.None)
            {
                search.SelectedText = "owl";
                Assert.AreEqual("owl", search.Text);
                Assert.AreEqual("owl", listing.Query);
                await listing.Settle();
            }
            else if (personalMove == Key.End)
            {
                Assert.AreEqual(3, search.CaretIndex);
                search.SelectedText = "x";
                Assert.AreEqual("catx dog", search.Text);
                await listing.Settle();
            }
        }));
    }

    [TestMethod]
    [DataRow("cat dog cat", 7, "dog", "dog")]
    [DataRow("cat dog  bird cat", 8, "dog bird", "dog ")]
    public async Task RemovingTwoCopiesOfATagPreservesTextSelectedBetweenThem(string query, int end, string revised, string selected)
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("one");
        await listed.Stash.Store!.AddTag([carrier], "cat dog");
        var listing = listed.Listing;
        await listing.SetQuery(query);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var search = WindowLayout.SearchBox();
            search.Text = listing.Query;
            var selection = new SummonSelection();
            SearchBoxQuery.Wire(search, listing, selection);
            search.SelectAll();
            selection.Made();
            selection.KeyDown(Key.Left, KeyModifiers.Shift);
            search.SelectionStart = 4;
            search.SelectionEnd = end;

            await listing.ChooseTag("cat");

            Assert.AreEqual(revised, search.Text);
            Assert.AreEqual(selected, search.SelectedText);
            Assert.IsTrue(selection.PersonSelected(search.SelectedText));
        }));
    }
}
