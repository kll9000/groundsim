namespace GroundSim;

/// <summary>
/// Stays home permanently. Processes raw material into farmed resource at the
/// colony's processing site, tends eggs when there's nothing to process.
/// NEVER gathers — role purity carried from Colony Builder.
/// Composes PathWalker (Agent's movement machinery) — Tenders don't dig.
/// </summary>
public sealed class Tender
{
    private readonly PathWalker _walker;
    private int _processProgress;

    public int X => _walker.X;
    public int Y => _walker.Y;

    public Tender(int x, int y) => _walker = new PathWalker(x, y);

    public void Tick(Colony colony, Grid grid)
    {
        // Priority 1: process raw material at the processing site.
        // (Site comes from the colony — Home Room now, Fungus Garden in
        // Phase 7 — Tender code has no room assumption baked in.)
        if (colony.RawMaterial >= 1)
        {
            if (_walker.MoveTowards(grid, colony.ProcessingSite))
            {
                if (++_processProgress >= colony.Config.ProcessTicks)
                {
                    _processProgress = 0;
                    colony.RawMaterial -= 1;
                    colony.FarmedResource += 1;
                    colony.Stats.RawProcessedByTenders += 1;
                    if (colony.GetRoom(RoomType.Garden) is { Excavated: true } garden
                        && garden.Contains(X, Y))
                    {
                        colony.Stats.ProcessedInGarden += 1;
                    }
                }
            }
            return;
        }

        _processProgress = 0;

        // Priority 2: tend the nearest unhatched egg (speeds maturation).
        var egg = colony.NearestEgg(X, Y);
        if (egg is null) return;
        if (_walker.MoveTowards(grid, (egg.X, egg.Y)))
        {
            egg.TendedThisTick = true;
        }
    }
}
