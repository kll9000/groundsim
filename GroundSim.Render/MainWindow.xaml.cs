using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GroundSim.Render;

/// <summary>
/// Render loop host for the live colony demo: the Queen founds the Home Room
/// on screen, workers mature, rooms trigger and excavate, spoil piles settle
/// outside. Simulation advances on a fixed TickClock; frames render on the
/// compositor's cadence — fully decoupled (Phase 3 machinery, unchanged).
/// Controls: Space = pause, Up/Down = speed.
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

    public MainWindow()
    {
        InitializeComponent();

        (_grid, _sim, _colony) = ColonyScenario.Create();
        _dirty = new DirtyTracker(_grid);
        _renderer = new GridRenderer(_grid);

        SurfaceImage.Source = _renderer.Bitmap;
        _renderer.DrawFull(_colony);
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
            MarkColonyEntities(); // old positions -> redrawn as background
            _colony.Tick();
            _sim.Tick();          // settles/digs mark cells via Grid.CellChanged
            MarkColonyEntities(); // new positions -> overlays drawn there
        }

        // A newly-excavated room needs its whole area repainted once for the
        // tint — its Air cells won't otherwise be dirty.
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

        StatusText.Text =
            $"PROTOTYPE (untuned constants)  stage: {_colony.CurrentStage}  " +
            $"T:{_colony.Tenders.Count} F:{_colony.Foragers.Count} M:{_colony.Majors.Count} eggs:{_colony.Eggs.Count}  " +
            $"raw {_colony.RawMaterial:0.0}  farmed {_colony.FarmedResource:0.0}  " +
            $"garden:{RoomState(garden)} nursery:{RoomState(nursery)}  " +
            $"tps {_clock.TicksPerSecond:0}{(_clock.Paused ? " PAUSED" : "")}  " +
            $"active {_sim.ActiveParticleCount}  dirty {_lastDirtyCount}  " +
            $"[Space] pause  [Up/Down] speed";
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
