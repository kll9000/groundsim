namespace GroundSim;

/// <summary>
/// An excavation job: an arbitrary set of cells (organic mask or rect) handed
/// to the frontier-fill diggers. Replaces the old rect-tuple ActiveDigSite —
/// the frontier rule itself is unchanged, only the shape it scans.
/// </summary>
public sealed class DigSite
{
    private readonly HashSet<(int X, int Y)> _cells;

    public IReadOnlyCollection<(int X, int Y)> Cells => _cells;

    public DigSite(IEnumerable<(int X, int Y)> cells)
    {
        _cells = new HashSet<(int, int)>(cells);
        if (_cells.Count == 0) throw new ArgumentException("A dig site needs at least one cell.");
    }

    public static DigSite FromRect(int x0, int y0, int x1, int y1)
    {
        var cells = new List<(int, int)>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++) cells.Add((x, y));
        }
        return new DigSite(cells);
    }

    public bool Contains(int x, int y) => _cells.Contains((x, y));

    /// <summary>Fraction of site cells currently Air (Phase 12.5): used to
    /// distinguish "excavation finished" from "excavation never started but
    /// its only air-adjacency was transiently buried" — the race that
    /// produced born-dead rooms marked excavated with zero cells dug.</summary>
    public double AirFraction(Grid grid)
    {
        int air = 0, total = 0;
        foreach (var (x, y) in _cells)
        {
            if (!grid.InBounds(x, y)) continue;
            total++;
            if (grid.IsAir(x, y)) air++;
        }
        return total == 0 ? 0 : air / (double)total;
    }

    /// <summary>
    /// True while any diggable cell remains that the dig frontier can still
    /// reach (has a 4-adjacent Air cell). Phase 13: Rock is diggable, so it
    /// COUNTS — completion means the site is genuinely cleared, rock
    /// included (no more permanent pockmarks), and the old sealed-pocket
    /// tolerance is unnecessary because nothing is undiggable anymore. This
    /// still matches the diggers' own idle condition exactly: excavation is
    /// complete precisely when no digger can find another target.
    /// </summary>
    public bool HasRemainingDiggable(Grid grid)
    {
        foreach (var (x, y) in _cells)
        {
            if (!grid.InBounds(x, y)) continue;
            if (grid[x, y] == CellMaterial.Air) continue;
            if (grid.IsAir(x - 1, y) || grid.IsAir(x + 1, y)
                || grid.IsAir(x, y - 1) || grid.IsAir(x, y + 1))
            {
                return true;
            }
        }
        return false;
    }
}
