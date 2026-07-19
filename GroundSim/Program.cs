using GroundSim;

// Demo run: a test agent digs from a trench area and dumps everything at one
// spot, so we can eyeball that a natural pile forms.
var grid = Grid.CreateTestWorld(width: 120, height: 60, groundLevel: 30);
var sim = new Simulation(grid);
var agent = new TestAgent(sim);

const int digColumn = 30;
const int walkDistance = 40; // drops land around x = 70
const int cycles = 60;

int completed = 0;
for (int i = 0; i < cycles; i++)
{
    // Spread digs across a few adjacent columns so a rock layer in one
    // column doesn't stall the whole run; all drops target the same spot.
    int dx = digColumn + i % 5;
    if (agent.DigCarryDrop(dx, walkDistance + digColumn - dx)) completed++;
}

Console.WriteLine($"GroundSim Phase 1 demo — {completed}/{cycles} dig+carry+drop cycles completed.");
Console.WriteLine($"Dug from column x={digColumn}, dropped at x={digColumn + walkDistance}.");
Console.WriteLine();
Console.WriteLine("Legend: '.' air  '#' dirt  '@' rock");
Console.WriteLine();
// Show a window covering the trench and the pile.
Console.WriteLine(AsciiRenderer.Render(grid, x0: 20, y0: 15, width: 90, height: 25));
