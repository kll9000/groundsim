using GroundSim;

namespace GroundSim.Tests;

public class RuleVsOracleAuditTests
{
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
