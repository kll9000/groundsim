using GroundSim;

namespace GroundSim.Tests;

public class RuleVsOracleAuditTests
{
    [Fact]
    public void FalseSupportShape_ExistsNowhereInTheWorld_FullGridSweep()
    {
        // Phase 9.5b: geometry-based regression gate. The agent-occupancy
        // test below answers "did any worker visibly float"; this one answers
        // "does the false-support shape exist ANYWHERE in the grid" — a bad
        // cell an agent never happens to walk into still fails here. It
        // exists specifically as the safety net for Phase 11's organic room/
        // tunnel shapes: the geometric argument that currently rules the
        // shape out ("rooms are open pits") stops being guaranteed the moment
        // room geometry changes.
        //
        // TEST-ONLY: this sweep runs at sparse checkpoints inside the test
        // suite; nothing in Colony.Tick() or any production path performs it.
        const int seeds = 5;
        const int totalTicks = 12_000;
        const int checkpointInterval = 400; // 30 checkpoints per seed

        for (int seed = 1; seed <= seeds; seed++)
        {
            var grid = Grid.CreateTestWorld(120, 60, groundLevel: 30, seed: seed);
            var sim = new Simulation(grid, seed: seed);
            var colony = Colony.Found(grid, sim, new ColonyConfig(),
                ColonyTestWorld.Chamber, startX: 56, startY: 29, seed: seed);
            colony.Nodes.Add(new ResourceNode(15, 29, 500));
            colony.Nodes.Add(new ResourceNode(105, 29, 500));

            for (int t = 0; t < totalTicks; t++)
            {
                colony.Tick();
                sim.Tick();
                if (t % checkpointInterval != 0) continue;

                for (int y = 0; y < grid.Height; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (!Terrain.IsVisiblyFloating(grid, x, y)) continue;
                        Assert.False(Terrain.IsSupported(grid, x, y),
                            $"seed {seed} tick {t}: cell ({x},{y}) is visibly floating " +
                            "(no 3×3 contact) yet IsSupported claims support — the " +
                            "enclosed/roof false-support shape exists in world geometry");
                    }
                }
            }
        }
    }

    [Fact]
    public void ProductionSupportRule_NeverContradictsOracle_InRealColonyRuns()
    {
        // Phase 9.5 item-1 audit, kept as a regression gate: across seeded
        // long colony runs, no worker may ever stand in a cell the production
        // rule calls supported while the independent oracle calls it visibly
        // floating. (The original 5-seed × 15k-tick audit measured zero
        // disagreements; oracle-floating instances were all mid-fall
        // transients, max 6 consecutive ticks.)
        for (int seed = 1; seed <= 3; seed++)
        {
            var grid = Grid.CreateTestWorld(120, 60, groundLevel: 30, seed: seed);
            var sim = new Simulation(grid, seed: seed);
            var colony = Colony.Found(grid, sim, new ColonyConfig(),
                ColonyTestWorld.Chamber, startX: 56, startY: 29, seed: seed);
            colony.Nodes.Add(new ResourceNode(15, 29, 500));
            colony.Nodes.Add(new ResourceNode(105, 29, 500));

            for (int t = 0; t < 10_000; t++)
            {
                colony.Tick();
                sim.Tick();
                foreach (var (x, y) in colony.Tenders.Select(w => (w.X, w.Y))
                    .Concat(colony.Foragers.Select(w => (w.X, w.Y)))
                    .Concat(colony.Majors.Select(w => (w.X, w.Y))))
                {
                    if (Terrain.IsVisiblyFloating(grid, x, y))
                    {
                        Assert.False(Terrain.IsSupported(grid, x, y),
                            $"seed {seed} tick {t}: rule says supported but oracle says " +
                            $"visibly floating at ({x},{y}) — the enclosed/roof false-positive is live");
                    }
                }
            }
        }
    }
}
