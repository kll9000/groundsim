namespace GroundSim;

/// <summary>
/// All tunable colony constants in one place.
///
/// IMPORTANT: every value here is INVENTED for Phase 6 — Colony Builder's
/// game.js (the ground truth for tuned values) was not available when this
/// was written. Swap these for the real numbers when porting them; nothing
/// else should need to change.
/// </summary>
public sealed class ColonyConfig
{
    /// <summary>Farmed resource the founding queen deposits (the fungal pellet).</summary>
    public double StarterResource { get; init; } = 10;

    /// <summary>Ticks between eggs once the queen is laying.</summary>
    public int EggLayIntervalTicks { get; init; } = 90;

    /// <summary>Untended ticks for an egg to mature.</summary>
    public int EggMaturationTicks { get; init; } = 600;

    /// <summary>Maturation speed multiplier while a Tender tends the egg.</summary>
    public int TendedMaturationSpeed { get; init; } = 2;

    /// <summary>Fraction of matured eggs that survive ("most don't survive").</summary>
    public double EggSurvivalChance { get; init; } = 0.35;

    /// <summary>Rarest-first caste rolls: Major first, then Forager vs Tender
    /// (Tender is the most-common default).</summary>
    public double MajorChance { get; init; } = 0.10;
    public double ForagerShareOfRemainder { get; init; } = 0.50;

    /// <summary>Forager haul size: base minus distance falloff, floored.
    /// Mirrors Colony Builder's gatherChunkBase/DistanceFactor/Min shape.</summary>
    public double GatherChunkBase { get; init; } = 8.0;
    public double GatherDistanceFalloff { get; init; } = 0.04;
    public double GatherChunkMin { get; init; } = 1.0;

    /// <summary>Ticks a Tender spends converting 1 raw material into 1 farmed resource.</summary>
    public int ProcessTicks { get; init; } = 20;

    /// <summary>Farmed resource mass at which the Fungus Garden triggers.</summary>
    public double GardenTriggerThreshold { get; init; } = 30;

    /// <summary>Brood-pressure integral (sum of egg count per tick since
    /// founding) at which the Nursery triggers. An integral, not an
    /// instantaneous egg count — matching Colony Builder's own documented
    /// finding that instantaneous checks near a cap almost never fire.</summary>
    public double NurseryBroodPressureThreshold { get; init; } = 25_000;

    /// <summary>Max idle Foragers assigned to assist room excavation.</summary>
    public int WorkerDiggers { get; init; } = 2;

    public double HaulSize(double distanceFromHome)
        => Math.Max(GatherChunkMin, GatherChunkBase - GatherDistanceFalloff * distanceFromHome);
}
