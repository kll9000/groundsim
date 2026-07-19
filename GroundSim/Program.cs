using GroundSim;

// Phase 2 demo: three side-by-side piles (dirt / loose rock / sticks) plus a
// mixed pile, all dropped concurrently, so material behavior differences are
// visible in one frame.
var grid = Grid.CreateTestWorld(width: 120, height: 60, groundLevel: 40);
var sim = new Simulation(grid);

// Seed some loose-rock debris on the surface (carryable, unlike terrain Rock).
for (int x = 100; x < 110; x++) grid[x, 39] = CellMaterial.LooseRock;

// Stream 20 of each material at fixed columns, one new particle every few
// ticks WITHOUT waiting for earlier ones to settle — many particles are in
// flight simultaneously, but as a falling stream rather than a single
// super-imposed clump (particles don't collide mid-air, so dropping them all
// from the exact same cell on the same tick makes them arrive as one burst
// and smear flat).
const int drops = 20;
for (int i = 0; i < drops; i++)
{
    sim.Drop(20, 0, CellMaterial.Dirt);
    sim.Drop(45, 0, CellMaterial.LooseRock);
    sim.Drop(70, 0, CellMaterial.Stick);
    for (int t = 0; t < 3; t++) sim.Tick();
}
int ticks = sim.RunUntilSettled();

// Mixed pile at x=90: dirt base, rock in the middle, sticks on top — each
// batch settles before the next so the layering is visible.
for (int i = 0; i < 8; i++) sim.Drop(90, 0, CellMaterial.Dirt);
ticks += sim.RunUntilSettled();
for (int i = 0; i < 5; i++) sim.Drop(90, 0, CellMaterial.LooseRock);
ticks += sim.RunUntilSettled();
for (int i = 0; i < 3; i++) sim.Drop(90, 0, CellMaterial.Stick);
ticks += sim.RunUntilSettled();

Console.WriteLine($"GroundSim Phase 2 demo — {drops} concurrent drops per pile, settled in {ticks} ticks.");
Console.WriteLine("Piles: x=20 dirt, x=45 loose rock, x=70 sticks, x=90 mixed (dirt+rock+sticks).");
Console.WriteLine();
Console.WriteLine("Legend: '.' air  '#' dirt  '@' rock(terrain)  'o' loose rock  '/' stick");
Console.WriteLine();
Console.WriteLine(AsciiRenderer.Render(grid, x0: 5, y0: 18, width: 110, height: 24));
