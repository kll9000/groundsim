using GroundSim;

namespace GroundSim.Tests;

public static class ColonyTestWorld
{
    public static (Grid grid, Simulation sim) Create()
    {
        var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60);
        var sim = new Simulation(grid);
        return (grid, sim);
    }

    // Phase 15: the 9x4 fixed test chamber scales to 18x8 (same physical room).
    public static readonly (int X0, int Y0, int X1, int Y1) Chamber = (104, 60, 121, 67);

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
        var colony = Colony.Found(grid, sim, new ColonyConfig(), chamber, startX: 112, startY: 59);

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

// Phase 18 Part A: Tender split into Gardener (processes) and Minim (tends
// eggs). The old TenderTests split with it — each new caste keeps the test
// for the behavior it inherited, plus adversarial role-purity tests in the
// same tempt-and-verify style as every purity test since Phase 6.
public class GardenerTests
{
    [Fact]
    public void Gardener_ProcessesRawIntoFarmed()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        colony.Spawn(Caste.Gardener, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.RawMaterial = 5;

        ColonyTestWorld.Run(colony, sim, 500);

        Assert.True(colony.Stats.RawProcessedByGardeners >= 5,
            $"Expected all 5 raw processed, got {colony.Stats.RawProcessedByGardeners}");
        Assert.Equal(0, colony.RawMaterial, 3);
        Assert.Equal(colony.Config.StarterResource + 5, colony.FarmedResource, 3);
    }

    [Fact]
    public void Gardener_NeverTendsEggs_AndNeverGathers_AcrossManyTicks()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        // Tempting targets: full nodes outside AND an egg right next to the
        // gardener, with NO raw to process — an idle gardener with an easy
        // egg to tend and easy nodes to raid must still do neither.
        colony.Nodes.Add(new ResourceNode(80, 59, 100));
        colony.Spawn(Caste.Gardener, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.LayEgg();
        int untendedMaturation = colony.Config.EggMaturationTicks;

        int t = 0;
        while (colony.Eggs.Count > 0 && t < 100_000)
        {
            Assert.False(colony.Eggs[0].TendedThisTick, "a Gardener tended an egg — role purity violated");
            colony.Tick();
            sim.Tick();
            t++;
        }
        // The egg matured at the full UNTENDED rate (no speed boost leaked in).
        Assert.True(t >= untendedMaturation,
            $"egg matured in {t} ticks < untended {untendedMaturation} — something tended it");
        Assert.Equal(100, colony.Nodes[0].Remaining, 3);
        Assert.Equal(0, colony.Stats.RawGatheredByForagers, 3);
    }
}

public class MinimTests
{
    [Fact]
    public void Minim_TendingSpeedsEggMaturation()
    {
        // Two identical colonies, one with a Minim: the tended egg matures
        // measurably sooner. (The mechanic inherited from Tender.)
        int MatureTicks(bool withMinim)
        {
            var (grid, sim) = ColonyTestWorld.Create();
            var config = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
            var colony = ColonyTestWorld.Founded(grid, sim, config);
            if (withMinim) colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);
            colony.LayEgg();
            for (int t = 0; t < 100_000; t++)
            {
                colony.Tick();
                sim.Tick();
                if (colony.Eggs.Count == 0) return t;
            }
            return int.MaxValue;
        }

        int untended = MatureTicks(withMinim: false);
        int tended = MatureTicks(withMinim: true);
        Assert.True(tended < untended,
            $"Tended egg ({tended} ticks) should mature faster than untended ({untended})");
    }

    [Fact]
    public void Minim_NeverProcesses_AndNeverGathers_AcrossManyTicks()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        // Tempting targets: full nodes outside AND a pile of raw material at
        // the processing site — minims with nothing to tend must not touch
        // either (no eggs are ever laid here).
        colony.Nodes.Add(new ResourceNode(80, 59, 100));
        colony.Nodes.Add(new ResourceNode(140, 59, 100));
        colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Minim, colony.HomeCenter.X + 1, colony.HomeCenter.Y);
        colony.RawMaterial = 50;
        double farmedBefore = colony.FarmedResource;

        ColonyTestWorld.Run(colony, sim, 4000);

        // Behavioral purity: nothing gathered, nothing processed.
        Assert.Equal(100, colony.Nodes[0].Remaining, 3);
        Assert.Equal(100, colony.Nodes[1].Remaining, 3);
        Assert.Equal(0, colony.Stats.RawGatheredByForagers, 3);
        Assert.Equal(50, colony.RawMaterial, 3);
        Assert.Equal(farmedBefore, colony.FarmedResource, 3);
        Assert.Equal(0, colony.Stats.RawProcessedByGardeners, 3);
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
        var node = new ResourceNode(160, 59, 20);
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
        Assert.Equal(0, colony.Stats.RawProcessedByGardeners, 3);
    }
}

public class SoldierTests
{
    [Fact]
    public void Soldier_SpeedsExcavation_WithFullMaterialConservation()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        // Phase 19 fix of a latent vacuity: the old rect (70,30)-(80,35)
        // has been SKY since Phase 15 scaled the world (ground starts at 60),
        // so 'dug cells > 0' passed on already-air cells without any digging.
        // The site now sits in real dirt glued to the chamber's right wall.
        var site = (X0: 122, Y0: 62, X1: 132, Y1: 67);
        colony.ActiveDigSite = DigSite.FromRect(site.X0, site.Y0, site.X1, site.Y1); // Phase 11: DigSite type
        colony.SpoilDropX = 100;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);

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

        // The soldier actually excavated the site (Major's inherited duty).
        int dugCells = 0;
        for (int y = site.Y0; y <= site.Y1; y++)
        {
            for (int x = site.X0; x <= site.X1; x++)
            {
                if (grid[x, y] == CellMaterial.Air) dugCells++;
            }
        }
        Assert.True(dugCells > 0, "Soldier should have excavated cells in the active dig site");

        // Conservation: dug material is settled spoil, in flight, or in jaws.
        int after = DiggableInWorld();
        int carried = colony.Soldiers[0].Carrying is null ? 0 : 1;
        Assert.Equal(before, after + carried + sim.ActiveParticleCount);
    }

    [Fact]
    public void Soldier_WithNoDigSite_StandsGuard_AndTouchesNothing()
    {
        // Phase 19: the old Major went idle-in-place here; a Soldier with no
        // dig and no burial work takes up the guard post instead — but the
        // role-purity half of the old test is unchanged: guarding must not
        // gather, process, or tend.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        colony.Nodes.Add(new ResourceNode(80, 59, 100));
        colony.RawMaterial = 10;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 3000);

        // Standing guard at the post (entrance mouth — the chamber floor at
        // the entrance column in this surface-open test world).
        Assert.True(colony.Soldiers[0].OnGuard, "Soldier never reached its guard post");
        Assert.Equal(colony.GuardPost, (colony.Soldiers[0].X, colony.Soldiers[0].Y));
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
        // Phase 18/19: satisfy BOTH population gates up front so the full
        // four-caste distribution is exercised; each gate has its own
        // dedicated test below.
        int gates = Math.Max(colony.Config.GardenerUnlockPopulation, colony.Config.SoldierUnlockPopulation);
        for (int i = 0; i < gates; i++)
        {
            colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);
        }

        const int trials = 20_000;
        int survived = 0, soldiers = 0, foragers = 0, minims = 0, gardeners = 0;
        for (int i = 0; i < trials; i++)
        {
            var o = colony.RollOffspring();
            if (!o.Survived) continue;
            survived++;
            switch (o.Caste)
            {
                case Caste.Soldier: soldiers++; break;
                case Caste.Forager: foragers++; break;
                case Caste.Minim: minims++; break;
                case Caste.Gardener: gardeners++; break;
            }
        }

        // Expectations derive from the live config (Phase 14: values are now
        // the real game.js ports, and this test should keep verifying the
        // roll logic against config rather than re-pinning magic numbers).
        // Generous ±0.03 tolerances — a distribution sanity check, not an
        // RNG audit.
        double pSurvive = colony.Config.EggSurvivalChance;
        double pSoldier = colony.Config.SoldierChance;
        double pForager = (1 - pSoldier) * colony.Config.ForagerShareOfRemainder;
        double pCaregiver = (1 - pSoldier) * (1 - colony.Config.ForagerShareOfRemainder);
        double pGardener = pCaregiver * colony.Config.GardenerShareOfCaregivers;
        double pMinim = pCaregiver * (1 - colony.Config.GardenerShareOfCaregivers);
        Assert.InRange(survived / (double)trials, pSurvive - 0.03, pSurvive + 0.03);
        Assert.InRange(soldiers / (double)survived, pSoldier - 0.03, pSoldier + 0.03);
        Assert.InRange(foragers / (double)survived, pForager - 0.03, pForager + 0.03);
        Assert.InRange(gardeners / (double)survived, pGardener - 0.03, pGardener + 0.03);
        Assert.InRange(minims / (double)survived, pMinim - 0.03, pMinim + 0.03);
    }

    [Fact]
    public void SoldierRolls_ArePopulationGated_TheColonysMaturityMarker()
    {
        // Phase 19: below SoldierUnlockPopulation (real game.js value: 5)
        // no Soldier can roll — the outline's "last caste to appear".
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim);
        Assert.True(colony.WorkerCount < colony.Config.SoldierUnlockPopulation);
        for (int i = 0; i < 5000; i++)
        {
            var o = colony.RollOffspring();
            Assert.False(o.Survived && o.Caste == Caste.Soldier,
                "a Soldier rolled below the population gate");
        }
        for (int i = 0; i < colony.Config.SoldierUnlockPopulation; i++)
        {
            colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);
        }
        bool anySoldier = false;
        for (int i = 0; i < 5000 && !anySoldier; i++)
        {
            var o = colony.RollOffspring();
            anySoldier = o.Survived && o.Caste == Caste.Soldier;
        }
        Assert.True(anySoldier, "no Soldier ever rolled once the gate was satisfied");
    }

    [Fact]
    public void GardenerRolls_ArePopulationGated_FallingThroughToMinim()
    {
        // Phase 18: below GardenerUnlockPopulation, the caregiver roll can
        // ONLY produce Minims (the gardener share falls through); at the
        // gate, Gardeners appear. First instance of Colony Builder's
        // population-gate mechanism (Phase 14 Part C gap #1).
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim);
        Assert.True(colony.WorkerCount < colony.Config.GardenerUnlockPopulation);

        for (int i = 0; i < 5000; i++)
        {
            var o = colony.RollOffspring();
            Assert.False(o.Survived && o.Caste == Caste.Gardener,
                "a Gardener rolled below the population gate");
        }

        for (int i = 0; i < colony.Config.GardenerUnlockPopulation; i++)
        {
            colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);
        }
        bool anyGardener = false;
        for (int i = 0; i < 5000 && !anyGardener; i++)
        {
            var o = colony.RollOffspring();
            anyGardener = o.Survived && o.Caste == Caste.Gardener;
        }
        Assert.True(anyGardener, "no Gardener ever rolled once the gate was satisfied");
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

        int workers = colony.WorkerCount;
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
        // Phase 19: Soldier came OFF this list — it's real scope now, per
        // the new outline. The rest remain deferred.
        string[] forbidden = { "Pupa", "Groom", "Contamination", "NewQueen", "Alate", "Nuptial" };
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
