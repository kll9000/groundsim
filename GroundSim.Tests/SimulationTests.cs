using System.Diagnostics;
using GroundSim;

namespace GroundSim.Tests;

public class GridTests
{
    [Fact]
    public void Dig_ConvertsDirtToAir_AndReturnsMaterial()
    {
        var grid = new Grid(10, 10);
        grid[5, 5] = CellMaterial.Dirt;

        var dug = grid.Dig(5, 5);

        Assert.Equal(CellMaterial.Dirt, dug);
        Assert.Equal(CellMaterial.Air, grid[5, 5]);
    }

    [Fact]
    public void Dig_Air_ReturnsNull()
    {
        var grid = new Grid(10, 10);
        Assert.Null(grid.Dig(3, 3));
    }

    [Fact]
    public void Dig_Rock_ReturnsLooseRockRubble_AndClearsCell()
    {
        // Phase 13 behavior change (deliberate): terrain Rock is diggable
        // and converts to LooseRock rubble when dug.
        var grid = new Grid(10, 10);
        grid[2, 2] = CellMaterial.Rock;

        Assert.Equal(CellMaterial.LooseRock, grid.Dig(2, 2));
        Assert.Equal(CellMaterial.Air, grid[2, 2]);
    }

    [Fact]
    public void CreateTestWorld_HasAirAboveAndSolidGround()
    {
        var grid = Grid.CreateTestWorld(50, 50, groundLevel: 25);

        Assert.Equal(CellMaterial.Air, grid[10, 0]);
        Assert.Equal(CellMaterial.Air, grid[10, 24]);
        for (int x = 0; x < grid.Width; x++)
        {
            Assert.NotEqual(CellMaterial.Air, grid[x, 25]);
        }
    }
}

public class ParticleTests
{
    [Fact]
    public void DroppedParticle_FallsUntilItHitsGround()
    {
        var grid = new Grid(10, 10);
        for (int x = 0; x < 10; x++) grid[x, 9] = CellMaterial.Rock; // floor

        var sim = new Simulation(grid);
        sim.Drop(5, 0, CellMaterial.Dirt);
        sim.RunUntilSettled();

        Assert.Equal(0, sim.ActiveParticleCount);
        Assert.Equal(CellMaterial.Dirt, grid[5, 8]); // rests on the floor
        Assert.Equal(CellMaterial.Air, grid[5, 0]);
    }

    [Fact]
    public void DroppedParticle_SlidesDiagonally_WhenDirectlyBlocked()
    {
        var grid = new Grid(10, 10);
        for (int x = 0; x < 10; x++) grid[x, 9] = CellMaterial.Rock; // floor
        grid[5, 8] = CellMaterial.Rock; // single-cell obstacle on the floor

        var sim = new Simulation(grid);
        sim.Drop(5, 0, CellMaterial.Dirt);
        sim.RunUntilSettled();

        // Blocked directly below → must have slid to one diagonal and landed
        // beside the obstacle on the floor.
        Assert.True(
            grid[4, 8] == CellMaterial.Dirt || grid[6, 8] == CellMaterial.Dirt,
            "Particle should settle diagonally beside the obstacle");
        Assert.Equal(CellMaterial.Rock, grid[5, 8]);
    }

    [Fact]
    public void DroppedParticle_DoesNotCutThroughSolidCorner()
    {
        // The particle comes to rest at (5, 7) on top of the rock at (5, 8).
        // The left diagonal-down (4, 8) is open, but the left SIDE cell (4, 7)
        // is rock — sliding to (4, 8) would cut through the solid corner
        // formed by (4, 7) and (5, 8). The right diagonal (6, 8) is blocked
        // outright. With the side-cell check, no slide is possible and the
        // particle must settle in place at (5, 7).
        var grid = new Grid(10, 10);
        for (int x = 0; x < 10; x++) grid[x, 9] = CellMaterial.Rock; // floor
        grid[5, 8] = CellMaterial.Rock; // directly below the particle
        grid[4, 7] = CellMaterial.Rock; // left side cell (corner blocker)
        grid[6, 8] = CellMaterial.Rock; // right diagonal blocked

        var sim = new Simulation(grid);
        sim.Drop(5, 0, CellMaterial.Dirt);
        sim.RunUntilSettled();

        Assert.Equal(CellMaterial.Dirt, grid[5, 7]);
        Assert.Equal(CellMaterial.Air, grid[4, 8]); // the corner-cut destination stays empty
    }

    [Fact]
    public void MultipleDropsAtSameSpot_FormAPile_NotAColumn()
    {
        var grid = new Grid(40, 40);
        for (int x = 0; x < 40; x++) grid[x, 39] = CellMaterial.Rock; // floor

        var sim = new Simulation(grid);
        for (int i = 0; i < 15; i++)
        {
            sim.Drop(20, 0, CellMaterial.Dirt);
            sim.RunUntilSettled();
        }

        // Count distinct columns containing settled dirt.
        int occupiedColumns = 0;
        for (int x = 0; x < 40; x++)
        {
            for (int y = 0; y < 39; y++)
            {
                if (grid[x, y] == CellMaterial.Dirt) { occupiedColumns++; break; }
            }
        }
        Assert.True(occupiedColumns > 1, $"Pile should be wider than 1 column, was {occupiedColumns}");

        // And it must not be a tall tower: pile height should be well under 15.
        int pileHeight = 0;
        for (int y = 0; y < 39; y++)
        {
            if (grid[20, y] == CellMaterial.Dirt) { pileHeight = 39 - y; break; }
        }
        Assert.True(pileHeight < 10, $"Center column height {pileHeight} suggests a tower, not a pile");
    }
}

public class AgentTests
{
    [Fact]
    public void AgentDigCarryDrop_MovesDirt_WithoutClippingThroughGround()
    {
        var grid = Grid.CreateTestWorld(100, 60, groundLevel: 30);
        var sim = new Simulation(grid);
        var agent = new TestAgent(sim);

        for (int i = 0; i < 20; i++)
        {
            agent.DigCarryDrop(digX: 20, walkDistance: 30);
        }

        // The drop site surface must now be ABOVE the original ground level
        // (a pile formed on top, nothing sank into solid ground).
        int dropSurface = agent.SurfaceY(50);
        Assert.True(dropSurface < 30, $"Expected a pile above y=30 at drop site, surface was y={dropSurface}");

        // The dig site should be lower than it started (a trench formed).
        Assert.True(agent.SurfaceY(20) > 30);

        // No dirt embedded inside the original solid ground beyond what existed:
        // every cell below the original surface at the drop site is still solid.
        for (int y = 30; y < grid.Height; y++)
        {
            Assert.NotEqual(CellMaterial.Air, grid[50, y]);
        }
    }
}

public class PerformanceTests
{
    [Fact]
    public void FiveHundredDigDropCycles_CompleteQuickly()
    {
        // Threshold rationale: 500 cycles on a 200x200 grid is far beyond one
        // frame of gameplay work. Each cycle settles a single particle whose
        // fall is O(grid height) ticks and each tick is O(active particles),
        // so the whole run is ~a few million cheap operations. 2 seconds is
        // ~20x headroom over observed time on a dev machine while still
        // catching an accidental O(all cells per tick) regression, which
        // would blow past it.
        var grid = Grid.CreateTestWorld(200, 200, groundLevel: 100);
        var sim = new Simulation(grid);
        var agent = new TestAgent(sim);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 500; i++)
        {
            // Spread digs across columns so we don't exhaust one column into rock.
            agent.DigCarryDrop(digX: 10 + i % 50, walkDistance: 100);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"500 dig+drop cycles took {sw.ElapsedMilliseconds} ms (limit 2000 ms)");
    }
}
