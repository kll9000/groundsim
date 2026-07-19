namespace GroundSim;

/// <summary>
/// A* pathfinding over 4-connected Air cells. Pure grid logic — no agent or
/// rendering dependency, unit-testable headlessly (the DirtyTracker/TickClock
/// pattern from Phase 3).
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// Finds a path from start to goal walking only through Air cells.
    /// The start cell itself is exempt from the Air requirement (the agent is
    /// standing there); the goal must be Air. Returns the sequence of cells to
    /// step through, excluding start and including goal — an empty list if
    /// start == goal — or null if no path exists (or the expansion cap was
    /// hit, treated as unreachable).
    /// </summary>
    public static List<(int X, int Y)>? FindPath(
        Grid grid, (int X, int Y) start, (int X, int Y) goal, int maxExpansions = 50_000)
    {
        if (!grid.InBounds(goal.X, goal.Y) || !grid.IsAir(goal.X, goal.Y)) return null;
        if (start == goal) return new List<(int, int)>();

        var open = new PriorityQueue<(int X, int Y), int>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), int> { [start] = 0 };
        open.Enqueue(start, Heuristic(start, goal));

        Span<(int dx, int dy)> neighbors = stackalloc (int, int)[]
        {
            (0, 1), (0, -1), (-1, 0), (1, 0),
        };

        int expansions = 0;
        while (open.Count > 0)
        {
            if (++expansions > maxExpansions) return null;
            var current = open.Dequeue();
            if (current == goal) return Reconstruct(cameFrom, start, goal);

            int g = gScore[current];
            foreach (var (dx, dy) in neighbors)
            {
                var next = (X: current.X + dx, Y: current.Y + dy);
                if (!grid.IsAir(next.X, next.Y)) continue;
                int tentative = g + 1;
                if (gScore.TryGetValue(next, out int known) && known <= tentative) continue;
                gScore[next] = tentative;
                cameFrom[next] = current;
                open.Enqueue(next, tentative + Heuristic(next, goal));
            }
        }
        return null;
    }

    private static int Heuristic((int X, int Y) a, (int X, int Y) b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static List<(int X, int Y)> Reconstruct(
        Dictionary<(int, int), (int, int)> cameFrom, (int X, int Y) start, (int X, int Y) goal)
    {
        var path = new List<(int X, int Y)> { goal };
        var current = goal;
        while (cameFrom[current] != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
