using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GroundSim.Render;

/// <summary>
/// Render loop host with a real camera: the full world renders into one
/// WriteableBitmap exactly as before (dirty-cell updates unchanged), and the
/// camera pans/zooms that bitmap via GPU render transforms — pan/zoom never
/// touches the bitmap, so a stationary camera over an idle colony still
/// redraws only changed cells.
///
/// Controls: left-drag pan · wheel zoom (cursor-centered) · click an agent to
/// follow it (smooth ease) · Esc or click empty space to release ·
/// Space pause · Up/Down speed.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Grid _grid;
    private readonly Simulation _sim;
    private readonly Colony _colony;
    private readonly DirtyTracker _dirty;
    private readonly TickClock _clock = new() { TicksPerSecond = 60 };
    private readonly GridRenderer _renderer;
    private readonly HashSet<Room> _tintedRooms = new();
    private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
    private int _lastDirtyCount;

    private readonly Camera _camera = new();
    private (string Label, Func<(int X, int Y)> Cell)? _follow;
    private Point _dragStart;
    private Point _lastDragPoint;
    private bool _dragging;
    private bool _mouseDown;

    public MainWindow()
    {
        InitializeComponent();

        (_grid, _sim, _colony) = ColonyScenario.Create();
        _dirty = new DirtyTracker(_grid);
        _renderer = new GridRenderer(_grid);

        SurfaceImage.Source = _renderer.Bitmap;
        _renderer.DrawFull(_colony);
        _dirty.Clear();

        Loaded += (_, _) =>
        {
            // Start focused on the founding site at a comfortable zoom.
            _camera.ZoomAt(0, 0, 2.0 / _camera.Zoom);
            var home = _colony.HomeCenter;
            _camera.CenterOn((home.X + 0.5) * GridRenderer.CellSize, (home.Y - 6) * GridRenderer.CellSize,
                ViewportHost.ActualWidth, ViewportHost.ActualHeight);
            ViewportHost.Focus();
        };

        KeyDown += OnKeyDown;
        ViewportHost.MouseWheel += OnMouseWheel;
        ViewportHost.MouseLeftButtonDown += OnMouseDown;
        ViewportHost.MouseMove += OnMouseMove;
        ViewportHost.MouseLeftButtonUp += OnMouseUp;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double dt = _frameTimer.Elapsed.TotalSeconds;
        _frameTimer.Restart();

        int ticks = _clock.Advance(dt);
        for (int t = 0; t < ticks; t++)
        {
            MarkColonyEntities();
            _colony.Tick();
            _sim.Tick();
            MarkColonyEntities();
        }

        foreach (var room in _colony.Rooms)
        {
            if (room.Excavated && _tintedRooms.Add(room))
            {
                for (int y = room.Y0; y <= room.Y1; y++)
                {
                    for (int x = room.X0; x <= room.X1; x++) _dirty.Mark(x, y);
                }
            }
        }

        if (ticks > 0)
        {
            _renderer.DrawFrame(_dirty.Cells, _sim, _colony);
            _lastDirtyCount = _dirty.Count;
            _dirty.Clear();
        }

        if (_follow is { } f)
        {
            var (cx, cy) = f.Cell();
            _camera.SmoothFollow((cx + 0.5) * GridRenderer.CellSize, (cy + 0.5) * GridRenderer.CellSize,
                ViewportHost.ActualWidth, ViewportHost.ActualHeight);
        }

        ZoomTransform.ScaleX = _camera.Zoom;
        ZoomTransform.ScaleY = _camera.Zoom;
        PanTransform.X = _camera.PanX;
        PanTransform.Y = _camera.PanY;

        UpdateStatus();
    }

    private void MarkColonyEntities()
    {
        _dirty.MarkParticles(_sim);
        _dirty.Mark(_colony.Queen.X, _colony.Queen.Y);
        foreach (var t in _colony.Tenders) _dirty.Mark(t.X, t.Y);
        foreach (var f in _colony.Foragers) _dirty.Mark(f.X, f.Y);
        foreach (var m in _colony.Majors) _dirty.Mark(m.X, m.Y);
        foreach (var egg in _colony.Eggs) _dirty.Mark(egg.X, egg.Y);
    }

    private void UpdateStatus()
    {
        static string RoomState(Room? r) => r is null ? "—" : r.Excavated ? "done" : "digging";
        var garden = _colony.GetRoom(RoomType.Garden);
        var nursery = _colony.GetRoom(RoomType.Nursery);
        string followText = _follow is { } f ? $"  following {f.Label} (Esc releases)" : "";

        StatusText.Text =
            $"PROTOTYPE (untuned constants)  stage: {_colony.CurrentStage}  " +
            $"T:{_colony.Tenders.Count} F:{_colony.Foragers.Count} M:{_colony.Majors.Count} eggs:{_colony.Eggs.Count}  " +
            $"raw {_colony.RawMaterial:0.0}  farmed {_colony.FarmedResource:0.0}  " +
            $"garden:{RoomState(garden)} nursery:{RoomState(nursery)}  " +
            $"tps {_clock.TicksPerSecond:0}{(_clock.Paused ? " PAUSED" : "")}  " +
            $"zoom {_camera.Zoom:0.0}x  active {_sim.ActiveParticleCount}  dirty {_lastDirtyCount}" +
            followText +
            "  ·  drag: pan  wheel: zoom  click ant: follow  [Space] pause  [Up/Down] speed";
    }

    // ---- camera input ----

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(ViewportHost);
        _camera.ZoomAt(p.X, p.Y, e.Delta > 0 ? 1.2 : 1 / 1.2);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _dragging = false;
        _dragStart = _lastDragPoint = e.GetPosition(ViewportHost);
        ViewportHost.CaptureMouse();
        ViewportHost.Focus();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;
        var p = e.GetPosition(ViewportHost);
        if (!_dragging && (Math.Abs(p.X - _dragStart.X) > 4 || Math.Abs(p.Y - _dragStart.Y) > 4))
        {
            _dragging = true;
            _follow = null; // grabbing the world releases tracking
        }
        if (_dragging)
        {
            _camera.PanBy(p.X - _lastDragPoint.X, p.Y - _lastDragPoint.Y);
        }
        _lastDragPoint = p;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ViewportHost.ReleaseMouseCapture();
        bool wasClick = _mouseDown && !_dragging;
        _mouseDown = false;
        _dragging = false;
        if (!wasClick) return;

        // Click: hit-test agents in world space at the current pan/zoom.
        var p = e.GetPosition(ViewportHost);
        var (wx, wy) = _camera.ScreenToWorld(p.X, p.Y);
        var candidates = new List<(double X, double Y)>();
        var targets = new List<(string Label, Func<(int X, int Y)> Cell)>();

        void Add(string label, Func<(int X, int Y)> cell)
        {
            var (cx, cy) = cell();
            candidates.Add(((cx + 0.5) * GridRenderer.CellSize, (cy + 0.5) * GridRenderer.CellSize));
            targets.Add((label, cell));
        }

        Add("Queen", () => (_colony.Queen.X, _colony.Queen.Y));
        foreach (var t in _colony.Tenders) Add("Tender", () => (t.X, t.Y));
        foreach (var f in _colony.Foragers) Add("Forager", () => (f.X, f.Y));
        foreach (var m in _colony.Majors) Add("Major", () => (m.X, m.Y));

        // Click tolerance: ~2 cells in world units, but never tighter than
        // 10 screen pixels when zoomed far out.
        double radius = Math.Max(2.0 * GridRenderer.CellSize, 10.0 / _camera.Zoom);
        int? hit = Camera.FindNearest((wx, wy), candidates, radius);
        _follow = hit is { } i ? targets[i] : null; // empty space releases
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: _follow = null; break;
            case Key.Space: _clock.Paused = !_clock.Paused; break;
            case Key.Up: _clock.TicksPerSecond = Math.Min(240, _clock.TicksPerSecond * 2); break;
            case Key.Down: _clock.TicksPerSecond = Math.Max(2, _clock.TicksPerSecond / 2); break;
        }
    }
}
