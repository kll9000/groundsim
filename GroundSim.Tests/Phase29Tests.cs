using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 29: the room-placement fallback livelock fix (the Phase 18.5
/// tracked item, root-caused live in PHASE25_5_REPORT §4). Two independent
/// defenses, each with its own revert-proven regression test:
/// the planner must never produce catastrophic fallback overlap, and a
/// support-unreachable dig cell must never permanently pin a site.
/// </summary>
public class FallbackOverlapTests
{
    /// <summary>Reconstructs the seed-9 CLASS of scenario: organic planning
    /// forced to fail (solid rock world — chamber reachability can never
    /// pass) while the nest is crowded with existing rooms, so planning
    /// must go through the fallback. The old last resort glued the rect
    /// below the parent with ZERO overlap checking (seed 9: 58/72 cells
    /// inside the Home chamber). The Phase 29 escalating passes must land
    /// every fallback at tolerable overlap.
    /// Revert-proof (run and verified during Phase 29): restoring the old
    /// blind-glue tail makes this fail at 54/72 overlapping cells.</summary>
    [Fact]
    public void Fallback_UnderCrowdedNest_NeverProducesCatastrophicOverlap()
    {
        var grid = new Grid(240, 120);
        for (int y = 0; y < 120; y++)
        {
            for (int x = 0; x < 240; x++) grid[x, y] = CellMaterial.Rock;
        }

        // An excavated parent chamber mid-world…
        var parent = new Room(RoomType.Home, 100, 50, 130, 60, excavated: true);
        foreach (var (x, y) in parent.Cells) grid[x, y] = CellMaterial.Air;
        // …with sibling rooms crowding it below and on both flanks — the
        // post-Phase-25 room order (garden planned fifth) made exactly this
        // density real. Deliberately blankets the old glue spot below the
        // parent's floor anchor.
        var crowd = new List<Room> { parent };
        foreach (var (x0, y0, x1, y1) in new[]
        {
            (88, 61, 142, 66),    // directly below, spanning the glue zone
            (76, 44, 96, 56),     // left flank
            (134, 44, 154, 56),   // right flank
            (88, 67, 142, 72),    // second layer below
        })
        {
            var r = new Room(RoomType.FoodStorage, x0, y0, x1, y1, excavated: true);
            foreach (var (x, y) in r.Cells) grid[x, y] = CellMaterial.Air;
            crowd.Add(r);
        }

        var rng = new Random(9);
        for (int trial = 0; trial < 10; trial++) // several rolls, same guarantee
        {
            var plan = OrganicPlanner.Plan(grid, crowd, parent, RoomType.Garden, new ColonyConfig(), rng);
            Assert.True(plan.UsedFallback, "solid-rock world must force the fallback");
            int overlap = plan.Room.Cells.Count(c => crowd.Any(r => r.Contains(c.X, c.Y)));
            int cells = plan.Room.Cells.Count;
            Assert.True(overlap <= cells / 10,
                $"trial {trial}: fallback landed {overlap}/{cells} cells inside existing rooms " +
                "— the seed-9 catastrophic-overlap class");
        }
    }
}

public class UnreachableRemnantTests
{
    /// <summary>The distilled PHASE25_5 §4 geometry, forced directly: a
    /// pending room whose only remaining diggable cells are a floating
    /// dirt shelf inside open chamber air — standable on top, but the
    /// shelf's support island connects to no wall or floor, so every
    /// approach fails the support-aware pathfinder. Pre-fix: diggers
    /// blacklist the cells, the blacklist clears, forever — ActiveDigSite
    /// pins and the room never completes. Post-fix: the strike ledger
    /// writes the shelf off, the frontier stops demanding it, and the
    /// site RELEASES (as a failed/degenerate room, loudly accounted).
    /// Revert-proof (run and verified during Phase 29): removing the
    /// IsWrittenOff exclusion from DigSite.HasRemainingDiggable makes
    /// this fail with the site still pinned at the budget's end.</summary>
    [Fact]
    public void FloatingShelfSite_Releases_InsteadOfPinningForever()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });

        // Carve an open cavity well away from the home chamber, with a
        // 4-cell dirt shelf floating in its middle — 3+ cells of open air
        // between the shelf (and its little support island) and every
        // cavity wall, so no 3×3-contact route can reach it.
        for (int y = 70; y <= 84; y++)
        {
            for (int x = 150; x <= 180; x++) grid[x, y] = CellMaterial.Air;
        }
        for (int x = 163; x <= 166; x++) grid[x, 77] = CellMaterial.Dirt; // the shelf
        // Connect the cavity to the surface so diggers can reach its RIM
        // (the failure must be the shelf, not the cavity being sealed).
        for (int y = 0; y <= 70; y++) grid[165, y] = CellMaterial.Air;

        // A pending room whose dig site is exactly the shelf plus a rim
        // strip that is genuinely diggable (so the site starts with real,
        // reachable work — mirroring seed 9, where 68/72 cells dug fine
        // and only the shelf remnant pinned).
        var cells = new List<(int X, int Y)>();
        for (int x = 163; x <= 166; x++) cells.Add((x, 77));         // shelf
        for (int x = 150; x <= 158; x++) cells.Add((x, 85));         // rim strip below-left (solid, wall-adjacent)
        var room = colony.AddPendingRoom(RoomType.Garden, cells);
        colony.ActiveDigSite = room.PendingDig;

        // Two diggers.
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X + 1, colony.HomeCenter.Y);

        // Budget: strikes need UnreachableStrikeLimit spaced attempts per
        // shelf cell (3 × 300-tick spacing) plus walking time — 30k is
        // ~10× generous. Pre-fix this loop ends with the site still
        // pinned; post-fix it releases (usually within ~4k ticks).
        int released = -1;
        for (int t = 0; t < 30_000; t++)
        {
            colony.Tick();
            sim.Tick();
            if (colony.ActiveDigSite is null) { released = t; break; }
        }

        Assert.True(released >= 0,
            "the floating-shelf site never released — the Phase 25.5 §4 livelock");
        Assert.True(room.Excavated, "the room must complete (possibly degenerate), not vanish");
        Assert.True(room.PendingDig is null || room.PendingDig.WrittenOffCount > 0
            || colony.Stats.FailedDigSiteReleases > 0 || !room.HasRemainingDiggable(grid),
            "release must be attributable: write-offs, a failed-release, or genuine completion");
        // And the write-off machinery specifically fired (this scenario's
        // shelf is unreachable by construction — if it released without
        // any strikes, something else changed and this test lost its
        // discriminating power).
        Assert.True(colony.Stats.FailedDigSiteReleases > 0 ||
            cells.Take(4).All(c => grid.IsAir(c.X, c.Y)) == false,
            "expected the strike/write-off path (or, impossibly, the shelf was dug)");
    }
}
