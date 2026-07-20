namespace GroundSim;

public sealed record RoomPlan(Room Room, DigSite Site, bool UsedFallback);

/// <summary>The founding excavation: entrance shaft + home chamber.
/// Entrance is the surface air cell above the shaft mouth.</summary>
public sealed record FoundingPlan(
    Room HomeRoom, DigSite Site, (int X, int Y) Entrance, int EntranceHalfWidth, bool UsedFallback);

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
            // Phase 12.5: the origin must be a CURRENTLY-AIR parent cell (a
            // spill-covered corner cell as origin gave a garden site with
            // zero air contact from birth), and the junction gets widened +
            // validated below — a single-cell junction can be sealed by one
            // settling particle, permanently orphaning the whole site.
            var centroid = Centroid(chamber);
            var originPool = parent.Cells.Where(c => grid.IsAir(c.X, c.Y)).ToList();
            if (originPool.Count == 0) originPool = parent.Cells.ToList();
            var origin = originPool.OrderBy(c =>
                Math.Abs(c.X - centroid.X) + Math.Abs(c.Y - centroid.Y)).First();
            var chamberHalo = new HashSet<(int, int)>();
            foreach (var cell in chamber) Dilate(chamberHalo, cell, 1);

            var tunnel = MaskGenerator.Tunnel(grid, origin, centroid,
                cfg.TunnelWidthMin, cfg.TunnelWidthMax, cfg.TunnelTurnJitter, cfg.TunnelMaxDeviation,
                rng, arrived: c => chamberHalo.Contains(c));
            // Widen the tunnel mouth at the parent wall (multi-cell junction).
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int mx = origin.X + dx, my = origin.Y + dy;
                    if (grid.InBounds(mx, my)) tunnel.Add((mx, my));
                }
            }
            if (!tunnel.Any(c => chamberHalo.Contains(c))) continue; // never arrived
            if (tunnel.Any(c => forbidden.Contains(c))) continue;    // clipped another room
            // Junction redundancy: at least 2 diggable tunnel cells must
            // touch parent air right now.
            var parentAir = new HashSet<(int, int)>(parent.Cells.Where(c => grid.IsAir(c.X, c.Y)));
            int junction = tunnel.Count(c =>
                grid.InBounds(c.X, c.Y)
                && grid[c.X, c.Y] != CellMaterial.Air && grid[c.X, c.Y] != CellMaterial.Rock
                && (parentAir.Contains((c.X - 1, c.Y)) || parentAir.Contains((c.X + 1, c.Y))
                    || parentAir.Contains((c.X, c.Y - 1)) || parentAir.Contains((c.X, c.Y + 1))));
            if (junction < 2) continue;

            var siteCells = new HashSet<(int, int)>(tunnel);
            siteCells.UnionWith(chamber);

            // Rock-connectivity validation (Phase 12): terrain Rock is known
            // at plan time, and a rock band can sever the tunnel so the
            // chamber is never reachable by the dig frontier — the site then
            // "completes" sealed (measured). Require most of the chamber to
            // be reachable from the origin through non-Rock site cells.
            if (ReachableChamberFraction(grid, origin, siteCells, chamber) < 0.7) continue;

            return new RoomPlan(
                new Room(type, chamber),
                new DigSite(siteCells),
                UsedFallback: false);
        }

        // Fallback of last resort: the pre-Phase-11 behavior — a small rect
        // glued below the parent. Phase 12.5: anchored under a parent air
        // cell whose below-neighbor is genuinely DIGGABLE — anchoring at the
        // bounding box (may touch no chamber cell) or at a floor cell over
        // rock (measured, seed 3) yields a fallback the frontier can never
        // enter. Candidates: deepest first, then nearest the chamber center.
        var anchorCandidates = parent.Cells
            .Where(c => grid.IsAir(c.X, c.Y) && grid.InBounds(c.X, c.Y + 1)
                && grid[c.X, c.Y + 1] != CellMaterial.Air
                && grid[c.X, c.Y + 1] != CellMaterial.Rock)
            .OrderByDescending(c => c.Y)
            .ThenBy(c => Math.Abs(c.X - parent.Center.X))
            .ToList();
        var anchor = anchorCandidates.Count > 0 ? anchorCandidates[0] : parent.FloorSite(grid);
        int fx0 = Math.Clamp(anchor.X - 3, 1, grid.Width - 7);
        int fy0 = Math.Clamp(anchor.Y + 1, 1, grid.Height - 5);
        var fallbackRoom = new Room(type, fx0, fy0, fx0 + 5, fy0 + 2);
        return new RoomPlan(fallbackRoom, new DigSite(fallbackRoom.Cells), UsedFallback: true);
    }

    /// <summary>
    /// Plans the founding excavation: a straight, narrow entrance shaft
    /// (MaskGenerator.Tunnel at near-zero jitter/deviation — the same
    /// generator as lateral corridors, parameterized rather than duplicated)
    /// down to a CA-blob home chamber. Same hardened guarantee as Plan():
    /// always returns a valid plan; the fallback of last resort is the old
    /// simple rect chamber, which cannot fail — a stalled founding would
    /// block everything else in the colony.
    /// </summary>
    public static FoundingPlan PlanFounding(Grid grid, int entranceX, ColonyConfig cfg, Random rng)
    {
        entranceX = Math.Clamp(entranceX, 6, grid.Width - 7);
        int surfaceY = 0;
        while (surfaceY < grid.Height && grid.IsAir(entranceX, surfaceY)) surfaceY++;
        var entrance = (X: entranceX, Y: Math.Max(0, surfaceY - 1));

        for (int attempt = 1; attempt <= cfg.MaskRetryAttempts; attempt++)
        {
            double shrink = 1.0 - 0.12 * (attempt - 1);
            int area = Math.Max(12, (int)(Lerp(cfg.HomeChamberMinArea, cfg.HomeChamberMaxArea, rng.NextDouble()) * shrink));
            int shaftLen = (int)Lerp(cfg.ShaftMinLength, cfg.ShaftMaxLength, rng.NextDouble());
            var target = (
                X: Math.Clamp(entranceX + rng.Next(-2, 3), 5, grid.Width - 6),
                Y: Math.Clamp(surfaceY + shaftLen + 3, 5, grid.Height - 6));

            var chamber = MaskGenerator.Chamber(grid, target, area,
                cfg.ChamberEdgeNoise, cfg.CaGenerations, cfg.CaThreshold, rng);
            if (chamber.Count < 12) continue;
            if (chamber.Min(c => c.Y) < surfaceY + 4) continue; // must sit below a real shaft
            int alreadyAir = chamber.Count(c => grid.IsAir(c.X, c.Y));
            if (alreadyAir > chamber.Count / 10) continue;

            var chamberHalo = new HashSet<(int, int)>();
            foreach (var cell in chamber) Dilate(chamberHalo, cell, 1);

            double ccx = chamber.Average(c => (double)c.X);
            double ccy = chamber.Average(c => (double)c.Y);
            var shaft = MaskGenerator.Tunnel(grid, entrance, (ccx, ccy),
                widthMin: 2, widthMax: 2, cfg.ShaftTurnJitter, cfg.ShaftMaxDeviation,
                rng, arrived: c => chamberHalo.Contains(c));
            if (!shaft.Any(c => chamberHalo.Contains(c))) continue;

            var siteCells = new HashSet<(int, int)>(shaft);
            siteCells.UnionWith(chamber);
            // Same rock-connectivity validation as room plans — a founding
            // chamber sealed behind rock would settle the Queen inside solid.
            if (ReachableChamberFraction(grid, entrance, siteCells, chamber) < 0.7) continue;
            AddChimney(siteCells, grid, entranceX, surfaceY);
            return new FoundingPlan(
                new Room(RoomType.Home, chamber), new DigSite(siteCells),
                entrance, EntranceHalfWidth: 2, UsedFallback: false);
        }

        // Fallback of last resort: the pre-Phase-12 simple rect chamber dug
        // straight down from the surface. Guaranteed constructible.
        int x0 = Math.Clamp(entranceX - 4, 1, grid.Width - 10);
        int y0 = Math.Clamp(surfaceY, 1, grid.Height - 5);
        var room = new Room(RoomType.Home, x0, y0, x0 + 8, y0 + 3);
        var fallbackCells = new HashSet<(int, int)>(room.Cells);
        AddChimney(fallbackCells, grid, entranceX, surfaceY);
        return new FoundingPlan(room, new DigSite(fallbackCells),
            entrance, EntranceHalfWidth: 5, UsedFallback: true);
    }

    /// <summary>
    /// The entrance chimney (Phase 12): the 3-wide column above the shaft
    /// mouth, up through any future mound height. Air at founding time —
    /// nothing to dig — but mound spill that caps the entrance settles into
    /// these cells, and cells outside every dig site would seal the colony
    /// shut permanently (measured: 3 of 10 seeds stranded their queen on the
    /// mound). With the chimney in the founding/maintenance site, the
    /// entrance hole stays maintained through the growing mound — the real
    /// ant-hill crater look.
    /// </summary>
    internal static void AddChimney(HashSet<(int, int)> site, Grid grid, int entranceX, int surfaceY)
    {
        int top = Math.Max(1, surfaceY - 12);
        for (int y = top; y < surfaceY; y++)
        {
            for (int x = entranceX - 1; x <= entranceX + 1; x++)
            {
                if (grid.InBounds(x, y)) site.Add((x, y));
            }
        }
    }

    /// <summary>Fraction of chamber cells reachable from origin through
    /// non-Rock site cells (4-adjacency). Rock is fixed terrain known at
    /// plan time; anything below ~1.0 means part of the chamber can only be
    /// reached by digging through undiggable material.</summary>
    private static double ReachableChamberFraction(
        Grid grid, (int X, int Y) origin, HashSet<(int, int)> site, HashSet<(int, int)> chamber)
    {
        var passable = new HashSet<(int, int)>(site.Where(c =>
            grid.InBounds(c.Item1, c.Item2) && grid[c.Item1, c.Item2] != CellMaterial.Rock))
        {
            origin,
        };
        var seen = new HashSet<(int, int)> { origin };
        var queue = new Queue<(int, int)>(seen);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (passable.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        int reached = chamber.Count(c => seen.Contains(c));
        return reached / (double)chamber.Count;
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
