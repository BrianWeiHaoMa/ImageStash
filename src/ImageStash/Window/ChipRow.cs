using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
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
/// that does not fit whole, with Every tag at the right edge when a live tag cannot be clicked in the strip.
/// Recent, Favorites and All clear the search and show that scope; tag choices change the search without moving chips.
/// All is lit during a search, beside the tags that are words of it. Tiles dragged out of the window and
/// dropped on a tag chip get the tag, on Favorites the flag (G17); any other drag over a chip is the window's drop, as
/// anywhere else in it (G11). Chips take no focus, so Tab goes from the box to the grid (G7). Only the tag chips that
/// could fit are made, however many tags the stash holds, so the summon stays within its second (G1).
/// </summary>
public sealed class ChipRow : Panel
{
    private const double Spacing = 6;

    /// <summary>A tag chip's padding each side, where the chip is built and where the row measures and counts chips.</summary>
    private const double TagChipPadding = 10;

    /// <summary>A scope chip's width, its glyph centred in it: compact, so the row leaves room for tags.</summary>
    private const double ScopeChipWidth = 36;

    /// <summary>The narrowest a tag chip can be, its padding alone, which bounds how many could ever fit in a width.</summary>
    internal const double NarrowestChip = 2 * TagChipPadding;

    /// <summary>The line between the scopes and the tag chips, and the room it takes each side.</summary>
    private const double SeparatorWidth = 1, SeparatorMargin = 2;

    /// <summary>
    /// Recent, Favorites and All, each shown as its Segoe Fluent Icons glyph (F10), a clock, the favorite star outlined as
    /// a scope rather than a flag, a grid, and called by G4's name in its tooltip and to a screen reader.
    /// </summary>
    private static readonly (Scope Scope, string Name, string Glyph)[] ScopeFaces =
    [
        (Scope.Recent, "Recent", ""),
        (Scope.Favorites, "Favorites", FavoriteStar.Outlined),
        (Scope.All, "All", ""),
    ];

    private readonly Listing listing;
    private readonly Organising organising;
    private readonly PressOnce? heldKeys;
    private readonly Func<OwnDrag?> ownDrag;
    private readonly (Button Chip, Scope Scope)[] scopes;
    private readonly List<(Button Chip, string Tag)> tagChips = [];

    /// <summary>The chip that lists every tag anchors its popup at the row's right edge.</summary>
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
    private IDisposable? ring;
    private Button? ringedChip;

    /// <param name="ownDrag">The window's own drag in progress, or null.</param>
    public ChipRow(Listing listing, Organising organising, Func<OwnDrag?> ownDrag, PressOnce? heldKeys = null)
    {
        this.listing = listing;
        this.organising = organising;
        this.ownDrag = ownDrag;
        this.heldKeys = heldKeys;
        picker.ConfigureTagManagement(listing, organising, heldKeys);
        // What does not fit is laid beyond the right edge: clipped, it is neither seen nor reached.
        ClipToBounds = true;
        scopes = ScopeFaces.Select(face => (ScopeChip(face.Name, face.Glyph, face.Scope), face.Scope)).ToArray();
        foreach (var (chip, scope) in scopes)
        {
            Children.Add(chip);
            if (scope == Scope.Favorites)
            {
                TakesOwnDrag(chip, tag: null);
            }
        }

        everyTag = EveryTagChip();
        everyTag.Click += (_, _) => _ = ToggleEveryTag();
        picker.Closed += (_, _) => InvalidateMeasure();
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
        Fit(finalSize.Width);
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

    private bool Shown(Control child) => ReferenceEquals(child, everyTag) ? everyTagShown : Children.IndexOf(child) < fitting;

    /// <summary>
    /// As many whole tag chips as fit, with Every tag when a live tag is hidden or unavailable for filtering.
    /// </summary>
    private void Fit(double width)
    {
        // Reserve the picker even before it first appears, so adding a tag never evicts an existing target.
        var besidePicker = width - Spacing - everyTag.DesiredSize.Width;
        fitting = Fitting(besidePicker);
        var selectable = tagChips.Take(Math.Max(0, fitting - scopes.Length - 1))
            .Count(entry => !entry.Chip.Classes.Contains(LookStyles.FilterUnavailable));
        everyTagShown = picker.IsOpen || listing.PickerTagsNeedRetry || selectable < listing.Tags.Count;
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

    /// <summary>Updates the tag places, their availability and the open list of live tags (G4, G17).</summary>
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
        if (picker.IsOpen && (listing.PickerTagsStale || listing.Tags.Count == 0))
        {
            picker.Close();
        }
        else if (picker.IsOpen)
        {
            picker.Show(EveryTag());
        }
    }

    private void LightTagChips()
    {
        var lit = listing.LitTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var searching = listing.IsSearch;
        for (var i = 0; i < tagChips.Count; i++)
        {
            var (chip, tag) = tagChips[i];
            var live = listing.Chips[i].Items > 0;
            var marked = lit.Contains(tag);
            var available = listing.CanFilterTag(tag, searching, marked);
            // A live chip keeps receiving drags even while its filter action is unavailable.
            chip.IsEnabled = live;
            chip.Opacity = available ? 1 : 0.45;
            chip.Classes.Set(LookStyles.FilterUnavailable, !available);
            chip.Classes.Set("accent", marked);
            if (!live)
            {
                EndRing(chip);
            }
        }
    }

    /// <summary>Every tag, the commonest first, whatever the query, the lit ones marked (G4); made when the list opens, not at every load (G1).</summary>
    private IReadOnlyList<TagPickerEntry> EveryTag() => TagPickerEntry.FromCounts(listing.Tags, listing.LitTags);

    private async Task ToggleEveryTag()
    {
        if (picker.IsOpen)
        {
            picker.Close();
            return;
        }

        if (await ReadEveryTag() && everyTag.IsEffectivelyVisible && !picker.IsOpen && listing.Tags.Count > 0)
        {
            picker.Open(everyTag, EveryTag(), tag => _ = PickEveryTag(tag));
        }
    }

    private async Task PickEveryTag(string tag)
    {
        var revision = listing.TagChoiceRevision;
        if (await ReadEveryTag() && revision == listing.TagChoiceRevision)
        {
            await listing.ChooseTag(tag, fromPicker: true);
        }
    }

    private async Task<bool> ReadEveryTag()
    {
        var store = listing.Store;
        if (listing.PickerTagsStale)
        {
            await listing.Refresh();
            await listing.Settle();
        }

        return store == listing.Store && !listing.PickerTagsStale;
    }

    /// <summary>
    /// The tag chips for the first of <see cref="chipTags"/>: one more than the widest the row can be now could hold, at
    /// the narrowest a chip can be, so the first that does not fit is among them and the row shows every one that does.
    /// </summary>
    private void MakeTagChips()
    {
        madeFor = Widest();
        var made = Math.Min(chipTags.Count, (int)(madeFor / (NarrowestChip + Spacing)) + 1);
        var keep = 0;
        while (keep < made && keep < tagChips.Count && tagChips[keep].Tag == chipTags[keep])
        {
            keep++;
        }

        if (tagChips.Count > keep)
        {
            foreach (var (chip, _) in tagChips.Skip(keep))
            {
                EndRing(chip);
            }

            Children.RemoveRange(scopes.Length + 1 + keep, tagChips.Count - keep);
            tagChips.RemoveRange(keep, tagChips.Count - keep);
        }

        var hasSeparator = Children.Count > scopes.Length + 1;
        if (made == 0 && hasSeparator)
        {
            Children.RemoveAt(scopes.Length);
        }
        else if (made > 0 && !hasSeparator)
        {
            var separator = new Border { Width = SeparatorWidth, Height = 20, Margin = new Thickness(SeparatorMargin, 0) };
            separator.Bind(Border.BackgroundProperty, separator.GetResourceObservable(LookKeys.Divider));
            Children.Insert(scopes.Length, separator);
        }

        foreach (var tag in chipTags.Skip(keep).Take(made - keep))
        {
            var chip = TagChip(tag);
            tagChips.Add((chip, tag));
            Children.Insert(Children.Count - 1, chip);
        }

        LightTagChips();
    }

    /// <summary>The widest the row can be now: the widest it was offered, or the window's, which holds it.</summary>
    private double Widest() => Math.Max(widest, TopLevel.GetTopLevel(this)?.ClientSize.Width ?? 0);

    /// <summary>
    /// A scope chip, its glyph alone on its face and its name in its tooltip and to a screen reader, which is how a check
    /// or a script finds it; its click clears the search and shows the scope (G4). The glyph takes the chip's words
    /// colour, so a lit chip reads as lit in either look as a chip of words does (G5, F10).
    /// </summary>
    private Button ScopeChip(string name, string glyph, Scope scope)
    {
        var chip = Chip(IconGlyph.Of(glyph, 16), LookStyles.ScopeChip, padding: 0);
        chip.Width = ScopeChipWidth;
        chip.HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(chip, name);
        ToolTipRule.Set(chip, $"{name} ({StashKeys.ScopeHint(scope)})");
        chip.Click += (_, _) => _ = listing.ShowScope(scope);
        return chip;
    }

    /// <summary>
    /// The chip that lists every tag: a tag glyph and a chevron, saying it opens the tags, about as wide as the "…" it
    /// replaced so the row keeps its room for tag chips (G4); "Every tag" in its tooltip and to a screen reader.
    /// </summary>
    private static Button EveryTagChip()
    {
        var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { IconGlyph.Of("", 14), IconGlyph.Of("", 10) } };
        var chip = Chip(face, LookStyles.ScopeChip, padding: 8);
        AutomationProperties.SetName(chip, "Every tag");
        ToolTipRule.Set(chip, "Every tag");
        return chip;
    }

    /// <summary>A tag chip: its click adds the tag as stored to the search, and a lit chip's click takes its word out (G4).</summary>
    private Button TagChip(string tag)
    {
        var chip = Chip("#" + tag, LookStyles.TagChip, TagChipPadding);
        chip.Click += (_, _) => _ = listing.ChooseTag(tag);
        chip.ContextMenu = new TagManagementMenu(chip, listing, organising, () => listing.LiveTag(tag), heldKeys);
        TakesOwnDrag(chip, tag);
        return chip;
    }

    /// <summary>A chip of the row, dressed by the take as its <paramref name="kind"/> of chip (<see cref="LookStyles"/>).</summary>
    private static Button Chip(object content, string kind, double padding)
    {
        var chip = new Button { Content = content, Height = 32, Padding = new Thickness(padding, 0), VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
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

            e.DragEffects = chip.IsEnabled && e.DragEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (e.DragEffects == DragDropEffects.Copy && ringedChip != chip)
            {
                if (ringedChip is { } before)
                {
                    EndRing(before);
                }

                // The drag ring must not grow a tag past the space reserved for its target.
                if (tag is not null && chip.DesiredSize.Width > 0)
                {
                    chip.Width = chip.DesiredSize.Width;
                }

                chip.BorderThickness = new Thickness(2);
                ring = chip.Bind(Button.BorderBrushProperty, chip.GetResourceObservable(LookKeys.AccentRing));
                ringedChip = chip;
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
            e.Handled = true;
            e.DragEffects = chip.IsEnabled ? DragDropEffects.Copy : DragDropEffects.None;
            if (chip.IsEnabled)
            {
                _ = organising.DropOnChip(drag.Items, tag);
            }
        });
    }

    private void EndRing(Button chip)
    {
        if (ringedChip != chip)
        {
            return;
        }

        ring?.Dispose();
        ring = null;
        ringedChip = null;
        if (chip.Content is string)
        {
            chip.ClearValue(Button.WidthProperty);
        }

        chip.ClearValue(Button.BorderBrushProperty);
        chip.ClearValue(Button.BorderThicknessProperty);
    }
}
