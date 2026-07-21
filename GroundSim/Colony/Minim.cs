namespace GroundSim;

/// <summary>
/// Phase 18 Part A (new outline): the colony's first and smallest workers.
/// Tends the queen's eggs (the maturation-speed-boost mechanic inherited
/// from the retired Tender caste). NEVER processes fungus, NEVER gathers
/// from surface resource nodes — role purity, same hard invariant as every
/// other caste. The outline's "gather substrate" duty is deliberately NOT
/// implemented yet: substrate has no source mechanic that wouldn't violate
/// the never-gathers-from-nodes rule, and it interacts with Part B's
/// Food-storage resource-flow decision — flagged as deferred in the Phase
/// 18 report, not silently dropped.
/// Composes PathWalker (Agent's movement machinery) — Minims don't dig.
/// </summary>
public sealed class Minim
{
    private readonly PathWalker _walker;

    public int X => _walker.X;
    public int Y => _walker.Y;

    /// <summary>Phase 18 Part C: tick at which this worker dies of old age
    /// (int.MaxValue = death disabled). Assigned by Colony.Spawn.</summary>
    public int DiesAtTick { get; init; } = int.MaxValue;

    public Minim(int x, int y) => _walker = new PathWalker(x, y);

    public void Tick(Colony colony, Grid grid)
    {
        // Tend the nearest unhatched egg (speeds maturation).
        var egg = colony.NearestEgg(X, Y);
        if (egg is null) return;
        if (_walker.MoveTowards(grid, (egg.X, egg.Y)))
        {
            egg.TendedThisTick = true;
        }
    }
}
