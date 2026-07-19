namespace GroundSim.Render;

/// <summary>
/// Scripted demo activity (Phase 3 stand-in for real agent AI): digs a trench
/// column by column and streams the spoil plus loose rock and sticks to drop
/// points, staggered over ticks (Phase 2 lesson: same-tick batch drops from
/// one cell arrive as a burst and smear flat).
/// </summary>
public sealed class DemoScript
{
    private readonly Grid _grid;
    private readonly Simulation _sim;
    private int _tick;

    public DemoScript(Grid grid, Simulation sim)
    {
        _grid = grid;
        _sim = sim;
    }

    public void OnTick()
    {
        _tick++;

        // Every 6 ticks: dig one cell from the trench area and drop the spoil.
        if (_tick % 6 == 0)
        {
            int digX = 30 + (_tick / 6) % 12;
            int surfY = SurfaceY(digX);
            if (surfY >= 0)
            {
                var dug = _grid.Dig(digX, surfY);
                if (dug is { } material) _sim.Drop(90, 0, material);
            }
        }

        // Every 10 ticks: a loose rock chunk; every 14: a stick.
        if (_tick % 10 == 0) _sim.Drop(120, 0, CellMaterial.LooseRock);
        if (_tick % 14 == 0) _sim.Drop(150, 0, CellMaterial.Stick);
    }

    private int SurfaceY(int x)
    {
        for (int y = 0; y < _grid.Height; y++)
        {
            if (_grid[x, y] != CellMaterial.Air) return _grid[x, y] == CellMaterial.Rock ? -1 : y;
        }
        return -1;
    }
}
