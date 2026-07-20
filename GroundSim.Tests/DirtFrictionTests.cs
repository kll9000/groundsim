using GroundSim;

namespace GroundSim.Tests;

public class DirtFrictionTests
{
    [Fact]
    public void BlockedDirt_SlidesAtRoughly_DirtSlideChance_Rate()
    {
        // Statistical: a dirt particle atop a pedestal with both diagonals
        // open (side cells clear) slides with probability DirtSlideChance,
        // else settles in place. 400 independent seeded sims.
        int slid = 0;
        const int trials = 400;
        for (int seed = 0; seed < trials; seed++)
        {
            var grid = new Grid(12, 12);
            for (int x = 0; x < 12; x++) grid[x, 10] = CellMaterial.Rock; // floor
            grid[5, 9] = CellMaterial.Rock; // pedestal

            var sim = new Simulation(grid, seed: seed);
            sim.Drop(5, 0, CellMaterial.Dirt);
            sim.RunUntilSettled();

            if (grid[5, 8] != CellMaterial.Dirt) slid++; // not atop the pedestal
        }

        double rate = slid / (double)trials;
        Assert.InRange(rate, Simulation.DirtSlideChance - 0.08, Simulation.DirtSlideChance + 0.08);
    }

    [Fact]
    public void DirtPiles_NowBuildRealHeight_WhileStayingLooserThanRock()
    {
        // 20 sequential drops at one column, per material, same seed.
        (int height, int columns) Measure(CellMaterial material, int seed)
        {
            var grid = new Grid(40, 40);
            for (int x = 0; x < 40; x++) grid[x, 39] = CellMaterial.Rock;
            var sim = new Simulation(grid, seed);
            for (int i = 0; i < 20; i++)
            {
                sim.Drop(20, 0, material);
                sim.RunUntilSettled();
            }
            int height = 0, columns = 0;
            for (int y = 0; y < 39; y++)
            {
                if (grid[20, y] == material) { height = 39 - y; break; }
            }
            for (int x = 0; x < 40; x++)
            {
                for (int y = 0; y < 39; y++)
                {
                    if (grid[x, y] == material) { columns++; break; }
                }
            }
            return (height, columns);
        }

        foreach (int seed in new[] { 3, 7, 11 })
        {
            var dirt = Measure(CellMaterial.Dirt, seed);
            var rock = Measure(CellMaterial.LooseRock, seed);

            // Friction gives dirt real height (the frictionless version
            // smeared out at height ~2-3 for 20 drops)...
            Assert.True(dirt.height >= 4,
                $"seed {seed}: dirt pile height {dirt.height} — still frictionless-flat");
            // ...but dirt remains looser than rock: wider spread, lower peak.
            Assert.True(dirt.columns >= rock.columns,
                $"seed {seed}: dirt ({dirt.columns} cols) should spread at least as wide as rock ({rock.columns})");
            Assert.True(dirt.height <= rock.height,
                $"seed {seed}: dirt (h={dirt.height}) should not out-peak rock (h={rock.height})");
        }
    }
}
