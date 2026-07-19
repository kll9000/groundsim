using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GroundSim.Render;

/// <summary>
/// Render loop host. The simulation and agents advance on a fixed tick rate
/// (TickClock) while frames render as fast as the compositor asks — the two
/// rates are fully decoupled. Controls: Space = pause, Up/Down = speed.
///
/// Phase 4 capstone demo: 8 agents quarry a pit into the ground and haul the
/// spoil to drop columns on both sides, where piles physically settle and
/// grow. Red = empty-handed agent, orange = carrying, yellow = falling
/// particle.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Grid _grid;
    private readonly Simulation _sim;
    private readonly DirtyTracker _dirty;
    private readonly TickClock _clock = new() { TicksPerSecond = 30 };
    private readonly GridRenderer _renderer;
    private readonly List<Agent> _agents = new();
    private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
    private int _lastDirtyCount;

    public MainWindow()
    {
        InitializeComponent();

        _grid = DemoWorld.Create(width: 200, height: 120, groundLevel: 70);
        _sim = new Simulation(_grid);
        _dirty = new DirtyTracker(_grid);
        _renderer = new GridRenderer(_grid);
        _agents.AddRange(DemoWorld.SpawnAgents(_grid, _sim, count: 8));

        SurfaceImage.Source = _renderer.Bitmap;
        _renderer.DrawFull();
        _dirty.Clear(); // the full draw already covers everything written so far

        KeyDown += OnKeyDown;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double dt = _frameTimer.Elapsed.TotalSeconds;
        _frameTimer.Restart();

        int ticks = _clock.Advance(dt);
        for (int t = 0; t < ticks; t++)
        {
            MarkMovables(); // old positions -> redrawn as background
            foreach (var a in _agents) a.Tick();
            _sim.Tick();    // settles/digs mark cells via Grid.CellChanged
            MarkMovables(); // new positions -> overlays drawn there
        }

        if (ticks > 0)
        {
            _renderer.DrawFrame(_dirty.Cells, _sim, _agents);
            _lastDirtyCount = _dirty.Count;
            _dirty.Clear();
        }

        int carrying = _agents.Count(a => a.Carried is not null);
        StatusText.Text =
            $"tps {_clock.TicksPerSecond:0}{(_clock.Paused ? " PAUSED" : "")}  " +
            $"agents {_agents.Count} (carrying {carrying})  " +
            $"active {_sim.ActiveParticleCount}  dirty {_lastDirtyCount}  " +
            $"[Space] pause  [Up/Down] speed";
    }

    private void MarkMovables()
    {
        _dirty.MarkParticles(_sim);
        foreach (var a in _agents) _dirty.Mark(a.X, a.Y);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space: _clock.Paused = !_clock.Paused; break;
            case Key.Up: _clock.TicksPerSecond = Math.Min(240, _clock.TicksPerSecond * 2); break;
            case Key.Down: _clock.TicksPerSecond = Math.Max(2, _clock.TicksPerSecond / 2); break;
        }
    }
}
