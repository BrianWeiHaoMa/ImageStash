using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using ImageStash.Look;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The window's own drag-out while it is in progress (G9): the items it carries, read when it started, and whether a
/// chip of the window took it. Each drag has its own, so a chip's take never outlives its drag (G10).
/// </summary>
public sealed class OwnDrag(IReadOnlyList<StashItem> items)
{
    public IReadOnlyList<StashItem> Items { get; } = items;

    /// <summary>A tag chip or Favorites took the drag: it landed nowhere outside the window, so it is no use (G10).</summary>
    public bool TakenByChip { get; set; }
}

/// <summary>
/// The chip row (G4, G5): Recent, Favorites and All, then the listing's tag chips in order, on one line, up to the first
/// that does not fit whole, none after it, and while the row cannot hold every tag a chip at its right edge opening the
/// list of them. Recent, Favorites and All clear the search and show that scope; a tag chip adds its tag to the search
/// box's words, in place of a typed word the tag contains, and a lit one takes its word out, so the box, the grid and the lit chips always agree. The lit chips are the
/// listing's: All while a query is typed, beside the tags that are words of it. Tiles dragged out of the window and
/// dropped on a tag chip get the tag, on Favorites the flag (G17); any other drag over a chip is the window's drop, as
/// anywhere else in it (G11). Chips take no focus, so Tab goes from the box to the grid (G7). Only the tag chips that
/// could fit are made, however many tags the stash holds, so the summon stays within its second (G1).
/// </summary>
public sealed class ChipRow : Panel
{
    private const double Spacing = 6;
    private const double ChipPadding = 14;

    /// <summary>The narrowest a tag chip can be, its padding alone, which bounds how many could ever fit in a width.</summary>
    private const double NarrowestChip = 2 * ChipPadding;

    private readonly Listing listing;
    private readonly Organising organising;
    private readonly Func<OwnDrag?> ownDrag;
    private readonly Action<string> search;
    private readonly (Button Chip, Scope Scope)[] scopes;
    private readonly List<(Button Chip, string Tag)> tagChips = [];

    /// <summary>The chip that lists every tag: always the row's last child, laid beyond the edge while it is not wanted, so the list open from it outlives its leaving the row (G4).</summary>
    private readonly Button everyTag;
    private readonly TagPicker picker = new();

    /// <summary>Every tag the listing's chips hold, in order; the tag chips are made for the first of them.</summary>
    private IReadOnlyList<string> chipTags = [];

    /// <summary>The width the tag chips were made for, and the widest the row has been offered.</summary>
    private double madeFor;
    private double widest;
    private bool makingMore;
    private int fitting;
    private bool everyTagShown;
    private readonly Dictionary<string, double> chipWidths = [];
    private IDisposable? ring;

    /// <param name="ownDrag">The window's own drag in progress, or null.</param>
    /// <param name="search">Puts text in the search box, which searches it.</param>
    public ChipRow(Listing listing, Organising organising, Func<OwnDrag?> ownDrag, Action<string> search)
    {
        this.listing = listing;
        this.organising = organising;
        this.ownDrag = ownDrag;
        this.search = search;
        // What does not fit is laid beyond the right edge: clipped, it is neither seen nor reached.
        ClipToBounds = true;
        scopes = [(ScopeChip("Recent", Scope.Recent), Scope.Recent), (ScopeChip("Favorites", Scope.Favorites), Scope.Favorites), (ScopeChip("All", Scope.All), Scope.All)];
        foreach (var (chip, _) in scopes)
        {
            Children.Add(chip);
        }

        TakesOwnDrag(scopes[1].Chip, tag: null);
        everyTag = Chip("…", LookStyles.ScopeChip);
        AutomationProperties.SetName(everyTag, "Every tag");
        ToolTipRule.Set(everyTag, "Every tag");
        everyTag.Click += (_, _) => ToggleEveryTag();
        Children.Add(everyTag);
        LogicalChildren.Add(picker);
        listing.Changed += Show;
        Show();
    }

    /// <summary>The list of every tag, while open (G4).</summary>
    public TagPicker Picker => picker;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width))
        {
            widest = Math.Max(widest, availableSize.Width);
        }

        // Wider than the chips were made for, with tags left: more are made after this pass, never during it.
        if (tagChips.Count < chipTags.Count && widest > madeFor && !makingMore)
        {
            makingMore = true;
            Dispatcher.UIThread.Post(() =>
            {
                makingMore = false;
                if (tagChips.Count < chipTags.Count && Widest() > madeFor)
                {
                    MakeTagChips();
                }
            });
        }

        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        Fit(availableSize.Width);
        var shown = Children.Where(Shown).ToList();
        return new Size(shown.Sum(child => child.DesiredSize.Width) + Spacing * Math.Max(0, shown.Count - 1), Children.Max(child => child.DesiredSize.Height));
    }

    /// <summary>
    /// The shown chips left to right, but the chip that lists every tag at the row's right edge, so it stays put as the
    /// chips before it come and go (G4); the hidden ones beyond the edge.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var shown = Shown(child);
            var y = (finalSize.Height - child.DesiredSize.Height) / 2;
            var at = !shown ? finalSize.Width + Spacing : ReferenceEquals(child, everyTag) ? Math.Max(x, finalSize.Width - child.DesiredSize.Width) : x;
            child.Arrange(new Rect(at, y, child.DesiredSize.Width, child.DesiredSize.Height));
            child.IsHitTestVisible = shown;
            x += shown ? child.DesiredSize.Width + Spacing : 0;
        }

        return finalSize;
    }

    /// <summary>
    /// Whether <paramref name="width"/> could not hold a chip for each of the stash's tags, the chips shown or not; it stops
    /// at the first tag past the width, so the cost is bounded by the row and not by the stash (G1, E4).
    /// </summary>
    private bool CannotHoldEveryTag(double width)
    {
        var used = scopes.Sum(scope => scope.Chip.DesiredSize.Width + Spacing);
        foreach (var count in listing.Tags)
        {
            used += TagChipWidth(count.Tag) + Spacing;
            if (used - Spacing > width)
            {
                return true;
            }
        }

        return false;
    }

    private double TagChipWidth(string tag)
    {
        if (!chipWidths.TryGetValue(tag, out var chipWidth))
        {
            using var words = new TextLayout("#" + tag, new Typeface(TextElement.GetFontFamily(this)), TextElement.GetFontSize(this), null);
            chipWidth = words.WidthIncludingTrailingWhitespace + NarrowestChip;
            chipWidths[tag] = chipWidth;
        }

        return chipWidth;
    }

    private bool Shown(Control child) => ReferenceEquals(child, everyTag) ? everyTagShown : Children.IndexOf(child) < fitting;

    /// <summary>
    /// What shows in <paramref name="width"/>: every tag chip when all of them fit; else as many as fit; and beside them,
    /// ending the row, itself whole, the chip that lists every tag whenever the row cannot hold every tag, under a query
    /// too, whose few chips may fit while the stash's other tags could not (G4).
    /// </summary>
    private void Fit(double width)
    {
        fitting = Fitting(width);
        everyTagShown = fitting < Children.Count - 1 || tagChips.Count < chipTags.Count || CannotHoldEveryTag(width);
        if (everyTagShown)
        {
            fitting = Fitting(width - Spacing - everyTag.DesiredSize.Width);
        }
    }

    /// <summary>
    /// How many children before the last chip show, in order: the three scopes always, then the separator and the tag
    /// chips up to the first that does not fit whole, so a lesser tag never shows where a greater one is missing (G4);
    /// the separator only beside a tag chip.
    /// </summary>
    private int Fitting(double width)
    {
        var count = 0;
        var used = 0.0;
        foreach (var child in Children.Take(Children.Count - 1))
        {
            var next = used + (count > 0 ? Spacing : 0) + child.DesiredSize.Width;
            if (count >= scopes.Length && next > width)
            {
                break;
            }

            used = next;
            count++;
        }

        return count == scopes.Length + 1 ? scopes.Length : count;
    }

    /// <summary>The lit chips, the open list's marks, and the tag chips again whenever the listing's tags changed (G17: a tag no item carries is gone).</summary>
    private void Show()
    {
        foreach (var (chip, scope) in scopes)
        {
            chip.Classes.Set("accent", listing.LitChip == scope);
        }

        var tags = listing.Chips.Select(count => count.Tag).ToList();
        if (!tags.SequenceEqual(chipTags))
        {
            chipTags = tags;
            MakeTagChips();
        }

        // The chips can stay the same while the stash's tags change, and with them whether the last chip shows.
        InvalidateMeasure();
        LightTagChips();
        if (picker.IsOpen)
        {
            picker.Show(EveryTag());
        }
    }

    private void LightTagChips()
    {
        var lit = listing.LitTags;
        foreach (var (chip, tag) in tagChips)
        {
            chip.Classes.Set("accent", lit.Contains(tag));
        }
    }

    /// <summary>Every tag, the commonest first, whatever the query, the lit ones marked (G4); made when the list opens, not at every load (G1).</summary>
    private List<TagPickerEntry> EveryTag()
    {
        var lit = listing.LitTags;
        return listing.Tags.Select(count => new TagPickerEntry(count.Tag, lit.Contains(count.Tag))).ToList();
    }

    private void ToggleEveryTag()
    {
        if (picker.IsOpen)
        {
            picker.Close();
        }
        else
        {
            picker.Open(everyTag, EveryTag(), ToggleWord);
        }
    }

    /// <summary>A chip's click, or a pick in the list: the tag added to the search as a word, or a lit one's word taken out (G4).</summary>
    private void ToggleWord(string tag) =>
        search(listing.LitTags.Contains(tag) ? ItemSearch.WithoutWord(listing.Query, tag) : ItemSearch.WithWord(listing.Query, tag));

    /// <summary>
    /// The tag chips for the first of <see cref="chipTags"/>: one more than the widest the row can be now could hold, at
    /// the narrowest a chip can be, so the first that does not fit is among them and the row shows every one that does.
    /// </summary>
    private void MakeTagChips()
    {
        Children.RemoveRange(scopes.Length, Children.Count - scopes.Length - 1);
        tagChips.Clear();
        madeFor = Widest();
        var made = Math.Min(chipTags.Count, (int)(madeFor / (NarrowestChip + Spacing)) + 1);
        var at = scopes.Length;
        if (made > 0)
        {
            var separator = new Border { Width = 1, Height = 20, Margin = new Thickness(2, 0) };
            separator.Bind(Border.BackgroundProperty, separator.GetResourceObservable(LookKeys.Divider));
            Children.Insert(at++, separator);
        }

        foreach (var tag in chipTags.Take(made))
        {
            var chip = TagChip(tag);
            tagChips.Add((chip, tag));
            Children.Insert(at++, chip);
        }

        LightTagChips();
    }

    /// <summary>The widest the row can be now: the widest it was offered, or the window's, which holds it.</summary>
    private double Widest() => Math.Max(widest, TopLevel.GetTopLevel(this)?.ClientSize.Width ?? 0);

    private Button ScopeChip(string label, Scope scope)
    {
        var chip = Chip(label, LookStyles.ScopeChip);
        chip.Click += (_, _) => _ = listing.ShowScope(scope);
        return chip;
    }

    /// <summary>A tag chip: its click adds the tag as stored to the search, and a lit chip's click takes its word out (G4).</summary>
    private Button TagChip(string tag)
    {
        var chip = Chip("#" + tag, LookStyles.TagChip);
        chip.Click += (_, _) => ToggleWord(tag);
        TakesOwnDrag(chip, tag);
        return chip;
    }

    /// <summary>A chip of the row, dressed by the take as its <paramref name="kind"/> of chip (<see cref="LookStyles"/>).</summary>
    private static Button Chip(string label, string kind)
    {
        var chip = new Button { Content = label, Height = 32, Padding = new Thickness(ChipPadding, 0), VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
        chip.Classes.Add(kind);
        return chip;
    }

    /// <summary>
    /// The chip takes the window's own drag: over it the drag is accepted as a copy and ringed; dropped, its items get
    /// <paramref name="tag"/>, or with none the favorite flag, set rather than toggled (G17). Nothing is imported and no
    /// use is recorded (G10, G11). Any other drag is left unhandled, for the window's drop target.
    /// </summary>
    private void TakesOwnDrag(Button chip, string? tag)
    {
        void Over(object? sender, DragEventArgs e)
        {
            if (ownDrag() is null)
            {
                return;
            }

            e.DragEffects = e.DragEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (ring is null)
            {
                chip.BorderThickness = new Thickness(2);
                ring = chip.Bind(Button.BorderBrushProperty, chip.GetResourceObservable(LookKeys.AccentRing));
            }
        }

        chip.AddHandler(DragDrop.DragEnterEvent, Over);
        chip.AddHandler(DragDrop.DragOverEvent, Over);
        chip.AddHandler(DragDrop.DragLeaveEvent, (_, _) => EndRing(chip));
        chip.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            EndRing(chip);
            if (ownDrag() is not { } drag)
            {
                return;
            }

            drag.TakenByChip = true;
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            _ = tag is null ? organising.SetFavorite(drag.Items, true) : organising.AddTag(drag.Items, tag);
        });
    }

    private void EndRing(Button chip)
    {
        ring?.Dispose();
        ring = null;
        chip.ClearValue(Button.BorderBrushProperty);
        chip.ClearValue(Button.BorderThicknessProperty);
    }
}
