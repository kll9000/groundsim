using System.Diagnostics;
using GroundSim;

namespace GroundSim.Tests;

public class PathfinderTests
{
    [Fact]
    public void FindPath_ReturnsValidAdjacentAirOnlyPath()
    {
        var grid = new Grid(20, 20); // all Air
        // Vertical wall at x=10 with a single gap at y=15.
        for (int y = 0; y < 20; y++) grid[10, y] = CellMaterial.Rock;
        grid[10, 15] = CellMaterial.Air;

        var start = (X: 2, Y: 2);
        var goal = (X: 17, Y: 3);
        var path = Pathfinder.FindPath(grid, start, goal);

        Assert.NotNull(path);
        Assert.Equal(goal, path[^1]);
        var prev = start;
        foreach (var step in path)
        {
            Assert.Equal(1, Math.Abs(step.X - prev.X) + Math.Abs(step.Y - prev.Y)); // adjacent
            Assert.Equal(CellMaterial.Air, grid[step.X, step.Y]);                    // never solid
            prev = step;
        }
        Assert.Contains((10, 15), path); // must have used the gap
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenGoalIsSealedOff()
    {
        var grid = new Grid(20, 20);
        // Box the goal in completely.
        for (int x = 14; x <= 18; x++) { grid[x, 4] = CellMaterial.Rock; grid[x, 8] = CellMaterial.Rock; }
        for (int y = 4; y <= 8; y++) { grid[14, y] = CellMaterial.Rock; grid[18, y] = CellMaterial.Rock; }

        Assert.Null(Pathfinder.FindPath(grid, (1, 1), (16, 6)));
    }

    [Fact]
    public void FindPath_StartEqualsGoal_ReturnsEmptyPath()
    {
        var grid = new Grid(5, 5);
        var path = Pathfinder.FindPath(grid, (2, 2), (2, 2));
        Assert.NotNull(path);
        Assert.Empty(path);
    }
}

public class AgentBehaviorTests
{
    /// <summary>Flat world: solid dirt at/below groundLevel, air above.</summary>
    private static Grid FlatWorld(int w, int h, int ground)
    {
        var grid = new Grid(w, h);
        for (int y = ground; y < h; y++)
        {
            for (int x = 0; x < w; x++) grid[x, y] = CellMaterial.Dirt;
        }
        return grid;
    }

    [Fact]
    public void Tick_NeverMovesMoreThanOneCell()
    {
        var grid = FlatWorld(60, 40, 20);
        var sim = new Simulation(grid);
        var agent = new Agent(grid, sim, new(), startX: 5, startY: 19,
            digRegion: (40, 20, 50, 25), dropX: 10);

        for (int t = 0; t < 500; t++)
        {
            int px = agent.X, py = agent.Y;
            agent.Tick();
            sim.Tick();
            int moved = Math.Abs(agent.X - px) + Math.Abs(agent.Y - py);
            Assert.True(moved <= 1, $"Tick {t}: agent moved {moved} cells in one tick");
        }
    }

    [Fact]
    public void Agent_CompletesFullDigCarryDropCycle()
    {
        var grid = FlatWorld(60, 40, 20);
        var sim = new Simulation(grid);
        var agent = new Agent(grid, sim, new(), startX: 45, startY: 19,
            digRegion: (40, 20, 50, 25), dropX: 10);

        for (int t = 0; t < 2000; t++) { agent.Tick(); sim.Tick(); }
        sim.RunUntilSettled();

        // Spoil arrived: dirt settled on top of the original surface at the
        // drop column (surface must now be above y=20).
        bool pileAtDrop = false;
        for (int y = 0; y < 20; y++)
        {
            if (grid[10, y] == CellMaterial.Dirt) { pileAtDrop = true; break; }
        }
        Assert.True(pileAtDrop, "Expected settled spoil above the original surface at the drop column");

        // And the dig region lost material (a hole exists).
        bool holeInRegion = false;
        for (int y = 20; y <= 25 && !holeInRegion; y++)
        {
            for (int x = 40; x <= 50; x++)
            {
                if (grid[x, y] == CellMaterial.Air) { holeInRegion = true; break; }
            }
        }
        Assert.True(holeInRegion, "Expected dug-out cells in the dig region");
    }

    [Fact]
    public void Agent_WhoseRouteIsWalledOff_GoesIdle_AndNeverEntersSolidCells()
    {
        var grid = FlatWorld(60, 40, 20);
        var sim = new Simulation(grid);
        var agent = new Agent(grid, sim, new(), startX: 3, startY: 19,
            digRegion: (45, 20, 55, 24), dropX: 5);

        // Let it start walking toward the dig region...
        for (int t = 0; t < 6; t++) { agent.Tick(); sim.Tick(); }
        Assert.Equal(AgentState.PathingToDig, agent.State);

        // ...then wall off the entire route sky-high at x=30.
        for (int y = 0; y < 20; y++) grid[30, y] = CellMaterial.Rock;

        for (int t = 0; t < 300; t++)
        {
            agent.Tick();
            sim.Tick();
            Assert.True(grid.IsAir(agent.X, agent.Y),
                $"Tick {t}: agent is inside solid material at ({agent.X},{agent.Y})");
            Assert.True(agent.X < 30, $"Tick {t}: agent crossed the wall (x={agent.X})");
        }
        Assert.Equal(AgentState.Idle, agent.State);
    }
}

public class ConcurrentAgentTests
{
    [Fact]
    public void EightAgents_ManyTicks_ConserveAllMaterial()
    {
        var grid = Grid.CreateTestWorld(100, 60, groundLevel: 30);
        var sim = new Simulation(grid);
        var claims = new HashSet<(int, int)>();
        var agents = new List<Agent>();
        for (int i = 0; i < 8; i++)
        {
            int dropX = i % 2 == 0 ? 10 + i : 90 - i;
            agents.Add(new Agent(grid, sim, claims, 40 + i * 2, 29, (35, 30, 65, 40), dropX));
        }

        int CountDiggable()
        {
            int n = 0;
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    var m = grid[x, y];
                    if (m != CellMaterial.Air && m != CellMaterial.Rock) n++;
                }
            }
            return n;
        }

        int before = CountDiggable();

        for (int t = 0; t < 4000; t++)
        {
            foreach (var a in agents) a.Tick();
            sim.Tick();
        }

        // Every unit of material is in exactly one place: settled in the
        // grid, carried by an agent, or in flight as a particle. Claims mean
        // no cell was double-dug; Dig() returning the material exactly once
        // means nothing was duplicated or lost.
        int settled = CountDiggable();
        int carried = agents.Count(a => a.Carried is not null);
        int inFlight = sim.ActiveParticleCount;
        Assert.Equal(before, settled + carried + inFlight);

        // No two agents claim-dug their way into corruption: all claims held
        // at this instant belong to agents mid-cycle; there can't be more
        // claims than agents.
        Assert.True(claims.Count <= agents.Count);
    }
}

public class ColonyScaleTests
{
    [Fact]
    public void FortyAgents_LargeGrid_TwoThousandTicks_CompleteQuickly()
    {
        // Threshold rationale: 40 agents x 2000 ticks = 80k agent-ticks, each
        // a single move/dig/drop (cheap) with occasional A* plans (bounded by
        // maxExpansions). Measured ~0.5 s on a dev machine; 5 s gives ~10x
        // CI headroom while still failing hard if per-tick work accidentally
        // becomes O(grid): 120,000 cells x 2000 ticks of even 1ns-per-cell
        // work is already 0.24 s of pure overhead, and any realistic
        // per-cell cost blows far past 5 s.
        var grid = Grid.CreateTestWorld(400, 300, groundLevel: 150);
        var sim = new Simulation(grid);
        var claims = new HashSet<(int, int)>();
        var agents = new List<Agent>();
        for (int i = 0; i < 40; i++)
        {
            int dropX = i % 2 == 0 ? 120 - i : 280 + i;
            agents.Add(new Agent(grid, sim, claims, 185 + (i * 3) % 30, 149, (185, 150, 215, 190), dropX));
        }

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < 2000; t++)
        {
            foreach (var a in agents) a.Tick();
            sim.Tick();
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Colony-scale run took {sw.ElapsedMilliseconds} ms (limit 5000 ms)");

        // Sanity: the run actually did work (agents dug and hauled).
        bool anyAir = false;
        for (int x = 185; x <= 215 && !anyAir; x++)
        {
            if (grid[x, 150] == CellMaterial.Air) anyAir = true;
        }
        Assert.True(anyAir, "Expected the dig region surface to be excavated");
    }
}
