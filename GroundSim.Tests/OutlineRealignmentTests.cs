using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 18 Parts B and C (new-outline realignment): Food-storage and
/// Graveyard rooms, worker death, and remains burial. Conservation
/// discipline throughout: Deaths == corpses-in-world + corpses-in-jaws +
/// Burials, always.
/// </summary>
public class NewRoomTriggerTests
{
    [Fact]
    public void FoodStorage_TriggersOnCumulativeGatheredRaw()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        Assert.Null(colony.GetRoom(RoomType.FoodStorage));

        colony.Stats.RawGatheredByForagers = colony.Config.FoodStorageTriggerThreshold - 0.001;
        ColonyTestWorld.Run(colony, sim, 1);
        Assert.Null(colony.GetRoom(RoomType.FoodStorage));

        colony.Stats.RawGatheredByForagers = colony.Config.FoodStorageTriggerThreshold;
        ColonyTestWorld.Run(colony, sim, 1);
        Assert.NotNull(colony.GetRoom(RoomType.FoodStorage));
    }

    [Fact]
    public void Graveyard_TriggersOnFirstDeath()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        ColonyTestWorld.Run(colony, sim, 100);
        Assert.Null(colony.GetRoom(RoomType.Graveyard));

        colony.Stats.Deaths = 1;
        ColonyTestWorld.Run(colony, sim, 1);
        Assert.NotNull(colony.GetRoom(RoomType.Graveyard));
    }
}

public class FoodStorageFlowTests
{
    [Fact]
    public void Forager_DepositsAtStorage_AndGardener_WithdrawsFromStorage()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        var h = ColonyTestWorld.Chamber;
        var storage = colony.AddExcavatedRoom(RoomType.FoodStorage, (h.X1 + 1, h.Y0 + 2, h.X1 + 9, h.Y1)); // touches home's air, walkable
        colony.Nodes.Add(new ResourceNode(160, 59, 100) { Discovered = true }); // Phase 28: pre-discovered — this test is about food-storage flow, not discovery
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Gardener, colony.HomeCenter.X + 1, colony.HomeCenter.Y);

        bool foragerDepositedInStorage = false, gardenerVisitedStorage = false;
        double lastCarrying = 0;
        for (int t = 0; t < 12_000; t++)
        {
            colony.Tick();
            sim.Tick();
            var f = colony.Foragers[0];
            if (lastCarrying > 0 && f.Carrying == 0 && storage.Contains(f.X, f.Y))
            {
                foragerDepositedInStorage = true;
            }
            lastCarrying = f.Carrying;
            var g = colony.Gardeners[0];
            if (storage.Contains(g.X, g.Y)) gardenerVisitedStorage = true;
        }

        // The flow is genuinely spatial: the forager physically dropped her
        // haul inside the storage room, the gardener physically fetched from
        // it, and processing still completed end-to-end.
        Assert.True(foragerDepositedInStorage, "Forager never deposited inside the Food-storage room");
        Assert.True(gardenerVisitedStorage, "Gardener never visited the Food-storage room to withdraw");
        Assert.True(colony.Stats.RawProcessedByGardeners > 0, "the storage-mediated pipeline never processed anything");
        // Conservation across the withdraw leg: gathered raw is at home,
        // in a gardener's grip, or already processed.
        double inGrip = colony.Gardeners[0].CarryingRaw ? 1 : 0;
        Assert.Equal(colony.Stats.RawGatheredByForagers,
            colony.RawMaterial + inGrip + colony.Stats.RawProcessedByGardeners, 3);
    }
}

public class DeathAndBurialTests
{
    [Fact]
    public void RawConservation_ClosesItsBooks_IncludingRawLostToDeaths()
    {
        // Phase 18.5 item 2: the raw-material invariant must include the
        // died-mid-carry term, so a silent break in that path gets caught.
        // Regen off → node depletion has exactly one cause; short fixed
        // lifespans → the whole crew dies inside the window, some mid-haul.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            NodeRegenPerTick = 0,
            WorkerLifespanMeanTicks = 1_500,
            WorkerLifespanJitterTicks = 300,
        });
        var node = new ResourceNode(160, 59, 10_000) { Discovered = true }; // Phase 28: pre-discovered — this test is about the conservation ledger, not discovery
        colony.Nodes.Add(node);
        for (int i = 0; i < 6; i++)
        {
            colony.Spawn(Caste.Forager, colony.HomeCenter.X + i - 3, colony.HomeCenter.Y);
        }

        ColonyTestWorld.Run(colony, sim, 4_000);

        Assert.Empty(colony.Foragers); // the whole crew died in-window
        double depleted = 10_000 - node.Remaining;
        // Every unit the node lost is at home, already processed, or died
        // in a forager's jaws — nothing silently vanished.
        Assert.Equal(depleted,
            colony.RawMaterial + colony.Stats.RawProcessedByGardeners + colony.Stats.RawLostToDeaths, 3);
        // The term is genuinely exercised: at least one forager died
        // mid-carry (deterministic seed; staggered spawn positions make
        // some death ticks land mid-haul).
        Assert.True(colony.Stats.RawLostToDeaths > 0,
            "no forager died carrying — the RawLostToDeaths path was never exercised");
    }

    [Fact]
    public void Worker_DiesAtLifespan_LeavingACorpse_QueenExempt()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 300,
            WorkerLifespanJitterTicks = 0,
        });
        colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);
        Assert.Single(colony.Minims);

        ColonyTestWorld.Run(colony, sim, 400);

        Assert.Empty(colony.Minims);
        Assert.Equal(1, colony.Stats.Deaths);
        Assert.Single(colony.Corpses);
        // The first death triggers the Graveyard room.
        Assert.NotNull(colony.GetRoom(RoomType.Graveyard));
        // The Queen is exempt: alive, laying, unmoved by the mechanic.
        Assert.Equal(QueenState.Laying, colony.Queen.State);

        // Conservation: the dead worker is fully accounted for.
        Assert.Equal(colony.Stats.Deaths, colony.Corpses.Count + colony.Stats.Burials);
    }

    [Fact]
    public void Corpse_IsHauledToGraveyard_AndBuried_WithConservation()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        // Death disabled: this test pins the HAULING mechanics with a fixed
        // workforce (same reason excavation tests disable it).
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
        });
        var h = ColonyTestWorld.Chamber;
        var graveyard = colony.AddExcavatedRoom(RoomType.Graveyard, (h.X0 - 10, h.Y0 + 2, h.X0 - 1, h.Y1)); // touches home's air
        colony.Corpses.Add(new Colony.Corpse { X = colony.HomeCenter.X - 3, Y = colony.HomeCenter.Y }); // on the home floor, walkable
        colony.Stats.Deaths = 1;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X + 2, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 8_000);

        Assert.Empty(colony.Corpses);
        Assert.Equal(1, colony.Stats.Burials);
        Assert.Equal(0, colony.Stats.EmergencyBurials);
        Assert.False(colony.Soldiers[0].CarryingCorpse);
        // The remains physically settled inside the graveyard.
        int remainsInGraveyard = graveyard.Cells.Count(c => grid[c.X, c.Y] == CellMaterial.Remains);
        Assert.True(remainsInGraveyard >= 1, "no Remains material settled in the graveyard");
        // Conservation.
        Assert.Equal(colony.Stats.Deaths, colony.Corpses.Count + colony.Stats.Burials);
    }

    [Fact]
    public void UnreachableCorpse_IsReleasedForRetry_NotChasedForever()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
        });
        var h = ColonyTestWorld.Chamber;
        colony.AddExcavatedRoom(RoomType.Graveyard, (h.X0 - 12, h.Y0 + 2, h.X0 - 3, h.Y0 + 6));
        // A corpse sealed in solid ground far below — no walkable route.
        colony.Corpses.Add(new Colony.Corpse { X = 30, Y = 110 });
        colony.Stats.Deaths = 1;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, Soldier.BurialLegBudgetTicks + 500);

        // Safety net fired: the corpse was released with a retry gate, the
        // hauler stood down (not carrying, not stuck), nothing was lost.
        Assert.Single(colony.Corpses);
        Assert.False(colony.Corpses[0].Claimed);
        Assert.True(colony.Corpses[0].NextAttemptTick > 0, "released corpse should carry a retry cooldown");
        Assert.False(colony.Soldiers[0].CarryingCorpse);
        Assert.Equal(0, colony.Stats.Burials);
        Assert.Equal(colony.Stats.Deaths, colony.Corpses.Count + colony.Stats.Burials);
    }

    [Fact]
    public void BlockedGraveyardRoute_TriggersEmergencyLayDown_NoDeadlock()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
        });
        var h = ColonyTestWorld.Chamber;
        var graveyard = colony.AddExcavatedRoom(RoomType.Graveyard, (h.X0 - 10, h.Y0 + 2, h.X0 - 1, h.Y1));
        // Re-seal the graveyard AFTER excavation: its floor site becomes
        // unreachable while the room still "exists" — the hostile case for
        // the carry leg.
        foreach (var c in graveyard.Cells) grid[c.X, c.Y] = CellMaterial.Dirt;
        colony.Corpses.Add(new Colony.Corpse { X = colony.HomeCenter.X - 3, Y = colony.HomeCenter.Y });
        colony.Stats.Deaths = 1;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X + 2, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, Soldier.BurialLegBudgetTicks * 2 + 1000);

        // The corpse was picked up, the route never resolved, and the
        // emergency lay-down fired: buried where the hauler stood, tracked
        // as an emergency, hauler free again, books balanced.
        Assert.Empty(colony.Corpses);
        Assert.Equal(1, colony.Stats.Burials);
        Assert.Equal(1, colony.Stats.EmergencyBurials);
        Assert.False(colony.Soldiers[0].CarryingCorpse);
        Assert.Equal(colony.Stats.Deaths, colony.Corpses.Count + colony.Stats.Burials);
    }

    [Fact]
    public void PopulationTurnover_AtScale_ConservesEveryDeath()
    {
        // The at-scale check: a real founded colony with a SHORT lifespan so
        // many generations die within the window; every death must be
        // accounted for at every sampled instant, and the queen survives.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            WorkerLifespanMeanTicks = 2_000,
            WorkerLifespanJitterTicks = 500,
        });
        colony.Nodes.Add(new ResourceNode(60, 59, 10_000));
        colony.Nodes.Add(new ResourceNode(180, 59, 10_000));

        for (int t = 0; t < 20_000; t++)
        {
            colony.Tick();
            sim.Tick();
            if (t % 500 == 0)
            {
                int inJaws = colony.Soldiers.Count(s => s.CarryingCorpse);
                // Phase 21: the ledger gains the decay term (the deliberate
                // conservation exception) — every death is still accounted.
                Assert.Equal(colony.Stats.Deaths,
                    colony.Corpses.Count + inJaws + colony.Stats.Burials + colony.Stats.CorpsesDecayed);
            }
        }

        Assert.True(colony.Stats.Deaths > 3, $"expected real turnover, saw {colony.Stats.Deaths} deaths");
        Assert.Equal(QueenState.Laying, colony.Queen.State);
    }
}
