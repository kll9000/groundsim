using GroundSim;

namespace GroundSim.Tests;

public class RockMiningTests
{
    [Fact]
    public void MinedRock_BehavesAsLooseRockParticle_WhenDropped()
    {
        // The full pipeline: dig terrain rock -> carry -> drop -> the falling
        // particle is LooseRock and settles under existing Phase 2 physics.
        var grid = new Grid(20, 20);
        for (int x = 0; x < 20; x++) grid[x, 15] = CellMaterial.Dirt; // floor
        var sim = new Simulation(grid);

        var dug = grid.Dig(5, 15); // wait — dig a rock cell instead
        Assert.Equal(CellMaterial.Dirt, dug);
        grid[5, 15] = CellMaterial.Rock;
        dug = grid.Dig(5, 15);
        Assert.Equal(CellMaterial.LooseRock, dug);

        sim.Drop(10, 0, dug!.Value);
        sim.RunUntilSettled();
        Assert.Equal(CellMaterial.LooseRock, grid[10, 14]); // settled on the floor
    }

    [Fact]
    public void Agent_TakesMultipleTicks_ToMineARockCell()
    {
        // One rock cell as the entire dig site: the agent must spend
        // RockDigTicks ticks chipping before the cell opens (vs 1 for dirt).
        var grid = new Grid(30, 30);
        for (int y = 20; y < 30; y++)
        {
            for (int x = 0; x < 30; x++) grid[x, y] = CellMaterial.Dirt;
        }
        grid[10, 20] = CellMaterial.Rock; // surface rock cell
        var sim = new Simulation(grid);
        var agent = new Agent(grid, sim, new HashSet<(int, int)>(), 10, 19,
            new List<(int, int)> { (10, 20) }, dropX: 25);

        int ticksUntilDug = 0;
        while (grid[10, 20] == CellMaterial.Rock && ticksUntilDug < 100)
        {
            agent.Tick();
            sim.Tick();
            ticksUntilDug++;
        }
        Assert.True(grid[10, 20] == CellMaterial.Air, "rock cell should eventually be mined");
        Assert.True(ticksUntilDug >= Agent.RockDigTicks,
            $"rock took only {ticksUntilDug} ticks — chipping cost not applied");
        Assert.Equal(CellMaterial.LooseRock, agent.Carried);
    }

    [Fact]
    public void RoomExcavation_ClearsRockToo_NoPermanentPockmarks()
    {
        // Phase 13 completion semantics: an excavated site is genuinely
        // clear — rock included.
        var (grid, sim) = ColonyTestWorld.Create();
        // Phase 18: death disabled — fixed two-worker crew, 200k window.
        var config = new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            WorkerLifespanMeanTicks = 0,
        };
        var colony = ColonyTestWorld.Founded(grid, sim, config);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Soldier, colony.HomeCenter.X + 1, colony.HomeCenter.Y);
        colony.FarmedResource = config.GardenTriggerThreshold;

        // Phase 15: 25k → 200k (×8: the dig is ×GridScale² the cells and
        // every haul walks ×GridScale the distance at 1 cell/tick).
        ColonyTestWorld.Run(colony, sim, 200_000);

        var garden = colony.GetRoom(RoomType.Garden);
        Assert.NotNull(garden);
        Assert.True(garden!.Excavated, "garden should complete");
        int solidCells = garden.Cells.Count(c => grid[c.X, c.Y] != CellMaterial.Air);
        // Spill may transiently sit inside, but no terrain Rock may remain.
        int rockCells = garden.Cells.Count(c => grid[c.X, c.Y] == CellMaterial.Rock);
        Assert.Equal(0, rockCells);
    }
}
