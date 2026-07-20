using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 16 Part B: pins the mechanism finding behind the flat-mound
/// investigation. The handoff's leading theory was that the finer grid
/// dilutes DirtSlideChance (same physical slope = more slide-checks →
/// shallower piles). Measurement showed the OPPOSITE: a grain's settle
/// run-length is ~1/(1−p) cells regardless of scale, which is half the
/// physical distance at GridScale 2, so equal physical volumes pile
/// STEEPER at the finer grid (measured ~2.6× mean steepness over 10
/// seeds). The real flatness cause was MoundMaxHeight's plateau (fixed by
/// retuning 14 → 20). This test keeps the mechanism honest: if friction
/// or slide semantics ever change such that fine-grid piles become
/// shallower than coarse-equivalent piles, the diagnosis (and the cap
/// retune built on it) needs revisiting.
/// </summary>
public class MoundShapeTests
{
    private static (double Peak, double HalfWidth) MeasurePile(int worldW, int worldH, int grains, int seed)
    {
        var grid = new Grid(worldW, worldH);
        int floor = worldH - 1;
        for (int x = 0; x < worldW; x++) grid[x, floor] = CellMaterial.Rock;
        var sim = new Simulation(grid, seed);
        for (int i = 0; i < grains; i++)
        {
            sim.Drop(worldW / 2, 0, CellMaterial.Dirt);
            sim.RunUntilSettled();
        }
        int peak = 0, cols = 0;
        for (int x = 0; x < worldW; x++)
        {
            int h = 0;
            for (int y = 0; y < floor; y++)
            {
                if (grid[x, y] == CellMaterial.Dirt) { h = floor - y; break; }
            }
            if (h > 0) cols++;
            peak = Math.Max(peak, h);
        }
        return (peak, cols / 2.0);
    }

    [Fact]
    public void FinerGrid_DoesNotDilute_DirtFriction()
    {
        // Same physical dirt volume at both discretizations: N grains in a
        // coarse world vs N×GridScale² grains in a world GridScale× larger.
        // Steepness (peak/halfwidth) is dimensionless, so it compares
        // directly across scales. The fine pile must be at least as steep —
        // per-seed, not just on average.
        const int S = ColonyConfig.GridScale;
        foreach (int seed in new[] { 3, 7, 11 })
        {
            var coarse = MeasurePile(40, 40, 30, seed);
            var fine = MeasurePile(40 * S, 40 * S, 30 * S * S, seed);
            double coarseSteep = coarse.Peak / coarse.HalfWidth;
            double fineSteep = fine.Peak / fine.HalfWidth;
            Assert.True(fineSteep >= coarseSteep,
                $"seed {seed}: fine-grid pile shallower than coarse ({fineSteep:0.00} < {coarseSteep:0.00}) — " +
                "the finer grid is now diluting friction; the Phase 16 mound diagnosis no longer holds");
        }
    }
}
