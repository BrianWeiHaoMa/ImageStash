using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The tag field's suggestions (G17, N6): the existing tags containing the word being typed, ignoring case and its
/// leading <c>#</c>s, those starting with it first, then by spelling, a few at most, never a tag the field shows and
/// never a name; a chosen suggestion takes the place of the word being typed; the list of every tag keeps the store's
/// order and marks the tags its owner says every item carries; the control itself, given keys with no window: a
/// suggestion is lit only while Enter would take it, and what Enter would commit, which focus leaving commits too, is
/// what Enter commits (G17).
/// </summary>
[TestClass]
public sealed class TagFieldTests
{
    [TestMethod]
    public void TheExistingTagsContainingTheWordComeThoseStartingWithItFirst()
    {
        var existing = Once("Scream", "reaction", "carpet", "Are");

        CollectionAssert.AreEqual(new[] { "reaction", "Are", "Scream" }, TagField.SuggestionsFor("re", existing, []).Select(count => count.Tag).ToList(), "contains, not only starts with: tags are search words (G17)");
        CollectionAssert.AreEqual(new[] { "reaction", "Are", "Scream" }, TagField.SuggestionsFor("RE", existing, []).Select(count => count.Tag).ToList(), "ignoring case");
        CollectionAssert.AreEqual(new[] { "reaction", "Are", "Scream" }, TagField.SuggestionsFor("##re", existing, []).Select(count => count.Tag).ToList(), "a tag drops its leading #s, so the word does too");
        CollectionAssert.AreEqual(new[] { "carpet" }, TagField.SuggestionsFor("funny car", existing, []).Select(count => count.Tag).ToList(), "the word being typed is the last one");
    }

    [TestMethod]
    public void TheListHoldsEveryExistingTagInTheStoresOrderTheOnesEveryItemCarriesMarked()
    {
        var listed = TagPickerEntry.FromCounts([new("reaction", 5), new("cat", 3), new("Dog", 2)], ["DOG", "cat"]);

        CollectionAssert.AreEqual(
            new[] { new TagPickerEntry("reaction", 5, false), new TagPickerEntry("cat", 3, true), new TagPickerEntry("Dog", 2, true) },
            listed.ToList(),
            "every tag, not the suggestions' few, and a tag every item carries marked ignoring case (G17)");
    }

    [TestMethod]
    public void NothingTypedOrAWordJustEndedSuggestsNothing()
    {
        var existing = Once("reaction", "carpet");

        Assert.IsEmpty(TagField.SuggestionsFor("", existing, []));
        Assert.IsEmpty(TagField.SuggestionsFor("#", existing, []));
        Assert.IsEmpty(TagField.SuggestionsFor("reaction ", existing, []), "after a space a new word has not started");
        Assert.IsEmpty(TagField.SuggestionsFor("zzz", existing, []));
    }

    [TestMethod]
    public void AtMostAFewShow()
    {
        var existing = Once([.. Enumerable.Range(0, 20).Select(i => $"tag{i:00}")]);

        CollectionAssert.AreEqual(new[] { "tag00", "tag01", "tag02", "tag03", "tag04" }, TagField.SuggestionsFor("tag", existing, []).Select(count => count.Tag).ToList());
        Assert.HasCount(TagField.MostSuggestions, TagField.SuggestionsFor("ta", existing, []));
    }

    [TestMethod]
    public void ATagTheFieldShowsIsNeverSuggestedInAnySpelling()
    {
        var existing = Once("cat", "catnip", "dog", "dove");
        string[] shown = ["cat", "dog"];

        CollectionAssert.AreEqual(new[] { "catnip" }, TagField.SuggestionsFor("c", existing, shown).Select(count => count.Tag).ToList(), "the item carries cat already (G17)");
        CollectionAssert.AreEqual(new[] { "dove" }, TagField.SuggestionsFor("d", existing, shown).Select(count => count.Tag).ToList());
        CollectionAssert.AreEqual(new[] { "catnip" }, TagField.SuggestionsFor("CAT", existing, ["CAT", "Dog"]).Select(count => count.Tag).ToList(), "shown in any spelling");
        Assert.IsEmpty(TagField.SuggestionsFor("cat", Once("cat"), shown), "every match shown, nothing to suggest");
    }

    [TestMethod]
    public void AChosenSuggestionTakesThePlaceOfTheWordBeingTyped()
    {
        Assert.AreEqual("reaction", TagField.WithSuggestion("rea", "reaction"));
        Assert.AreEqual("reaction", TagField.WithSuggestion("#rea", "reaction"));
        Assert.AreEqual("funny reaction", TagField.WithSuggestion("funny re", "reaction"));
        Assert.AreEqual("reaction", TagField.WithSuggestion("", "reaction"));
    }

    [TestMethod]
    public async Task SuggestionsComeFromTheStashsTagsNeverFromAName()
    {
        using var listed = await ListedStash.Open();
        var red = await listed.Stash.Import("red", TestStash.Bytes(1));
        var carpet = await listed.Stash.Import("red carpet", TestStash.Bytes(2));
        await listed.Stash.Store!.AddTag([carpet], "reaction Scream");
        var counts = (await listed.Stash.Store!.TagCounts()).Value!;

        var suggested = TagField.SuggestionsFor("re", counts, []).Select(count => count.Tag).ToList();

        Assert.AreEqual("red", red.Name, "an item whose name contains the word, carrying no tag");
        CollectionAssert.AreEqual(new[] { "reaction", "Scream" }, suggested, "the existing tags alone, the one starting with the word first (G17, N6)");
    }

    [TestMethod]
    public void TypingAfterDownDropsTheChoiceAndItsHighlightSoEnterCommitsWhatIsTyped() => ControlThread.Run(() =>
    {
        var (field, box) = Field("reaction");
        string? committed = null;
        field.Committed += text => committed = text;

        box.Text = "r";
        Press(box, Key.Down);
        Assert.AreEqual("reaction", field.Chosen);
        Assert.IsTrue(Lit(field, "reaction"), "Down lights the suggestion it chose");
        box.Text = "re";

        Assert.IsNull(field.Chosen, "typing drops the choice, as it always has");
        Assert.IsFalse(Lit(field, "reaction"), "and its highlight: no suggestion is lit that Enter would not take (G17)");
        Press(box, Key.Enter);
        Assert.AreEqual("re", committed, "Enter commits what the box shows");
    });

    [TestMethod]
    public void DownThenEnterCommitsTheLitSuggestion() => ControlThread.Run(() =>
    {
        var (field, box) = Field("reaction");
        string? committed = null;
        field.Committed += text => committed = text;

        box.Text = "r";
        Press(box, Key.Down);
        Press(box, Key.Enter);

        Assert.AreEqual("reaction", committed, "the suggestion lit is the one Enter takes (G17)");
    });

    [TestMethod]
    public void WhatEnterWouldCommitIsWhatEnterCommits() => ControlThread.Run(() =>
    {
        Assert.AreEqual("reaction", EnterCommitsAfter("r", Key.Down, "reaction"), "the lit suggestion in place of the word typed");
        Assert.AreEqual("funny reaction", EnterCommitsAfter("funny re", Key.Down, "reaction"), "the earlier words kept");
        Assert.AreEqual("re", EnterCommitsAfter("re", null, "reaction"), "nothing lit: the words as typed");
    });

    [TestMethod]
    public void EmptyBackspaceHighlightsTheLastTagAndOnlyAnotherPressRemovesIt() => ControlThread.Run(() =>
    {
        var (field, box) = Field();
        field.Show(["cat", "dog"], [new("cat", 1), new("dog", 1)], ["cat", "dog"]);
        var removed = new List<string>();
        object? reveal = null;
        field.Removed += removed.Add;
        field.AddHandler(Control.RequestBringIntoViewEvent, (_, e) => reveal = e.Source);

        Press(box, Key.Back);
        Assert.IsTrue(Highlighted(field, "dog"));
        Assert.IsFalse(Highlighted(field, "cat"));
        Assert.AreSame(RemoveButton(field, "dog").FindAncestorOfType<Border>(), reveal, "bring the highlighted chip into view");
        Press(box, Key.Back);
        Assert.IsEmpty(removed, "the first press and its repeat must leave both tags attached");

        Release(box, Key.Back);
        Press(box, Key.Back);
        CollectionAssert.AreEqual(new[] { "dog" }, removed);
        Assert.IsFalse(Highlighted(field, "dog"));
    });

    [TestMethod]
    public void RefreshAndSwitchingFieldsCannotRemoveANewLastTag() => ControlThread.Run(() =>
    {
        var (field, box) = Field();
        var removed = new List<string>();
        field.Removed += removed.Add;
        field.Show(["cat", "dog"], [], []);
        Tap(box, Key.Back);

        field.Show(["cat", "emu"], [], []);
        Tap(box, Key.Back);
        Assert.IsEmpty(removed, "refresh made emu last, so it needs its own confirmation");
        Assert.IsTrue(Highlighted(field, "emu"));

        field.Clear();
        field.Show(["cat", "emu"], [], []);
        Tap(box, Key.Back);
        Assert.IsEmpty(removed, "the same last tag on a different item needs its own confirmation");
        Tap(box, Key.Back);
        CollectionAssert.AreEqual(new[] { "emu" }, removed);
    });

    [TestMethod]
    public void TypingAndEscapeCancelTheHighlightAndTextBackspaceStaysWithTheBox() => ControlThread.Run(() =>
    {
        var (field, box) = Field();
        var removed = new List<string>();
        var cancelled = 0;
        field.Removed += removed.Add;
        field.Cancelled += () => cancelled++;
        Tap(box, Key.Back);
        Assert.IsEmpty(removed, "the empty row has nothing to arm");

        field.Show(["dog"], [], []);
        Tap(box, Key.Back);
        box.Text = "d";
        Assert.IsFalse(Highlighted(field, "dog"));
        Assert.IsFalse(Press(box, Key.Back), "with text, the field leaves Backspace for text editing");
        box.Text = "";
        Press(box, Key.Back);
        Release(box, Key.Back);
        Assert.IsFalse(Highlighted(field, "dog"), "holding Backspace while editing text cannot start a removal");

        Tap(box, Key.Back);
        Tap(box, Key.Escape);
        Assert.AreEqual(1, cancelled);
        Assert.IsFalse(Highlighted(field, "dog"));
        Tap(box, Key.Back);
        Assert.IsEmpty(removed);
    });

    [TestMethod]
    public void TheRemoveButtonTakesFocusAndItsEnterLeavesTheDraftForTheBox() => ControlThread.Run(() =>
    {
        var (field, box) = Field();
        field.Show(["dog"], [], []);
        var removed = new List<string>();
        var committed = new List<string>();
        field.Removed += removed.Add;
        field.Committed += committed.Add;
        box.Text = "draft";
        var remove = RemoveButton(field, "dog");

        Assert.IsTrue(remove.Focusable && remove.IsTabStop);
        Tap(remove, Key.Enter);

        CollectionAssert.AreEqual(new[] { "dog" }, removed);
        Assert.IsEmpty(committed);
        Assert.AreEqual("draft", box.Text);
    });

    [TestMethod]
    public void ModifiedEnterDoesNotRemoveAChipsTag() => ControlThread.Run(() =>
    {
        var (field, _) = Field();
        field.Show(["dog"], [], []);
        var removed = new List<string>();
        field.Removed += removed.Add;
        var remove = RemoveButton(field, "dog");

        remove.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });

        Assert.IsEmpty(removed);
    });

    [TestMethod]
    public void DownBelongsToTheEmptyTagFieldWhileItsCatalogueIsBeingRead() => ControlThread.Run(() =>
    {
        var (field, box) = Field();
        field.Show([], null, []);

        Assert.IsTrue(Press(box, Key.Down), "Down is accepted while the catalogue is read");
        Assert.IsFalse(field.Picker.IsOpen, "wait for the current catalogue before opening the picker");
    });

    /// <summary>
    /// What a field with <paramref name="existing"/> to suggest would commit on Enter once <paramref name="typed"/> is in
    /// its box and <paramref name="key"/> pressed, checked against what its Enter then commits; on the control thread.
    /// </summary>
    internal static string EnterCommitsAfter(string typed, Key? key, params string[] existing)
    {
        var (field, box) = Field(existing);
        string? committed = null;
        field.Committed += text => committed = text;
        box.Text = typed;
        if (key is { } pressed)
        {
            Press(box, pressed);
        }

        var wouldCommit = field.EnterCommits;
        Press(box, Key.Enter);
        Assert.AreEqual(committed, wouldCommit, "what focus leaving commits is what Enter commits (G17)");
        return wouldCommit;
    }

    /// <summary>A tag field with <paramref name="existing"/> to suggest, built with no window, and its text box.</summary>
    private static (TagField Field, TextBox Box) Field(params string[] existing)
    {
        var field = new TagField();
        field.Show([], existing.Select(tag => new TagCount(tag, 1)).ToList(), []);
        return (field, field.GetVisualDescendants().OfType<TextBox>().Single());
    }

    private static bool Press(Control control, Key key)
    {
        var press = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        control.RaiseEvent(press);
        return press.Handled;
    }

    private static void Release(Control control, Key key) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });

    private static void Tap(Control control, Key key)
    {
        Press(control, key);
        Release(control, key);
    }

    private static Button RemoveButton(TagField field, string tag) => field.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == $"Remove tag {tag}");

    private static bool Highlighted(TagField field, string tag) =>
        RemoveButton(field, tag).FindAncestorOfType<Border>()!.Classes.Contains("accent");

    private static bool Lit(TagField field, string suggestion) => SuggestionButton(field, suggestion).Classes.Contains("accent");

    /// <summary>Tags each carried by one item, as the store counts them.</summary>
    private static TagCount[] Once(params string[] tags) => [.. tags.Select(tag => new TagCount(tag, 1))];

    [TestMethod]
    public void SuggestionsOfferTheCommonestOfTheTagsTheWordStartsFirstEachWithItsCountAfterTheWord() => ControlThread.Run(() =>
    {
        var field = new TagField();
        field.Show([], [new("pecan", 20), new("caz", 9), new("caa", 1), new("cab", 1), new("cac", 1), new("cad", 1), new("cae", 1)], []);
        var box = field.GetVisualDescendants().OfType<TextBox>().Single();

        box.Text = "ca";

        CollectionAssert.AreEqual(new[] { "caz", "caa", "cab", "cac", "cad" }, field.Suggestions.ToList(), "the tags the word starts first, the commonest of them first though last by spelling, five at most; pecan, the commonest of all, only contains it (G17)");
        var shown = (Grid)SuggestionButton(field, "caz").Content!;
        var count = (TextBlock)shown.Children[1];
        Assert.AreEqual("caz", ((TextBlock)shown.Children[0]).Text);
        Assert.AreEqual("9", count.Text, "the number of items in the stash that carry it, after the word");
        Assert.IsLessThan(1, count.Opacity, "faint");

        box.Text = "pe";
        CollectionAssert.AreEqual(new[] { "pecan" }, field.Suggestions.ToList());
    });

    [TestMethod]
    public void ATagTheWordStartsComesBeforeACommonerTagThatOnlyContainsIt()
    {
        CollectionAssert.AreEqual(new[] { "reaction", "Scream" }, TagField.SuggestionsFor("re", [new("Scream", 9), new("reaction", 1)], []).Select(count => count.Tag).ToList());
        CollectionAssert.AreEqual(new[] { "Scream", "cream" }, TagField.SuggestionsFor("re", [new("cream", 2), new("Scream", 9)], []).Select(count => count.Tag).ToList(), "then the commonest first");
    }

    private static Button SuggestionButton(TagField field, string suggestion) =>
        field.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == suggestion);

    [TestMethod]
    public void ASuggestionsCountFollowsTheStashWhileItsPlaceStays() => ControlThread.Run(() =>
    {
        var field = new TagField();
        field.Show([], [new("cat", 1)], []);
        field.GetVisualDescendants().OfType<TextBox>().Single().Text = "ca";
        Assert.AreEqual("1", ((TextBlock)((Grid)SuggestionButton(field, "cat").Content!).Children[1]).Text);

        field.Show([], [new("cat", 2)], []);

        Assert.AreEqual("2", ((TextBlock)((Grid)SuggestionButton(field, "cat").Content!).Children[1]).Text, "the number the store now holds (G17)");
    });
}
