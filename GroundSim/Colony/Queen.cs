namespace GroundSim;

public enum QueenState { Founding, Laying }

/// <summary>
/// The colony's origin. NOT an Agent subtype: after founding she never moves,
/// digs, or carries again — she only lays eggs.
///
/// Founding: she composes a temporary Agent that excavates the founding
/// DigSite (Phase 12: an entrance shaft + organic home chamber; the rect
/// fallback uses the same machinery). Completion uses the frontier-accessible
/// rule (Phase 9.5b semantics via DigSite.HasRemainingDiggable), then the
/// agent is discarded entirely, she settles at the chamber's floor center,
/// deposits the starter resource, and enters Laying — from which no code
/// path moves her again.
/// </summary>
public sealed class Queen
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public QueenState State { get; private set; }

    private Agent? _foundingAgent;

    /// <summary>Read-only introspection of the founding agent (diagnostics/tests).</summary>
    public Agent? FoundingAgent => _foundingAgent;
    private readonly DigSite? _foundingSite;
    private readonly (int X, int Y) _settlePoint;
    private int _layTimer;

    /// <summary>A founding queen: digs out the site, then settles at
    /// settlePoint permanently.</summary>
    public Queen(DigSite foundingSite, (int X, int Y) settlePoint, int startX, int startY)
    {
        _foundingSite = foundingSite;
        _settlePoint = settlePoint;
        X = startX;
        Y = startY;
        State = QueenState.Founding;
    }

    /// <summary>Test/Phase-7 constructor: an already-founded queen, stationary at (x, y).</summary>
    public Queen(int x, int y)
    {
        X = x;
        Y = y;
        _settlePoint = (x, y);
        State = QueenState.Laying;
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
        // Lazily created so her spoil deliveries use the colony's mound
        // drop-point provider like every other digger.
        _foundingAgent ??= new Agent(colony.Grid, colony.Sim, new HashSet<(int, int)>(),
            X, Y, _foundingSite!.Cells, colony.NextSpoilDropX);

        _foundingAgent.Tick();
        X = _foundingAgent.X;
        Y = _foundingAgent.Y;

        if (_foundingAgent.Carried is null && !_foundingSite!.HasRemainingDiggable(colony.Grid))
        {
            // Settle permanently: discard the dig machinery, deposit starter.
            _foundingAgent = null;
            (X, Y) = _settlePoint;
            colony.FarmedResource += colony.Config.StarterResource;
            colony.NotifyHomeFounded();
            State = QueenState.Laying;
        }
    }
}
