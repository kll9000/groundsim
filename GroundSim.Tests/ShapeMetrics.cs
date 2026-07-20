namespace GroundSim.Tests;

/// <summary>Shape metrics for chamber-irregularity testing (Phase 11.5,
/// item 2): scores are only meaningful RELATIVE to a same-area perfect
/// disc's score — at chamber scale (~40–70 cells) absolute radial deviation
/// is dominated by pixelation noise (a perfect digital disc scores
/// 0.05–0.08 by itself).</summary>
public static class ShapeMetrics
{
    /// <summary>Relative radial deviation (std/mean of centroid→boundary
    /// distances). Zero for an ideal continuous circle; nonzero for any
    /// digital shape (pixelation).</summary>
    public static double RadialDeviation(HashSet<(int X, int Y)> mask)
    {
        double cx = mask.Average(c => (double)c.X);
        double cy = mask.Average(c => (double)c.Y);
        var boundary = mask.Where(c =>
            !mask.Contains((c.X + 1, c.Y)) || !mask.Contains((c.X - 1, c.Y))
            || !mask.Contains((c.X, c.Y + 1)) || !mask.Contains((c.X, c.Y - 1))).ToList();
        var radii = boundary.Select(c => Math.Sqrt((c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy))).ToList();
        double mean = radii.Average();
        double std = Math.Sqrt(radii.Average(r => (r - mean) * (r - mean)));
        return std / mean;
    }

    /// <summary>The `area` cells nearest the origin — the most circular
    /// digital shape of that cell count; the pixelation-noise baseline.</summary>
    public static HashSet<(int X, int Y)> PerfectDisc(int area)
    {
        var candidates = new List<(int X, int Y, double D)>();
        for (int y = -20; y <= 20; y++)
        {
            for (int x = -20; x <= 20; x++) candidates.Add((x, y, Math.Sqrt(x * x + y * y)));
        }
        return candidates.OrderBy(c => c.D).Take(area).Select(c => (c.X, c.Y)).ToHashSet();
    }
}
