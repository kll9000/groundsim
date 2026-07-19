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
    private readonly DigAssist _dig = new();
    private ResourceNode? _targetNode;

    public int X => _walker.X;
    public int Y => _walker.Y;
    public double Carrying { get; private set; }

    /// <summary>Set by Colony.AssignDiggers: this Forager is temporarily on
    /// communal room-excavation duty (gathering resumes when the site is
    /// done). Digging is communal work, not another caste's job — the
    /// gather/process exclusivity invariants are untouched.</summary>
    public bool AssignedToDig { get; set; }

    /// <summary>Read-only introspection of the dig-assist agent, if active.</summary>
    public Agent? DigAgent => _dig.ActiveAgent;

    public Forager(int x, int y) => _walker = new PathWalker(x, y);

    public void Tick(Colony colony, Grid grid)
    {
        int dx = X, dy = Y;
        if (_dig.Tick(colony, grid, ref dx, ref dy, AssignedToDig))
        {
            _walker.SetPosition(dx, dy);
            return;
        }

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
