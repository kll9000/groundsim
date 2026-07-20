namespace GroundSim;

public enum AgentState
{
    Idle,
    PathingToDig,
    Digging,
    PathingToDrop,
    Dropping,
}

/// <summary>
/// A non-blocking digging agent: digs cells in an assigned region, carries
/// the material to a drop column, drops it as a falling particle, repeats.
///
/// CONTRACT (the Phase 3 §9.4 fix): <see cref="Tick"/> performs at most ONE
/// unit of work per call — one cell of movement, one dig, one drop, or one
/// plan/replan — and never loops until an outcome completes. The host loop
/// calls it once per simulation tick alongside <see cref="Simulation.Tick"/>.
///
/// Agents are "climbers": any Air cell is walkable (think ants clinging to
/// tunnel walls), so agents have no gravity of their own. Agents are pure
/// positions, not matter — they never occupy the grid, don't collide with
/// each other, and a particle can settle into the cell an agent stands in,
/// in which case the agent pushes up one cell per tick until it is in Air.
///
/// Deliberately generic ("a thing that digs and carries") — no ant-specific
/// behavior, per the Arc 2 scoping rule.
/// </summary>
public sealed class Agent
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public CellMaterial? Carried { get; private set; }
    public AgentState State { get; private set; }

    private readonly Grid _grid;
    private readonly Simulation _sim;
    private readonly HashSet<(int X, int Y)> _claims; // shared across agents
    private readonly IReadOnlyCollection<(int X, int Y)> _digCells; // the region, any shape
    private readonly Func<int> _dropXProvider; // Phase 12: per-delivery drop column (mound)
    private int _currentDropX; // the column the active drop plan targets

    private Queue<(int X, int Y)>? _path;
    private (int X, int Y)? _digTarget;
    private int _idleCooldown;
    private int _dropPathFailures;

    /// <summary>Targets whose approach could not be planned (e.g. a sealed
    /// air pocket in a partially-refilled chamber). Without this, the
    /// deterministic nearest-first selection re-picks the same unreachable
    /// cell forever and a lone digger livelocks while reachable targets sit
    /// ignored (Phase 12, measured on 3 of 10 founding seeds). Cleared on
    /// any successful dig (terrain changed) or when it blocks everything.</summary>
    private readonly HashSet<(int X, int Y)> _unreachableTargets = new();

    /// <summary>Consecutive failed drop-path plans before the agent dumps its
    /// carried material in place instead of deadlocking. Excavation can sever
    /// the only climbable route to the spoil column mid-dig (Phase 9: support
    /// rules made routes severable); dumping keeps material conserved as a
    /// normal falling particle — worst case it lands back in the dig region
    /// and is re-dug once a route exists again.</summary>
    private const int MaxDropPathFailures = 3;

    /// <summary>Ticks to wait before rescanning after failing to find work —
    /// keeps a workless agent from re-scanning its whole region every tick.</summary>
    private const int IdleCooldownTicks = 15;

    /// <summary>Phase 11: the dig region is an arbitrary cell set (organic
    /// mask). Phase 12: the drop column comes from a provider evaluated per
    /// drop plan, so deliveries can spread across a mound.</summary>
    public Agent(
        Grid grid, Simulation sim, HashSet<(int X, int Y)> sharedClaims,
        int startX, int startY, IReadOnlyCollection<(int X, int Y)> digCells, Func<int> dropXProvider)
    {
        _grid = grid;
        _sim = sim;
        _claims = sharedClaims;
        X = startX;
        Y = startY;
        _digCells = digCells;
        _dropXProvider = dropXProvider;
        _currentDropX = dropXProvider();
    }

    /// <summary>Fixed-column convenience (tests, raw agent scenarios).</summary>
    public Agent(
        Grid grid, Simulation sim, HashSet<(int X, int Y)> sharedClaims,
        int startX, int startY, IReadOnlyCollection<(int X, int Y)> digCells, int dropX)
        : this(grid, sim, sharedClaims, startX, startY, digCells, () => dropX)
    {
    }

    /// <summary>Rect convenience (founding fallback, tests).</summary>
    public Agent(
        Grid grid, Simulation sim, HashSet<(int X, int Y)> sharedClaims,
        int startX, int startY, (int X0, int Y0, int X1, int Y1) digRegion, int dropX)
        : this(grid, sim, sharedClaims, startX, startY, RectCells(digRegion), () => dropX)
    {
    }

    private static List<(int X, int Y)> RectCells((int X0, int Y0, int X1, int Y1) r)
    {
        var cells = new List<(int X, int Y)>();
        for (int y = r.Y0; y <= r.Y1; y++)
        {
            for (int x = r.X0; x <= r.X1; x++) cells.Add((x, y));
        }
        return cells;
    }

    public void Tick()
    {
        // Buried by a particle that settled into our cell? Push up one cell
        // per tick until back in Air. This is this tick's unit of work.
        if (!_grid.IsAir(X, Y))
        {
            if (Y > 0) Y--;
            return;
        }

        // Unsupported in open air (Phase 9): fall one cell per tick. Agent-
        // side movement, deliberately NOT a Simulation particle.
        if (!Terrain.IsSupported(_grid, X, Y))
        {
            Y++;
            return;
        }

        switch (State)
        {
            case AgentState.Idle: TickIdle(); break;
            case AgentState.PathingToDig: TickPathing(pathingToDig: true); break;
            case AgentState.Digging: TickDig(); break;
            case AgentState.PathingToDrop: TickPathing(pathingToDig: false); break;
            case AgentState.Dropping: TickDrop(); break;
        }
    }

    private void TickIdle()
    {
        if (_idleCooldown > 0) { _idleCooldown--; return; }

        // Still holding material (e.g. a failed drop-path earlier)? Deliver
        // before taking new work.
        if (Carried is not null)
        {
            if (PlanDropPath())
            {
                _dropPathFailures = 0;
                State = AgentState.PathingToDrop;
            }
            else if (++_dropPathFailures >= MaxDropPathFailures)
            {
                // Emergency dump (see MaxDropPathFailures): no route to the
                // spoil column — drop here rather than deadlock carrying.
                if (_sim.Drop(X, Y, Carried.Value)) Carried = null;
                _dropPathFailures = 0;
                _idleCooldown = IdleCooldownTicks;
            }
            else
            {
                _idleCooldown = IdleCooldownTicks;
            }
            return;
        }

        var target = FindDigTarget();
        if (target is null)
        {
            // If the blacklist is what's blocking us, give those targets
            // another chance next scan — terrain may have changed.
            if (_unreachableTargets.Count > 0) _unreachableTargets.Clear();
            _idleCooldown = IdleCooldownTicks;
            return;
        }

        _claims.Add(target.Value);
        _digTarget = target;
        _digProgress = 0;
        if (PlanPathToDigTarget()) State = AgentState.PathingToDig;
        else AbandonDigTarget();
    }

    private void TickPathing(bool pathingToDig)
    {
        if (_path is null || _path.Count == 0)
        {
            State = pathingToDig ? AgentState.Digging : AgentState.Dropping;
            return;
        }

        var next = _path.Peek();
        if (Math.Abs(next.X - X) + Math.Abs(next.Y - Y) != 1)
        {
            // Falling desynced us from the plan — replan, never teleport.
            bool replanned = pathingToDig ? PlanPathToDigTarget() : PlanDropPath();
            if (!replanned)
            {
                if (pathingToDig) AbandonDigTarget();
                else { State = AgentState.Idle; _idleCooldown = IdleCooldownTicks; }
            }
            return;
        }
        if (!_grid.IsAir(next.X, next.Y))
        {
            // Terrain changed under the path (settled particle, other agent's
            // dig altering the frontier) — replanning is this tick's work.
            bool ok = pathingToDig ? PlanPathToDigTarget() : PlanDropPath();
            if (!ok)
            {
                if (pathingToDig) AbandonDigTarget();
                else { State = AgentState.Idle; _idleCooldown = IdleCooldownTicks; }
            }
            return;
        }

        _path.Dequeue();
        X = next.X;
        Y = next.Y;
    }

    private void TickDig()
    {
        if (_digTarget is not { } t) { State = AgentState.Idle; return; }

        if (Math.Abs(X - t.X) + Math.Abs(Y - t.Y) != 1)
        {
            // Stale arrival (we were bumped/pushed) — replan approach.
            if (!PlanPathToDigTarget()) AbandonDigTarget();
            else State = AgentState.PathingToDig;
            return;
        }

        // Rock takes multiple ticks of chipping; each chip is one tick's
        // unit of work (the non-blocking contract holds).
        if (_grid[t.X, t.Y] == CellMaterial.Rock && ++_digProgress < RockDigTicks)
        {
            return;
        }
        _digProgress = 0;

        var dug = _grid.Dig(t.X, t.Y);
        _claims.Remove(t);
        _digTarget = null;
        if (dug is not null) _unreachableTargets.Clear(); // terrain changed: retry everything
        Carried = dug; // null if another change beat us to it — Idle handles both
        State = AgentState.Idle;
    }

    private void TickDrop()
    {
        if (Carried is { } material && _sim.Drop(_currentDropX, Y, material))
        {
            Carried = null;
        }
        // On the rare full-column failure, keep carrying; Idle will retry.
        State = AgentState.Idle;
    }

    // ---- planning helpers (each used as a single tick's unit of work) ----

    private bool PlanPathToDigTarget()
    {
        if (_digTarget is not { } t) return false;
        // Path to any Air cell adjacent to the target, nearest-first.
        foreach (var n in AirNeighborsByDistance(t))
        {
            var path = Pathfinder.FindPath(_grid, (X, Y), n);
            if (path is not null) { _path = new Queue<(int, int)>(path); return true; }
        }
        return false;
    }

    private bool PlanDropPath()
    {
        // A fresh drop column per plan (mound spreading), then the approach
        // cell = the Air cell just above that column's current surface —
        // recomputed at every (re)plan, so a growing pile simply moves the
        // approach point up.
        _currentDropX = _dropXProvider();
        int surfY = 0;
        while (surfY < _grid.Height && _grid.IsAir(_currentDropX, surfY)) surfY++;
        if (surfY == 0) return false; // column solid to the sky
        var approach = (X: _currentDropX, Y: surfY - 1);
        var path = Pathfinder.FindPath(_grid, (X, Y), approach);
        if (path is null) return false;
        _path = new Queue<(int, int)>(path);
        return true;
    }

    /// <summary>
    /// Releases any held dig-target claim. MUST be called when discarding an
    /// agent mid-cycle (e.g. dig-assist reassignment) — otherwise the claim
    /// leaks and the claimed cell can never be dug by anyone.
    /// </summary>
    public void ReleaseClaims()
    {
        if (_digTarget is { } t) _claims.Remove(t);
        _digTarget = null;
    }

    private void AbandonDigTarget()
    {
        if (_digTarget is { } t)
        {
            _claims.Remove(t);
            _unreachableTargets.Add(t); // don't re-pick it next scan
        }
        _digTarget = null;
        State = AgentState.Idle;
        _idleCooldown = IdleCooldownTicks;
    }

    /// <summary>Phase 13: everything solid is diggable — terrain Rock just
    /// takes longer (see RockDigTicks chipping in TickDig).</summary>
    private static bool IsDiggable(CellMaterial m)
        => m != CellMaterial.Air;

    /// <summary>Ticks of chipping needed per Rock cell (dirt and everything
    /// else: 1). Rock mining is open to ALL diggers — restricting it to
    /// Majors would recreate the waits-on-a-caste stall class; Majors still
    /// speed excavation the way they always have, by being extra diggers.
    /// INVENTED constant, same status as ColonyConfig values.</summary>
    public const int RockDigTicks = 4;

    private int _digProgress;

    /// <summary>
    /// Nearest unclaimed diggable cell in the region that touches Air (the
    /// dig frontier) — cells sealed inside solid ground are skipped until
    /// digging exposes them. Selection rule unchanged since Phase 4; it now
    /// scans an arbitrary cell set instead of a rect.
    /// </summary>
    private (int X, int Y)? FindDigTarget()
    {
        (int X, int Y)? best = null;
        int bestDist = int.MaxValue;
        foreach (var (x, y) in _digCells)
        {
            if (!_grid.InBounds(x, y) || !IsDiggable(_grid[x, y])) continue;
            if (_claims.Contains((x, y)) || _unreachableTargets.Contains((x, y))) continue;
            if (!HasAirNeighbor(x, y)) continue;
            int dist = Math.Abs(X - x) + Math.Abs(Y - y);
            if (dist < bestDist) { bestDist = dist; best = (x, y); }
        }
        return best;
    }

    private bool HasAirNeighbor(int x, int y)
        => _grid.IsAir(x - 1, y) || _grid.IsAir(x + 1, y)
        || _grid.IsAir(x, y - 1) || _grid.IsAir(x, y + 1);

    private IEnumerable<(int X, int Y)> AirNeighborsByDistance((int X, int Y) t)
    {
        var candidates = new List<(int X, int Y)>(4);
        if (_grid.IsAir(t.X - 1, t.Y)) candidates.Add((t.X - 1, t.Y));
        if (_grid.IsAir(t.X + 1, t.Y)) candidates.Add((t.X + 1, t.Y));
        if (_grid.IsAir(t.X, t.Y - 1)) candidates.Add((t.X, t.Y - 1));
        if (_grid.IsAir(t.X, t.Y + 1)) candidates.Add((t.X, t.Y + 1));
        candidates.Sort((a, b) =>
            (Math.Abs(X - a.X) + Math.Abs(Y - a.Y)).CompareTo(Math.Abs(X - b.X) + Math.Abs(Y - b.Y)));
        return candidates;
    }
}
