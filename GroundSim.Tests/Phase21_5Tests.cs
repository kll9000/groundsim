using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 21.5: hardening Phase 21's coverage per verification — a
/// genuinely discriminating exhumation test, the phantom-pickup race
/// forced for real, and the Agent-clause reachability question resolved.
///
/// The death/matter ledgers, stated once, correctly (verification item 5):
///   Deaths  == corpses-in-world + corpses-in-jaws + Burials + CorpsesDecayed
///   Burials == standing Remains cells + RemainsDecayed + physics-displaced
/// (RemainsDecayed belongs to the SECOND equation only.)
/// </summary>
public class ExhumationDiscriminationTests
{
    [Fact]
    public void MaintenanceOverRemains_NeverActivates_NeverExhumes_NeverLivelocks()
    {
        // Phase 21.5 item 1: the Phase 21 version of this test never gave
        // maintenance a chance to exhume (AddExcavatedRoom registers no
        // maintenance site), so reverting the fix still passed. This one
        // reconstructs the real pre-fix failure geometry: a completed
        // graveyard REGISTERED for maintenance, standing remains inside it,
        // and an idle Soldier crew that would previously have been drafted
        // to dig the remains out.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
            RemainsDecayTicks = 0, // decay off: exhumation is the only way remains could move
        });
        var h = ColonyTestWorld.Chamber;
        var graveyard = colony.AddExcavatedRoom(RoomType.Graveyard, (h.X0 - 10, h.Y0 + 2, h.X0 - 1, h.Y1));
        // The step Phase 21's test was missing: register the graveyard as a
        // standing maintenance responsibility, exactly as ManageExcavation
        // does for every genuinely dug room.
        colony.MaintenanceSites.Add(new DigSite(graveyard.Cells));

        // A buried body standing in the registered site.
        colony.BuryRemains(graveyard.Center.X, graveyard.Y0 + 1);
        var remainsCell = graveyard.Cells.First(c => grid[c.X, c.Y] == CellMaterial.Remains);
        colony.SpoilDropX = 200;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 10_000);

        // Discrimination, all three ways:
        // (a) Full revert (remains diggable everywhere): maintenance
        //     activates, the soldier digs the body and hauls it out —
        //     this assertion catches it.
        Assert.Equal(CellMaterial.Remains, grid[remainsCell.X, remainsCell.Y]);
        // (b) Frontier-only revert (HasRemainingDiggable counts remains,
        //     Agent still refuses): the maintenance site activates and can
        //     NEVER complete — a maintenance livelock that would block all
        //     future room digs. This assertion catches it.
        Assert.Null(colony.ActiveDigSite);
        // (c) And no remains anywhere outside the graveyard (nothing was
        //     exhumed-and-dropped mid-haul).
        int outside = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                if (grid[x, y] == CellMaterial.Remains && !graveyard.Contains(x, y)) outside++;
            }
        }
        Assert.Equal(0, outside);
    }

    [Fact]
    public void RemainsInsideAnActiveDigSite_AreSkipped_AndDoNotBlockCompletion()
    {
        // Phase 21.5 item 3, resolved: Agent.IsDiggable's Remains clause is
        // NOT dead code — it is the only guard on the path where remains sit
        // inside an ACTIVELY-DUG site (e.g. an emergency lay-down landing in
        // a planned room's footprint), which never passes through the
        // maintenance-activation gate the frontier predicates protect. The
        // deeper property is AGREEMENT: agent and frontier must give the
        // same answer for remains, or you get exhumation (agent digs what
        // frontier ignores... this test) / livelock (frontier demands what
        // agent refuses... the maintenance test above).
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
            RemainsDecayTicks = 0,
        });
        // An active dig site glued to the chamber wall, with one Remains
        // cell planted inside it amid the dirt.
        grid[124, 64] = CellMaterial.Remains;
        colony.ActiveDigSite = DigSite.FromRect(122, 62, 132, 67);
        colony.SpoilDropX = 200;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X + 1, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 30_000);

        // The body was left in peace (fails if Agent.IsDiggable's clause is
        // reverted — the agent would dig and haul it to column 200)...
        Assert.Equal(CellMaterial.Remains, grid[124, 64]);
        // ...and it did not block completion (the site is frontier-done:
        // fails with a stuck site if the frontier predicates are reverted).
        Assert.False(colony.ActiveDigSite!.HasRemainingDiggable(grid),
            "the remains cell must not count as remaining dig work");
    }
}

public class PhantomPickupRaceTests
{
    [Fact]
    public void CorpseDecaysMidWalk_HaulerStandsDown_NoPhantomBurial()
    {
        // Phase 21.5 item 2: verification proved this guard load-bearing
        // (ledger off by exactly 1 in 31/400 samples with it removed) but
        // nothing exercised it. Force the race deterministically: the
        // hauler commits to a distant corpse whose decay lands mid-walk.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
            RemainsDecayTicks = 100, // decays at t≈100; the walk takes far longer
        });
        var h = ColonyTestWorld.Chamber;
        colony.AddExcavatedRoom(RoomType.Graveyard, (h.X0 - 10, h.Y0 + 2, h.X0 - 1, h.Y1));
        // Distant surface corpse: ~80+ cells of walking from home.
        colony.Corpses.Add(new Colony.Corpse { X = 30, Y = 59, DiedAtTick = 0 });
        colony.Stats.Deaths = 1;
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);

        // Let the soldier claim and start walking, then run past both the
        // decay tick and the would-be arrival tick.
        ColonyTestWorld.Run(colony, sim, 2_500);

        // The corpse decayed mid-walk; the hauler stood down cleanly.
        Assert.Empty(colony.Corpses);
        Assert.Equal(1, colony.Stats.CorpsesDecayed);
        Assert.False(colony.Soldiers[0].CarryingCorpse);
        Assert.Equal(0, colony.Stats.Burials); // NO phantom burial
        // Deaths ledger: 1 == 0 (world) + 0 (jaws) + 0 (buried) + 1 (decayed).
        Assert.Equal(colony.Stats.Deaths,
            colony.Corpses.Count + colony.Stats.Burials + colony.Stats.CorpsesDecayed);
        // And the soldier is back on normal duty (guarding), not stuck.
        Assert.True(colony.Soldiers[0].OnGuard, "hauler should have returned to guard duty");
    }
}
