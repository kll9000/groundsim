using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// ⚠️ HONEST LABEL (Phase 11.5, item 1 Option B applied to the OLD net):
///
/// Since Phase 11's support-rule unification, IsSupported(x,y) and
/// IsVisiblyFloating(x,y) are EXACT LOGICAL COMPLEMENTS by construction —
/// independently verified across 16,078 air cells of live organic geometry
/// with zero violations. Both reduce to "any of the 8 neighbors solid, or
/// bottom row."
///
/// Therefore the tests in this class are REGRESSION PINS on the two
/// functions staying in lockstep. They will catch an ACCIDENTAL future edit
/// to either implementation (the functions remain deliberately separate
/// source code), but they CANNOT verify that the shared logic is CORRECT —
/// a predicate compared against its own complement is unfalsifiable as a
/// correctness check. Correctness is now guarded by the genuinely
/// independent trajectory-plausibility net in TrajectoryAuditTests, which
/// judges observed agent behavior over time instead of comparing grid
/// predicates (see that file for the disjoint-failure-domain argument).
/// </summary>
public class SupportRuleImplementationPinTests
{
    [Fact]
    public void Pin_FullGridSweep_RuleAndOracleStayExactComplements()
    {
        // Formerly "FalseSupportShape_ExistsNowhereInTheWorld" — the sweep
        // shape is retained (it exercises live organic geometry at sparse
        // checkpoints, test-suite-only), but its meaning is now the pin
        // described in the class comment, nothing more.
        const int seeds = 5;
        const int totalTicks = 12_000;
        const int checkpointInterval = 400;

        for (int seed = 1; seed <= seeds; seed++)
        {
            var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: seed);
            var sim = new Simulation(grid, seed: seed);
            // Phase 12: organic founding — covers the shaft+chamber home shape.
            var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX: 112, seed: seed);
            colony.Nodes.Add(new ResourceNode(30, 59, 500));
            colony.Nodes.Add(new ResourceNode(210, 59, 500));

            for (int t = 0; t < totalTicks; t++)
            {
                colony.Tick();
                sim.Tick();
                if (t % checkpointInterval != 0) continue;

                for (int y = 0; y < grid.Height; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (!grid.IsAir(x, y)) continue;
                        Assert.True(Terrain.IsSupported(grid, x, y) != Terrain.IsVisiblyFloating(grid, x, y),
                            $"seed {seed} tick {t}: IsSupported and IsVisiblyFloating fell out of " +
                            $"lockstep at ({x},{y}) — one implementation was edited without the other");
                    }
                }
            }
        }
    }

    [Fact]
    public void Pin_AgentOccupiedCells_RuleAndOracleStayExactComplements()
    {
        for (int seed = 1; seed <= 3; seed++)
        {
            var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: seed);
            var sim = new Simulation(grid, seed: seed);
            // Phase 12: organic founding — covers the shaft+chamber home shape.
            var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX: 112, seed: seed);
            colony.Nodes.Add(new ResourceNode(30, 59, 500));
            colony.Nodes.Add(new ResourceNode(210, 59, 500));

            for (int t = 0; t < 10_000; t++)
            {
                colony.Tick();
                sim.Tick();
                foreach (var (x, y) in colony.Tenders.Select(w => (w.X, w.Y))
                    .Concat(colony.Foragers.Select(w => (w.X, w.Y)))
                    .Concat(colony.Majors.Select(w => (w.X, w.Y))))
                {
                    if (!grid.IsAir(x, y)) continue; // transiently buried
                    Assert.True(Terrain.IsSupported(grid, x, y) != Terrain.IsVisiblyFloating(grid, x, y),
                        $"seed {seed} tick {t}: predicates out of lockstep at agent cell ({x},{y})");
                }
            }
        }
    }
}
