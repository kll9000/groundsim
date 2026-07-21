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
    private DigSite? _site;

    public CellMaterial? Carrying => _agent?.Carried;

    /// <summary>The internal dig agent, if any — read-only introspection for
    /// diagnostics and tests.</summary>
    public Agent? ActiveAgent => _agent;

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

        if (_agent is null || !ReferenceEquals(_site, site))
        {
            _agent?.ReleaseClaims();
            _site = site;
            _agent = new Agent(grid, colony.Sim, colony.DigClaims, x, y, site.Cells, colony.NextSpoilDropX);
        }

        _agent.Tick();
        x = _agent.X;
        y = _agent.Y;
        return true;
    }

    /// <summary>Phase 18 Part C: owner died mid-assist. Release any claimed
    /// dig cell (a leaked claim permanently seals that cell — the Phase 4
    /// claim-leak bug class) and drop carried spoil as a real particle at
    /// the death spot so dig-material conservation holds.</summary>
    public void AbandonOnDeath(Colony colony, int x, int y)
    {
        if (_agent is null) return;
        if (_agent.Carried is { } material) colony.Sim.Drop(x, y, material);
        _agent.ReleaseClaims();
        _agent = null;
        _site = null;
    }
}
