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
        Assert.Equal(colony.GetRoom(RoomType.Garden)!.Rect, colony.ActiveDigSite);
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
        var config = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
        var colony = ColonyTestWorld.Founded(grid, sim, config);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Major, colony.HomeCenter.X + 1, colony.HomeCenter.Y);

        colony.FarmedResource = config.GardenTriggerThreshold; // trigger Garden
        ColonyTestWorld.Run(colony, sim, 8000);

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
            ColonyTestWorld.Chamber, startX: 56, startY: 29);

        Assert.Equal(ColonyStage.Founding, colony.CurrentStage);

        while (colony.Queen.State == QueenState.Founding)
        {
            colony.Tick();
            sim.Tick();
        }
        Assert.Equal(ColonyStage.FirstBrood, colony.CurrentStage);

        colony.Spawn(Caste.Tender, colony.HomeCenter.X, colony.HomeCenter.Y);
        Assert.Equal(ColonyStage.Establishment, colony.CurrentStage);

        colony.FarmedResource = colony.Config.GardenTriggerThreshold;
        ColonyTestWorld.Run(colony, sim, 1);
        Assert.Equal(ColonyStage.Expansion, colony.CurrentStage);
    }
}

public class BehaviorRelocationTests
{
    [Fact]
    public void Tenders_ActuallyProcessInsideTheGarden_OnceItExists()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        var h = ColonyTestWorld.Chamber;
        var garden = colony.AddExcavatedRoom(RoomType.Garden, (h.X0 + 1, h.Y1 + 1, h.X0 + 6, h.Y1 + 3));
        colony.Spawn(Caste.Tender, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.RawMaterial = 5;

        bool observedTenderProcessingInGarden = false;
        for (int t = 0; t < 2000; t++)
        {
            colony.Tick();
            sim.Tick();
            var tender = colony.Tenders[0];
            if (garden.Contains(tender.X, tender.Y) && colony.Stats.ProcessedInGarden > 0)
            {
                observedTenderProcessingInGarden = true;
            }
        }

        // Not just "the site value changed" — the tender physically walked
        // into the garden and processing completed there.
        Assert.True(observedTenderProcessingInGarden,
            "Never observed a Tender processing while inside the Garden");
        Assert.Equal(colony.Stats.RawProcessedByTenders, colony.Stats.ProcessedInGarden, 3);
        Assert.Equal(garden.Center, colony.ProcessingSite);
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
        // Spread-of-runs discipline: 10 seeds, not one playthrough. Timings
        // below are measured but built on INVENTED ColonyConfig constants —
        // placeholders until game.js values arrive.
        const int seeds = 10;
        const int maxTicks = 40_000;
        var firstWorker = new List<int>();
        var gardenDone = new List<int>();
        var nurseryDone = new List<int>();

        for (int seed = 1; seed <= seeds; seed++)
        {
            var grid = Grid.CreateTestWorld(120, 60, groundLevel: 30, seed: seed);
            var sim = new Simulation(grid, seed: seed);
            var colony = Colony.Found(grid, sim, new ColonyConfig(),
                ColonyTestWorld.Chamber, startX: 56, startY: 29, seed: seed);
            colony.Nodes.Add(new ResourceNode(15, 29, 10_000));
            colony.Nodes.Add(new ResourceNode(105, 29, 10_000));

            int t = 0;
            var m = colony.Milestones;
            while (t < maxTicks &&
                   (m.NurseryExcavatedTick is null || m.GardenExcavatedTick is null))
            {
                colony.Tick();
                sim.Tick();
                t++;
            }

            // Stage 1 — Founding.
            Assert.Equal(QueenState.Laying, colony.Queen.State);
            Assert.NotNull(m.HomeFoundedTick);
            // Stage 2 — First Brood.
            Assert.True(m.FirstWorkerTick is not null,
                $"seed {seed}: no worker matured within {maxTicks} ticks");
            // Stage 3 — Establishment: the gather loop genuinely ran.
            Assert.True(colony.Stats.RawGatheredByForagers > 0, $"seed {seed}: nothing gathered");
            Assert.True(colony.Stats.RawProcessedByTenders > 0, $"seed {seed}: nothing processed");
            // Stage 4 — Expansion: both rooms triggered, excavated for real,
            // and behavior relocated into them.
            Assert.True(m.GardenExcavatedTick is not null,
                $"seed {seed}: Garden not excavated within {maxTicks} ticks");
            Assert.True(m.NurseryExcavatedTick is not null,
                $"seed {seed}: Nursery not excavated within {maxTicks} ticks");
            Assert.True(colony.GetRoom(RoomType.Garden)!.Excavated);
            Assert.True(colony.GetRoom(RoomType.Nursery)!.Excavated);
            Assert.Equal(colony.GetRoom(RoomType.Garden)!.Center, colony.ProcessingSite);

            firstWorker.Add(m.FirstWorkerTick!.Value);
            gardenDone.Add(m.GardenExcavatedTick!.Value);
            nurseryDone.Add(m.NurseryExcavatedTick!.Value);
        }

        static int Median(List<int> xs)
        {
            var s = xs.OrderBy(v => v).ToList();
            return s[s.Count / 2];
        }
        _output.WriteLine($"Milestone medians over {seeds} runs (ticks; INVENTED constants):");
        _output.WriteLine($"  first worker:      median {Median(firstWorker)}  (min {firstWorker.Min()}, max {firstWorker.Max()})");
        _output.WriteLine($"  garden excavated:  median {Median(gardenDone)}  (min {gardenDone.Min()}, max {gardenDone.Max()})");
        _output.WriteLine($"  nursery excavated: median {Median(nurseryDone)}  (min {nurseryDone.Min()}, max {nurseryDone.Max()})");
    }
}
