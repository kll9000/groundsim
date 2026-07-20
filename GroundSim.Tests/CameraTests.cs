using GroundSim.Render;

namespace GroundSim.Tests;

public class CameraTests
{
    [Fact]
    public void ScreenAndWorldConversions_RoundTrip_UnderPanAndZoom()
    {
        var cam = new Camera();
        cam.ZoomAt(0, 0, 2.5);      // zoom 2.5 anchored at origin
        cam.PanBy(-340, 125.5);

        var (wx, wy) = cam.ScreenToWorld(812, 447);
        var (sx, sy) = cam.WorldToScreen(wx, wy);
        Assert.Equal(812, sx, 9);
        Assert.Equal(447, sy, 9);

        // And a known forward mapping: screen = world * zoom + pan.
        var (sx2, sy2) = cam.WorldToScreen(100, 200);
        Assert.Equal(100 * cam.Zoom + cam.PanX, sx2, 9);
        Assert.Equal(200 * cam.Zoom + cam.PanY, sy2, 9);
    }

    [Fact]
    public void ZoomAtCursor_KeepsTheWorldPointUnderTheCursor()
    {
        var cam = new Camera();
        cam.PanBy(-150, -80);
        var cursor = (X: 640.0, Y: 360.0);
        var before = cam.ScreenToWorld(cursor.X, cursor.Y);

        cam.ZoomAt(cursor.X, cursor.Y, 1.2);   // in
        var afterIn = cam.ScreenToWorld(cursor.X, cursor.Y);
        Assert.Equal(before.X, afterIn.X, 9);
        Assert.Equal(before.Y, afterIn.Y, 9);

        cam.ZoomAt(cursor.X, cursor.Y, 1 / 1.44); // back out two steps
        var afterOut = cam.ScreenToWorld(cursor.X, cursor.Y);
        Assert.Equal(before.X, afterOut.X, 9);
        Assert.Equal(before.Y, afterOut.Y, 9);
    }

    [Fact]
    public void Zoom_ClampsAtConfiguredBounds()
    {
        var cam = new Camera { MinZoom = 0.5, MaxZoom = 8.0 };
        for (int i = 0; i < 50; i++) cam.ZoomAt(0, 0, 1.5);
        Assert.Equal(8.0, cam.Zoom, 9);
        for (int i = 0; i < 50; i++) cam.ZoomAt(0, 0, 1 / 1.5);
        Assert.Equal(0.5, cam.Zoom, 9);
        // Clamped zoom must still keep the anchor point fixed.
        var before = cam.ScreenToWorld(300, 200);
        cam.ZoomAt(300, 200, 0.0001); // clamps to MinZoom (no change)
        var after = cam.ScreenToWorld(300, 200);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
    }

    [Fact]
    public void SmoothFollow_ConvergesOnCenteringTheTarget_WithoutTeleporting()
    {
        var cam = new Camera();
        cam.CenterOn(0, 0, 1600, 860);
        var target = (X: 500.0, Y: 300.0);

        double DistanceFromCenter()
        {
            var (sx, sy) = cam.WorldToScreen(target.X, target.Y);
            return Math.Sqrt(Math.Pow(sx - 800, 2) + Math.Pow(sy - 430, 2));
        }

        double previous = DistanceFromCenter();
        Assert.True(previous > 100, "target starts well off-center");
        for (int frame = 0; frame < 120; frame++)
        {
            cam.SmoothFollow(target.X, target.Y, 1600, 860);
            double now = DistanceFromCenter();
            Assert.True(now <= previous + 1e-9, $"frame {frame}: follow moved AWAY from target");
            // No teleport: a single frame never closes more than half the gap.
            Assert.True(previous - now <= previous * 0.5 + 1e-9, $"frame {frame}: follow jumped");
            previous = now;
        }
        Assert.True(previous < 1.0, $"after 120 frames target should be ~centered, off by {previous:0.00}px");
    }

    [Fact]
    public void FindNearest_PicksClosestWithinRadius_NullOutside()
    {
        var agents = new List<(double X, double Y)> { (100, 100), (110, 100), (300, 300) };

        Assert.Equal(1, Camera.FindNearest((108, 101), agents, maxDistance: 10));
        Assert.Equal(0, Camera.FindNearest((101, 99), agents, maxDistance: 10));
        Assert.Equal(2, Camera.FindNearest((295, 305), agents, maxDistance: 10));
        Assert.Null(Camera.FindNearest((200, 200), agents, maxDistance: 10)); // nothing near
        Assert.Null(Camera.FindNearest((0, 0), new List<(double, double)>(), 50)); // no agents
    }
}
