namespace GroundSim;

/// <summary>
/// Leaves the nest to gather, and ONLY gathers: paths to a surface resource
/// node, takes a haul (smaller the farther the node is from home), hauls it
/// back as RAW material. NEVER processes — a Tender does that; the two-stage
/// raw→farmed pipeline is a deliberate Colony Builder design decision.
/// Composes PathWalker — Foragers don't dig.
/// </summary>
public sealed class Forager
{
    private readonly PathWalker _walker;
    private ResourceNode? _targetNode;

    public int X => _walker.X;
    public int Y => _walker.Y;
    public double Carrying { get; private set; }

    public Forager(int x, int y) => _walker = new PathWalker(x, y);

    public void Tick(Colony colony, Grid grid)
    {
        if (Carrying > 0)
        {
            if (_walker.MoveTowards(grid, colony.HomeCenter))
            {
                colony.RawMaterial += Carrying;
                colony.Stats.RawGatheredByForagers += Carrying;
                Carrying = 0;
                _targetNode = null;
            }
            return;
        }

        if (_targetNode is null || _targetNode.Remaining <= 0)
        {
            _targetNode = colony.NearestNodeWithMaterial(X, Y);
            if (_targetNode is null) return; // nothing left to gather anywhere
        }

        if (_walker.MoveTowards(grid, (_targetNode.X, _targetNode.Y)))
        {
            // Haul size shrinks with the node's distance from home.
            var home = colony.HomeCenter;
            double dist = Math.Abs(home.X - _targetNode.X) + Math.Abs(home.Y - _targetNode.Y);
            double taken = Math.Min(colony.Config.HaulSize(dist), _targetNode.Remaining);
            _targetNode.Remaining -= taken;
            Carrying = taken;
        }
    }
}
