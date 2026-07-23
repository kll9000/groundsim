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

    // ------------------------------------------------------------------
    // Phase 29: the unreachability strike ledger — defense-in-depth
    // against geometry livelocks (the seed-9 floating-shelf class from
    // PHASE25_5_REPORT §4). When an agent PROVES a cell unreachable (all
    // four approach cells failed a support-aware path plan), it records a
    // strike here. Unlike Agent's _unreachableTargets blacklist — which
    // deliberately clears on every terrain change so transient blockage
    // recovers — strikes PERSIST. A cell struck UnreachableStrikeLimit
    // times, with strikes at least UnreachableStrikeSpacingTicks apart
    // (so a burst of retries against one transient blockage counts once),
    // is WRITTEN OFF: HasRemainingDiggable stops demanding it, restoring
    // the Phase 21.5 agreement property (the frontier must never demand
    // what agents have proven they cannot do) in the geometric direction.
    // Worst case of a wrong write-off is a room completing with a few
    // undug cells — cosmetic; the failure it prevents is a permanent
    // colony-wide excavation pin. Both constants INVENTED.
    // ------------------------------------------------------------------
    public const int UnreachableStrikeLimit = 3;
    public const int UnreachableStrikeSpacingTicks = 300;

    private readonly Dictionary<(int X, int Y), (int Strikes, int LastTick)> _unreachable = new();

    /// <summary>Records a proven approach-exhaustion for a site cell at the
    /// given colony tick. Strikes closer together than the spacing window
    /// are ignored (one transient blockage = one strike, however many
    /// agents bounce off it meanwhile).</summary>
    public void RecordUnreachable(int x, int y, int tick)
    {
        if (!_cells.Contains((x, y))) return;
        if (_unreachable.TryGetValue((x, y), out var e))
        {
            if (tick - e.LastTick < UnreachableStrikeSpacingTicks) return;
            _unreachable[(x, y)] = (e.Strikes + 1, tick);
        }
        else
        {
            _unreachable[(x, y)] = (1, tick);
        }
    }

    /// <summary>True when the cell has accumulated enough spaced strikes to
    /// stop counting as remaining work.</summary>
    public bool IsWrittenOff(int x, int y) =>
        _unreachable.TryGetValue((x, y), out var e) && e.Strikes >= UnreachableStrikeLimit;

    /// <summary>Number of written-off cells (diagnostics and the
    /// failed-site release decision in Colony.ManageExcavation).</summary>
    public int WrittenOffCount
    {
        get
        {
            int n = 0;
            foreach (var e in _unreachable.Values)
            {
                if (e.Strikes >= UnreachableStrikeLimit) n++;
            }
            return n;
        }
    }

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
            // Phase 21: Remains is inert (not diggable, see Agent.IsDiggable)
            // so it must not count as remaining work — kept in exact lockstep
            // with the diggers' own idle condition, as documented above.
            if (grid[x, y] is CellMaterial.Air or CellMaterial.Remains) continue;
            // Phase 29: written-off cells (proven unreachable, see the
            // strike ledger above) must not count either — the agreement
            // property's geometric arm.
            if (IsWrittenOff(x, y)) continue;
            if (grid.IsAir(x - 1, y) || grid.IsAir(x + 1, y)
                || grid.IsAir(x, y - 1) || grid.IsAir(x, y + 1))
            {
                return true;
            }
        }
        return false;
    }
}
