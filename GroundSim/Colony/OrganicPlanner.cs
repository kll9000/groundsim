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
    // Phase 15: the grid-fineness factor. Every literal in this file that
    // denotes a physical distance/offset (margins, jitters, shaft width,
    // rect fallbacks, chimney) scales by S; areas scale by S*S; fractions
    // (0.7 reachability, /10 already-air) and pure adjacency (halo radius
    // 1, +1 below-parent) are scale-invariant and deliberately unscaled.
    private const int S = ColonyConfig.GridScale;

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
            int area = Math.Max(12 * S * S, (int)(Lerp(cfg.ChamberMinArea, cfg.ChamberMaxArea, rng.NextDouble()) * shrink));
            double dist = Lerp(cfg.RoomBranchMinDistance, cfg.RoomBranchMaxDistance, rng.NextDouble());
            double angle = Math.PI / 2 + (rng.NextDouble() * 2 - 1) * cfg.RoomBranchAngleSpread; // downward cone

            var target = (
                X: (int)Math.Round(parentAnchor.X + Math.Cos(angle) * dist),
                Y: (int)Math.Round(parentAnchor.Y + Math.Sin(angle) * dist));
            int margin = 4 * S;
            // Prefer deeper-than-parent, but a parent near the world bottom
            // simply can't go deeper — clamp bounds must stay ordered (the
            // attempt then tries max depth and the overlap checks / fallback
            // handle the rest).
            int maxY = grid.Height - 1 - margin;
            int minY = Math.Clamp(parentAnchor.Y + 3 * S, margin, maxY);
            target = (
                Math.Clamp(target.X, margin, grid.Width - 1 - margin),
                Math.Clamp(target.Y, minY, maxY));

            var chamber = MaskGenerator.Chamber(grid, target, area,
                cfg.ChamberEdgeNoise, cfg.CaGenerations, cfg.CaThreshold, rng);
            if (chamber.Count < 12 * S * S) continue; // degenerate blob (area floor, ×S²)

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
            // Phase 15: radius scales — the widening exists so one settling
            // particle can't seal the mouth, and particles are cell-sized,
            // so this is a physical opening size, not an adjacency idiom.
            for (int dy = -S; dy <= S; dy++)
            {
                for (int dx = -S; dx <= S; dx++)
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
                && grid[c.X, c.Y] != CellMaterial.Air // Phase 13: rock is diggable, counts as junction
                && (parentAir.Contains((c.X - 1, c.Y)) || parentAir.Contains((c.X + 1, c.Y))
                    || parentAir.Contains((c.X, c.Y - 1)) || parentAir.Contains((c.X, c.Y + 1))));
            // Phase 15: 2 → 2*S. The handoff guessed counts are scale-
            // invariant; this one is not — it's a physical opening width in
            // disguise (2 fine cells = 1 old cell, exactly the single-cell
            // fragility Phase 12.5 deemed seal-prone). Divergence flagged in
            // the report.
            if (junction < 2 * S) continue;

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
                && grid[c.X, c.Y + 1] != CellMaterial.Air) // Phase 13: rock below is fine — it's diggable
            .OrderByDescending(c => c.Y)
            .ThenBy(c => Math.Abs(c.X - parent.Center.X))
            .ToList();
        var anchor = anchorCandidates.Count > 0 ? anchorCandidates[0] : parent.FloorSite(grid);
        // Phase 15: the 6×3-cell fallback rect scales to 6S×3S (same physical
        // room); the +1 below-anchor stays (adjacency, not distance).
        //
        // Phase 18.5: the rect must not land ON an existing room, and must
        // keep a real air junction to the excavated nest. The original
        // glue-below-parent placement predates multi-room nests; once
        // several rooms stack under Home, the anchor's below-space may BE a
        // sibling room. Measured consequence at seed 6: a garden fallback
        // overlapped the Food-storage rect, leaving a degenerate 3-cell
        // site that hit the unreachable-target blacklist livelock Phase 15
        // had documented as impossible from planner geometry — so
        // non-overlap is a correctness requirement here, not cosmetics.
        // Candidate search, bounded: the classic glue spot first, then
        // positions below and beside each excavated room (nests grow
        // outward/downward), each requiring (a) zero overlap with ANY
        // room's cells and (b) ≥2S rect cells adjacent to the anchor
        // room's air (the junction-redundancy rule — a sealed rect would
        // be a born-dead site). Last resort if nothing fits: the original
        // glue position, overlap and all — the cannot-fail guarantee holds.
        int glueX = Math.Clamp(anchor.X - 3 * S, 1, grid.Width - 6 * S - 1);
        int glueY = Math.Clamp(anchor.Y + 1, 1, grid.Height - 3 * S - 2);

        bool InBounds(int x0, int y0) =>
            x0 >= 1 && y0 >= 1 && x0 + 6 * S - 1 <= grid.Width - 2 && y0 + 3 * S - 1 <= grid.Height - 2;
        bool OverlapsAnyRoom(int x0, int y0)
        {
            foreach (var room in existingRooms)
            {
                foreach (var (cx, cy) in room.Cells)
                {
                    if (cx >= x0 && cx < x0 + 6 * S && cy >= y0 && cy < y0 + 3 * S) return true;
                }
            }
            return false;
        }
        int AirJunction(int x0, int y0, Room touch)
        {
            int n = 0;
            foreach (var (cx, cy) in touch.Cells)
            {
                if (!grid.IsAir(cx, cy)) continue;
                // touch-cell adjacent to any rect cell?
                bool adj = (cx >= x0 - 1 && cx <= x0 + 6 * S && cy >= y0 && cy < y0 + 3 * S)
                        || (cy >= y0 - 1 && cy <= y0 + 3 * S && cx >= x0 && cx < x0 + 6 * S);
                if (adj && (cx == x0 - 1 || cx == x0 + 6 * S || cy == y0 - 1 || cy == y0 + 3 * S
                            || (cx >= x0 && cx < x0 + 6 * S && cy >= y0 && cy < y0 + 3 * S))) n++;
            }
            return n;
        }
        RoomPlan? TryCandidate(int x0, int y0, Room touch)
        {
            if (!InBounds(x0, y0) || OverlapsAnyRoom(x0, y0)) return null;
            if (AirJunction(x0, y0, touch) < 2 * S) return null;
            var room = new Room(type, x0, y0, x0 + 6 * S - 1, y0 + 3 * S - 1);
            return new RoomPlan(room, new DigSite(room.Cells), UsedFallback: true);
        }

        var anchors = new List<Room> { parent };
        anchors.AddRange(existingRooms.Where(r => r.Excavated && r != parent));
        foreach (var touch in anchors)
        {
            // Below the room, scanning across its width; then flanking left
            // and right, scanning down its height.
            for (int x0 = touch.X0 - 3 * S; x0 <= touch.X1 + 1; x0 += 3 * S)
            {
                if (TryCandidate(x0, touch.Y1 + 1, touch) is { } below) return below;
            }
            for (int y0 = touch.Y0; y0 <= touch.Y1 + 1; y0 += 3 * S)
            {
                if (TryCandidate(touch.X1 + 1, y0, touch) is { } right) return right;
                if (TryCandidate(touch.X0 - 6 * S, y0, touch) is { } left) return left;
            }
        }

        // Absolute last resort: the pre-18.5 glue placement.
        var fallbackRoom = new Room(type, glueX, glueY, glueX + 6 * S - 1, glueY + 3 * S - 1);
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
        entranceX = Math.Clamp(entranceX, 6 * S, grid.Width - 6 * S - 1);
        int surfaceY = 0;
        while (surfaceY < grid.Height && grid.IsAir(entranceX, surfaceY)) surfaceY++;
        var entrance = (X: entranceX, Y: Math.Max(0, surfaceY - 1));

        for (int attempt = 1; attempt <= cfg.MaskRetryAttempts; attempt++)
        {
            double shrink = 1.0 - 0.12 * (attempt - 1);
            int area = Math.Max(12 * S * S, (int)(Lerp(cfg.HomeChamberMinArea, cfg.HomeChamberMaxArea, rng.NextDouble()) * shrink));
            int shaftLen = (int)Lerp(cfg.ShaftMinLength, cfg.ShaftMaxLength, rng.NextDouble());
            var target = (
                X: Math.Clamp(entranceX + rng.Next(-2 * S, 2 * S + 1), 5 * S, grid.Width - 5 * S - 1),
                Y: Math.Clamp(surfaceY + shaftLen + 3 * S, 5 * S, grid.Height - 5 * S - 1));

            var chamber = MaskGenerator.Chamber(grid, target, area,
                cfg.ChamberEdgeNoise, cfg.CaGenerations, cfg.CaThreshold, rng);
            if (chamber.Count < 12 * S * S) continue;
            if (chamber.Min(c => c.Y) < surfaceY + 4 * S) continue; // must sit below a real shaft
            int alreadyAir = chamber.Count(c => grid.IsAir(c.X, c.Y));
            if (alreadyAir > chamber.Count / 10) continue;

            var chamberHalo = new HashSet<(int, int)>();
            foreach (var cell in chamber) Dilate(chamberHalo, cell, 1);

            double ccx = chamber.Average(c => (double)c.X);
            double ccy = chamber.Average(c => (double)c.Y);
            var shaft = MaskGenerator.Tunnel(grid, entrance, (ccx, ccy),
                widthMin: 2 * S, widthMax: 2 * S, cfg.ShaftTurnJitter, cfg.ShaftMaxDeviation,
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
                entrance, EntranceHalfWidth: 2 * S, UsedFallback: false);
        }

        // Fallback of last resort: the pre-Phase-12 simple rect chamber dug
        // straight down from the surface. Guaranteed constructible.
        // Phase 15: the 9×4-cell fallback chamber scales to 9S×4S (same
        // physical room), and its clamps scale with it.
        int x0 = Math.Clamp(entranceX - 4 * S, 1, grid.Width - 9 * S - 1);
        int y0 = Math.Clamp(surfaceY, 1, grid.Height - 4 * S - 1);
        var room = new Room(RoomType.Home, x0, y0, x0 + 9 * S - 1, y0 + 4 * S - 1);
        var fallbackCells = new HashSet<(int, int)>(room.Cells);
        AddChimney(fallbackCells, grid, entranceX, surfaceY);
        return new FoundingPlan(room, new DigSite(fallbackCells),
            entrance, EntranceHalfWidth: 5 * S, UsedFallback: true);
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
        // Phase 15: height 12 → 12*S (must clear the scaled MoundMaxHeight,
        // preserving the old 12-vs-7 headroom ratio as 24-vs-14); width
        // ±1 → ±S (the physical entrance-hole width — must stay at least as
        // wide as the scaled 2S shaft bore, preserving the old 3-covers-2
        // relationship as 2S+1 covers 2S).
        int top = Math.Max(1, surfaceY - 12 * S);
        for (int y = top; y < surfaceY; y++)
        {
            for (int x = entranceX - S; x <= entranceX + S; x++)
            {
                if (grid.InBounds(x, y)) site.Add((x, y));
            }
        }
    }

    /// <summary>Fraction of chamber cells connected to the origin through
    /// site cells (4-adjacency). Phase 13: Rock is diggable, so it no longer
    /// blocks passability — this is now a pure mask-connectivity guard
    /// (rejects malformed/clipped masks) rather than a rock-sealing check.
    /// The 70% threshold is retained as that structural guard.</summary>
    private static double ReachableChamberFraction(
        Grid grid, (int X, int Y) origin, HashSet<(int, int)> site, HashSet<(int, int)> chamber)
    {
        var passable = new HashSet<(int, int)>(site.Where(c =>
            grid.InBounds(c.Item1, c.Item2)))
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
