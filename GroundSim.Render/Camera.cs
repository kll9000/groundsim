namespace GroundSim.Render;

/// <summary>
/// Pure camera math: a zoom + pan transform between world-bitmap pixels and
/// screen (viewport) pixels. No WPF types — headlessly unit-testable.
///
///   screen = world * Zoom + Pan        world = (screen - Pan) / Zoom
///
/// The camera never touches simulation state and the simulation never knows
/// the camera exists.
/// </summary>
public sealed class Camera
{
    public double Zoom { get; private set; } = 1.0;
    public double PanX { get; private set; }
    public double PanY { get; private set; }

    public double MinZoom { get; init; } = 0.5;
    public double MaxZoom { get; init; } = 8.0;

    public (double X, double Y) ScreenToWorld(double sx, double sy)
        => ((sx - PanX) / Zoom, (sy - PanY) / Zoom);

    public (double X, double Y) WorldToScreen(double wx, double wy)
        => (wx * Zoom + PanX, wy * Zoom + PanY);

    public void PanBy(double dx, double dy)
    {
        PanX += dx;
        PanY += dy;
    }

    /// <summary>
    /// Zooms by <paramref name="factor"/> keeping the world point under the
    /// screen position (sx, sy) exactly under it afterwards — the standard
    /// cursor-centered zoom.
    /// </summary>
    public void ZoomAt(double sx, double sy, double factor)
    {
        var (wx, wy) = ScreenToWorld(sx, sy);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        PanX = sx - wx * Zoom;
        PanY = sy - wy * Zoom;
    }

    /// <summary>Instantly centers the given world point in the viewport.</summary>
    public void CenterOn(double wx, double wy, double viewportW, double viewportH)
    {
        PanX = viewportW / 2 - wx * Zoom;
        PanY = viewportH / 2 - wy * Zoom;
    }

    /// <summary>
    /// One frame of smooth follow: eases the pan toward centering the given
    /// world point. Exponential easing — each frame closes
    /// <paramref name="easing"/> of the remaining distance, so following a
    /// moving agent trails it slightly instead of hard-locking to center.
    /// </summary>
    public void SmoothFollow(double wx, double wy, double viewportW, double viewportH, double easing = 0.12)
    {
        double targetPanX = viewportW / 2 - wx * Zoom;
        double targetPanY = viewportH / 2 - wy * Zoom;
        PanX += (targetPanX - PanX) * easing;
        PanY += (targetPanY - PanY) * easing;
    }

    /// <summary>
    /// Click hit-testing helper: index of the candidate nearest to the given
    /// world point, if within maxDistance (same units as the points). Null
    /// when nothing is close enough.
    /// </summary>
    public static int? FindNearest(
        (double X, double Y) worldPoint, IReadOnlyList<(double X, double Y)> candidates, double maxDistance)
    {
        int? best = null;
        double bestSq = maxDistance * maxDistance;
        for (int i = 0; i < candidates.Count; i++)
        {
            double dx = candidates[i].X - worldPoint.X;
            double dy = candidates[i].Y - worldPoint.Y;
            double dSq = dx * dx + dy * dy;
            if (dSq <= bestSq)
            {
                bestSq = dSq;
                best = i;
            }
        }
        return best;
    }
}
