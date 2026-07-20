using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 11.5 item 1 (Option A): the rebuilt safety net. Judges the PHYSICAL
/// PLAUSIBILITY OF OBSERVED AGENT TRAJECTORIES over time — a genuinely
/// different principle from comparing two grid-geometry predicates:
///
///   - the old net asked "do IsSupported and IsVisiblyFloating agree?" —
///     which became unfalsifiable when the Phase 11 unification made them
///     exact complements (verified: identical over 16k cells);
///   - this net asks "did any agent MOVE in a way physics forbids?" — no
///     teleports, and an agent starting a tick in fully-open air (direct
///     grid reads: all 8 neighbors Air, not bottom row) must fall exactly
///     (0,+1), never hover, climb, or sidestep.
///
/// The failure domains are disjoint, which is the independence proof: if
/// movement code stopped applying falls, the predicate pins would still pass
/// (neither function changed) while this auditor fails on the hover — and
/// that exact scenario is unit-tested below (Auditor_Flags_Hover etc.).
/// </summary>
public static class TrajectoryAuditor
{
    /// <summary>Judges one per-tick transition. gridBefore must reflect the
    /// world state at the START of the tick. Returns a violation kind, or
    /// null if the transition is physically plausible.</summary>
    public static string? JudgeTransition(Grid gridBefore, (int X, int Y) from, (int X, int Y) to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return "teleport";

        if (Terrain.IsVisiblyFloating(gridBefore, from.X, from.Y))
        {
            if (dx == 0 && dy == 1) return null;        // a genuine fall
            if (dx == 0 && dy == 0) return "hover-in-open-air";
            return "airborne-but-not-falling";
        }
        return null;
    }
}

public class TrajectoryAuditorSelfTests
{
    private static Grid OpenWorld()
    {
        var grid = new Grid(20, 20);
        for (int x = 0; x < 20; x++) grid[x, 15] = CellMaterial.Dirt; // floor
        return grid;
    }

    [Fact]
    public void Auditor_Flags_Teleports()
    {
        var grid = OpenWorld();
        Assert.Equal("teleport", TrajectoryAuditor.JudgeTransition(grid, (5, 14), (8, 14)));
        Assert.Equal("teleport", TrajectoryAuditor.JudgeTransition(grid, (5, 14), (5, 10)));
    }

    [Fact]
    public void Auditor_Flags_Hover_TheFailureMode_PredicatePinsCannotSee()
    {
        // An agent sitting motionless in fully-open air: if fall application
        // were accidentally removed from movement code, the predicate-pin
        // tests would still pass (both predicates unchanged) — ONLY this
        // auditor catches it. This test is the independence demonstration.
        var grid = OpenWorld();
        Assert.Equal("hover-in-open-air", TrajectoryAuditor.JudgeTransition(grid, (10, 5), (10, 5)));
    }

    [Fact]
    public void Auditor_Flags_AirborneLateralOrUpwardMotion()
    {
        var grid = OpenWorld();
        Assert.Equal("airborne-but-not-falling", TrajectoryAuditor.JudgeTransition(grid, (10, 5), (11, 5)));
        Assert.Equal("airborne-but-not-falling", TrajectoryAuditor.JudgeTransition(grid, (10, 5), (10, 4)));
        Assert.Equal("airborne-but-not-falling", TrajectoryAuditor.JudgeTransition(grid, (10, 5), (11, 6)));
    }

    [Fact]
    public void Auditor_Accepts_LegitimateMovement()
    {
        var grid = OpenWorld();
        grid[8, 14] = CellMaterial.Dirt; // a wall cell

        Assert.Null(TrajectoryAuditor.JudgeTransition(grid, (5, 14), (6, 14))); // walk on floor
        Assert.Null(TrajectoryAuditor.JudgeTransition(grid, (10, 5), (10, 6))); // genuine fall
        Assert.Null(TrajectoryAuditor.JudgeTransition(grid, (7, 14), (7, 13))); // climb beside wall
        Assert.Null(TrajectoryAuditor.JudgeTransition(grid, (5, 14), (5, 14))); // idle on floor
    }
}

public class ColonyTrajectoryAuditTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    public void FullColonyRun_EveryWorkerTransition_IsPhysicallyPlausible(int seed)
    {
        var grid = Grid.CreateTestWorld(120, 60, groundLevel: 30, seed: seed);
        var sim = new Simulation(grid, seed: seed);
        var colony = Colony.Found(grid, sim, new ColonyConfig(),
            ColonyTestWorld.Chamber, startX: 56, startY: 29, seed: seed);
        colony.Nodes.Add(new ResourceNode(15, 29, 500));
        colony.Nodes.Add(new ResourceNode(105, 29, 500));

        var previous = new Dictionary<object, ((int X, int Y) Pos, bool Airborne)>();
        for (int t = 0; t < 9000; t++)
        {
            // Snapshot airborne state from the grid BEFORE the tick mutates it.
            foreach (var (id, pos) in Workers(colony))
            {
                previous[id] = (pos, Terrain.IsVisiblyFloating(grid, pos.X, pos.Y));
            }

            colony.Tick();
            sim.Tick();

            foreach (var (id, pos) in Workers(colony))
            {
                if (!previous.TryGetValue(id, out var prev)) continue; // spawned this tick

                int dx = pos.X - prev.Pos.X, dy = pos.Y - prev.Pos.Y;
                Assert.True(Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1,
                    $"seed {seed} tick {t}: teleport {prev.Pos} -> {pos}");
                if (prev.Airborne)
                {
                    Assert.True(dx == 0 && dy == 1,
                        $"seed {seed} tick {t}: airborne at {prev.Pos} but moved ({dx},{dy}) instead of falling");
                }
            }
        }
    }

    private static IEnumerable<(object id, (int X, int Y) pos)> Workers(Colony c)
    {
        foreach (var t in c.Tenders) yield return (t, (t.X, t.Y));
        foreach (var f in c.Foragers) yield return (f, (f.X, f.Y));
        foreach (var m in c.Majors) yield return (m, (m.X, m.Y));
    }
}
