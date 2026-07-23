using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 28: discovery + scout wander — the omniscience removal. These pin
/// the new contract: undiscovered nodes are invisible to target selection,
/// scouts find nodes by proximity, discovery is colony-wide, laden returns
/// lay trail, scouts never get functionally lost, and all of it is
/// deterministic per seed.
/// </summary>
public class DiscoveryTests
{
    [Fact]
    public void UndiscoveredNodes_AreInvisibleToTargetSelection()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim);
        colony.Nodes.Add(new ResourceNode(30, 59, 1_000)); // never discovered in this test
        Assert.Null(colony.NearestNodeWithMaterial(colony.HomeCenter.X, colony.HomeCenter.Y));

        colony.Nodes[0].Discovered = true;
        Assert.Same(colony.Nodes[0],
            colony.NearestNodeWithMaterial(colony.HomeCenter.X, colony.HomeCenter.Y));
    }

    [Fact]
    public void ScoutingForager_DiscoversANode_AndGatheringBegins()
    {
        // Real behavioral loop: one Forager, one undiscovered node a real
        // scouting distance away. The Forager must find it by wandering
        // (nothing else can discover), then gather from it via the normal
        // loop — discovery genuinely gates and then genuinely unblocks.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        var node = new ResourceNode(160, 59, 1_000);
        colony.Nodes.Add(node);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);

        int discoveredAt = -1;
        for (int t = 0; t < 30_000; t++)
        {
            colony.Tick();
            sim.Tick();
            if (discoveredAt < 0 && node.Discovered) discoveredAt = t;
            if (colony.Stats.RawGatheredByForagers > 0) break;
        }
        Assert.True(node.Discovered, "the scout never found the node");
        Assert.True(colony.Stats.RawGatheredByForagers > 0,
            $"node discovered at t={discoveredAt} but gathering never started");
    }

    [Fact]
    public void Discovery_IsColonyWide_NotPrivateMemory()
    {
        // Manually mark a node discovered: a fresh Forager that has never
        // scouted must immediately be able to target it — shared state.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        colony.Nodes.Add(new ResourceNode(150, 59, 1_000) { Discovered = true });
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 3_000);
        Assert.True(colony.Stats.RawGatheredByForagers > 0,
            "a Forager that never scouted must still act on a colony-discovered node");
    }

    [Fact]
    public void LadenReturn_LaysTrail()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        colony.Nodes.Add(new ResourceNode(150, 59, 1_000) { Discovered = true });
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);

        // Run until at least one deposit has completed, then check the map.
        int t = 0;
        while (colony.Stats.RawGatheredByForagers == 0 && t++ < 10_000)
        {
            colony.Tick();
            sim.Tick();
        }
        Assert.True(colony.Stats.RawGatheredByForagers > 0, "no haul completed");
        Assert.True(colony.Trails.Count > 0,
            "a completed laden return must have reinforced trail cells");
    }

    [Fact]
    public void Scout_WithNothingToFind_ReturnsHome_NeverFunctionallyLost()
    {
        // No nodes at all: the Forager scouts, exhausts its budget, walks
        // home, and repeats. Over several budget cycles it must keep coming
        // back near home — the "returns rather than wanders forever" pin.
        var (grid, sim) = ColonyTestWorld.Create();
        var cfg = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
        var colony = ColonyTestWorld.Founded(grid, sim, cfg);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        var forager = colony.Foragers[0];
        var home = colony.HomeCenter;

        int cycles = 3;
        int window = cfg.ScoutBudgetTicks * 2; // budget + generous walk-home time
        for (int c = 0; c < cycles; c++)
        {
            bool cameHome = false;
            for (int t = 0; t < window && !cameHome; t++)
            {
                colony.Tick();
                sim.Tick();
                cameHome = Math.Abs(forager.X - home.X) + Math.Abs(forager.Y - home.Y) <= 12;
            }
            Assert.True(cameHome,
                $"cycle {c}: scout did not return near home within {window} ticks");
            // Let it head back out before the next cycle's check.
            ColonyTestWorld.Run(colony, sim, cfg.ScoutBudgetTicks / 3);
        }
    }

    [Fact]
    public void DiscoveryAndScouting_AreDeterministic_PerSeed()
    {
        // Same seed, two full colonies: identical discovery outcomes and
        // identical forager positions at every sample. The close-out's
        // non-optional requirement, pinned as a test.
        static List<string> Run()
        {
            var (grid, sim) = ColonyTestWorld.Create();
            var colony = ColonyTestWorld.Founded(grid, sim,
                new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
            colony.Nodes.Add(new ResourceNode(40, 59, 500));
            colony.Nodes.Add(new ResourceNode(150, 59, 500));
            colony.Nodes.Add(new ResourceNode(200, 59, 500));
            for (int i = 0; i < 3; i++)
            {
                colony.Spawn(Caste.Forager, colony.HomeCenter.X + i, colony.HomeCenter.Y);
            }
            var samples = new List<string>();
            for (int t = 0; t < 20_000; t++)
            {
                colony.Tick();
                sim.Tick();
                if (t % 1_000 == 0)
                {
                    samples.Add(string.Join(";",
                        colony.Nodes.ConvertAll(n => n.Discovered ? "D" : "-"))
                        + "|" + string.Join(";", colony.Foragers.ConvertAll(f => $"{f.X},{f.Y}"))
                        + "|" + colony.Trails.Count);
                }
            }
            return samples;
        }
        Assert.Equal(Run(), Run());
    }
}
