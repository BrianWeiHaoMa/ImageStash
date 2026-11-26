using Avalonia;
using Avalonia.Controls;

namespace ImageStash.Window;

/// <summary>
/// The footer's row and what gives way in it when it cannot hold everything whole (G8, G35, G33): Enter's hint, or a
/// copy's confirmation in its place, then Ctrl+C's hint and the paused line on the left; the count, then the busy state
/// against the right edge, which stays put as the selection and its hints come and go; only a row too narrow for both
/// narrows the busy state to make room for Enter's hint. Each piece
/// measured whole first; then, until the row holds them, the paused line gives way whole, then Ctrl+C's hint whole, then
/// the busy words down to their opening (the bar and the words naming the act), then the count whole (not for a copy's
/// confirmation), then Enter's hint or the confirmation is cut short, and last the busy state's opening. A cut piece trims its own words with an ellipsis; nothing is drawn
/// over another piece or outside the row.
/// </summary>
internal sealed class FooterFit : Panel
{
    /// <summary>The space between Enter's hint, Ctrl+C's and the paused line, and between the left pieces and the right.</summary>
    public const double Gap = 16;

    /// <summary>The space between the count and the busy state.</summary>
    public const double CountGap = 12;

    /// <summary>
    /// The width of the busy state kept before Enter's hint is cut: its 48-wide bar, the space after it, and about the
    /// first two words of its words, which name the act ("Adding 12 of…", "Moving your stash…", "Copying “cat”…").
    /// </summary>
    public const double BusyOpening = 160;

    private readonly Slot lead;
    private readonly Slot copy;
    private readonly Slot paused;
    private readonly Slot count;
    private readonly Slot busy;
    private Widths given = new(0, 0, 0, 0, 0);
    private bool countGivesWay = true;

    public FooterFit(Control lead, Control copy, Control paused, Control count, Control busy)
    {
        this.lead = new Slot(this, lead);
        this.copy = new Slot(this, copy);
        this.paused = new Slot(this, paused);
        this.count = new Slot(this, count);
        this.busy = new Slot(this, busy);
        Pieces = [lead, copy, paused, count, busy];
        ClipToBounds = true;
        Children.AddRange([this.lead, this.copy, this.paused, this.count, this.busy]);
    }

    /// <summary>Enter's hint (or the confirmation), Ctrl+C's hint, the paused line, the count and the busy state.</summary>
    public IReadOnlyList<Control> Pieces { get; }

    /// <summary>The widths the pieces were given at the last layout, zero for one not shown.</summary>
    public Widths Given => given;

    /// <summary>
    /// Whether the count gives way before the left piece is cut: so for Enter's hint, which names where Enter goes (G8);
    /// not for a copy's confirmation, whose long name is cut short before the count instead (G10).
    /// </summary>
    public bool CountGivesWay
    {
        get => countGivesWay;
        set
        {
            if (value != countGivesWay)
            {
                countGivesWay = value;
                InvalidateMeasure();
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = availableSize.Height;
        var whole = new Widths(Whole(lead, height), Whole(copy, height), Whole(paused, height), Whole(count, height), Whole(busy, height));
        given = Fit(availableSize.Width, whole, countGivesWay);
        Measure(lead, given.Lead, height);
        Measure(copy, given.Copy, height);
        Measure(paused, given.Paused, height);
        Measure(count, given.Count, height);
        Measure(busy, given.Busy, height);
        var tallest = Children.Max(child => child.IsVisible ? child.DesiredSize.Height : 0);
        return new Size(double.IsInfinity(availableSize.Width) ? Total(given) : availableSize.Width, tallest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var (piece, width) in new[] { (lead, given.Lead), (copy, given.Copy), (paused, given.Paused) })
        {
            x = Place(piece, width, x, finalSize.Height, Gap);
        }

        var right = finalSize.Width;
        foreach (var (piece, width) in new[] { (busy, given.Busy), (count, given.Count) })
        {
            if (width > 0)
            {
                right -= width;
                piece.Arrange(new Rect(right, 0, width, finalSize.Height));
                right -= CountGap;
            }
            else
            {
                piece.Arrange(default);
            }
        }

        return finalSize;
    }

    /// <summary>
    /// What each piece is given in a row <paramref name="room"/> wide, from its <paramref name="whole"/> width, zero for
    /// one not shown: the order in which the pieces give way, as this class says, the count kept whole unless
    /// <paramref name="countGivesWay"/>.
    /// </summary>
    public static Widths Fit(double room, Widths whole, bool countGivesWay = true)
    {
        var fit = whole;
        if (Total(fit) <= room)
        {
            return fit;
        }

        fit = fit with { Paused = 0 };
        if (Total(fit) <= room)
        {
            return fit;
        }

        fit = fit with { Copy = 0 };
        if (Total(fit) <= room)
        {
            return fit;
        }

        var opening = Math.Min(whole.Busy, BusyOpening);
        fit = fit with { Busy = opening };
        if (Total(fit) <= room)
        {
            return fit with { Busy = opening + room - Total(fit) };
        }

        if (countGivesWay)
        {
            fit = fit with { Count = 0 };
            if (Total(fit) <= room)
            {
                return fit with { Busy = Math.Min(whole.Busy, opening + room - Total(fit)) };
            }
        }

        fit = fit with { Lead = Math.Max(0, fit.Lead - (Total(fit) - room)) };
        if (Total(fit) <= room)
        {
            return fit;
        }

        return fit with { Busy = Math.Max(0, fit.Busy - (Total(fit) - room)) };
    }

    /// <summary>The width the pieces take side by side with the gaps between those shown.</summary>
    public static double Total(Widths widths)
    {
        double[] left = [widths.Lead, widths.Copy, widths.Paused];
        double[] right = [widths.Count, widths.Busy];
        var shownLeft = left.Where(width => width > 0).ToList();
        var shownRight = right.Where(width => width > 0).ToList();
        return shownLeft.Sum() + Gap * Math.Max(0, shownLeft.Count - 1)
            + shownRight.Sum() + CountGap * Math.Max(0, shownRight.Count - 1)
            + (shownLeft.Count > 0 && shownRight.Count > 0 ? Gap : 0);
    }

    // The piece itself, since its slot is hidden while it has given way.
    private static double Whole(Slot slot, double height)
    {
        var piece = slot.Child!;
        piece.Measure(new Size(double.PositiveInfinity, height));
        return piece.IsVisible ? piece.DesiredSize.Width : 0;
    }

    // A piece given no room is hidden whole, so neither the eye nor whatever reads names finds it (G33's Resume included);
    // the row's next layout settles at once, the same widths given again.
    private static void Measure(Slot slot, double width, double height)
    {
        slot.IsVisible = width > 0;
        slot.Measure(new Size(width, height));
    }

    private static double Place(Control piece, double width, double x, double height, double gap)
    {
        if (width <= 0)
        {
            piece.Arrange(default);
            return x;
        }

        piece.Arrange(new Rect(x, 0, width, height));
        return x + width + gap;
    }

    /// <summary>
    /// A piece's place in the row, clipping it to the width it was given, and hidden while it has given way, so not even a
    /// part of fixed size inside it (a key cap, the busy bar) is drawn, hit or read.
    /// </summary>
    private sealed class Slot : Decorator
    {
        public Slot(FooterFit row, Control piece)
        {
            Child = piece;
            ClipToBounds = true;
            // Layout passes over a hidden slot, so a piece coming to show inside one would never reach the row: tell it.
            piece.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                {
                    row.InvalidateMeasure();
                }
            };
        }
    }

    /// <summary>The widths of Enter's hint (or the confirmation), Ctrl+C's hint, the paused line, the count and the busy state.</summary>
    public readonly record struct Widths(double Lead, double Copy, double Paused, double Count, double Busy);
}
