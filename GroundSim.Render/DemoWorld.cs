namespace GroundSim.Render;

/// <summary>
/// Builds the Phase 4 capstone demo scenario: a test world with some loose
/// rock and sticks mixed into the dig region (so spoil piles are visibly
/// mixed-material), and a crew of agents quarrying a central pit whose spoil
/// goes to drop columns flanking the site.
/// </summary>
public static class DemoWorld
{
    public static Grid Create(int width, int height, int groundLevel)
    {
        var grid = Grid.CreateTestWorld(width, height, groundLevel);

        // Mix loose rock and sticks into the future dig region so the spoil
        // piles contain all three materials (deterministic scatter).
        var (x0, y0, x1, y1) = DigRegion(width, groundLevel);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (grid[x, y] != CellMaterial.Dirt) continue;
                int hash = x * 7 + y * 13;
                if (hash % 17 == 0) grid[x, y] = CellMaterial.LooseRock;
                else if (hash % 23 == 0) grid[x, y] = CellMaterial.Stick;
            }
        }
        return grid;
    }

    public static (int X0, int Y0, int X1, int Y1) DigRegion(int width, int groundLevel)
        => (width / 2 - 15, groundLevel, width / 2 + 15, groundLevel + 30);

    public static IEnumerable<Agent> SpawnAgents(Grid grid, Simulation sim, int count)
    {
        var claims = new HashSet<(int, int)>();
        var region = DigRegion(grid.Width, SurfaceLevel(grid));
        for (int i = 0; i < count; i++)
        {
            // Alternate spoil between a west and an east drop column, with a
            // small per-pair spread so piles merge into broad heaps.
            int dropX = i % 2 == 0
                ? grid.Width / 2 - 40 - (i / 2) * 3
                : grid.Width / 2 + 40 + (i / 2) * 3;
            int startX = region.X0 + 2 + (i * 4) % (region.X1 - region.X0 - 3);
            yield return new Agent(grid, sim, claims, startX, region.Y0 - 1, region, dropX);
        }
    }

    private static int SurfaceLevel(Grid grid)
    {
        int x = grid.Width / 2;
        for (int y = 0; y < grid.Height; y++)
        {
            if (grid[x, y] != CellMaterial.Air) return y;
        }
        return grid.Height - 1;
    }
}
