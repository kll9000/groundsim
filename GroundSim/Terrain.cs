namespace GroundSim;

/// <summary>
/// Phase 9 movement-support rules. Distinguishes enclosed (dug) space, where
/// agents climb freely as always, from open-surface space, where movement
/// must respect the ground.
///
/// IMPORTANT DEVIATION (flagged): the handoff's strict rule — surface-open
/// cells require solid DIRECTLY BELOW — would strand agents permanently,
/// because this project's rooms and founding excavations are open pits under
/// open sky (documented since Phase 4), making their interiors and shaft
/// walls "surface-open" and therefore unclimbable. Amendment: a surface-open
/// cell also counts as supported when a solid CARDINAL SIDE-neighbor exists
/// (wall-cling). Result: pit/shaft walls stay climbable; an agent in truly
/// open air — no floor below, no wall beside — falls.
///
/// Cost note: IsSurfaceOpen is a per-column scan above the cell, O(height)
/// worst case per query, invoked per agent per tick — NOT per grid cell. No
/// O(grid) cost is introduced (verified by the Phase 9 performance test).
/// </summary>
public static class Terrain
{
    /// <summary>Air cell under an unbroken run of Air to the top of the grid
    /// (open sky). Any solid cell above — even one — makes it enclosed.</summary>
    public static bool IsSurfaceOpen(Grid grid, int x, int y)
    {
        if (!grid.IsAir(x, y)) return false;
        for (int yy = y - 1; yy >= 0; yy--)
        {
            if (grid[x, yy] != CellMaterial.Air) return false;
        }
        return true;
    }

    /// <summary>
    /// Whether an agent standing in (x, y) may stay there rather than fall:
    /// grid bottom, solid below, enclosed space (free climbing, unchanged
    /// Phase 4 behavior), or cling contact — a solid side-neighbor OR a solid
    /// diagonal-down neighbor. Diagonal-down matters at excavation mouths: the
    /// cell above a freshly dug 1-wide hole has air below and beside it, and
    /// without corner-cling an agent could never climb out of its own dig
    /// (the founding queen would trap herself in her first excavated cell).
    /// </summary>
    public static bool IsSupported(Grid grid, int x, int y)
    {
        // O(1) contact checks first — the column scan only runs for cells
        // with no solid contact at all (free air, or the interior of a large
        // enclosed cavern). Out-of-bounds sides read as walls, which
        // conveniently treats the grid edge as a climbable boundary.
        if (y >= grid.Height - 1) return true;            // grid bottom
        if (!grid.IsAir(x, y + 1)) return true;           // standing on solid
        if (!grid.IsAir(x - 1, y) || !grid.IsAir(x + 1, y)) return true;         // wall-cling
        if (!grid.IsAir(x - 1, y + 1) || !grid.IsAir(x + 1, y + 1)) return true; // corner-cling
        return !IsSurfaceOpen(grid, x, y);                // enclosed: climbable
    }
}
