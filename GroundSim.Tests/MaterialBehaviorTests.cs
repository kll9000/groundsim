using System.Diagnostics;
using GroundSim;

namespace GroundSim.Tests;

public class MaterialBehaviorTests
{
    private static Grid FlatFloorGrid(int size)
    {
        var grid = new Grid(size, size);
        for (int x = 0; x < size; x++) grid[x, size - 1] = CellMaterial.Rock;
        return grid;
    }

    private static (int occupiedColumns, int centerHeight) PileShape(Grid grid, int centerX, CellMaterial material)
    {
        int floor = grid.Height - 1;
        int occupiedColumns = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < floor; y++)
            {
                if (grid[x, y] == material) { occupiedColumns++; break; }
            }
        }
        int centerHeight = 0;
        for (int y = 0; y < floor; y++)
        {
            if (grid[centerX, y] == material) { centerHeight = floor - y; break; }
        }
        return (occupiedColumns, centerHeight);
    }

    [Fact]
    public void LooseRock_IsDiggable_UnlikeTerrainRock()
    {
        var grid = new Grid(10, 10);
        grid[3, 5] = CellMaterial.LooseRock;
        grid[4, 5] = CellMaterial.Rock;

        Assert.Equal(CellMaterial.LooseRock, grid.Dig(3, 5));
        Assert.Equal(CellMaterial.Air, grid[3, 5]);
        Assert.Null(grid.Dig(4, 5)); // terrain rock stays undiggable
    }

    [Fact]
    public void RockPile_IsMeasurablySteeperAndNarrower_ThanDirtPile()
    {
        // Same drop count, same seed, same geometry — only the material
        // differs, so any shape difference is the material rule.
        const int dropsPerPile = 15;

        var dirtGrid = FlatFloorGrid(40);
        var dirtSim = new Simulation(dirtGrid, seed: 7);
        for (int i = 0; i < dropsPerPile; i++)
        {
            dirtSim.Drop(20, 0, CellMaterial.Dirt);
            dirtSim.RunUntilSettled();
        }

        var rockGrid = FlatFloorGrid(40);
        var rockSim = new Simulation(rockGrid, seed: 7);
        for (int i = 0; i < dropsPerPile; i++)
        {
            rockSim.Drop(20, 0, CellMaterial.LooseRock);
            rockSim.RunUntilSettled();
        }

        var dirt = PileShape(dirtGrid, 20, CellMaterial.Dirt);
        var rock = PileShape(rockGrid, 20, CellMaterial.LooseRock);

        Assert.True(rock.occupiedColumns < dirt.occupiedColumns,
            $"Rock pile ({rock.occupiedColumns} cols) should be narrower than dirt ({dirt.occupiedColumns} cols)");
        Assert.True(rock.centerHeight > dirt.centerHeight,
            $"Rock pile (h={rock.centerHeight}) should be taller than dirt (h={dirt.centerHeight})");
    }

    [Fact]
    public void Sticks_NeverSlide_TheyStackInASingleColumn()
    {
        var grid = FlatFloorGrid(30);
        var sim = new Simulation(grid);
        const int drops = 10;
        for (int i = 0; i < drops; i++)
        {
            sim.Drop(15, 0, CellMaterial.Stick);
            sim.RunUntilSettled();
        }

        var (occupiedColumns, centerHeight) = PileShape(grid, 15, CellMaterial.Stick);
        Assert.Equal(1, occupiedColumns);      // no spread at all
        Assert.Equal(drops, centerHeight);     // a 10-high stack
    }

    [Fact]
    public void MixedDrops_LayerInDropOrder_WithoutClipping()
    {
        var grid = FlatFloorGrid(30);
        var sim = new Simulation(grid);

        void DropAndSettle(CellMaterial m) { sim.Drop(15, 0, m); sim.RunUntilSettled(); }
        for (int i = 0; i < 6; i++) DropAndSettle(CellMaterial.Dirt);
        for (int i = 0; i < 3; i++) DropAndSettle(CellMaterial.LooseRock);
        for (int i = 0; i < 2; i++) DropAndSettle(CellMaterial.Stick);

        // Conservation: every drop occupies exactly one cell — nothing was
        // overwritten/embedded inside another settled cell.
        int dirt = 0, rock = 0, stick = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height - 1; y++)
            {
                switch (grid[x, y])
                {
                    case CellMaterial.Dirt: dirt++; break;
                    case CellMaterial.LooseRock: rock++; break;
                    case CellMaterial.Stick: stick++; break;
                }
            }
        }
        Assert.Equal(6, dirt);
        Assert.Equal(3, rock);
        Assert.Equal(2, stick);

        // Layering in the center column matches drop order: scanning downward
        // from the sky we must meet stick(s), then rock, then dirt — never a
        // later-dropped material below an earlier-dropped one.
        var order = new List<CellMaterial>();
        for (int y = 0; y < grid.Height - 1; y++)
        {
            var m = grid[15, y];
            if (m != CellMaterial.Air && (order.Count == 0 || order[^1] != m)) order.Add(m);
        }
        Assert.Equal(
            new[] { CellMaterial.Stick, CellMaterial.LooseRock, CellMaterial.Dirt },
            order);
    }
}

public class ConcurrentParticleTests
{
    [Fact]
    public void ManyConcurrentDrops_AllSettle_WithNoOverlapOrClipping()
    {
        var grid = new Grid(60, 60);
        for (int x = 0; x < 60; x++) grid[x, 59] = CellMaterial.Rock; // floor

        var sim = new Simulation(grid);
        // 50 particles in flight at once, several sharing columns, no
        // intermediate settling.
        const int drops = 50;
        for (int i = 0; i < drops; i++)
        {
            sim.Drop(20 + i % 7, 0, CellMaterial.Dirt);
        }
        Assert.Equal(drops, sim.ActiveParticleCount);

        sim.RunUntilSettled();
        Assert.Equal(0, sim.ActiveParticleCount);

        // Conservation: exactly 50 dirt cells settled (no overwrites, no loss).
        int settledDirt = 0;
        for (int x = 0; x < 60; x++)
        {
            for (int y = 0; y < 59; y++)
            {
                if (grid[x, y] == CellMaterial.Dirt) settledDirt++;
            }
        }
        Assert.Equal(drops, settledDirt);

        // No floating dirt: every dirt cell rests on something solid.
        for (int x = 0; x < 60; x++)
        {
            for (int y = 0; y < 59; y++)
            {
                if (grid[x, y] == CellMaterial.Dirt)
                {
                    Assert.NotEqual(CellMaterial.Air, grid[x, y + 1]);
                }
            }
        }
    }

    [Fact]
    public void HundredConcurrentParticles_SettleQuickly()
    {
        // Threshold rationale: 100 particles falling ~100 cells is ~10k
        // particle-steps, each a few array reads — microseconds of real work.
        // 1 second is enormous headroom for CI jitter but still fails hard if
        // concurrent-drop handling accidentally becomes O(grid cells) per tick
        // (200x200 = 40k cells scanned per tick for ~200 ticks would blow it).
        var grid = Grid.CreateTestWorld(200, 200, groundLevel: 100);
        var sim = new Simulation(grid);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            sim.Drop(50 + i % 20, 0, CellMaterial.Dirt);
        }
        sim.RunUntilSettled();
        sw.Stop();

        Assert.Equal(0, sim.ActiveParticleCount);
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"100 concurrent particles took {sw.ElapsedMilliseconds} ms to settle (limit 1000 ms)");
    }
}
