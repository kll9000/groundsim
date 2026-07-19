namespace GroundSim;

/// <summary>
/// Minimal test agent (not real ant AI): digs a cell from the surface, walks
/// N cells sideways, drops the carried dirt, and lets it settle.
/// </summary>
public sealed class TestAgent
{
    private readonly Simulation _sim;

    public CellMaterial? Carried { get; private set; }

    public TestAgent(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>Finds the topmost solid cell in column x, or -1 if the column is all air.</summary>
    public int SurfaceY(int x)
    {
        var grid = _sim.Grid;
        for (int y = 0; y < grid.Height; y++)
        {
            if (grid[x, y] != CellMaterial.Air) return y;
        }
        return -1;
    }

    /// <summary>
    /// Digs the surface cell at column digX, carries the material walkDistance
    /// cells to the right (clamped in bounds), drops it just above the surface
    /// there, and runs the simulation until the particle settles.
    /// Returns true if a full dig-carry-drop cycle happened.
    /// </summary>
    public bool DigCarryDrop(int digX, int walkDistance)
    {
        var grid = _sim.Grid;
        int surfY = SurfaceY(digX);
        if (surfY < 0) return false;

        Carried = grid.Dig(digX, surfY);
        if (Carried is null) return false; // hit rock or air

        int dropX = Math.Clamp(digX + walkDistance, 0, grid.Width - 1);
        int dropSurf = SurfaceY(dropX);
        int dropY = dropSurf < 0 ? grid.Height - 1 : Math.Max(0, dropSurf - 1);

        bool dropped = _sim.Drop(dropX, dropY, Carried.Value);
        Carried = null;
        if (!dropped) return false;

        _sim.RunUntilSettled();
        return true;
    }
}
