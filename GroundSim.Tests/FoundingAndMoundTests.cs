using GroundSim;

namespace GroundSim.Tests;

public class FoundingShapeTests
{
    [Fact]
    public void FoundingPlan_ProducesConnectedShaftAndChamber_NotARect()
    {
        var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: 5);
        var plan = OrganicPlanner.PlanFounding(grid, entranceX: 112, new ColonyConfig(), new Random(5));

        Assert.False(plan.UsedFallback, "fresh ground: organic founding should succeed");
        var site = plan.Site.Cells.ToHashSet();

        // Connected as one excavation (chimney included — it shares columns
        // with the shaft mouth).
        var seen = new HashSet<(int, int)> { site.First() };
        var queue = new Queue<(int, int)>(seen);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (site.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        Assert.Equal(site.Count, seen.Count);

        // The shaft (below-surface site cells above the chamber) is narrow
        // and straight: per-row width ≤ 6 and total horizontal wander ≤ 8 —
        // meaningfully tighter than lateral tunnels (jitter 0.15/dev 0.55
        // vs the shaft's 0.03/0.08). Phase 15: all bounds ×GridScale (the
        // shaft bore is 2×GridScale = 4 cells now; same physical shape).
        int chamberTop = plan.HomeRoom.Y0;
        var shaftRows = site.Where(c => c.Y >= 60 && c.Y < chamberTop)
            .GroupBy(c => c.Y).ToList();
        Assert.True(shaftRows.Count >= 10, "a real shaft spans multiple rows");
        foreach (var row in shaftRows)
        {
            Assert.True(row.Count() <= 6, $"shaft row y={row.Key} is {row.Count()} wide — not a narrow shaft");
        }
        int minX = shaftRows.SelectMany(r => r).Min(c => c.X);
        int maxX = shaftRows.SelectMany(r => r).Max(c => c.X);
        Assert.True(maxX - minX <= 8, $"shaft wanders {maxX - minX} columns — should be near-vertical");

        // The chamber is a real blob at depth, below the shaft.
        // Phase 15: area bounds ×GridScale² (12..90 → 48..360).
        Assert.InRange(plan.HomeRoom.Cells.Count, 48, 360);
        Assert.True(plan.HomeRoom.Y0 >= 68, "chamber sits below a real shaft length"); // 60 + 4×GridScale
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(10)]
    public void OrganicFounding_QueenCompletes_AndPhase6GuaranteesHold(int seed)
    {
        // Includes the three seeds that livelocked before the unreachable-
        // target blacklist fix (2, 7, 10).
        var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: seed);
        var sim = new Simulation(grid, seed: seed);
        var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX: 112, seed: seed);

        int t = 0;
        while (colony.Queen.State == QueenState.Founding && t < 180_000)
        {
            colony.Tick();
            sim.Tick();
            t++;
        }
        Assert.Equal(QueenState.Laying, colony.Queen.State);
        Assert.NotNull(colony.Milestones.HomeFoundedTick);
        Assert.Equal(colony.Config.StarterResource, colony.FarmedResource);
        // Phase 12.5 item 2: the founding dig actually FINISHED, by the
        // project's own completion definition (frontier-accessible rule) —
        // not just that the state flipped.
        Assert.False(colony.Rooms[0].HasRemainingDiggable(grid),
            "queen entered Laying with frontier-accessible diggable cells remaining in the home chamber");
        Assert.True(grid.IsAir(colony.Queen.X, colony.Queen.Y), "queen settled in a real air cell");
        Assert.True(colony.Rooms[0].Contains(colony.Queen.X, colony.Queen.Y), "queen settled inside the home chamber");

        // Never moves again (Phase 6 guarantee).
        var pos = (colony.Queen.X, colony.Queen.Y);
        ColonyTestWorld.Run(colony, sim, 3000);
        Assert.Equal(pos, (colony.Queen.X, colony.Queen.Y));
        Assert.Equal(QueenState.Laying, colony.Queen.State);
    }

    [Fact]
    public void FoundingFallback_Engages_AndFoundingStillCompletes()
    {
        // Force organic failure: carve everything below the surface to air
        // so every chamber candidate fails the fresh-ground check. The
        // fallback rect at the surface still contains its top row of real
        // material only where uncarved — use a shallower carve so the
        // fallback rect (surface..+3) keeps real dirt to dig.
        var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: 4);
        for (int y = 72; y < 120; y++) // Phase 15: carve depth ×GridScale
        {
            for (int x = 10; x < 230; x++) grid[x, y] = CellMaterial.Air;
        }

        var plan = OrganicPlanner.PlanFounding(grid, entranceX: 112, new ColonyConfig(), new Random(4));
        Assert.True(plan.UsedFallback, "no room for a shaft+chamber: must fall back to the rect");

        var sim = new Simulation(grid, seed: 4);
        var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX: 112, seed: 4);

        // Phase 12.5 item 3: pin that the fallback site contains REAL undug
        // material before founding runs (the Phase 11.5 pattern) — a future
        // carve-depth or geometry shift must not silently degenerate this
        // back into a nothing-to-dig test.
        int Diggable() => colony.Rooms[0].Cells.Count(c =>
            grid[c.X, c.Y] != CellMaterial.Air && grid[c.X, c.Y] != CellMaterial.Rock);
        int diggableBefore = Diggable();
        Assert.True(diggableBefore >= 80, // Phase 15: ×GridScale² (fallback rect is 4× the cells)
            $"fallback founding chamber should hold real undug material, found {diggableBefore}");

        int t = 0;
        while (colony.Queen.State == QueenState.Founding && t < 180_000)
        {
            colony.Tick();
            sim.Tick();
            t++;
        }
        Assert.Equal(QueenState.Laying, colony.Queen.State);
        Assert.NotNull(colony.Milestones.HomeFoundedTick);
        int diggableAfter = Diggable();
        Assert.True(diggableBefore > diggableAfter,
            $"founding must have genuinely removed material ({diggableBefore} -> {diggableAfter})");
        Assert.False(colony.Rooms[0].HasRemainingDiggable(grid),
            "fallback founding should complete by the frontier-accessible rule");
    }
}

public class SpoilMoundTests
{
    [Fact]
    public void Spoil_BuildsOnBothSidesOfTheEntrance_NotOneFixedColumn()
    {
        var grid = Grid.CreateTestWorld(240, 120, groundLevel: 60, seed: 6);
        var sim = new Simulation(grid, seed: 6);
        var colony = Colony.Found(grid, sim, new ColonyConfig(), entranceX: 112, seed: 6);
        colony.Nodes.Add(new ResourceNode(30, 59, 10_000));
        colony.Nodes.Add(new ResourceNode(210, 59, 10_000));

        // Through founding and at least the garden excavation.
        int t = 0;
        while (t < 240_000 && colony.Milestones.GardenExcavatedTick is null)
        {
            colony.Tick();
            sim.Tick();
            t++;
        }
        Assert.NotNull(colony.Milestones.GardenExcavatedTick);

        // Settled material ABOVE the original surface (y < 60), per column.
        var perColumn = new Dictionary<int, int>();
        int left = 0, right = 0, total = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < 60; y++)
            {
                var m = grid[x, y];
                if (m == CellMaterial.Air) continue;
                total++;
                perColumn[x] = perColumn.GetValueOrDefault(x) + 1;
                if (x < colony.EntranceX) left++;
                else if (x > colony.EntranceX) right++;
            }
        }

        // Phase 15: volume bound ×GridScale² (same physical spoil is 4× the
        // cells), column-spread bound ×GridScale; fractions unchanged.
        Assert.True(total >= 240, $"expected substantial surface spoil, found {total} cells");
        Assert.True(left >= total / 5, $"left of entrance holds only {left}/{total} spoil cells — lopsided");
        Assert.True(right >= total / 5, $"right of entrance holds only {right}/{total} spoil cells — lopsided");
        Assert.True(perColumn.Count >= 16, $"spoil spread across only {perColumn.Count} columns");
        Assert.True(perColumn.Values.Max() < total / 2,
            "more than half the spoil sits in a single column — a spike, not a mound");
    }
}
