namespace GroundSim;

/// <summary>
/// Reusable per-tick movement: one step per call, replanning when the path is
/// blocked, buried-push-up like Agent. Castes compose this (the movement half
/// of Agent's machinery) instead of the full dig-cycle Agent, because their
/// work loops differ from dig-carry-drop.
/// </summary>
public sealed class PathWalker
{
    public int X { get; private set; }
    public int Y { get; private set; }

    private Queue<(int X, int Y)>? _path;
    private (int X, int Y)? _plannedTarget;
    private int _replanCooldown;

    private const int ReplanCooldownTicks = 15;

    public PathWalker(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Adopt an externally-moved position (e.g. after a DigAssist
    /// stint moved the owner via Agent machinery) and drop any stale path.</summary>
    public void SetPosition(int x, int y)
    {
        if (x == X && y == Y) return;
        X = x;
        Y = y;
        _path = null;
        _plannedTarget = null;
    }

    /// <summary>
    /// One unit of movement work toward the target. Returns true when standing
    /// on the target cell. Unreachable targets retry on a cooldown.
    /// </summary>
    public bool MoveTowards(Grid grid, (int X, int Y) target)
    {
        // Buried by settled material: push up one cell per tick.
        if (!grid.IsAir(X, Y))
        {
            if (Y > 0) Y--;
            return false;
        }

        // Unsupported in open air: fall one cell — this tick's unit of work.
        // Applied before anything else, regardless of what the path says.
        if (!Terrain.IsSupported(grid, X, Y))
        {
            Y++;
            return false;
        }

        if ((X, Y) == target) return true;

        if (_replanCooldown > 0) { _replanCooldown--; return false; }

        if (_plannedTarget != target || _path is null || _path.Count == 0)
        {
            Plan(grid, target); // planning is this tick's unit of work
            return false;
        }

        var next = _path.Peek();
        if (Math.Abs(next.X - X) + Math.Abs(next.Y - Y) != 1)
        {
            // Falling desynced us from the plan — replan, never teleport.
            Plan(grid, target);
            return false;
        }
        if (!grid.IsAir(next.X, next.Y))
        {
            Plan(grid, target); // terrain changed under the path
            return false;
        }

        _path.Dequeue();
        X = next.X;
        Y = next.Y;
        return (X, Y) == target;
    }

    private void Plan(Grid grid, (int X, int Y) target)
    {
        _plannedTarget = target;
        var path = Pathfinder.FindPath(grid, (X, Y), target);
        if (path is null)
        {
            _path = null;
            _replanCooldown = ReplanCooldownTicks;
        }
        else
        {
            _path = new Queue<(int X, int Y)>(path);
        }
    }
}
