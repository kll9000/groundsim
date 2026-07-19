namespace GroundSim;

/// <summary>
/// Shared "help excavate the active dig site" machinery: composes a full
/// Agent aimed at Colony.ActiveDigSite, and always finishes delivering
/// carried spoil before standing down (conservation). Used by Major (always
/// willing) and by dig-assigned Foragers (communal room excavation).
/// </summary>
public sealed class DigAssist
{
    private Agent? _agent;
    private (int X0, int Y0, int X1, int Y1)? _site;

    public CellMaterial? Carrying => _agent?.Carried;

    /// <summary>
    /// One tick of dig work if wanted and a site is active (or leftover spoil
    /// needs delivering). Returns true if this consumed the tick; x/y are
    /// updated to the digger's position.
    /// </summary>
    public bool Tick(Colony colony, Grid grid, ref int x, ref int y, bool wantDig)
    {
        var site = wantDig ? colony.ActiveDigSite : null;
        if (site is null)
        {
            if (_agent is { Carried: not null })
            {
                _agent.Tick();
                x = _agent.X;
                y = _agent.Y;
                return true;
            }
            _agent?.ReleaseClaims(); // never leak a claimed cell on stand-down
            _agent = null;
            _site = null;
            return false;
        }

        if (_agent is null || _site != site)
        {
            _agent?.ReleaseClaims();
            _site = site;
            _agent = new Agent(grid, colony.Sim, colony.DigClaims, x, y, site.Value, colony.SpoilDropX);
        }

        _agent.Tick();
        x = _agent.X;
        y = _agent.Y;
        return true;
    }
}
