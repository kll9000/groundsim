using GroundSim;
using GroundSim.Render;

namespace GroundSim.Tests;

/// <summary>
/// Phase 21: remains decay (the deliberate conservation exception), the
/// exhumation fix (Remains is inert, not spoil), the day counter, and the
/// halved circle scale.
/// </summary>
public class RemainsDecayTests
{
    [Fact]
    public void SettledRemains_DecayToAir_AfterThreshold_WithAccounting()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
            RemainsDecayTicks = 500,
        });
        var hc = colony.HomeCenter;
        colony.BuryRemains(hc.X - 2, hc.Y - 1);
        // The remains cell rests on the floor below the lay-down point.
        Assert.Equal(CellMaterial.Remains, grid[hc.X - 2, hc.Y]);
        Assert.Equal(1, colony.Stats.Burials);

        ColonyTestWorld.Run(colony, sim, 600);

        // DELIBERATE conservation exception: the matter is destroyed, and
        // the ledger records it — Burials == standing remains (0) + decayed.
        Assert.NotEqual(CellMaterial.Remains, grid[hc.X - 2, hc.Y]);
        Assert.Equal(1, colony.Stats.RemainsDecayed);
    }

    [Fact]
    public void UnburiedCorpse_DecaysWhereItFell_LedgerCloses()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 300,
            WorkerLifespanJitterTicks = 0,
            RemainsDecayTicks = 500,
        });
        // No graveyard, no soldiers: the corpse must decay where it fell.
        colony.Spawn(Caste.Minim, colony.HomeCenter.X, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 1_000); // dies ~300, decays ~800

        Assert.Empty(colony.Corpses);
        Assert.Equal(1, colony.Stats.Deaths);
        Assert.Equal(1, colony.Stats.CorpsesDecayed);
        Assert.Equal(0, colony.Stats.Burials);
        // The four-term death ledger closes.
        Assert.Equal(colony.Stats.Deaths,
            colony.Corpses.Count + colony.Stats.Burials + colony.Stats.CorpsesDecayed);
    }

    [Fact]
    public void BuriedRemains_AreNotExhumedByMaintenance_Phase21Regression()
    {
        // THE Part A root cause: Remains was diggable, so the graveyard's
        // own maintenance site treated each burial as blockage, dug it up,
        // and hauled it to the mound (measured pre-fix: 188 burials → 1
        // remains cell in the graveyard, 167 outside). Remains is now inert:
        // it can't be a dig target and doesn't count as remaining work.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
            RemainsDecayTicks = 0, // decay disabled: exhumation is the ONLY way it could vanish
        });
        var h = ColonyTestWorld.Chamber;
        var graveyard = colony.AddExcavatedRoom(RoomType.Graveyard, (h.X0 - 10, h.Y0 + 2, h.X0 - 1, h.Y1));
        colony.Corpses.Add(new Colony.Corpse { X = colony.HomeCenter.X - 3, Y = colony.HomeCenter.Y });
        colony.Stats.Deaths = 1;
        // A soldier AND an active digging economy that would previously
        // exhume: the graveyard registered as a dig-capable region.
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X + 2, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 12_000);

        Assert.Equal(1, colony.Stats.Burials);
        int remainsInGraveyard = graveyard.Cells.Count(c => grid[c.X, c.Y] == CellMaterial.Remains);
        Assert.Equal(1, remainsInGraveyard); // still there — never exhumed
        // And the inertness is visible to the frontier predicates directly:
        Assert.False(graveyard.HasRemainingDiggable(grid),
            "a buried body must not read as remaining dig work");
    }
}

public class DayCounterTests
{
    [Fact]
    public void DayNumber_CorrespondsToTicks()
    {
        Assert.Equal(1.0, SimCalendar.DayNumber(0), 6);
        Assert.Equal(2.0, SimCalendar.DayNumber(SimCalendar.TicksPerDay), 6);
        Assert.Equal(1.5, SimCalendar.DayNumber(SimCalendar.TicksPerDay / 2), 6);
        // The chosen presentation scale: one display-day = 24 sim-minutes
        // at the canonical 30 ticks/sec (INVENTED, presentation-only).
        Assert.Equal(24 * 60 * 30, SimCalendar.TicksPerDay);
    }
}

public class CircleScaleTests
{
    [Fact]
    public void CasteCircles_AreHalved_RatiosPreserved()
    {
        // Phase 21 Part C: Kevin's halving. Unit table unchanged (2/3/4/4/5,
        // pinned by CasteSizeTests); the pixel conversion is halved.
        Assert.Equal(0.5, GridRenderer.CasteCircleScale);
        Assert.Equal(2, GridRenderer.CirclePixelDiameter(GridRenderer.SizeUnits(Caste.Minim)));
        Assert.Equal(3, GridRenderer.CirclePixelDiameter(GridRenderer.SizeUnits(Caste.Gardener)));
        Assert.Equal(4, GridRenderer.CirclePixelDiameter(GridRenderer.SizeUnits(Caste.Forager)));
        Assert.Equal(4, GridRenderer.CirclePixelDiameter(GridRenderer.QueenSizeUnits));
        Assert.Equal(5, GridRenderer.CirclePixelDiameter(GridRenderer.SizeUnits(Caste.Soldier)));
    }
}
