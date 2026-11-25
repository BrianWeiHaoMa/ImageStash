using Avalonia.Input;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// <c>Ctrl+V</c> with the caret in a text or caption-bar layer's open text, over a real store and a real edit, the
/// clipboard preset (G13, G25): the words alone go in at the caret, in place of the selection, as typed text (a Meme's
/// uppercased, G21), each line of the clipboard a line of the layer, and become part of the edit's typing; files, a
/// picture or nothing add no item and get the notice that the clipboard holds no text to paste; one read per press
/// (N9, R5, <see cref="PressOnce"/>); a late answer lands only in the opening the key was pressed in; a whole-stash act
/// waiting for the edit does not stop it; outside the editor's text and hex fields the ordinary window paste applies.
/// </summary>
[TestClass]
public sealed class LayerTextClipboardPasteTests
{
    private const string NoText = "Paste failed: the clipboard, it holds no text to paste";

    /// <summary>The picture each check edits, 64 × 48, room for two layers.</summary>
    private static readonly byte[] Picture = MediaFixtures.DistinctPng(0x808080, 64, 48);

    [TestMethod]
    public async Task WordsGoInAtTheCaretOfAMemesTextUppercasedWithNothingAddedOrSaid()
    {
        using var listed = await ListedStash.Open();
        var (session, field) = await TextInEdit(listed, TextKind.Meme, "one three");
        field.CaretAt("ONE ".Length);
        listed.Clipboard.Reading = Words("two ß ");

        Assert.AreEqual(KeyAction.Paste, await PressCtrlV(listed));

        Assert.AreEqual("ONE TWO SS THREE", LayerText(session), "as typed on a Meme (G21), at the caret (G25)");
        Assert.AreEqual(("ONE TWO SS THREE", 11, 11), (field.Text, field.SelectionStart, field.SelectionEnd), "the caret after the letters the words became");
        Assert.IsEmpty(listed.Notices.Shown, "no notice (G13)");
        Assert.HasCount(1, await listed.Stash.All(), "nothing added (G13)");
        Assert.AreEqual(1, listed.Clipboard.Reads, "one read through the seam (N9, R5)");
        Assert.IsTrue(session.Typing, "the text stays in edit");
    }

    [TestMethod]
    public async Task InALabelAndACaptionBarTheWordsGoInAsCopiedInPlaceOfTheSelection()
    {
        using var listed = await ListedStash.Open();
        var (label, field) = await TextInEdit(listed, TextKind.Label, "one three");
        field.SelectionStart = "one three".Length;
        field.SelectionEnd = "one ".Length;
        listed.Clipboard.Reading = Words("Two ß");

        await PressCtrlV(listed);

        Assert.AreEqual("one Two ß", LayerText(label), "the selection, made either way, replaced by the words as copied");
        Assert.AreEqual(9, field.SelectionStart);

        using var captioned = await ListedStash.Open();
        var (bar, _) = await TextInEdit(captioned, TextKind.CaptionBar, "");
        captioned.Clipboard.Reading = Words("Two ß");
        await PressCtrlV(captioned);

        Assert.AreEqual("Two ß", ((CaptionBarLayer)bar.SelectedLayer!).Text, "a caption bar's text takes them as copied");
    }

    [TestMethod]
    public async Task EachLineOfTheClipboardIsALineOfTheLayerStoredAsEnterStoresIt()
    {
        using var listed = await ListedStash.Open();
        var (session, _) = await TextInEdit(listed, TextKind.Label, "");
        listed.Clipboard.Reading = Words("top\r\nmiddle\rbottom\n\tend\u0007");

        await PressCtrlV(listed);

        Assert.AreEqual("top\nmiddle\nbottom\n end", LayerText(session), "Windows' line ends and a lone return as Enter's line; a tab a space; a bell left out");
    }

    [TestMethod]
    public async Task WordsThatTypeNothingLeaveTheTextAndItsSelectionAsTheyWere()
    {
        using var listed = await ListedStash.Open();
        var (session, field) = await TextInEdit(listed, TextKind.Label, "one three");
        field.SelectionStart = "one ".Length;
        field.SelectionEnd = "one three".Length;
        listed.Clipboard.Reading = Words("\u007F\u0007");

        await PressCtrlV(listed);

        Assert.AreEqual("one three", LayerText(session), "nothing a key could type, so nothing typed over the selection (G13)");
        Assert.AreEqual((0, 4, 9), (field.Puts, field.SelectionStart, field.SelectionEnd), "the selection kept");
        Assert.IsEmpty(listed.Notices.Shown, "the clipboard held text: no notice");
    }

    [TestMethod]
    public async Task APictureFilesOrNothingAddNoItemChangeNoTextAndGetTheNoticeWhileALinkAloneIsWords()
    {
        using var listed = await ListedStash.Open();
        var (session, field) = await TextInEdit(listed, TextKind.Label, "so");
        var cases = new (string Held, Outcome<ClipboardReading> Reading)[]
        {
            ("a picture", Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false, "words"))),
            ("a bitmap", Outcome<ClipboardReading>.Of(new ClipboardReading([], null, new MediaFrame(1, 1, 0, new byte[4]), false))),
            ("files", Outcome<ClipboardReading>.Of(new ClipboardReading([ListedStash.File(MediaFixtures.Path("still.png"))], null, null, false, "words"))),
            ("nothing", Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false))),
            ("empty words", Words("")),
        };

        foreach (var (held, reading) in cases)
        {
            listed.Clipboard.Reading = reading;
            await PressCtrlV(listed);

            Assert.AreEqual(NoText, listed.Notices.Shown.Single().Text, $"{held}: the notice (G13)");
            Assert.AreEqual("so", LayerText(session), $"{held}: the text unchanged");
            Assert.AreEqual(0, field.Puts, $"{held}: nothing put");
            Assert.HasCount(1, await listed.Stash.All(), $"{held}: nothing added (G13)");
        }

        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, OnlyLink: true, "https://example.com/cat.gif"));
        await PressCtrlV(listed);

        Assert.AreEqual("sohttps://example.com/cat.gif", LayerText(session), "a web address alone is words there, nothing fetched (N2)");
        Assert.HasCount(1, await listed.Stash.All());
        Assert.AreEqual(cases.Length + 1, listed.Clipboard.Reads, "one read per press");
    }

    [TestMethod]
    public async Task AHeldCtrlVPastesOnce()
    {
        using var listed = await ListedStash.Open();
        var (session, _) = await TextInEdit(listed, TextKind.Label, "");
        listed.Clipboard.Reading = Words("so");

        Assert.AreEqual(KeyAction.Paste, await listed.Press(Key.V, KeyModifiers.Control, KeyPlace.Field));
        Assert.AreEqual(KeyAction.Held, await listed.Press(Key.V, KeyModifiers.Control, KeyPlace.Field), "Windows repeating the held key (G13)");

        Assert.AreEqual("so", LayerText(session));
        Assert.AreEqual(1, listed.Clipboard.Reads);
    }

    [TestMethod]
    public async Task PastedWordsArePartOfTheEditsTypingUndoneWithItAndRenderedBySave()
    {
        using var listed = await ListedStash.Open();
        var (session, _) = await TextInEdit(listed, TextKind.Meme, "so ");
        listed.Clipboard.Reading = Words("true");

        await PressCtrlV(listed);
        session.EndTyping();

        Assert.AreEqual("SO TRUE", ((TextLayer)session.Layers.Layers.Single()).Text, "Ctrl+Enter leaves the words in the layer (G25)");
        session.Undo();
        Assert.IsEmpty(session.Layers.Layers, "one Ctrl+Z takes the typing back, pasted words and the click that added the layer included (G25)");

        session.Click(10, 5);
        listed.LayerTextCaret = TestLayerText.Over(session);
        listed.Keys.Released(Key.V);
        await PressCtrlV(listed);
        session.EndTyping();
        var item = (await listed.Stash.All()).Single();
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);

        var saved = (await listed.Stash.All()).Single();
        Assert.IsTrue(saved.Edited && saved.RenderedRevision > item.RenderedRevision, "a finished picture rendered from the layers (G21, G26)");
        var unedited = listed.Stash.Outside("unedited.png");
        File.WriteAllBytes(unedited, Picture);
        var (original, finished) = (MediaFixtures.FirstFrame(unedited), MediaFixtures.FirstFrame(saved.FilePath));
        Assert.AreEqual((original.Width, original.Height), (finished.Width, finished.Height));
        CollectionAssert.AreNotEqual(original.Pixels, finished.Pixels, "the lettering drawn into the finished picture (G21)");
        Assert.IsTrue((await listed.Editing.Open(saved, EditOrigin.Preview)).Succeeded);
        Assert.AreEqual("TRUE", ((TextLayer)listed.Editing.Session!.Layers.Layers.Single()).Text, "the pasted words saved as the layer's text, live at the next Edit (G26)");
    }

    [TestMethod]
    public async Task ALateAnswerGoesNowhereOnceTheTextClosedOrClosedAndOpenedAgainOrAnotherLayerOrTheEditorWent()
    {
        var ways = new (string Way, Action<ListedStash, EditorSession> Meanwhile)[]
        {
            ("the text closed", (_, session) => session.EndTyping()),
            ("closed and opened again", (_, session) => { session.EndTyping(); session.BeginTyping(); }),
            ("another layer in edit", (_, session) => { session.EndTyping(); session.Deselect(); session.Click(10, 40); }),
            ("the editor gone", (listed, _) => Assert.IsTrue(listed.Editing.Cancel())),
        };

        foreach (var (way, meanwhile) in ways)
        {
            using var listed = await ListedStash.Open();
            var (session, field) = await TextInEdit(listed, TextKind.Label, "so");
            var held = new TaskCompletionSource();
            listed.Clipboard.HoldRead = held.Task;
            listed.Clipboard.Reading = Words(" true");

            var pressing = PressCtrlV(listed);
            meanwhile(listed, session);
            held.SetResult();
            await pressing;

            Assert.AreEqual(0, field.Puts, $"{way}: the words go nowhere");
            Assert.AreEqual("so", ((TextLayer)session.Layers.Layers[0]).Text, $"{way}: the text as it was");
            Assert.IsFalse(listed.Notices.Shown.Any(notice => notice.Text.StartsWith("Paste")), $"{way}: nothing said of the paste");
            Assert.HasCount(1, await listed.Stash.All(), $"{way}: nothing added");
        }
    }

    [TestMethod]
    public async Task ALateAnswerToATextStillInEditGoesInAtItsCaretAsItThenIsAndOneWithoutWordsGetsTheNotice()
    {
        using var listed = await ListedStash.Open();
        var (session, field) = await TextInEdit(listed, TextKind.Label, "so true");
        var held = new TaskCompletionSource();
        listed.Clipboard.HoldRead = held.Task;
        listed.Clipboard.Reading = Words("very ");

        var pressing = PressCtrlV(listed);
        field.CaretAt("so ".Length);
        held.SetResult();
        await pressing;

        Assert.AreEqual("so very true", LayerText(session), "at the caret as it is when the read returns");

        held = new TaskCompletionSource();
        listed.Clipboard.HoldRead = held.Task;
        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false));
        listed.Keys.Released(Key.V);
        pressing = PressCtrlV(listed);
        session.EndTyping();
        held.SetResult();
        await pressing;

        Assert.AreEqual(NoText, listed.Notices.Shown.Single().Text, "late or not, a picture gets the notice and is not imported");
        Assert.HasCount(1, await listed.Stash.All());
    }

    [TestMethod]
    public async Task AReadThatFailsIsNamedAndChangesNothing()
    {
        using var listed = await ListedStash.Open();
        var (session, _) = await TextInEdit(listed, TextKind.Label, "so");
        listed.Clipboard.Reading = new Failure(ClipboardPaste.Action, "the clipboard", HeldClipboard.Reason);

        await PressCtrlV(listed);

        Assert.AreEqual("Paste failed: the clipboard, another program is holding the clipboard open", listed.Notices.Shown.Single().Text, "G35, E3");
        Assert.AreEqual("so", LayerText(session));
    }

    [TestMethod]
    public async Task WhileAnExportWaitsForTheEditTheWordsStillGoIn()
    {
        using var listed = await ListedStash.Open();
        var (session, _) = await TextInEdit(listed, TextKind.Label, "so ");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(listed.Stash.Outside("mine.zip"));
        var exporting = listed.Transferring.Export(null!);
        await Handing.Eventually(() => listed.Listing.CannotStartReason is not null);
        listed.Clipboard.Reading = Words("true");

        await PressCtrlV(listed);

        Assert.AreEqual("so true", LayerText(session), "nothing is added, so nothing waits (State: Move)");
        Assert.IsEmpty(listed.Notices.Shown);
        session.EndTyping();
        listed.Turns.Act(() => Assert.IsTrue(listed.Editing.Cancel()));
        // The Cancel's notice offers its Undo, which the export waits for in turn (State: Move).
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        Assert.IsTrue((await exporting.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);
    }

    [TestMethod]
    public async Task WithFocusOutsideTheEditorFieldsCtrlVPastesAnImageWhileALayersTextStaysOpen()
    {
        using var listed = await ListedStash.Open();
        var (session, field) = await TextInEdit(listed, TextKind.Label, "so");
        listed.LayerTextCaret = null;
        listed.Clipboard.Reading = Words("ff0000");

        await listed.Press(Key.V, KeyModifiers.Control, KeyPlace.Editor);

        Assert.AreEqual("Paste failed: the clipboard, it holds no picture or file", listed.Notices.Shown.Single().Text, "the ordinary window paste (G13)");
        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false));
        listed.Keys.Released(Key.V);
        await listed.Press(Key.V, KeyModifiers.Control, KeyPlace.Editor);

        Assert.HasCount(2, await listed.Stash.All(), "a picture imported from there, as from the editor with no field focused (G13)");
        Assert.AreEqual("so", LayerText(session), "nothing in the layer");
        Assert.AreEqual(0, field.Puts);
        Assert.IsTrue(session.Typing);
    }

    /// <summary>The editor opened from the preview on a real picture, a layer of <paramref name="kind"/> added with <paramref name="typed"/> typed, its text in edit and the caret in it at its end.</summary>
    private static async Task<(EditorSession Session, TestLayerText Field)> TextInEdit(ListedStash listed, TextKind kind, string typed)
    {
        var item = await listed.Stash.ImportPicture("picture", "picture.png", Picture);
        await listed.Listing.ShowScope(Scope.All);
        listed.PreviewOpen = true;
        listed.PreviewItem = listed.Listing.Items.Single();
        Assert.AreEqual(KeyAction.Edit, await listed.Press(Key.E, KeyModifiers.Control, KeyPlace.Preview));
        var session = listed.Editing.Session!;
        Assert.AreEqual(item.Id, session.Item.Id);
        session.Text().Choose(kind);
        session.Click(10, 5);
        session.SetTypedText(typed);
        var field = TestLayerText.Over(session);
        listed.LayerTextCaret = field;
        return (session, field);
    }

    /// <summary><c>Ctrl+V</c> from a field as the window takes it, V let go first so the press acts.</summary>
    private static Task<KeyAction> PressCtrlV(ListedStash listed)
    {
        listed.Keys.Released(Key.V);
        return listed.Press(Key.V, KeyModifiers.Control, KeyPlace.Field);
    }

    private static string LayerText(EditorSession session) => session.SelectedLayer switch
    {
        TextLayer text => text.Text,
        CaptionBarLayer bar => bar.Text,
        var other => throw new AssertFailedException($"no text layer selected: {other}"),
    };

    private static Outcome<ClipboardReading> Words(string text) =>
        Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false, text));
}
