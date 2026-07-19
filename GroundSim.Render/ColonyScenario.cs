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
    public const int Width = 200;
    public const int Height = 120;
    public const int GroundLevel = 70;

    public static (Grid grid, Simulation sim, Colony colony) Create(int seed = 42)
    {
        var grid = Grid.CreateTestWorld(Width, Height, GroundLevel, seed);
        var sim = new Simulation(grid, seed);

        var chamber = (X0: 92, Y0: GroundLevel, X1: 100, Y1: GroundLevel + 3);
        var colony = Colony.Found(grid, sim, new ColonyConfig(), chamber,
            startX: 96, startY: GroundLevel - 1, seed);

        // Modest caps + default regeneration (Phase 9): patches visibly
        // deplete and regrow instead of the colony running dry.
        colony.Nodes.Add(new ResourceNode(30, GroundLevel - 1, 800));
        colony.Nodes.Add(new ResourceNode(170, GroundLevel - 1, 800));
        return (grid, sim, colony);
    }
}
