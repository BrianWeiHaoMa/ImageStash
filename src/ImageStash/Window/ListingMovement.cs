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
    /// <summary>The item reached by grid navigation becomes the whole selection (G7).</summary>
    public void MoveFocus(Direction direction, IReadOnlyList<GridRow> rows, long? input = null, double viewportHeight = 0)
    {
        long? destination;
        lock (gate)
        {
            destination = MovementDestination(direction, rows, viewportHeight);
        }

        if (destination is { } target)
        {
            Select([target], target, byImport: false, byPerson: true, input);
        }
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
