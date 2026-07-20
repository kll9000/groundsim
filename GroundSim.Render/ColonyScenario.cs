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

    public static (Grid grid, Simulation sim, Colony colony) Create(int seed = 42)
    {
        var grid = Grid.CreateTestWorld(Width, Height, GroundLevel, seed);
        var sim = new Simulation(grid, seed);

        // Phase 12: organic founding — entrance shaft + home chamber,
        // spoil mounding around the shaft mouth.
        var colony = Colony.Found(grid, sim, new ColonyConfig(),
            entranceX: 96 * ColonyConfig.GridScale, seed);

        // Modest caps + default regeneration (Phase 9): patches visibly
        // deplete and regrow instead of the colony running dry.
        colony.Nodes.Add(new ResourceNode(30 * ColonyConfig.GridScale, GroundLevel - 1, 800));
        colony.Nodes.Add(new ResourceNode(170 * ColonyConfig.GridScale, GroundLevel - 1, 800));
        return (grid, sim, colony);
    }
}
