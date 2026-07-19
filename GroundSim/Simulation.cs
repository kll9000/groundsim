namespace GroundSim;

/// <summary>
/// Falling-sand simulation over a Grid.
///
/// PERFORMANCE NOTE: only particles in <see cref="_active"/> are processed
/// each tick. Once a particle settles it is written into the grid as static
/// material and removed from the list — settled cells are never re-scanned.
/// The per-tick cost is O(active particles), independent of grid size.
/// </summary>
public sealed class Simulation
{
    public Grid Grid { get; }

    private readonly List<Particle> _active = new();
    private readonly Random _rng;

    public int ActiveParticleCount => _active.Count;

    /// <summary>
    /// Read-only view of in-flight particles. Added in Phase 3 for the
    /// renderer's awake/settled debug overlay; nothing in the simulation
    /// reads this.
    /// </summary>
    public IReadOnlyList<Particle> ActiveParticles => _active;

    public Simulation(Grid grid, int seed = 42)
    {
        Grid = grid;
        _rng = new Random(seed);
    }

    /// <summary>
    /// Drops a chunk of material at (x, y). If that cell is occupied, the drop
    /// point walks upward to the first Air cell (dropping onto a pile lands on
    /// top of it). Returns false if the whole column is solid.
    /// </summary>
    public bool Drop(int x, int y, CellMaterial material)
    {
        if (!Grid.InBounds(x, y)) return false;
        while (y >= 0 && Grid[x, y] != CellMaterial.Air) y--;
        if (y < 0) return false;
        _active.Add(new Particle(x, y, material));
        return true;
    }

    /// <summary>
    /// Probability that a blocked LooseRock particle attempts a diagonal slide
    /// on a given tick instead of settling immediately. Dirt always slides
    /// when a diagonal is open; rock usually locks in place, producing
    /// visibly steeper/narrower piles than dirt.
    /// </summary>
    public const double RockSlideChance = 0.25;

    /// <summary>
    /// Advances all active particles by one step. Rules per particle:
    ///  1. If the cell below is Air, fall one cell.
    ///  2. Else the material decides:
    ///     - Stick: settle where it landed — sticks never slide diagonally.
    ///     - LooseRock: with probability <see cref="RockSlideChance"/> try a
    ///       diagonal slide (as dirt below); otherwise settle immediately.
    ///     - Dirt (and anything else): if a diagonal-down cell AND the side
    ///       cell on that same side (same row as the particle) are both Air,
    ///       slide to the diagonal. Requiring the side cell prevents cutting
    ///       through a solid corner. When both sides are open, one is chosen
    ///       randomly to avoid a directional bias in pile shapes.
    ///  3. Else settle: write the material into the grid and deactivate.
    /// A particle at the bottom row settles in place.
    ///
    /// Concurrency note: multiple particles may share a cell mid-flight (the
    /// grid only stores settled material). Settling checks for that — see
    /// <see cref="Settle"/>.
    /// </summary>
    public void Tick()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var p = _active[i];
            int below = p.Y + 1;

            if (below >= Grid.Height)
            {
                Settle(i, p);
                continue;
            }

            if (Grid.IsAir(p.X, below))
            {
                p.Y = below;
                _active[i] = p;
                continue;
            }

            if (p.Material == CellMaterial.Stick)
            {
                Settle(i, p);
                continue;
            }

            if (p.Material == CellMaterial.LooseRock && _rng.NextDouble() >= RockSlideChance)
            {
                Settle(i, p);
                continue;
            }

            bool leftOpen = Grid.IsAir(p.X - 1, below) && Grid.IsAir(p.X - 1, p.Y);
            bool rightOpen = Grid.IsAir(p.X + 1, below) && Grid.IsAir(p.X + 1, p.Y);

            if (leftOpen && rightOpen)
            {
                if (_rng.Next(2) == 0) rightOpen = false;
                else leftOpen = false;
            }

            if (leftOpen)
            {
                p.X -= 1;
                p.Y = below;
                _active[i] = p;
            }
            else if (rightOpen)
            {
                p.X += 1;
                p.Y = below;
                _active[i] = p;
            }
            else
            {
                Settle(i, p);
            }
        }
    }

    /// <summary>Runs ticks until no particles remain active (or maxTicks is hit).</summary>
    public int RunUntilSettled(int maxTicks = 100_000)
    {
        int ticks = 0;
        while (_active.Count > 0 && ticks < maxTicks)
        {
            Tick();
            ticks++;
        }
        return ticks;
    }

    private void Settle(int index, Particle p)
    {
        // With many particles in flight, another particle may have settled
        // into this exact cell earlier in the same tick (in-flight particles
        // are invisible to the grid). Never overwrite settled material:
        // bump the particle up to the first Air cell and keep it ACTIVE so it
        // re-evaluates falling/sliding from there next tick — this keeps
        // concurrent drops forming natural pile shapes instead of freezing
        // mid-air where the collision happened.
        if (Grid[p.X, p.Y] != CellMaterial.Air)
        {
            while (p.Y >= 0 && Grid[p.X, p.Y] != CellMaterial.Air) p.Y--;
            if (p.Y < 0)
            {
                // Whole column solid — discard the chunk (never hit in a
                // world with open sky, but must not corrupt the grid).
                _active.RemoveAt(index);
                return;
            }
            _active[index] = p;
            return;
        }

        Grid[p.X, p.Y] = p.Material;
        _active.RemoveAt(index);
    }
}
