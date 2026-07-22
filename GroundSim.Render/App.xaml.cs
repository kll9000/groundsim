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
        // Phase 23: same sky band as the window, so the smoke exercises the
        // tint path (and its cost) too.
        var renderer = new GridRenderer(grid, ColonyScenario.GroundLevel);
        renderer.DrawFull(colony);
        dirty.Clear();

        // Phase 22: same prune-on-death logic as MainWindow.MarkColonyEntities
        // (leak fix + stale-circle footprint marking) — kept in lockstep.
        var lastEntityPos = new Dictionary<object, (int X, int Y, int Units)>();
        var seenEntities = new HashSet<object>();
        void MarkEntities()
        {
            dirty.MarkParticles(sim);
            dirty.Mark(colony.Queen.X, colony.Queen.Y);
            foreach (var c in colony.Corpses) dirty.Mark(c.X, c.Y);
            void MarkArea(int x, int y, int units)
            {
                int r = units / 2 + 1;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++) dirty.Mark(x + dx, y + dy);
            }
            void MarkIfMoved(object who, int x, int y, int units)
            {
                seenEntities.Add(who);
                if (lastEntityPos.TryGetValue(who, out var last))
                {
                    if ((last.X, last.Y) == (x, y)) return;
                    MarkArea(last.X, last.Y, units);
                }
                MarkArea(x, y, units);
                lastEntityPos[who] = (x, y, units);
            }
            foreach (var m in colony.Minims) MarkIfMoved(m, m.X, m.Y, GridRenderer.SizeUnits(Caste.Minim));
            foreach (var g in colony.Gardeners) MarkIfMoved(g, g.X, g.Y, GridRenderer.SizeUnits(Caste.Gardener));
            foreach (var f in colony.Foragers) MarkIfMoved(f, f.X, f.Y, GridRenderer.SizeUnits(Caste.Forager));
            foreach (var s in colony.Soldiers) MarkIfMoved(s, s.X, s.Y, GridRenderer.SizeUnits(Caste.Soldier));
            MarkIfMoved(colony.Queen, colony.Queen.X, colony.Queen.Y, GridRenderer.QueenSizeUnits);
            foreach (var egg in colony.Eggs) dirty.Mark(egg.X, egg.Y);
            if (lastEntityPos.Count > seenEntities.Count)
            {
                List<object>? gone = null;
                foreach (var (who, last) in lastEntityPos)
                {
                    if (seenEntities.Contains(who)) continue;
                    MarkArea(last.X, last.Y, last.Units);
                    (gone ??= new()).Add(who);
                }
                if (gone is not null) foreach (var who in gone) lastEntityPos.Remove(who);
            }
            seenEntities.Clear();
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
            renderer.SetTimeOfDay(SimCalendar.DayNumber(colony.TickCount));
            renderer.DrawFrame(dirty.Cells, sim, colony);
            dirty.Clear();
        }
        sw.Stop();

        var m = colony.Milestones;
        int totalCells = grid.Width * grid.Height;
        Console.WriteLine(
            $"colony smoke ok: {ticks} ticks in {sw.ElapsedMilliseconds} ms " +
            $"({sw.Elapsed.TotalMilliseconds / ticks:0.000} ms/tick), stage {colony.CurrentStage}, " +
            $"workers Mi:{colony.Minims.Count} G:{colony.Gardeners.Count} F:{colony.Foragers.Count} S:{colony.Soldiers.Count}, " +
            $"deaths {colony.Stats.Deaths} burials {colony.Stats.Burials} (emerg {colony.Stats.EmergencyBurials}), " +
            $"milestones: home={m.HomeFoundedTick} worker={m.FirstWorkerTick} " +
            $"gardenDone={m.GardenExcavatedTick} nurseryDone={m.NurseryExcavatedTick}, " +
            $"max dirty cells/frame {maxDirty} ({100.0 * maxDirty / totalCells:0.00}% of grid)");
    }
}
