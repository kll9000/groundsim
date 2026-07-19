namespace GroundSim;

/// <summary>
/// The largest worker. Narrowed post-Soldier-split definition: speeds
/// excavation of whatever site is currently being dug, by digging alongside
/// (composing the full Agent dig-carry-drop machinery, so its spoil is
/// physically hauled and conserved like everyone else's). With no active dig
/// site it is simply idle — guard behavior is deliberately absent (that's
/// the deferred Soldier's job).
/// </summary>
public sealed class Major
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public CellMaterial? Carrying => _digAgent?.Carried;

    private Agent? _digAgent;
    private (int X0, int Y0, int X1, int Y1)? _agentSite;

    public Major(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void Tick(Colony colony, Grid grid)
    {
        var site = colony.ActiveDigSite;
        if (site is null)
        {
            // Never abandon carried spoil: finish delivering before idling.
            if (_digAgent is { Carried: not null })
            {
                _digAgent.Tick();
                (X, Y) = (_digAgent.X, _digAgent.Y);
                return;
            }
            _digAgent = null;
            _agentSite = null;
            return; // idle — no guard stance, no other caste's work
        }

        if (_digAgent is null || _agentSite != site)
        {
            _agentSite = site;
            _digAgent = new Agent(grid, colony.Sim, colony.DigClaims, X, Y, site.Value, colony.SpoilDropX);
        }

        _digAgent.Tick();
        (X, Y) = (_digAgent.X, _digAgent.Y);
    }
}
