using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Window;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The empty stash as the real window shows it, read on the UI thread as a person reads it: its lines of words, and its
/// rows, each its words and its key caps joined as a shortcut's text, or the words shown in their place, past the icon
/// leading it.
/// </summary>
public static class ShownEmptyStash
{
    /// <summary>The empty stash's view, found by its heading, or null while it does not show.</summary>
    public static Panel? View(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(block => block.IsEffectivelyVisible && block.Text == EmptyStashWaysIn.Heading)?.Parent as Panel;

    /// <summary>The view's lines of words outside its rows, in order.</summary>
    public static List<string> Lines(ShownWindow shown) => Shown(shown).Children.OfType<TextBlock>().Select(block => block.Text ?? "").ToList();

    /// <summary>The view's rows, in order.</summary>
    public static List<Button> Rows(ShownWindow shown) => Shown(shown).Children.OfType<Panel>().SelectMany(rows => rows.Children.OfType<Button>()).ToList();

    /// <summary>The row reading <paramref name="words"/>.</summary>
    public static Button Row(ShownWindow shown, string words) => Rows(shown).Single(row => Words(row) == words);

    /// <summary>Each row as "words · keys": its caps joined with <c>+</c>, or the words shown in their place.</summary>
    public static List<string> Ways(ShownWindow shown) => Rows(shown).Select(row => $"{Words(row)} · {Keys(row)}").ToList();

    /// <summary>The row's key caps, each a cap's border.</summary>
    public static List<Border> Caps(Button row) => ((Grid)row.Content!).Children.OfType<StackPanel>().SelectMany(caps => caps.Children.OfType<Border>()).ToList();

    private static Panel Shown(ShownWindow shown) => View(shown) ?? throw new AssertFailedException($"“{EmptyStashWaysIn.Heading}” is not shown");

    /// <summary>The row's words and its right part, in the order its columns lay them out, past the glyph of the look's icon font leading them.</summary>
    private static List<Control> Parts(Button row)
    {
        var parts = ((Grid)row.Content!).Children.OrderBy(Grid.GetColumn).ToList();
        return parts[0] is TextBlock { FontFamily.Name: "Segoe Fluent Icons" } ? parts.Skip(1).ToList() : parts;
    }

    private static string Words(Button row) => ((TextBlock)Parts(row)[0]).Text ?? "";

    private static string Keys(Button row) => Parts(row)[1] switch
    {
        StackPanel caps => string.Join("+", caps.Children.OfType<Border>().Select(cap => ((TextBlock)cap.Child!).Text)),
        TextBlock instead => instead.Text ?? "",
        var other => other.GetType().Name,
    };
}
