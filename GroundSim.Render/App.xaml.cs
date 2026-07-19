using System.Windows;

namespace GroundSim.Render;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--smoke"))
        {
            // Headless smoke test for CI/agents: run the full render pipeline
            // (sim ticks + dirty tracking + bitmap writes) without showing a
            // window, then exit 0. Verifies the renderer wiring end-to-end.
            RunSmoke();
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }

    private static void RunSmoke()
    {
        var grid = Grid.CreateTestWorld(width: 200, height: 120, groundLevel: 70);
        var sim = new Simulation(grid);
        var dirty = new DirtyTracker(grid);
        var renderer = new GridRenderer(grid);
        var demo = new DemoScript(grid, sim);

        renderer.DrawFull();
        dirty.Clear();

        int maxDirty = 0;
        const int ticks = 600; // ~20 seconds of sim at 30 tps
        for (int t = 0; t < ticks; t++)
        {
            dirty.MarkParticles(sim);
            demo.OnTick();
            sim.Tick();
            dirty.MarkParticles(sim);

            maxDirty = Math.Max(maxDirty, dirty.Count);
            renderer.DrawFrame(dirty.Cells, sim);
            dirty.Clear();
        }

        int totalCells = grid.Width * grid.Height;
        Console.WriteLine(
            $"smoke ok: {ticks} ticks rendered, grid {grid.Width}x{grid.Height} ({totalCells} cells), " +
            $"max dirty cells/frame {maxDirty} ({100.0 * maxDirty / totalCells:0.00}% of grid), " +
            $"active at end {sim.ActiveParticleCount}");
    }
}
