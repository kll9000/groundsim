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
    /// INDEPENDENT DIAGNOSTIC ORACLE (Phase 9.5) — deliberately NOT used by
    /// any movement logic, and deliberately not sharing IsSupported's
    /// reasoning: an agent is "visibly floating" if its cell is Air and there
    /// is no solid cell anywhere in its 3×3 neighborhood — nothing to stand
    /// on, cling to, or hang from, including a ceiling directly overhead.
    /// Exists so floating audits can catch IsSupported being WRONG (e.g. its
    /// enclosed/roof rule classifying a cell as supported because of solid
    /// material rows overhead with nothing touching the agent), instead of
    /// only confirming the production rule agrees with itself.
    /// </summary>
    public static bool IsVisiblyFloating(Grid grid, int x, int y)
    {
        if (!grid.IsAir(x, y)) return false; // buried, not floating
        if (y >= grid.Height - 1) return false; // resting on the world edge
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                // Phase 20: out-of-bounds is open void, NOT a wall — the old
                // "edge counts as a wall" convention (shared with IsSupported)
                // made the world border a climbable highway, and because BOTH
                // the rule and this oracle agreed on it, every floating audit
                // signed off on ants walking the window edge. Changed in
                // lockstep with IsSupported (the pin tests require it).
                if (!grid.InBounds(nx, ny)) continue;
                if (grid[nx, ny] != CellMaterial.Air) return false;
            }
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
        // Phase 11 unification: support is pure 3×3 CONTACT — solid anywhere
        // in the 8-neighborhood (floor, wall, corner, or ceiling to cling to)
        // or the grid bottom/edge. The former enclosed/roof branch ("any
        // roofed cell is climbable regardless of contact") was the Phase 9.5
        // documented divergence from the visual-floating oracle; it was
        // harmless while every excavation was an open pit, but Phase 11's
        // enclosed organic chambers made it live — a chamber-interior cell
        // with no contact would have counted as supported while visibly
        // floating mid-room. Under contact-based support, tunnels (≤3 wide,
        // every cell touches a wall) behave exactly as before; chamber
        // interiors are fall-through air, so agents traverse chambers along
        // their floors, walls, and ceilings like actual ants.
        // Deliberately implemented independently of IsVisiblyFloating so the
        // rule-vs-oracle audit tests remain a meaningful gate.
        if (y >= grid.Height - 1) return true; // grid bottom
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                // Phase 20 fix: out-of-bounds is open void, NOT a wall. The
                // old convention (`!IsAir` returns true for OOB) silently
                // made every border cell "supported", turning the world's
                // side columns into climbable walls and the top row into a
                // walkable ceiling — measured in a 240k-tick app-world run
                // as foragers strung along x=0 climbing to y=0 (Kevin's
                // "ants walking on the window edge"). Only REAL solid cells
                // grant contact; the grid bottom (above) remains supported.
                if (!grid.InBounds(nx, ny)) continue;
                if (grid[nx, ny] != CellMaterial.Air) return true;
            }
        }
        return false;
    }
}
