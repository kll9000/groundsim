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
            // Headless smoke: runs the SAME live-colony scenario the window
            // shows, through the full render pipeline, printing measured
            // numbers. Exits 0 on success.
            RunColonySmoke();
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }

    private static void RunColonySmoke()
    {
        var (grid, sim, colony) = ColonyScenario.Create();
        var dirty = new DirtyTracker(grid);
        var renderer = new GridRenderer(grid);
        renderer.DrawFull(colony);
        dirty.Clear();

        void MarkEntities()
        {
            dirty.MarkParticles(sim);
            dirty.Mark(colony.Queen.X, colony.Queen.Y);
            foreach (var t in colony.Tenders) dirty.Mark(t.X, t.Y);
            foreach (var f in colony.Foragers) dirty.Mark(f.X, f.Y);
            foreach (var m in colony.Majors) dirty.Mark(m.X, m.Y);
            foreach (var egg in colony.Eggs) dirty.Mark(egg.X, egg.Y);
        }

        int maxDirty = 0;
        // Phase 15: 40k → 160k. Same physical excavation is ~4× the cells at
        // an unchanged 1-cell-per-tick dig rate (and hauls walk 2× the
        // cells), so tick-denominated milestones inflate ~4×; the budget
        // scales with them.
        const int ticks = 160_000;
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < ticks; t++)
        {
            MarkEntities();
            colony.Tick();
            sim.Tick();
            MarkEntities();
            maxDirty = Math.Max(maxDirty, dirty.Count);
            renderer.DrawFrame(dirty.Cells, sim, colony);
            dirty.Clear();
        }
        sw.Stop();

        var m = colony.Milestones;
        int totalCells = grid.Width * grid.Height;
        Console.WriteLine(
            $"colony smoke ok: {ticks} ticks in {sw.ElapsedMilliseconds} ms " +
            $"({sw.Elapsed.TotalMilliseconds / ticks:0.000} ms/tick), stage {colony.CurrentStage}, " +
            $"workers T:{colony.Tenders.Count} F:{colony.Foragers.Count} M:{colony.Majors.Count}, " +
            $"milestones: home={m.HomeFoundedTick} worker={m.FirstWorkerTick} " +
            $"gardenDone={m.GardenExcavatedTick} nurseryDone={m.NurseryExcavatedTick}, " +
            $"max dirty cells/frame {maxDirty} ({100.0 * maxDirty / totalCells:0.00}% of grid)");
    }
}
