namespace GroundSim;

/// <summary>
/// Phase 27: pheromone-trail infrastructure. SPARSE by design — a
/// dictionary of only the cells that currently carry non-negligible trail
/// strength, so decay processing scales with actual trail activity, never
/// with world size. Nothing populates it yet (Phase 28 wires Forager
/// behavior in); the Colony owns and ticks one so the system is live,
/// tested, and renderable before any behavior depends on it.
///
/// Architecture (decided in the Phase 27 handoff): trails inform target
/// SELECTION, not step-by-step movement — A* still does the walking. This
/// structure therefore needs no pathing semantics at all: it is strength
/// bookkeeping, pure math, and contains NO randomness (nothing here rolls;
/// stated per the determinism requirement rather than adding a seeded RNG
/// it doesn't need).
/// </summary>
public sealed class TrailMap
{
    private readonly Dictionary<(int X, int Y), double> _strength = new();
    private readonly List<(int X, int Y)> _cullScratch = new();
    private readonly ColonyConfig _cfg;

    public TrailMap(ColonyConfig cfg) => _cfg = cfg;

    /// <summary>Number of cells currently carrying trail (sparse size —
    /// the boundedness tests pin that this stays proportional to activity).</summary>
    public int Count => _strength.Count;

    public double Strength(int x, int y) =>
        _strength.TryGetValue((x, y), out double s) ? s : 0;

    /// <summary>Live view for rendering: every cell with its strength.</summary>
    public IEnumerable<KeyValuePair<(int X, int Y), double>> Entries => _strength;

    /// <summary>One reinforcement event (Phase 28: a laden Forager walking
    /// this cell homeward). Adds TrailReinforcePerVisit, capped at
    /// TrailMaxStrength so heavily-trafficked cells can't grow unboundedly.</summary>
    public void Reinforce(int x, int y)
    {
        double s = Strength(x, y) + _cfg.TrailReinforcePerVisit;
        _strength[(x, y)] = Math.Min(_cfg.TrailMaxStrength, s);
    }

    /// <summary>Per-tick decay: every entry multiplies by TrailDecayFactor
    /// (exponential — chosen over linear because one factor gives a
    /// scale-free half-life and can never overshoot below zero). Entries
    /// falling under TrailCullThreshold are REMOVED outright, keeping the
    /// structure bounded and the per-tick cost O(active trail cells).</summary>
    public void Tick()
    {
        if (_strength.Count == 0) return;
        _cullScratch.Clear();
        foreach (var (cell, s) in _strength)
        {
            double decayed = s * _cfg.TrailDecayFactor;
            if (decayed < _cfg.TrailCullThreshold) _cullScratch.Add(cell);
            else _strength[cell] = decayed;
        }
        foreach (var cell in _cullScratch) _strength.Remove(cell);
    }
}
