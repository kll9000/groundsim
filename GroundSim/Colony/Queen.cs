namespace GroundSim;

public enum QueenState { Founding, Laying }

/// <summary>
/// The colony's origin. NOT an Agent subtype: after founding she never moves,
/// digs, or carries again — she only lays eggs.
///
/// Founding transition (flagged design decision): during Founding she
/// COMPOSES a temporary Agent to excavate the Home Room cell-by-cell with
/// real physics (spoil hauled out and dropped). When the chamber is fully
/// excavated and the agent is empty-handed, the agent is discarded entirely,
/// she settles at the chamber center, deposits the starter resource, and
/// enters Laying — from which no code path moves her again.
/// </summary>
public sealed class Queen
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public QueenState State { get; private set; }

    private Agent? _foundingAgent;
    private int _layTimer;
    private readonly (int X0, int Y0, int X1, int Y1) _chamber;

    public Queen(Grid grid, Simulation sim, (int X0, int Y0, int X1, int Y1) chamber,
        int startX, int startY, int spoilDropX)
    {
        _chamber = chamber;
        X = startX;
        Y = startY;
        State = QueenState.Founding;
        _foundingAgent = new Agent(grid, sim, new HashSet<(int, int)>(),
            startX, startY, chamber, spoilDropX);
    }

    /// <summary>Test/Phase-7 constructor: an already-founded queen, stationary at (x, y).</summary>
    public Queen(int x, int y)
    {
        X = x;
        Y = y;
        State = QueenState.Laying;
        _chamber = (x, y, x, y);
    }

    public void Tick(Colony colony)
    {
        if (State == QueenState.Founding)
        {
            TickFounding(colony);
            return;
        }

        // Laying: the ONLY behavior for the rest of her life.
        _layTimer++;
        if (_layTimer >= colony.Config.EggLayIntervalTicks)
        {
            _layTimer = 0;
            colony.LayEgg();
        }
    }

    private void TickFounding(Colony colony)
    {
        var agent = _foundingAgent!;
        agent.Tick();
        X = agent.X;
        Y = agent.Y;

        if (agent.Carried is null && ChamberFullyExcavated(colony.Grid))
        {
            // Settle permanently: discard the dig machinery, deposit starter.
            _foundingAgent = null;
            // Settle on the chamber FLOOR (Phase 9 terrain-following) — she
            // shouldn't hover at the pit's geometric center.
            (X, Y) = ((_chamber.X0 + _chamber.X1) / 2, _chamber.Y1);
            colony.FarmedResource += colony.Config.StarterResource;
            colony.NotifyHomeFounded();
            State = QueenState.Laying;
        }
    }

    private bool ChamberFullyExcavated(Grid grid)
    {
        for (int y = _chamber.Y0; y <= _chamber.Y1; y++)
        {
            for (int x = _chamber.X0; x <= _chamber.X1; x++)
            {
                if (grid[x, y] != CellMaterial.Air) return false;
            }
        }
        return true;
    }
}
