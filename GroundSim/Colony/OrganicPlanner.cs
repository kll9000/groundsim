namespace GroundSim;

public sealed record RoomPlan(Room Room, DigSite Site, bool UsedFallback);

/// <summary>
/// Plans a new room as an organic chamber at real distance from its parent,
/// connected by a winding tunnel — replacing the old "small rect glued to
/// Home's edge" placement. Runs once per room trigger, never per tick.
///
/// Hardened guarantee (Phase 11 Part C): Plan() ALWAYS returns a valid dig
/// plan. A bounded retry loop shrinks the target on each failed attempt; if
/// every organic attempt fails, the fallback of last resort is the old
/// glued-rect behavior, which cannot fail. A degraded-but-working room beats
/// a stuck colony (the Phase 9 severed-route lesson).
/// </summary>
public static class OrganicPlanner
{
    public static RoomPlan Plan(
        Grid grid, IReadOnlyList<Room> existingRooms, Room parent, RoomType type,
        ColonyConfig cfg, Random rng)
    {
        // Cells near any existing room (except the parent, which the tunnel
        // legitimately touches at its origin) are off-limits, with a buffer
        // so new digs don't fuse into old rooms accidentally.
        var forbidden = new HashSet<(int, int)>();
        foreach (var room in existingRooms)
        {
            if (room == parent) continue;
            foreach (var cell in room.Cells) Dilate(forbidden, cell, cfg.RoomOverlapBuffer);
        }
        var parentHalo = new HashSet<(int, int)>();
        foreach (var cell in parent.Cells) Dilate(parentHalo, cell, cfg.RoomOverlapBuffer);

        var parentAnchor = parent.FloorCenter;

        for (int attempt = 1; attempt <= cfg.MaskRetryAttempts; attempt++)
        {
            // Shrink applies to AREA only — shrinking distance would pull
            // later attempts back toward whatever blocked the earlier ones.
            double shrink = 1.0 - 0.12 * (attempt - 1);
            int area = Math.Max(12, (int)(Lerp(cfg.ChamberMinArea, cfg.ChamberMaxArea, rng.NextDouble()) * shrink));
            double dist = Lerp(cfg.RoomBranchMinDistance, cfg.RoomBranchMaxDistance, rng.NextDouble());
            double angle = Math.PI / 2 + (rng.NextDouble() * 2 - 1) * cfg.RoomBranchAngleSpread; // downward cone

            var target = (
                X: (int)Math.Round(parentAnchor.X + Math.Cos(angle) * dist),
                Y: (int)Math.Round(parentAnchor.Y + Math.Sin(angle) * dist));
            int margin = 4;
            // Prefer deeper-than-parent, but a parent near the world bottom
            // simply can't go deeper — clamp bounds must stay ordered (the
            // attempt then tries max depth and the overlap checks / fallback
            // handle the rest).
            int maxY = grid.Height - 1 - margin;
            int minY = Math.Clamp(parentAnchor.Y + 3, margin, maxY);
            target = (
                Math.Clamp(target.X, margin, grid.Width - 1 - margin),
                Math.Clamp(target.Y, minY, maxY));

            var chamber = MaskGenerator.Chamber(grid, target, area,
                cfg.ChamberEdgeNoise, cfg.CaGenerations, cfg.CaThreshold, rng);
            if (chamber.Count < 12) continue; // degenerate blob

            // Chamber must be fresh ground: not near other rooms, not near
            // the parent (the tunnel provides the connection), and almost
            // entirely undug.
            if (chamber.Any(c => forbidden.Contains(c) || parentHalo.Contains(c))) continue;
            int alreadyAir = chamber.Count(c => grid.IsAir(c.Item1, c.Item2));
            if (alreadyAir > chamber.Count / 10) continue;

            // Connecting tunnel: from the parent cell nearest the chamber,
            // biased at the chamber centroid, terminating on arrival at the
            // chamber's halo — so tunnels meet chamber walls at varied points.
            var centroid = Centroid(chamber);
            var origin = parent.Cells.OrderBy(c =>
                Math.Abs(c.X - centroid.X) + Math.Abs(c.Y - centroid.Y)).First();
            var chamberHalo = new HashSet<(int, int)>();
            foreach (var cell in chamber) Dilate(chamberHalo, cell, 1);

            var tunnel = MaskGenerator.Tunnel(grid, origin, centroid,
                cfg.TunnelWidthMin, cfg.TunnelWidthMax, cfg.TunnelTurnJitter, cfg.TunnelMaxDeviation,
                rng, arrived: c => chamberHalo.Contains(c));
            if (!tunnel.Any(c => chamberHalo.Contains(c))) continue; // never arrived
            if (tunnel.Any(c => forbidden.Contains(c))) continue;    // clipped another room

            var siteCells = new HashSet<(int, int)>(tunnel);
            siteCells.UnionWith(chamber);
            return new RoomPlan(
                new Room(type, chamber),
                new DigSite(siteCells),
                UsedFallback: false);
        }

        // Fallback of last resort: the pre-Phase-11 behavior — a small rect
        // glued below the parent. Guaranteed constructible on any grid.
        int fx0 = Math.Clamp(parent.X0 + 1, 1, grid.Width - 7);
        int fy0 = Math.Clamp(parent.Y1 + 1, 1, grid.Height - 5);
        var fallbackRoom = new Room(type, fx0, fy0, fx0 + 5, fy0 + 2);
        return new RoomPlan(fallbackRoom, new DigSite(fallbackRoom.Cells), UsedFallback: true);
    }

    private static void Dilate(HashSet<(int, int)> into, (int X, int Y) cell, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++) into.Add((cell.X + dx, cell.Y + dy));
        }
    }

    private static (double X, double Y) Centroid(IReadOnlyCollection<(int X, int Y)> cells)
        => (cells.Average(c => (double)c.X), cells.Average(c => (double)c.Y));

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
