using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
    // Phase 15.5: default 60 → 240. Phase 15's finer grid inflated
    // excavation ticks ~7.7× (founding 4,072 → 31,224), so 60 tps meant
    // ~8.7 min of real time to found; 240 tps ≈ 2.2 min (Phase 15's own
    // measured number). 240 = 4 ticks/frame at ~60 fps, leaving 2×
    // headroom under MaxTicksPerAdvance (8) so hitches catch up instead
    // of dropping ticks. Pure playback speed — the canonical sim second
    // stays the core TickClock's 30 ticks (the Phase 14 conversion basis).
    private readonly TickClock _clock = new() { TicksPerSecond = 240 };
    private readonly GridRenderer _renderer;
    private readonly HashSet<Room> _tintedRooms = new();
    private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
    private int _lastDirtyCount;
    // Phase 22: value carries Units so a dead worker's final circle
    // footprint can be dirty-marked when its entry is pruned (see
    // MarkColonyEntities) — fixing both the unbounded-dictionary leak and
    // the stale-circle-next-to-the-corpse visual.
    private readonly Dictionary<object, (int X, int Y, int Units)> _lastEntityPos = new();
    private readonly HashSet<object> _seenEntities = new();

    // MaxZoom raised with the Phase 13 resolution change so the deepest
    // zoom still reaches ~40 px per cell (2 px × 20, matching the old
    // 5 px × 8 ceiling).
    private readonly Camera _camera = new() { MaxZoom = 20 };
    private readonly int _seed;

    private static int? ParseSeedArg()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--seed" && int.TryParse(args[i + 1], out int s)) return s;
        }
        return null;
    }

    private (string Label, Func<(int X, int Y)> Cell)? _follow;
    private Point _dragStart;
    private Point _lastDragPoint;
    private bool _dragging;
    private bool _mouseDown;

    /// <summary>The git commit this build was compiled from, injected at
    /// build time via AssemblyInformationalVersion (see the csproj's
    /// EmbedGitCommit target) — automatic, never needs a manual bump.</summary>
    private static readonly string BuildCommit =
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is [System.Reflection.AssemblyInformationalVersionAttribute a, ..] ? a.InformationalVersion : "unknown";

    public MainWindow()
    {
        InitializeComponent();
        // Build identity in the title bar (always visible, even when the
        // status line is busy) — Kevin can match it against git log.
        Title = $"GroundSim — Colony Prototype  [build {BuildCommit}]";

        // Phase 24 item 1: the interactive window (and ONLY the window —
        // ColonyScenario.Create's default-42 stays for tests and --smoke,
        // which depend on it for reproducibility) randomizes its seed per
        // launch, or takes --seed N to revisit a specific run. The status
        // bar shows the seed either way. Random.Shared here is the one
        // deliberate unseeded draw in the app: it only CHOOSES the seed;
        // everything downstream of the chosen seed stays deterministic.
        _seed = ParseSeedArg() ?? Random.Shared.Next(1_000_000);
        (_grid, _sim, _colony) = ColonyScenario.Create(_seed);
        _dirty = new DirtyTracker(_grid);
        // Phase 23: sky band above the scenario's ground level gets the
        // day/night tint.
        _renderer = new GridRenderer(_grid, ColonyScenario.GroundLevel);

        SurfaceImage.Source = _renderer.Bitmap;
        _renderer.DrawFull(_colony);
        _dirty.Clear();
        BuildLegend();

        Loaded += (_, _) =>
        {
            // Start focused on the founding site at a comfortable zoom.
            _camera.ZoomAt(0, 0, 2.0 / _camera.Zoom);
            var home = _colony.HomeCenter;
            // Phase 15: -6 → -6 × GridScale, framing the same physical spot.
            _camera.CenterOn((home.X + 0.5) * GridRenderer.CellSize, (home.Y - 6 * ColonyConfig.GridScale) * GridRenderer.CellSize,
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

    /// <summary>Phase 16: builds the legend rows from GridRenderer's own
    /// color table — a swatch plus label per entry, nothing hand-copied.</summary>
    private void BuildLegend()
    {
        LegendStack.Children.Add(new TextBlock
        {
            Text = "Legend  [L to hide]",
            Foreground = System.Windows.Media.Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 5),
        });
        foreach (var (label, color) in GridRenderer.LegendEntries)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var stroke = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            // Phase 19: caste entries get a CIRCLE swatch scaled to the
            // caste's relative size (5 units = the full 12px slot), so the
            // legend itself teaches the size hierarchy; everything else
            // keeps the square swatch.
            System.Windows.FrameworkElement swatch;
            if (GridRenderer.LegendSizeUnits(label) is { } units)
            {
                double d = 12.0 * units / 5.0;
                swatch = new System.Windows.Shapes.Ellipse
                {
                    Width = d,
                    Height = d,
                    Fill = new System.Windows.Media.SolidColorBrush(color),
                    Stroke = stroke,
                    StrokeThickness = 0.5,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                };
                var holder = new System.Windows.Controls.Grid { Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0) };
                holder.Children.Add(swatch);
                row.Children.Add(holder);
            }
            else
            {
                row.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = 12,
                    Height = 12,
                    Fill = new System.Windows.Media.SolidColorBrush(color),
                    Stroke = stroke,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 6, 0),
                });
            }
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.Gainsboro,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
            });
            LegendStack.Children.Add(row);
        }
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
            _renderer.SetTimeOfDay(SimCalendar.DayNumber(_colony.TickCount));
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
        foreach (var c in _colony.Corpses) _dirty.Mark(c.X, c.Y);
        // Phase 19: circles span multiple cells, so a MOVING ant must mark
        // its old and new footprints or it leaves ghost trails of stale
        // circle paint. Stationary ants mark nothing: circles are repainted
        // by every DrawFrame regardless, and any terrain change beneath one
        // is already marked by the physics — marking every footprint every
        // tick was measured at 7x the per-tick render cost.
        void MarkArea(int x, int y, int units)
        {
            int r = units / 2 + 1;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++) _dirty.Mark(x + dx, y + dy);
        }
        void MarkIfMoved(object who, int x, int y, int units)
        {
            _seenEntities.Add(who);
            if (_lastEntityPos.TryGetValue(who, out var last))
            {
                if ((last.X, last.Y) == (x, y)) return;
                MarkArea(last.X, last.Y, units);
            }
            MarkArea(x, y, units);
            _lastEntityPos[who] = (x, y, units);
        }
        foreach (var m in _colony.Minims) MarkIfMoved(m, m.X, m.Y, GridRenderer.SizeUnits(Caste.Minim));
        foreach (var g in _colony.Gardeners) MarkIfMoved(g, g.X, g.Y, GridRenderer.SizeUnits(Caste.Gardener));
        foreach (var f in _colony.Foragers) MarkIfMoved(f, f.X, f.Y, GridRenderer.SizeUnits(Caste.Forager));
        foreach (var s in _colony.Soldiers) MarkIfMoved(s, s.X, s.Y, GridRenderer.SizeUnits(Caste.Soldier));
        MarkIfMoved(_colony.Queen, _colony.Queen.X, _colony.Queen.Y, GridRenderer.QueenSizeUnits);
        foreach (var egg in _colony.Eggs) _dirty.Mark(egg.X, egg.Y);

        // Phase 22: prune entries for workers that no longer exist (death
        // removes them from the caste lists). Without this the dictionary
        // leaks unboundedly over a long run, AND the dead ant's final circle
        // lingers on screen until nearby terrain changes happen to redraw it
        // — marking the stale footprint here repaints it as background.
        if (_lastEntityPos.Count > _seenEntities.Count)
        {
            List<object>? gone = null;
            foreach (var (who, last) in _lastEntityPos)
            {
                if (_seenEntities.Contains(who)) continue;
                MarkArea(last.X, last.Y, last.Units);
                (gone ??= new()).Add(who);
            }
            if (gone is not null) foreach (var who in gone) _lastEntityPos.Remove(who);
        }
        _seenEntities.Clear();
    }

    private void UpdateStatus()
    {
        static string RoomState(Room? r) => r is null ? "—" : r.Excavated ? "done" : "digging";
        var garden = _colony.GetRoom(RoomType.Garden);
        var nursery = _colony.GetRoom(RoomType.Nursery);
        string followText = _follow is { } f ? $"  following {f.Label} (Esc releases)" : "";

        StatusText.Text =
            $"build {BuildCommit}  seed {_seed}  day {SimCalendar.DayNumber(_colony.TickCount):0.0}  PROTOTYPE (untuned constants)  stage: {_colony.CurrentStage}  " +
            $"Mi:{_colony.Minims.Count} G:{_colony.Gardeners.Count} F:{_colony.Foragers.Count} S:{_colony.Soldiers.Count} eggs:{_colony.Eggs.Count}  " +
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
        foreach (var m in _colony.Minims) Add("Minim", () => (m.X, m.Y));
        foreach (var g in _colony.Gardeners) Add("Gardener", () => (g.X, g.Y));
        foreach (var f in _colony.Foragers) Add("Forager", () => (f.X, f.Y));
        foreach (var s in _colony.Soldiers) Add("Soldier", () => (s.X, s.Y));

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
            case Key.L:
                LegendPanel.Visibility =
                    LegendPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                break;
            // Phase 15.5: ceiling 240 → 480 so Up-arrow still fast-forwards
            // above the new 240 default. 480 is the honest max — 8 ticks/
            // frame at ~60 fps is exactly MaxTicksPerAdvance; advertising
            // more would silently drop ticks.
            case Key.Up: _clock.TicksPerSecond = Math.Min(480, _clock.TicksPerSecond * 2); break;
            case Key.Down: _clock.TicksPerSecond = Math.Max(2, _clock.TicksPerSecond / 2); break;
        }
    }
}
