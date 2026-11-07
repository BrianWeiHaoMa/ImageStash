using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The tag field's suggestions (G17, N6): the existing tags containing the word being typed, ignoring case and its
/// leading <c>#</c>s, those starting with it first, then by spelling, a few at most, never a tag the field shows and
/// never a name; a chosen suggestion takes the place of the word being typed; the list of every tag keeps the store's
/// order and marks the tags its owner says every item carries; the control itself, given keys with no window: a
/// suggestion is lit only while Enter would take it (G17).
/// </summary>
[TestClass]
public sealed class TagFieldTests
{
    [TestMethod]
    public void TheExistingTagsContainingTheWordComeThoseStartingWithItFirst()
    {
        string[] existing = ["Scream", "reaction", "carpet", "Are"];

        CollectionAssert.AreEqual(new[] { "reaction", "Are", "Scream" }, TagField.SuggestionsFor("re", existing, []).ToList(), "contains, not only starts with: tags are search words (G17)");
        CollectionAssert.AreEqual(new[] { "reaction", "Are", "Scream" }, TagField.SuggestionsFor("RE", existing, []).ToList(), "ignoring case");
        CollectionAssert.AreEqual(new[] { "reaction", "Are", "Scream" }, TagField.SuggestionsFor("##re", existing, []).ToList(), "a tag drops its leading #s, so the word does too");
        CollectionAssert.AreEqual(new[] { "carpet" }, TagField.SuggestionsFor("funny car", existing, []).ToList(), "the word being typed is the last one");
    }

    [TestMethod]
    public void TheListHoldsEveryExistingTagInTheStoresOrderTheOnesEveryItemCarriesMarked()
    {
        var listed = TagField.Listed(["reaction", "cat", "Dog"], ["DOG", "cat"]);

        CollectionAssert.AreEqual(
            new[] { new TagPickerEntry("reaction", false), new TagPickerEntry("cat", true), new TagPickerEntry("Dog", true) },
            listed.ToList(),
            "every tag, not the suggestions' few, and a tag every item carries marked ignoring case (G17)");
    }

    [TestMethod]
    public void NothingTypedOrAWordJustEndedSuggestsNothing()
    {
        string[] existing = ["reaction", "carpet"];

        Assert.IsEmpty(TagField.SuggestionsFor("", existing, []));
        Assert.IsEmpty(TagField.SuggestionsFor("#", existing, []));
        Assert.IsEmpty(TagField.SuggestionsFor("reaction ", existing, []), "after a space a new word has not started");
        Assert.IsEmpty(TagField.SuggestionsFor("zzz", existing, []));
    }

    [TestMethod]
    public void AtMostAFewShow()
    {
        var existing = Enumerable.Range(0, 20).Select(i => $"tag{i:00}").ToList();

        CollectionAssert.AreEqual(new[] { "tag00", "tag01", "tag02", "tag03", "tag04" }, TagField.SuggestionsFor("tag", existing, []).ToList());
        Assert.HasCount(TagField.MostSuggestions, TagField.SuggestionsFor("ta", existing, []));
    }

    [TestMethod]
    public void ATagTheFieldShowsIsNeverSuggestedInAnySpelling()
    {
        string[] existing = ["cat", "catnip", "dog", "dove"];
        string[] shown = ["cat", "dog"];

        CollectionAssert.AreEqual(new[] { "catnip" }, TagField.SuggestionsFor("c", existing, shown).ToList(), "the item carries cat already (G17)");
        CollectionAssert.AreEqual(new[] { "dove" }, TagField.SuggestionsFor("d", existing, shown).ToList());
        CollectionAssert.AreEqual(new[] { "catnip" }, TagField.SuggestionsFor("CAT", existing, ["CAT", "Dog"]).ToList(), "shown in any spelling");
        Assert.IsEmpty(TagField.SuggestionsFor("cat", ["cat"], shown), "every match shown, nothing to suggest");
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

        var suggested = TagField.SuggestionsFor("re", counts.Select(count => count.Tag), []).ToList();

        Assert.AreEqual("red", red.Name, "an item whose name contains the word, carrying no tag");
        CollectionAssert.AreEqual(new[] { "reaction", "Scream" }, suggested, "the existing tags alone, the one starting with the word first (G17, N6)");
    }

    [TestMethod]
    public void TypingAfterDownDropsTheChoiceAndItsHighlightSoEnterCommitsWhatIsTyped() => OnControlThread(() =>
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
    public void DownThenEnterCommitsTheLitSuggestion() => OnControlThread(() =>
    {
        var (field, box) = Field("reaction");
        string? committed = null;
        field.Committed += text => committed = text;

        box.Text = "r";
        Press(box, Key.Down);
        Press(box, Key.Enter);

        Assert.AreEqual("reaction", committed, "the suggestion lit is the one Enter takes (G17)");
    });

    /// <summary>The one thread the controls of these checks live on: Avalonia binds its dispatcher to the first thread that builds a control, and checks run in parallel.</summary>
    private static readonly BlockingCollection<(Action Check, TaskCompletionSource Done)> ControlThread = StartControlThread();

    private static BlockingCollection<(Action Check, TaskCompletionSource Done)> StartControlThread()
    {
        var checks = new BlockingCollection<(Action Check, TaskCompletionSource Done)>();
        new Thread(() =>
        {
            foreach (var (check, done) in checks.GetConsumingEnumerable())
            {
                try
                {
                    check();
                    done.SetResult();
                }
                catch (Exception failed)
                {
                    done.SetException(failed);
                }
            }
        }) { IsBackground = true, Name = "TagField checks" }.Start();
        return checks;
    }

    private static void OnControlThread(Action check)
    {
        var done = new TaskCompletionSource();
        ControlThread.Add((check, done));
        done.Task.GetAwaiter().GetResult();
    }

    /// <summary>A tag field with <paramref name="existing"/> to suggest, built with no window, and its text box.</summary>
    private static (TagField Field, TextBox Box) Field(params string[] existing)
    {
        var field = new TagField();
        field.Show([], existing, []);
        return (field, field.GetVisualDescendants().OfType<TextBox>().Single());
    }

    private static void Press(TextBox box, Key key) => box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static bool Lit(TagField field, string suggestion) =>
        field.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == suggestion).Classes.Contains("accent");
}
