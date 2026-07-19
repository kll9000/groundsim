namespace GroundSim;

/// <summary>
/// The largest worker. Narrowed post-Soldier-split definition: speeds
/// excavation of whatever site is currently being dug, by digging alongside
/// (via DigAssist, which composes the full Agent dig-carry-drop machinery so
/// its spoil is physically hauled and conserved like everyone else's). With
/// no active dig site it is simply idle — guard behavior is deliberately
/// absent (that's the deferred Soldier's job).
/// </summary>
public sealed class Major
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public CellMaterial? Carrying => _dig.Carrying;

    private readonly DigAssist _dig = new();

    public Major(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void Tick(Colony colony, Grid grid)
    {
        int x = X, y = Y;
        _dig.Tick(colony, grid, ref x, ref y, wantDig: true);
        (X, Y) = (x, y);
        // No dig site and no leftover spoil: idle — no guard stance, no
        // other caste's work.
    }
}
