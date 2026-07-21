using GroundSim;
using Xunit.Abstractions;

namespace GroundSim.Tests;

public class RoomTests
{
    [Fact]
    public void Room_ContainsAndCenter_AreCorrect()
    {
        var room = new Room(RoomType.Garden, 10, 20, 16, 24);
        Assert.True(room.Contains(10, 20));
        Assert.True(room.Contains(16, 24));
        Assert.True(room.Contains(13, 22));
        Assert.False(room.Contains(9, 22));
        Assert.False(room.Contains(13, 25));
        Assert.Equal((13, 22), room.Center);
    }

    [Fact]
    public void GardenTrigger_FiresExactlyAtThreshold_NotBefore()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var config = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
        var colony = ColonyTestWorld.Founded(grid, sim, config);

        // Just under the threshold: many ticks, no Garden.
        colony.FarmedResource = config.GardenTriggerThreshold - 0.001;
        ColonyTestWorld.Run(colony, sim, 1000);
        Assert.Null(colony.GetRoom(RoomType.Garden));
        Assert.Null(colony.Milestones.GardenTriggeredTick);

        // At the threshold: triggers on the very next tick.
        colony.FarmedResource = config.GardenTriggerThreshold;
        ColonyTestWorld.Run(colony, sim, 1);
        Assert.NotNull(colony.GetRoom(RoomType.Garden));
        Assert.Equal(colony.TickCount, colony.Milestones.GardenTriggeredTick);
        // Phase 11: the active site is the garden's planned organic dig
        // (tunnel + chamber), matched by identity.
        Assert.NotNull(colony.ActiveDigSite);
        Assert.Same(colony.GetRoom(RoomType.Garden)!.PendingDig, colony.ActiveDigSite);
    }

    [Fact]
    public void NurseryTrigger_IsAnIntegralOverTime_NotAnInstantaneousCount()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        // 5 eggs held constant: pressure grows by exactly 5/tick, so the
        // trigger must fire at ~threshold/5 ticks — proving time-integration.
        var config = new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggMaturationTicks = 1_000_000,
            EggLayIntervalTicks = 1_000_000,
            NurseryBroodPressureThreshold = 5000,
        };
        var colony = ColonyTestWorld.Founded(grid, sim, config);
        for (int i = 0; i < 5; i++) colony.LayEgg();

        // Well before threshold/5 = 1000 ticks: no trigger despite 5 eggs
        // existing the whole time (an instantaneous "N eggs" check would have
        // fired immediately or never — never at exactly this tick).
        ColonyTestWorld.Run(colony, sim, 900);
        Assert.Null(colony.Milestones.NurseryTriggeredTick);

        ColonyTestWorld.Run(colony, sim, 150);
        Assert.NotNull(colony.Milestones.NurseryTriggeredTick);
        Assert.InRange(colony.Milestones.NurseryTriggeredTick!.Value, 1000, 1005);
    }

    [Fact]
    public void RoomExcavation_CompletesViaCommunalDiggers_AndClearsSite()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        // Phase 18: death disabled — this test pins excavation mechanics
        // with a fixed two-worker crew whose 64k window exceeds the natural
        // lifespan; mortality has its own dedicated tests.
        var config = new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
        };
        var colony = ColonyTestWorld.Founded(grid, sim, config);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Major, colony.HomeCenter.X + 1, colony.HomeCenter.Y);

        colony.FarmedResource = config.GardenTriggerThreshold; // trigger Garden
        // Phase 15: 8k → 64k (×8: the dig is ×GridScale² the cells and every
        // haul walks ×GridScale the distance at 1 cell/tick).
        ColonyTestWorld.Run(colony, sim, 64_000);

        var garden = colony.GetRoom(RoomType.Garden);
        Assert.NotNull(garden);
        Assert.True(garden!.Excavated, "Garden should be fully excavated");
        Assert.False(garden.HasRemainingDiggable(grid));
        Assert.Null(colony.ActiveDigSite);
        Assert.NotNull(colony.Milestones.GardenExcavatedTick);
    }
}

public class ColonyStageTests
{
    [Fact]
    public void CurrentStage_TracksMilestonesThroughAllFourStages()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = Colony.Found(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 },
            ColonyTestWorld.Chamber, startX: 112, startY: 59);

        Assert.Equal(ColonyStage.Founding, colony.CurrentStage);

        while (colony.Queen.State == QueenState.Founding)
        {
            colony.Tick();
            sim.Tick();
        }
        Assert.Equal(ColonyStage.FirstBrood, colony.CurrentStage);

        colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);
        Assert.Equal(ColonyStage.Establishment, colony.CurrentStage);

        colony.FarmedResource = colony.Config.GardenTriggerThreshold;
        ColonyTestWorld.Run(colony, sim, 1);
        Assert.Equal(ColonyStage.Expansion, colony.CurrentStage);
    }
}

public class BehaviorRelocationTests
{
    [Fact]
    public void Gardeners_ActuallyProcessInsideTheGarden_OnceItExists()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        var h = ColonyTestWorld.Chamber;
        var garden = colony.AddExcavatedRoom(RoomType.Garden, (h.X0 + 1, h.Y1 + 1, h.X0 + 6, h.Y1 + 3));
        colony.Spawn(Caste.Gardener, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.RawMaterial = 5;

        bool observedGardenerProcessingInGarden = false;
        for (int t = 0; t < 2000; t++)
        {
            colony.Tick();
            sim.Tick();
            var gardener = colony.Gardeners[0];
            if (garden.Contains(gardener.X, gardener.Y) && colony.Stats.ProcessedInGarden > 0)
            {
                observedGardenerProcessingInGarden = true;
            }
        }

        // Not just "the site value changed" — the gardener physically walked
        // into the garden and processing completed there.
        Assert.True(observedGardenerProcessingInGarden,
            "Never observed a Gardener processing while inside the Garden");
        Assert.Equal(colony.Stats.RawProcessedByGardeners, colony.Stats.ProcessedInGarden, 3);
        // Phase 9 update: the site is the garden's FLOOR center (terrain-
        // following made mid-air targets unreachable).
        Assert.Equal((garden.Center.X, garden.Y1), colony.ProcessingSite);
    }

    [Fact]
    public void NewEggs_AreLaidInTheNursery_OnceItExists()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggMaturationTicks = 1_000_000 });
        var h = ColonyTestWorld.Chamber;
        var nursery = colony.AddExcavatedRoom(RoomType.Nursery, (h.X1 + 1, h.Y0, h.X1 + 5, h.Y0 + 2));

        ColonyTestWorld.Run(colony, sim, colony.Config.EggLayIntervalTicks * 5);

        Assert.True(colony.Eggs.Count >= 5);
        Assert.All(colony.Eggs, egg => Assert.True(nursery.Contains(egg.X, egg.Y),
            $"Egg at ({egg.X},{egg.Y}) is outside the Nursery"));
    }
}

public class EndToEndStageTests
{
    private readonly ITestOutputHelper _output;

    public EndToEndStageTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Stages1Through4_CompleteAcrossASpreadOfSeededRuns()
    {
        // Spread-of-runs discipline: 10 seeds, not one playthrough. Phase 14:
        // caste/egg/gather/trigger constants are now the real game.js ports;
        // excavation-geometry constants remain invented (game.js has no
        // analog for cell-by-cell digging).
        const int seeds = 10;
        // Phase 15: 60k → 360k. Excavation is ×GridScale² the cells at an
        // unchanged 1-cell-per-tick dig rate plus ×GridScale haul walks, so
        // milestones inflate ~4-6×; the budget scales with generous headroom
        // (it only bounds failure time — passing runs exit at the milestone).
        const int maxTicks = 360_000;
        var firstWorker = new List<int>();
        var gardenDone = new List<int>();
        var nurseryDone = new List<int>();

        for (int seed = 1; seed <= seeds; seed++)
        {
            var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: seed);
            var sim = new Simulation(grid, seed: seed);
            // Phase 12: organic founding (shaft + chamber) — the product path.
            var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX: 112, seed: seed);
            colony.Nodes.Add(new ResourceNode(30, 59, 10_000));
            colony.Nodes.Add(new ResourceNode(210, 59, 10_000));

            int t = 0;
            var m = colony.Milestones;
            int gardenSamples = 0, gardenUnusableSamples = 0; // Phase 12.5 accounting
            while (t < maxTicks &&
                   (m.NurseryExcavatedTick is null || m.GardenExcavatedTick is null))
            {
                colony.Tick();
                sim.Tick();
                t++;
                if (t % 200 == 0 && colony.GetRoom(RoomType.Garden) is { Excavated: true } gs)
                {
                    gardenSamples++;
                    if (!gs.Cells.Any(c => grid.IsAir(c.X, c.Y) && !grid.IsAir(c.X, c.Y + 1)))
                    {
                        gardenUnusableSamples++;
                    }
                }
            }

            // Stage 1 — Founding.
            Assert.Equal(QueenState.Laying, colony.Queen.State);
            Assert.NotNull(m.HomeFoundedTick);
            // Stage 2 — First Brood.
            Assert.True(m.FirstWorkerTick is not null,
                $"seed {seed}: no worker matured within {maxTicks} ticks");
            // Stage 3 — Establishment: the gather loop genuinely ran.
            Assert.True(colony.Stats.RawGatheredByForagers > 0, $"seed {seed}: nothing gathered");
            Assert.True(colony.Stats.RawProcessedByGardeners > 0, $"seed {seed}: nothing processed");
            // Stage 4 — Expansion: both rooms triggered, excavated for real,
            // and behavior relocated into them.
            Assert.True(m.GardenExcavatedTick is not null,
                $"seed {seed}: Garden not excavated within {maxTicks} ticks");
            Assert.True(m.NurseryExcavatedTick is not null,
                $"seed {seed}: Nursery not excavated within {maxTicks} ticks");
            Assert.True(colony.GetRoom(RoomType.Garden)!.Excavated);
            Assert.True(colony.GetRoom(RoomType.Nursery)!.Excavated);
            // Phase 12.5: STRICT Phase 7 guarantee restored — no conditional
            // escape hatch. The garden must be usable, processing must be
            // happening in it, and any transient burial fallback must stay
            // rare (explicit accounting, so this can't regress silently).
            var g = colony.GetRoom(RoomType.Garden)!;
            var site = colony.ProcessingSite;
            Assert.True(grid.IsAir(site.X, site.Y), $"seed {seed}: processing site {site} is buried");
            Assert.True(g.Cells.Any(c => grid.IsAir(c.X, c.Y) && !grid.IsAir(c.X, c.Y + 1)),
                $"seed {seed}: garden has no usable floor cell at end of run");
            Assert.True(g.Contains(site.X, site.Y),
                $"seed {seed}: processing site {site} is outside the garden");
            Assert.True(gardenUnusableSamples <= Math.Max(1, gardenSamples / 10),
                $"seed {seed}: garden was unusable at {gardenUnusableSamples}/{gardenSamples} sampled instants");

            firstWorker.Add(m.FirstWorkerTick!.Value);
            gardenDone.Add(m.GardenExcavatedTick!.Value);
            nurseryDone.Add(m.NurseryExcavatedTick!.Value);
        }

        static int Median(List<int> xs)
        {
            var s = xs.OrderBy(v => v).ToList();
            return s[s.Count / 2];
        }
        _output.WriteLine($"Milestone medians over {seeds} runs (ticks; real game.js constants since Phase 14):");
        _output.WriteLine($"  first worker:      median {Median(firstWorker)}  (min {firstWorker.Min()}, max {firstWorker.Max()})");
        _output.WriteLine($"  garden excavated:  median {Median(gardenDone)}  (min {gardenDone.Min()}, max {gardenDone.Max()})");
        _output.WriteLine($"  nursery excavated: median {Median(nurseryDone)}  (min {nurseryDone.Min()}, max {nurseryDone.Max()})");
    }
}
