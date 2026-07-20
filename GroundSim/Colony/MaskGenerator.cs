namespace GroundSim;

/// <summary>
/// Organic excavation mask generators (Phase 11, per Kevin's design doc):
/// biased random-walk-with-jitter tunnels and CA-smoothed blob chambers.
/// Pure geometry — runs once per room creation, never per tick.
/// </summary>
public static class MaskGenerator
{
    /// <summary>
    /// Winding corridor: a random walk from origin biased toward the target,
    /// with per-step heading jitter clamped to maxDeviation of the (re-aimed
    /// each step) bias direction, stamping a variable-radius disc at each
    /// step. Terminates on reaching <paramref name="arrived"/> (e.g. the
    /// target chamber's halo), on reaching the target point, at the step cap,
    /// or at the grid edge.
    /// </summary>
    public static HashSet<(int X, int Y)> Tunnel(
        Grid grid, (int X, int Y) origin, (double X, double Y) target,
        double widthMin, double widthMax, double turnJitter, double maxDeviation,
        Random rng, Func<(int X, int Y), bool>? arrived = null,
        // Phase 15: 400 → 800 (×GridScale). Steps are ~1 cell each and
        // branch distances doubled; an unscaled cap would truncate long
        // winding tunnels mid-flight (silent "never arrived" plan rejects).
        int maxSteps = 400 * ColonyConfig.GridScale)
    {
        var mask = new HashSet<(int X, int Y)>();
        double px = origin.X + 0.5, py = origin.Y + 0.5;
        double heading = Math.Atan2(target.Y - py, target.X - px);

        for (int step = 0; step < maxSteps; step++)
        {
            double bias = Math.Atan2(target.Y - py, target.X - px);
            heading += (rng.NextDouble() * 2 - 1) * turnJitter;
            double delta = NormalizeAngle(heading - bias);
            if (delta > maxDeviation) heading = bias + maxDeviation;
            else if (delta < -maxDeviation) heading = bias - maxDeviation;

            px += Math.Cos(heading);
            py += Math.Sin(heading);
            var center = (X: (int)Math.Round(px), Y: (int)Math.Round(py));
            if (!grid.InBounds(center.X, center.Y)) break;

            double radius = (widthMin + rng.NextDouble() * (widthMax - widthMin)) / 2.0;
            Stamp(mask, grid, center, radius);

            if (arrived is not null && arrived(center)) break;
            if (Math.Abs(px - target.X) < 1.0 && Math.Abs(py - target.Y) < 1.0) break;
        }
        return mask;
    }

    /// <summary>
    /// Lumpy chamber blob: noisy-disc seed smoothed by a cellular automaton,
    /// keeping only the largest connected component. Returned in world
    /// coordinates centered on <paramref name="center"/>; out-of-bounds cells
    /// are dropped.
    /// </summary>
    public static HashSet<(int X, int Y)> Chamber(
        Grid grid, (int X, int Y) center, int targetArea, double edgeNoise,
        int generations, int threshold, Random rng)
    {
        // +1.4 pad compensates for the pure CA rule eroding the noisy rim.
        // Phase 15: the rim constants here (1.4 / 1.0 / 2.5 / 0.12) are
        // deliberately UNSCALED — they're the blob's edge texture in cells,
        // and at the finer grid that texture is proportionally finer, which
        // is the added detail this phase exists for. If Kevin's visual check
        // finds chamber edges too rough, these (with CaGenerations) are the
        // knobs — flagged in the Phase 15 report.
        double r = Math.Sqrt(targetArea / Math.PI) + 1.4;
        int size = (int)Math.Ceiling(r * 2) + 10;
        double c = size / 2.0;

        var work = new bool[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double d = Math.Sqrt((x + 0.5 - c) * (x + 0.5 - c) + (y + 0.5 - c) * (y + 0.5 - c));
                // Wide noisy rim (r±2.5) is what breaks circular symmetry;
                // the CA then smooths it into lobes rather than a disc.
                if (d <= r - 1.0) work[x, y] = true;
                else if (d <= r + 2.5 && rng.NextDouble() < edgeNoise + (r - d) * 0.12) work[x, y] = true;
            }
        }

        for (int g = 0; g < generations; g++)
        {
            var next = new bool[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && nx < size && ny >= 0 && ny < size && work[nx, ny]) n++;
                        }
                    }
                    next[x, y] = n >= threshold; // pure birth/survival rule per the design doc
                }
            }
            work = next;
        }

        var largest = LargestComponent(work, size);

        var mask = new HashSet<(int X, int Y)>();
        int half = size / 2;
        foreach (var (lx, ly) in largest)
        {
            int wx = center.X - half + lx;
            int wy = center.Y - half + ly;
            if (grid.InBounds(wx, wy)) mask.Add((wx, wy));
        }
        return mask;
    }

    private static void Stamp(HashSet<(int X, int Y)> mask, Grid grid, (int X, int Y) center, double radius)
    {
        int r = (int)Math.Ceiling(radius);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > radius * radius + 0.01) continue;
                int x = center.X + dx, y = center.Y + dy;
                if (grid.InBounds(x, y)) mask.Add((x, y));
            }
        }
        mask.Add(center); // radius < 1 must still stamp the center cell
    }

    private static List<(int X, int Y)> LargestComponent(bool[,] work, int size)
    {
        var seen = new bool[size, size];
        var best = new List<(int, int)>();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!work[x, y] || seen[x, y]) continue;
                var component = new List<(int, int)>();
                var queue = new Queue<(int, int)>();
                queue.Enqueue((x, y));
                seen[x, y] = true;
                (int, int)[] nbrs = { (0, 1), (0, -1), (1, 0), (-1, 0) };
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    component.Add((cx, cy));
                    foreach (var (dx, dy) in nbrs)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx >= 0 && nx < size && ny >= 0 && ny < size && work[nx, ny] && !seen[nx, ny])
                        {
                            seen[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
                if (component.Count > best.Count) best = component;
            }
        }
        return best;
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
