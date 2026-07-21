namespace GroundSim;

// Phase 18 Part B (new outline): FoodStorage (foragers deposit, gardeners
// withdraw) and Graveyard (the dead are hauled here). Waste/Pupa still
// deferred per the Phase 5 decision, unaffected by the outline swap.
public enum RoomType { Home, Garden, Nursery, FoodStorage, Graveyard }

/// <summary>
/// A first-class labeled room. Phase 11: a room is a SET OF CELLS (organic
/// chamber mask), not a rect — the rect constructor remains for the Home Room
/// and tests, building the equivalent cell set. X0..Y1 are the bounding box.
/// </summary>
public sealed class Room
{
    private readonly HashSet<(int X, int Y)> _cells;

    public RoomType Type { get; }
    public bool Excavated { get; internal set; }

    /// <summary>The excavation job that will carve this room (chamber +
    /// connecting tunnel); null once excavated or for pre-carved rooms.</summary>
    public DigSite? PendingDig { get; internal set; }

    public IReadOnlyCollection<(int X, int Y)> Cells => _cells;

    public int X0 { get; }
    public int Y0 { get; }
    public int X1 { get; }
    public int Y1 { get; }

    public Room(RoomType type, IEnumerable<(int X, int Y)> cells, bool excavated = false)
    {
        Type = type;
        Excavated = excavated;
        _cells = new HashSet<(int, int)>(cells);
        if (_cells.Count == 0) throw new ArgumentException("A room needs at least one cell.");
        X0 = _cells.Min(c => c.X);
        X1 = _cells.Max(c => c.X);
        Y0 = _cells.Min(c => c.Y);
        Y1 = _cells.Max(c => c.Y);
    }

    public Room(RoomType type, int x0, int y0, int x1, int y1, bool excavated = false)
        : this(type, RectCells(x0, y0, x1, y1), excavated)
    {
    }

    private static IEnumerable<(int, int)> RectCells(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++) yield return (x, y);
        }
    }

    public (int X0, int Y0, int X1, int Y1) Rect => (X0, Y0, X1, Y1);
    public (int X, int Y) Center => ((X0 + X1) / 2, (Y0 + Y1) / 2);

    /// <summary>The deepest room cell in (or nearest to) the center column —
    /// where floor-anchored sites (processing, queen) live under
    /// terrain-following movement.</summary>
    public (int X, int Y) FloorCenter
    {
        get
        {
            int cx = (X0 + X1) / 2;
            (int X, int Y) best = default;
            int bestKey = int.MinValue;
            foreach (var (x, y) in _cells)
            {
                int key = y * 1000 - Math.Abs(x - cx); // deepest first, then closest to center
                if (key > bestKey)
                {
                    bestKey = key;
                    best = (x, y);
                }
            }
            return best;
        }
    }

    public bool Contains(int x, int y) => _cells.Contains((x, y));

    /// <summary>
    /// The room's CURRENT usable floor site: the deepest Air cell (preferring
    /// cells resting on solid) nearest the center column, computed live
    /// against the grid. Phase 12: fixed cells like FloorCenter can be buried
    /// by mound spill settling inside the room — a work target must adapt to
    /// what the floor actually is right now.
    /// </summary>
    public (int X, int Y) FloorSite(Grid grid)
    {
        int cx = (X0 + X1) / 2;
        (int X, int Y)? bestResting = null, bestAir = null;
        int restingKey = int.MinValue, airKey = int.MinValue;
        foreach (var (x, y) in _cells)
        {
            if (!grid.IsAir(x, y)) continue;
            int key = y * 1000 - Math.Abs(x - cx);
            if (!grid.IsAir(x, y + 1))
            {
                if (key > restingKey) { restingKey = key; bestResting = (x, y); }
            }
            else if (key > airKey)
            {
                airKey = key;
                bestAir = (x, y);
            }
        }
        return bestResting ?? bestAir ?? FloorCenter;
    }

    /// <summary>
    /// True while any FRONTIER-REACHABLE diggable cell remains (same
    /// accessible-frontier semantics as DigSite.HasRemainingDiggable).
    /// Phase 13: Rock is diggable and counts — completed rooms are fully
    /// cleared, no permanent pockmarks.
    /// </summary>
    public bool HasRemainingDiggable(Grid grid)
    {
        foreach (var (x, y) in _cells)
        {
            // Phase 12 verification finding, closed in the Phase 19.5
            // follow-up: DigSite.HasRemainingDiggable guards this read but
            // Room's did not, despite the doc claiming identical semantics —
            // and Grid's indexer does no bounds checking, so an out-of-bounds
            // room cell would throw or misread rather than be skipped.
            if (!grid.InBounds(x, y)) continue;
            // Phase 21: Remains is inert — same exclusion as DigSite/Agent.
            if (grid[x, y] is CellMaterial.Air or CellMaterial.Remains) continue;
            if (grid.IsAir(x - 1, y) || grid.IsAir(x + 1, y)
                || grid.IsAir(x, y - 1) || grid.IsAir(x, y + 1))
            {
                return true;
            }
        }
        return false;
    }
}
