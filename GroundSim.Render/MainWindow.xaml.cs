using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GroundSim.Render;

/// <summary>
/// Render loop host. The simulation advances on a fixed tick rate (TickClock)
/// while frames render as fast as the compositor asks — the two rates are
/// fully decoupled. Controls: Space = pause, Up/Down = speed.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Grid _grid;
    private readonly Simulation _sim;
    private readonly DirtyTracker _dirty;
    private readonly TickClock _clock = new() { TicksPerSecond = 30 };
    private readonly GridRenderer _renderer;
    private readonly DemoScript _demo;
    private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
    private int _lastDirtyCount;

    public MainWindow()
    {
        InitializeComponent();

        _grid = Grid.CreateTestWorld(width: 200, height: 120, groundLevel: 70);
        for (int x = 115; x < 125; x++) _grid[x, 69] = CellMaterial.LooseRock;
        _sim = new Simulation(_grid);
        _dirty = new DirtyTracker(_grid);
        _renderer = new GridRenderer(_grid);
        _demo = new DemoScript(_grid, _sim);

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
            _dirty.MarkParticles(_sim); // old positions -> redrawn as background
            _demo.OnTick();
            _sim.Tick();                // settles/digs mark cells via Grid.CellChanged
            _dirty.MarkParticles(_sim); // new positions -> particle overlay drawn there
        }

        if (ticks > 0)
        {
            _renderer.DrawFrame(_dirty.Cells, _sim);
            _lastDirtyCount = _dirty.Count;
            _dirty.Clear();
        }

        StatusText.Text =
            $"tps {_clock.TicksPerSecond:0}{(_clock.Paused ? " PAUSED" : "")}  " +
            $"active {_sim.ActiveParticleCount}  dirty {_lastDirtyCount}  " +
            $"[Space] pause  [Up/Down] speed  (yellow = awake particle)";
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
