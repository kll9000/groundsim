namespace GroundSim;

public enum RoomType { Home, Garden, Nursery } // Waste/Pupa deferred per Phase 5 decision

/// <summary>
/// A first-class labeled room: a rect region of the grid plus a type tag and
/// excavation status. A mutable class rather than the handoff's suggested
/// record because Excavated flips in place when digging completes (flagged in
/// the Phase 7 report).
/// </summary>
public sealed class Room
{
    public RoomType Type { get; }
    public int X0 { get; }
    public int Y0 { get; }
    public int X1 { get; }
    public int Y1 { get; }
    public bool Excavated { get; internal set; }

    public Room(RoomType type, int x0, int y0, int x1, int y1, bool excavated = false)
    {
        Type = type;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        Excavated = excavated;
    }

    public (int X0, int Y0, int X1, int Y1) Rect => (X0, Y0, X1, Y1);
    public (int X, int Y) Center => ((X0 + X1) / 2, (Y0 + Y1) / 2);

    public bool Contains(int x, int y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    /// <summary>
    /// True while any diggable cell remains. Terrain Rock inside the rect is
    /// tolerated as a natural pillar — excavation is "complete" when nothing
    /// diggable is left, not when every cell is Air.
    /// </summary>
    public bool HasRemainingDiggable(Grid grid)
    {
        for (int y = Y0; y <= Y1; y++)
        {
            for (int x = X0; x <= X1; x++)
            {
                var m = grid[x, y];
                if (m != CellMaterial.Air && m != CellMaterial.Rock) return true;
            }
        }
        return false;
    }
}
