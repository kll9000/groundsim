namespace GroundSim;

/// <summary>
/// Collects the set of cells that changed since the last <see cref="Clear"/>,
/// so a renderer redraws only those instead of the whole grid every frame —
/// the rendering counterpart of the simulation's O(active particles) rule.
///
/// Subscribes to <see cref="Grid.CellChanged"/> (digs, settles) and offers
/// <see cref="MarkParticles"/> for in-flight particle positions, which live
/// outside the grid. Pure logic, no rendering dependency, unit-testable.
/// </summary>
public sealed class DirtyTracker
{
    private readonly HashSet<(int X, int Y)> _dirty = new();
    private readonly Grid _grid;

    public DirtyTracker(Grid grid)
    {
        _grid = grid;
        _grid.CellChanged += Mark;
    }

    public int Count => _dirty.Count;
    public IReadOnlyCollection<(int X, int Y)> Cells => _dirty;

    public void Mark(int x, int y)
    {
        if (_grid.InBounds(x, y)) _dirty.Add((x, y));
    }

    /// <summary>
    /// Marks every active particle's current cell. Call before AND after a
    /// tick: the before-positions need redrawing as background once the
    /// particle moves away, the after-positions need the particle drawn.
    /// </summary>
    public void MarkParticles(Simulation sim)
    {
        foreach (var p in sim.ActiveParticles) Mark(p.X, p.Y);
    }

    public void Clear() => _dirty.Clear();

    public void Detach() => _grid.CellChanged -= Mark;
}
