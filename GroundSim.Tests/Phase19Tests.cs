using GroundSim;
using GroundSim.Render;

namespace GroundSim.Tests;

/// <summary>
/// Phase 19: Soldier caste (Major's successor) and the caste-size visual
/// hierarchy.
/// </summary>
public class CasteSizeTests
{
    [Fact]
    public void CasteCircleSizes_MatchKevinsSpecExactly()
    {
        // The spec table, pinned against the renderer's single source of
        // truth (the same values drive drawing, dirty-marking, and legend).
        Assert.Equal(2, GridRenderer.SizeUnits(Caste.Minim));
        Assert.Equal(3, GridRenderer.SizeUnits(Caste.Gardener));
        Assert.Equal(4, GridRenderer.SizeUnits(Caste.Forager));
        Assert.Equal(5, GridRenderer.SizeUnits(Caste.Soldier));
        // Queen and Forager share 4 — intentional per spec, not an error.
        Assert.Equal(4, GridRenderer.QueenSizeUnits);
        Assert.Equal(GridRenderer.SizeUnits(Caste.Forager), GridRenderer.QueenSizeUnits);
    }

    [Fact]
    public void CircleSize_IsPurelyVisual_GridPositionUnchanged()
    {
        // A Soldier's 5-unit circle must not change where the agent IS: run
        // a soldier to its guard post and confirm its coordinates are a
        // single grid cell (the pathing/dig/haul position), not anything
        // circle-derived.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0 });
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
        ColonyTestWorld.Run(colony, sim, 2000);
        var s = colony.Soldiers[0];
        Assert.True(grid.InBounds(s.X, s.Y), "agent position is a plain grid cell");
        Assert.True(grid.IsAir(s.X, s.Y), "agent occupies exactly one air cell regardless of circle size");
    }
}

public class SoldierGuardTests
{
    [Fact]
    public void Soldier_SwitchesBetweenDigAndGuard_NeverStuckInNeither()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
        var soldier = colony.Soldiers[0];

        // Phase 1: no dig work → takes up the guard post.
        ColonyTestWorld.Run(colony, sim, 2000);
        Assert.True(soldier.OnGuard, "with nothing to dig, the Soldier should stand guard");
        Assert.Equal(colony.GuardPost, (soldier.X, soldier.Y));

        // Phase 2: dig work appears → guard is abandoned for the dig.
        // Site glued to the chamber's right wall so the dig frontier has
        // real air contact (a detached rect in solid dirt is undiggable).
        // Phase 19.5: before/after DELTA (the structural fix for the
        // vacuous-absolute-state pattern), with a setup guard that the
        // site genuinely starts solid.
        int SolidInSite()
        {
            int n = 0;
            for (int y = 62; y <= 67; y++)
            {
                for (int x = 122; x <= 132; x++)
                {
                    if (grid[x, y] != CellMaterial.Air) n++;
                }
            }
            return n;
        }
        int solidBefore = SolidInSite();
        Assert.True(solidBefore > 0, "test setup: the dig site must start with real material");
        colony.ActiveDigSite = DigSite.FromRect(122, 62, 132, 67);
        colony.SpoilDropX = 180;
        ColonyTestWorld.Run(colony, sim, 3000);
        Assert.True(SolidInSite() < solidBefore, "Soldier should have dug once a site went active");
        Assert.False(soldier.OnGuard, "digging Soldier must not report OnGuard");

        // Phase 3: dig work ends → back to the guard post. Never stuck.
        colony.ActiveDigSite = null;
        ColonyTestWorld.Run(colony, sim, 3000);
        Assert.True(soldier.OnGuard, "with the dig done, the Soldier should return to guard");
        Assert.Equal(colony.GuardPost, (soldier.X, soldier.Y));
    }

    [Fact]
    public void GuardPost_SitsAtTheSurfaceMouth_AcrossMoundStates()
    {
        // Phase 19.5: the post must be at/near the surface opening — NOT
        // down the shaft at the chamber floor (the original scan fell
        // straight through the open entrance column; verification finding).
        // Checked across states, not just the one that happens to hold.
        var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: 3);
        var sim = new Simulation(grid, 3);
        // Organic founding plans a real shaft+chamber under entranceX; the
        // guard post must not depend on how much of it has been dug yet.
        var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX: 112, seed: 3);

        // State 1: fresh world — the post stands ON the original surface
        // beside the entrance column (rim ground is solid at groundLevel).
        var post = colony.GuardPost;
        Assert.True(post.Y == 59, $"fresh-world post should stand on the surface (y=59), got {post}");
        Assert.True(Math.Abs(post.X - colony.EntranceX) <= 8, $"post {post} should flank the entrance column");
        Assert.True(grid.IsAir(post.X, post.Y) && !grid.IsAir(post.X, post.Y + 1),
            "post must be a standable surface cell");

        // State 2: the mound grows — pile dirt on the rim column; the post
        // must ride UP with the rim, never sink into the shaft.
        for (int y = 53; y <= 58; y++) grid[post.X, y] = CellMaterial.Dirt; // six cells of mound on the rim
        var raised = colony.GuardPost;
        Assert.Equal(post.X, raised.X);
        Assert.True(raised.Y < post.Y, $"post should rise with the mound: was {post}, now {raised}");
        Assert.True(grid.IsAir(raised.X, raised.Y) && !grid.IsAir(raised.X, raised.Y + 1),
            "raised post must still be a standable surface cell");

        // State 3: carve the shaft fully open (simulate completed founding
        // excavation down the entrance column) — the post must NOT fall
        // through the open shaft to the chamber below.
        for (int y = 60; y < 100; y++) grid[colony.EntranceX, y] = CellMaterial.Air;
        var afterShaft = colony.GuardPost;
        Assert.True(afterShaft.Y <= 59, $"post {afterShaft} must stay at the surface, not fall down the shaft");
    }

    [Fact]
    public void GuardingSoldier_NeverGathersProcessesOrTends()
    {
        // Adversarial role purity: a guarding Soldier surrounded by
        // temptations — full nodes, unprocessed raw, an untended egg —
        // touches none of them.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        colony.Nodes.Add(new ResourceNode(80, 59, 100));
        colony.RawMaterial = 25;
        colony.LayEgg();
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
        double farmedBefore = colony.FarmedResource;
        int untendedMaturation = colony.Config.EggMaturationTicks;

        int t = 0;
        while (colony.Eggs.Count > 0 && t < 100_000)
        {
            Assert.False(colony.Eggs[0].TendedThisTick, "a Soldier tended an egg — role purity violated");
            colony.Tick();
            sim.Tick();
            t++;
        }

        Assert.True(t >= untendedMaturation, "egg matured faster than untended — something tended it");
        Assert.Equal(100, colony.Nodes[0].Remaining, 3);
        Assert.Equal(0, colony.Stats.RawGatheredByForagers, 3);
        Assert.Equal(25, colony.RawMaterial, 3);
        Assert.Equal(farmedBefore, colony.FarmedResource, 3);
    }
}
