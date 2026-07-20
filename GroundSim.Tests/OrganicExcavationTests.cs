using System.Diagnostics;
using GroundSim;

namespace GroundSim.Tests;

public class MaskGeneratorTests
{
    private static Grid SolidWorld(int w, int h)
    {
        var grid = new Grid(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) grid[x, y] = CellMaterial.Dirt;
        }
        return grid;
    }

    private static bool IsFourConnected(HashSet<(int X, int Y)> mask)
    {
        if (mask.Count == 0) return false;
        var seen = new HashSet<(int, int)> { mask.First() };
        var queue = new Queue<(int, int)>(seen);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (mask.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return seen.Count == mask.Count;
    }

    [Fact]
    public void TunnelMask_IsConnected_ProgressesTowardBias_StaysBounded()
    {
        var grid = SolidWorld(100, 100);
        var rng = new Random(7);
        var mask = MaskGenerator.Tunnel(grid, origin: (50, 10), target: (54, 60),
            widthMin: 2, widthMax: 3, turnJitter: 0.15, maxDeviation: 0.55, rng);

        Assert.True(IsFourConnected(mask), "tunnel must be one connected corridor");
        Assert.True(mask.Max(c => c.Y) >= 55, "tunnel should progress most of the way to the target depth");
        // Deviation bound: heading never exceeds ~31.5° from the (re-aimed)
        // bias, so horizontal wander is bounded well inside tan(0.55)*length
        // plus stamping width.
        int maxDrift = mask.Max(c => Math.Abs(c.X - 52));
        Assert.True(maxDrift < 35, $"tunnel drifted {maxDrift} cells — beyond the deviation bound");
        // Width sanity: strictly wider than a 1-cell line, far less than a blob.
        Assert.InRange(mask.Count / (double)(mask.Max(c => c.Y) - 10), 1.5, 6.0);
    }

    [Fact]
    public void ChamberMask_SingleComponent_WithinAreaBounds_AndIrregular()
    {
        var grid = SolidWorld(100, 100);
        var rng = new Random(11);
        var mask = MaskGenerator.Chamber(grid, center: (50, 50), targetArea: 60,
            edgeNoise: 0.4, generations: 4, threshold: 5, rng);

        Assert.True(IsFourConnected(mask), "chamber must have no detached crumbs");
        Assert.InRange(mask.Count, 30, 110); // within tolerance of target 60

        // Concrete irregularity metric (not eyeballs): the radial distance
        // from centroid to boundary cells varies meaningfully — a perfect
        // disc's boundary radius has near-zero relative deviation.
        double cx = mask.Average(c => (double)c.X);
        double cy = mask.Average(c => (double)c.Y);
        var boundary = mask.Where(c =>
            !mask.Contains((c.X + 1, c.Y)) || !mask.Contains((c.X - 1, c.Y))
            || !mask.Contains((c.X, c.Y + 1)) || !mask.Contains((c.X, c.Y - 1))).ToList();
        var radii = boundary.Select(c => Math.Sqrt((c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy))).ToList();
        double mean = radii.Average();
        double std = Math.Sqrt(radii.Average(r => (r - mean) * (r - mean)));
        Assert.True(std / mean > 0.08,
            $"chamber boundary too circular: relative radial deviation {std / mean:0.000}");
    }

    [Fact]
    public void Tunnel_TerminatesUponReachingTargetChamber()
    {
        var grid = SolidWorld(100, 100);
        var rng = new Random(3);
        var chamber = MaskGenerator.Chamber(grid, (50, 60), 60, 0.4, 4, 5, rng);
        var halo = new HashSet<(int, int)>();
        foreach (var (x, y) in chamber)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++) halo.Add((x + dx, y + dy));
            }
        }

        double ccx = chamber.Average(c => (double)c.X);
        double ccy = chamber.Average(c => (double)c.Y);
        var tunnel = MaskGenerator.Tunnel(grid, (50, 20), (ccx, ccy),
            2, 3, 0.15, 0.55, rng, arrived: c => halo.Contains(c));

        Assert.True(tunnel.Any(c => halo.Contains(c)), "tunnel must reach the chamber's halo");
        // Terminates upon arrival: it doesn't burrow deep into the chamber.
        int insideChamber = tunnel.Count(c => chamber.Contains(c));
        Assert.True(insideChamber <= 10,
            $"tunnel should stop at the chamber wall, but {insideChamber} cells are inside it");
    }
}

public class OrganicPlannerTests
{
    [Fact]
    public void Plan_AvoidsExistingRooms_WithBufferMargin()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig());
        // An existing excavated room square in the middle of the branch cone.
        var blocker = colony.AddExcavatedRoom(RoomType.Nursery, (50, 40, 62, 46));

        var rng = new Random(5);
        var plan = OrganicPlanner.Plan(grid, colony.Rooms, colony.Rooms[0], RoomType.Garden,
            colony.Config, rng);

        Assert.False(plan.UsedFallback, "organic placement should succeed around the blocker");
        foreach (var cell in plan.Site.Cells)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Assert.False(blocker.Contains(cell.X + dx, cell.Y + dy) && dx == 0 && dy == 0,
                        $"site cell {cell} lies inside the existing room");
                }
            }
            Assert.False(blocker.Contains(cell.X, cell.Y), $"site cell {cell} overlaps the blocker room");
        }
        // Chamber additionally respects the 1-cell buffer.
        foreach (var cell in plan.Room.Cells)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Assert.False(blocker.Contains(cell.X + dx, cell.Y + dy),
                        $"chamber cell {cell} is within the buffer of the existing room");
                }
            }
        }
    }

    [Fact]
    public void Plan_FallsBackGracefully_WhenNoOrganicPlacementExists()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
        // Carve the ENTIRE underground reachable by the branch cone to air:
        // every chamber candidate lands in already-dug space and fails the
        // freshness check on all attempts.
        for (int y = 30; y < 60; y++)
        {
            for (int x = 20; x < 100; x++) grid[x, y] = CellMaterial.Air;
        }

        var plan = OrganicPlanner.Plan(grid, colony.Rooms, colony.Rooms[0], RoomType.Garden,
            colony.Config, new Random(1));
        Assert.True(plan.UsedFallback, "with no fresh ground, the planner must use the fallback");
        Assert.NotEmpty(plan.Site.Cells);

        // And at colony level: the trigger fires, the (instantly-complete)
        // fallback room excavates, and nothing stalls.
        colony.FarmedResource = colony.Config.GardenTriggerThreshold;
        ColonyTestWorld.Run(colony, sim, 500);
        var garden = colony.GetRoom(RoomType.Garden);
        Assert.NotNull(garden);
        Assert.True(garden!.Excavated, "fallback room must complete, not stall the colony");
        Assert.Null(colony.ActiveDigSite);
    }

    [Fact]
    public void MaskGeneration_IsCheap_Measured()
    {
        // Standing instruction: measure, don't assert "cheap by design."
        // 100 full plans (chamber CA + tunnel walk + overlap checks) —
        // planning happens twice per colony lifetime, so even 1 ms/plan
        // would be irrelevant; the threshold guards against an accidental
        // O(grid²) regression.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig());
        var rng = new Random(9);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            OrganicPlanner.Plan(grid, colony.Rooms, colony.Rooms[0], RoomType.Garden, colony.Config, rng);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"100 plans took {sw.ElapsedMilliseconds} ms — mask generation should be trivially cheap");
    }
}
