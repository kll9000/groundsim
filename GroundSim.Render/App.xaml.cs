using System.Diagnostics;
using System.Windows;

namespace GroundSim.Render;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--smoke"))
        {
            // Headless smoke + perf probe: runs the colony-scale scenario
            // (large grid, many agents) through the full render pipeline
            // without showing a window, prints measured numbers, exits 0.
            RunColonySmoke();
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }

    private static void RunColonySmoke()
    {
        var grid = DemoWorld.Create(width: 400, height: 300, groundLevel: 150);
        var sim = new Simulation(grid);
        var dirty = new DirtyTracker(grid);
        var renderer = new GridRenderer(grid);
        var agents = DemoWorld.SpawnAgents(grid, sim, count: 40).ToList();

        renderer.DrawFull();
        dirty.Clear();

        int maxDirty = 0;
        const int ticks = 2000;
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < ticks; t++)
        {
            dirty.MarkParticles(sim);
            foreach (var a in agents) dirty.Mark(a.X, a.Y);
            foreach (var a in agents) a.Tick();
            sim.Tick();
            dirty.MarkParticles(sim);
            foreach (var a in agents) dirty.Mark(a.X, a.Y);

            maxDirty = Math.Max(maxDirty, dirty.Count);
            renderer.DrawFrame(dirty.Cells, sim, agents);
            dirty.Clear();
        }
        sw.Stop();

        int totalCells = grid.Width * grid.Height;
        int carrying = agents.Count(a => a.Carried is not null);
        Console.WriteLine(
            $"colony smoke ok: {ticks} ticks, grid {grid.Width}x{grid.Height} ({totalCells} cells), " +
            $"{agents.Count} agents ({carrying} carrying at end), " +
            $"elapsed {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds / ticks:0.000} ms/tick), " +
            $"max dirty cells/frame {maxDirty} ({100.0 * maxDirty / totalCells:0.00}% of grid), " +
            $"active particles at end {sim.ActiveParticleCount}");
    }
}
