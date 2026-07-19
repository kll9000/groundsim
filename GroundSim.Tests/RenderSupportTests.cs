using GroundSim;

namespace GroundSim.Tests;

public class DirtyTrackerTests
{
    [Fact]
    public void IdleSimulation_ProducesZeroDirtyCells()
    {
        // Directly protects the rendering performance property: settled
        // terrain with no activity must cost nothing to redraw.
        var grid = Grid.CreateTestWorld(50, 50, groundLevel: 25);
        var sim = new Simulation(grid);
        var dirty = new DirtyTracker(grid);
        dirty.Clear(); // world-gen writes are not "this frame's" changes

        for (int t = 0; t < 10; t++)
        {
            dirty.MarkParticles(sim);
            sim.Tick();
            dirty.MarkParticles(sim);
        }

        Assert.Equal(0, dirty.Count);
    }

    [Fact]
    public void FallingParticle_MarksOnlyItsOwnCells()
    {
        var grid = new Grid(20, 20);
        for (int x = 0; x < 20; x++) grid[x, 19] = CellMaterial.Rock;

        var sim = new Simulation(grid);
        var dirty = new DirtyTracker(grid);
        dirty.Clear();

        sim.Drop(10, 0, CellMaterial.Dirt);
        dirty.MarkParticles(sim);
        sim.Tick();
        dirty.MarkParticles(sim);

        // One particle fell one cell: exactly its old and new position are
        // dirty — not the terrain, not the rest of the column.
        Assert.Equal(2, dirty.Count);
        Assert.Contains((10, 0), dirty.Cells);
        Assert.Contains((10, 1), dirty.Cells);
    }

    [Fact]
    public void GridWrites_AreMarkedViaCellChanged()
    {
        var grid = new Grid(10, 10);
        var dirty = new DirtyTracker(grid);
        dirty.Clear();

        grid[3, 4] = CellMaterial.Dirt;   // e.g. a settle
        var dug = grid.Dig(3, 4);          // and a dig

        Assert.Equal(CellMaterial.Dirt, dug);
        Assert.Equal(1, dirty.Count);      // same cell both times
        Assert.Contains((3, 4), dirty.Cells);
    }
}

public class TickClockTests
{
    [Fact]
    public void Advance_ProducesTicksAtConfiguredRate()
    {
        var clock = new TickClock { TicksPerSecond = 30 };
        int total = 0;
        // 60 frames of ~16.7ms ≈ 1 second of wall time.
        for (int f = 0; f < 60; f++) total += clock.Advance(1.0 / 60.0);
        Assert.InRange(total, 29, 31);
    }

    [Fact]
    public void Advance_AccumulatesFractionalTicksAcrossFrames()
    {
        // At 10 tps, a 60fps frame is 1/6 of a tick — each frame alone yields
        // 0 ticks, but the fractions must accumulate to a tick instead of
        // being truncated away every frame. 7 frames (not 6) because the
        // 1/60-second frame time isn't exactly representable in binary and
        // sums to fractionally under 1.0 at frame six.
        var clock = new TickClock { TicksPerSecond = 10 };
        int total = 0;
        for (int f = 0; f < 5; f++)
        {
            Assert.Equal(0, clock.Advance(1.0 / 60.0)); // no premature tick
        }
        for (int f = 0; f < 2; f++) total += clock.Advance(1.0 / 60.0);
        Assert.Equal(1, total);
    }

    [Fact]
    public void Paused_ProducesNoTicks_AndStallIsCapped()
    {
        var clock = new TickClock { TicksPerSecond = 30, MaxTicksPerAdvance = 8 };

        clock.Paused = true;
        Assert.Equal(0, clock.Advance(1.0));

        clock.Paused = false;
        // A 5-second stall at 30 tps owes 150 ticks; the cap prevents a burst.
        Assert.Equal(8, clock.Advance(5.0));
        // And the excess debt was dropped, not owed.
        Assert.Equal(0, clock.Advance(0.001));
    }
}
