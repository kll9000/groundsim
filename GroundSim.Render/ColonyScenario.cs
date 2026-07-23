namespace GroundSim.Render;

/// <summary>
/// The live-colony scenario shown by the window (and run headless by
/// --smoke): a real founding — the Queen digs the Home Room from scratch —
/// with surface resource nodes for Foragers, on GroundSim's standard test
/// terrain. All colony behavior comes from Colony itself; this only sets the
/// scene.
/// </summary>
public static class ColonyScenario
{
    // Phase 15: all dimensions ×GridScale — the SAME physical world at a
    // finer discretization (200×120 old cells → 400×240 new cells), not a
    // bigger world. Node/entrance coordinates scale with it; node CAPS are
    // mass (scale-invariant) and stay 800.
    public const int Width = 200 * ColonyConfig.GridScale;
    public const int Height = 120 * ColonyConfig.GridScale;
    public const int GroundLevel = 70 * ColonyConfig.GridScale;

    // Phase 26: scattered resource sites, ALL INVENTED constants (same
    // status as every feel-tuned value). Counts/margins in physical cells
    // × GridScale where they describe distance.
    public const int NodeCount = 12;
    private const int NodeEdgeMargin = 10 * ColonyConfig.GridScale;
    private const int NodeEntranceExclusion = 15 * ColonyConfig.GridScale;
    private const int NodeMinSpacing = 8 * ColonyConfig.GridScale;
    /// <summary>Per-site caps scale with distance from the entrance —
    /// near sites are convenient but modest, far sites richer (the
    /// incentive a future scouting mechanic needs). Old world: 2 × 800.</summary>
    public const double NodeCapNear = 150;
    public const double NodeCapFar = 500;

    public static (Grid grid, Simulation sim, Colony colony) Create(int seed = 42)
    {
        var grid = Grid.CreateTestWorld(Width, Height, GroundLevel, seed);
        var sim = new Simulation(grid, seed);

        // Phase 12: organic founding — entrance shaft + home chamber,
        // spoil mounding around the shaft mouth.
        int entranceX = 96 * ColonyConfig.GridScale;
        var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX, seed);

        // Phase 26: 12 seed-randomized surface sites instead of the old two
        // fixed ones. Placement uses a SEPARATE Random seeded with the same
        // seed — deterministic (same seed → same layout) without consuming
        // draws from the colony's own stream (the Phase 24 lesson: don't
        // perturb sim RNG from outside the sim). Sites sit at GroundLevel-1,
        // the air cell on the flat starting surface — the same convention
        // the old two nodes used. Rejection sampling with a fixed attempt
        // bound keeps placement deterministic and terminating: margin from
        // the world edges, an exclusion zone around the entrance (nothing
        // on top of the shaft/mound), and minimum spacing between sites.
        var placer = new Random(seed);
        var xs = new List<int>();
        for (int attempts = 0; xs.Count < NodeCount && attempts < 1000; attempts++)
        {
            int x = NodeEdgeMargin + placer.Next(Width - 2 * NodeEdgeMargin);
            if (Math.Abs(x - entranceX) < NodeEntranceExclusion) continue;
            if (xs.Exists(e => Math.Abs(e - x) < NodeMinSpacing)) continue;
            xs.Add(x);
        }
        foreach (int x in xs)
        {
            double distFrac = Math.Min(1.0, Math.Abs(x - entranceX) / (Width / 2.0));
            double cap = NodeCapNear + (NodeCapFar - NodeCapNear) * distFrac;
            colony.Nodes.Add(new ResourceNode(x, GroundLevel - 1, cap));
        }
        return (grid, sim, colony);
    }
}
