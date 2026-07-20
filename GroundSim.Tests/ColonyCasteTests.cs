using GroundSim;

namespace GroundSim.Tests;

public static class ColonyTestWorld
{
    public static (Grid grid, Simulation sim) Create()
    {
        var grid = Grid.CreateTestWorld(120, 60, groundLevel: 30);
        var sim = new Simulation(grid);
        return (grid, sim);
    }

    public static readonly (int X0, int Y0, int X1, int Y1) Chamber = (52, 30, 60, 33);

    public static Colony Founded(Grid grid, Simulation sim, ColonyConfig? config = null)
        => Colony.CreateFounded(grid, sim, config ?? new ColonyConfig(), Chamber);

    public static void Run(Colony colony, Simulation sim, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            colony.Tick();
            sim.Tick();
        }
    }
}

public class QueenTests
{
    [Fact]
    public void Queen_FoundsChamber_DepositsStarter_ThenNeverMovesAgain()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var chamber = ColonyTestWorld.Chamber;
        var colony = Colony.Found(grid, sim, new ColonyConfig(), chamber, startX: 56, startY: 29);

        int foundingTicks = 0;
        while (colony.Queen.State == QueenState.Founding && foundingTicks < 30_000)
        {
            colony.Tick();
            sim.Tick();
            foundingTicks++;
        }
        Assert.Equal(QueenState.Laying, colony.Queen.State);

        // Chamber genuinely excavated, starter deposited.
        for (int y = chamber.Y0; y <= chamber.Y1; y++)
        {
            for (int x = chamber.X0; x <= chamber.X1; x++)
            {
                Assert.Equal(CellMaterial.Air, grid[x, y]);
            }
        }
        Assert.Equal(colony.Config.StarterResource, colony.FarmedResource);

        // Post-founding: she never moves again, across thousands of ticks.
        int qx = colony.Queen.X, qy = colony.Queen.Y;
        ColonyTestWorld.Run(colony, sim, 3000);
        Assert.Equal((qx, qy), (colony.Queen.X, colony.Queen.Y));
        Assert.Equal(QueenState.Laying, colony.Queen.State);
    }

    [Fact]
    public void Queen_LaysEggsOnExpectedCadence()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        // Survival 0 so matured eggs vanish instead of spawning workers that
        // would muddy the count; maturation long so none mature mid-test.
        var config = new ColonyConfig { EggSurvivalChance = 0, EggMaturationTicks = 100_000 };
        var colony = ColonyTestWorld.Founded(grid, sim, config);

        ColonyTestWorld.Run(colony, sim, config.EggLayIntervalTicks * 10);

        Assert.Equal(10, colony.TotalEggsLaid);
        Assert.Equal(10, colony.Eggs.Count);
    }
}

public class TenderTests
{
    [Fact]
    public void Tender_ProcessesRawIntoFarmed()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        colony.Spawn(Caste.Tender, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.RawMaterial = 5;

        ColonyTestWorld.Run(colony, sim, 500);

        Assert.True(colony.Stats.RawProcessedByTenders >= 5,
            $"Expected all 5 raw processed, got {colony.Stats.RawProcessedByTenders}");
        Assert.Equal(0, colony.RawMaterial, 3);
        Assert.Equal(colony.Config.StarterResource + 5, colony.FarmedResource, 3);
    }

    [Fact]
    public void Tender_TendingSpeedsEggMaturation()
    {
        // Two identical colonies, one with a Tender: the tended egg matures
        // measurably sooner.
        int MatureTicks(bool withTender)
        {
            var (grid, sim) = ColonyTestWorld.Create();
            var config = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
            var colony = ColonyTestWorld.Founded(grid, sim, config);
            if (withTender) colony.Spawn(Caste.Tender, colony.HomeCenter.X, colony.HomeCenter.Y);
            colony.LayEgg();
            for (int t = 0; t < 100_000; t++)
            {
                colony.Tick();
                sim.Tick();
                if (colony.Eggs.Count == 0) return t;
            }
            return int.MaxValue;
        }

        int untended = MatureTicks(withTender: false);
        int tended = MatureTicks(withTender: true);
        Assert.True(tended < untended,
            $"Tended egg ({tended} ticks) should mature faster than untended ({untended})");
    }

    [Fact]
    public void Tender_NeverGathers_AcrossManyTicks()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        // Tempting targets: full resource nodes right outside home.
        colony.Nodes.Add(new ResourceNode(40, 29, 100));
        colony.Nodes.Add(new ResourceNode(70, 29, 100));
        colony.Spawn(Caste.Tender, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Tender, colony.HomeCenter.X + 1, colony.HomeCenter.Y);
        colony.RawMaterial = 3; // some processing work, then idle/tending time

        ColonyTestWorld.Run(colony, sim, 4000);

        // Behavioral purity: nothing was gathered by anyone.
        Assert.Equal(100, colony.Nodes[0].Remaining, 3);
        Assert.Equal(100, colony.Nodes[1].Remaining, 3);
        Assert.Equal(0, colony.Stats.RawGatheredByForagers, 3);
        // Raw only went DOWN (processing); it can never rise without foragers.
        Assert.True(colony.RawMaterial <= 3);
    }
}

public class ForagerTests
{
    [Fact]
    public void Forager_GathersAndHaulsRawHome()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        // Regen disabled (Phase 9): this test's conservation equation needs
        // node depletion to have exactly one cause — gathering.
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, NodeRegenPerTick = 0 });
        var node = new ResourceNode(80, 29, 20);
        colony.Nodes.Add(node);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 4000);

        Assert.True(node.Remaining < 20, "Node should be partially depleted");
        Assert.True(colony.RawMaterial > 0, "Raw material should have arrived home");
        // Conservation across the gather pipeline: everything the node lost is
        // either home or in the forager's jaws.
        double inJaws = colony.Foragers[0].Carrying;
        Assert.Equal(20 - node.Remaining, colony.RawMaterial + inJaws, 3);
        Assert.Equal(colony.Stats.RawGatheredByForagers, colony.RawMaterial, 3);
    }

    [Fact]
    public void Forager_HaulShrinksWithDistance_AndClampsAtMin()
    {
        var config = new ColonyConfig();
        double near = config.HaulSize(20);
        double far = config.HaulSize(120);
        Assert.True(near > far, $"near haul {near} should exceed far haul {far}");
        Assert.Equal(config.GatherChunkMin, config.HaulSize(100_000));
    }

    [Fact]
    public void Forager_NeverProcesses_AcrossManyTicks()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        // Tempting target: a pile of unprocessed raw material at home, no nodes.
        colony.RawMaterial = 50;
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X + 1, colony.HomeCenter.Y);

        double farmedBefore = colony.FarmedResource;
        ColonyTestWorld.Run(colony, sim, 4000);

        // Behavioral purity: farmed resource NEVER grew — no forager
        // shortcut around the two-stage raw->farmed pipeline.
        Assert.Equal(farmedBefore, colony.FarmedResource, 3);
        Assert.Equal(50, colony.RawMaterial, 3);
        Assert.Equal(0, colony.Stats.RawProcessedByTenders, 3);
    }
}

public class MajorTests
{
    [Fact]
    public void Major_SpeedsExcavation_WithFullMaterialConservation()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        var site = (X0: 70, Y0: 30, X1: 80, Y1: 35);
        colony.ActiveDigSite = DigSite.FromRect(site.X0, site.Y0, site.X1, site.Y1); // Phase 11: DigSite type
        colony.SpoilDropX = 100;
        colony.Spawn(Caste.Major, colony.HomeCenter.X, colony.HomeCenter.Y);

        // Phase 13: rock is diggable (Rock -> LooseRock), so conservation is
        // over ALL solid cells + carried + in-flight.
        int DiggableInWorld()
        {
            int n = 0;
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    if (grid[x, y] != CellMaterial.Air) n++;
                }
            }
            return n;
        }
        int before = DiggableInWorld();

        ColonyTestWorld.Run(colony, sim, 4000);

        // The major actually excavated the site.
        int dugCells = 0;
        for (int y = site.Y0; y <= site.Y1; y++)
        {
            for (int x = site.X0; x <= site.X1; x++)
            {
                if (grid[x, y] == CellMaterial.Air) dugCells++;
            }
        }
        Assert.True(dugCells > 0, "Major should have excavated cells in the active dig site");

        // Conservation: dug material is settled spoil, in flight, or in jaws.
        int after = DiggableInWorld();
        int carried = colony.Majors[0].Carrying is null ? 0 : 1;
        Assert.Equal(before, after + carried + sim.ActiveParticleCount);
    }

    [Fact]
    public void Major_WithNoDigSite_IsIdle_AndTouchesNothing()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        colony.Nodes.Add(new ResourceNode(40, 29, 100));
        colony.RawMaterial = 10;
        colony.Spawn(Caste.Major, colony.HomeCenter.X, colony.HomeCenter.Y);
        var majorPos = (colony.Majors[0].X, colony.Majors[0].Y);

        ColonyTestWorld.Run(colony, sim, 3000);

        // Idle means idle: no movement, no gathering, no processing.
        Assert.Equal(majorPos, (colony.Majors[0].X, colony.Majors[0].Y));
        Assert.Equal(100, colony.Nodes[0].Remaining, 3);
        Assert.Equal(10, colony.RawMaterial, 3);
        Assert.Equal(colony.Config.StarterResource, colony.FarmedResource, 3);
    }
}

public class OffspringTests
{
    [Fact]
    public void SurvivalAndCasteRolls_MatchConfiguredDistribution()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim);

        const int trials = 20_000;
        int survived = 0, majors = 0, foragers = 0, tenders = 0;
        for (int i = 0; i < trials; i++)
        {
            var o = colony.RollOffspring();
            if (!o.Survived) continue;
            survived++;
            switch (o.Caste)
            {
                case Caste.Major: majors++; break;
                case Caste.Forager: foragers++; break;
                case Caste.Tender: tenders++; break;
            }
        }

        // Expectations derive from the live config (Phase 14: values are now
        // the real game.js ports, and this test should keep verifying the
        // roll logic against config rather than re-pinning magic numbers).
        // Generous ±0.03 tolerances — a distribution sanity check, not an
        // RNG audit.
        double pSurvive = colony.Config.EggSurvivalChance;
        double pMajor = colony.Config.MajorChance;
        double pForager = (1 - pMajor) * colony.Config.ForagerShareOfRemainder;
        double pTender = (1 - pMajor) * (1 - colony.Config.ForagerShareOfRemainder);
        Assert.InRange(survived / (double)trials, pSurvive - 0.03, pSurvive + 0.03);
        Assert.InRange(majors / (double)survived, pMajor - 0.03, pMajor + 0.03);
        Assert.InRange(foragers / (double)survived, pForager - 0.03, pForager + 0.03);
        Assert.InRange(tenders / (double)survived, pTender - 0.03, pTender + 0.03);
    }

    [Fact]
    public void MaturedSurvivors_SpawnAsRealCasteMembers()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        // Survival 1.0 and instant maturation: every egg becomes a worker.
        var config = new ColonyConfig
        {
            EggSurvivalChance = 1.0,
            EggMaturationTicks = 5,
            EggLayIntervalTicks = 10,
        };
        var colony = ColonyTestWorld.Founded(grid, sim, config);

        ColonyTestWorld.Run(colony, sim, 500);

        int workers = colony.Tenders.Count + colony.Foragers.Count + colony.Majors.Count;
        Assert.True(workers >= 40, $"Expected ~49 workers from 500 ticks, got {workers}");
        Assert.DoesNotContain(colony.Eggs, e => e.IsMature);
    }
}

public class ScopeBoundaryTests
{
    [Fact]
    public void DeferredFeatures_DoNotExistYet()
    {
        // The Phase 6 boundary: no Soldier, Pupa Chamber, grooming/
        // contamination, or New Queen / nuptial flight code anywhere in the
        // core assembly. Cheap check, documents the line clearly.
        var names = typeof(Grid).Assembly.GetTypes().Select(t => t.Name).ToList();
        string[] forbidden = { "Soldier", "Pupa", "Groom", "Contamination", "NewQueen", "Alate", "Nuptial" };
        foreach (var name in names)
        {
            foreach (var f in forbidden)
            {
                Assert.False(name.Contains(f, StringComparison.OrdinalIgnoreCase),
                    $"Deferred-scope type found in core assembly: {name}");
            }
        }
    }
}
