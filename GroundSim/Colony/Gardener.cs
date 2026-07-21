namespace GroundSim;

/// <summary>
/// Phase 18 Part A (new outline): tends the fungus itself — inherits the
/// retired Tender caste's process-raw-into-farmed behavior at the colony's
/// processing site (Home Room floor until the Fungus Garden exists; no room
/// assumption baked in here). NEVER gathers (from nodes or otherwise),
/// NEVER tends eggs — role purity, same hard invariant as every other
/// caste. Rolls are population-gated (ColonyConfig.GardenerUnlockPopulation)
/// so Gardeners "appear as the operation scales up", per the outline.
/// Composes PathWalker (Agent's movement machinery) — Gardeners don't dig.
/// </summary>
public sealed class Gardener
{
    private readonly PathWalker _walker;
    private int _processProgress;

    public int X => _walker.X;
    public int Y => _walker.Y;

    /// <summary>Phase 18 Part C: tick at which this worker dies of old age
    /// (int.MaxValue = death disabled). Assigned by Colony.Spawn.</summary>
    public int DiesAtTick { get; init; } = int.MaxValue;

    /// <summary>Phase 18 Part B: true while holding one withdrawn-but-not-
    /// yet-processed raw unit (the storage→garden leg). Conservation: on
    /// death this unit returns to the colony pool.</summary>
    public bool CarryingRaw { get; private set; }

    public Gardener(int x, int y) => _walker = new PathWalker(x, y);

    public void Tick(Colony colony, Grid grid)
    {
        // Phase 18 Part B: once the Food-storage room is active the flow is
        // spatial — walk to storage, withdraw one unit, carry it to the
        // processing site, process it there. Before that: the original
        // process-directly-at-site flow.
        if (colony.FoodStorageActive && !CarryingRaw)
        {
            _processProgress = 0;
            if (colony.RawMaterial < 1) return;
            if (_walker.MoveTowards(grid, colony.RawDepositSite))
            {
                colony.RawMaterial -= 1;
                CarryingRaw = true;
            }
            return;
        }

        if (!colony.FoodStorageActive && colony.RawMaterial < 1)
        {
            _processProgress = 0;
            return;
        }

        if (_walker.MoveTowards(grid, colony.ProcessingSite))
        {
            if (++_processProgress >= colony.Config.ProcessTicks)
            {
                _processProgress = 0;
                if (CarryingRaw) CarryingRaw = false;
                else colony.RawMaterial -= 1;
                colony.FarmedResource += 1;
                colony.Stats.RawProcessedByGardeners += 1;
                if (colony.GetRoom(RoomType.Garden) is { Excavated: true } garden
                    && garden.Contains(X, Y))
                {
                    colony.Stats.ProcessedInGarden += 1;
                }
            }
        }
    }
}
