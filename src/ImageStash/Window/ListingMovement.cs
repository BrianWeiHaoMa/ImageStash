namespace ImageStash.Window;

public enum Direction
{
    Up,
    Down,
    Left,
    Right,
    First,
    Last,
    PageUp,
    PageDown,
}

public sealed partial class Listing
{
    /// <summary>Selects the destination, or extends to it while holding the range start (G7, G17).</summary>
    public void MoveFocus(Direction direction, IReadOnlyList<GridRow> rows, long? input = null, double viewportHeight = 0,
        bool extendSelection = false)
    {
        lock (gate)
        {
            if (MovementDestination(direction, rows, viewportHeight) is not { } target ||
                (extendSelection && target == Focused))
            {
                return;
            }

            if (extendSelection)
            {
                var anchor = RangeAnchorOr(target);
                ApplyRange(anchor, target, focus: target, input);
                keyboardRangeAnchor = anchor;
            }
            else
            {
                ApplySelection([target], target, byImport: false, byPerson: true, input);
                keyboardRangeAnchor = null;
            }
        }

        Raise();
    }

    private long? MovementDestination(Direction direction, IReadOnlyList<GridRow> rows, double viewportHeight)
    {
        if (Items.Count == 0)
        {
            return null;
        }

        if (direction == Direction.Last)
        {
            return Items[^1].Id;
        }

        if (direction == Direction.First || Focused is not { } focused || !indexOf.TryGetValue(focused, out var index))
        {
            return Items[0].Id;
        }

        var reached = direction switch
        {
            Direction.Left => Items[Math.Max(0, index - 1)].Id,
            Direction.Right => Items[Math.Min(Items.Count - 1, index + 1)].Id,
            Direction.Up => MoveByRow(focused, rows, -1),
            Direction.Down => MoveByRow(focused, rows, 1),
            Direction.PageUp => MoveByRow(focused, rows, -1, viewportHeight),
            Direction.PageDown => MoveByRow(focused, rows, 1, viewportHeight),
            _ => null,
        };
        return reached is { } id && indexOf.ContainsKey(id) ? id : focused;
    }

    private static long? MoveByRow(long focused, IReadOnlyList<GridRow> rows, int step, double? viewportHeight = null)
    {
        for (var row = 0; row < rows.Count; row++)
        {
            if (rows[row].Tiles.FirstOrDefault(tile => tile.Id == focused) is not { } from)
            {
                continue;
            }

            var to = viewportHeight is { } height ? JustifiedRows.PageRow(rows, row, step, height) : row + step;
            if (to < 0 || to >= rows.Count || to == row)
            {
                return null;
            }

            var tiles = rows[to].Tiles;
            var centre = from.CentreX;
            return (tiles.FirstOrDefault(tile => tile.X <= centre && centre < tile.X + tile.Width)
                ?? tiles.MinBy(tile => Math.Abs(tile.CentreX - centre)))?.Id;
        }

        return null;
    }
}
