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

    /// <summary>Resource-node regrowth per tick toward each node's cap
    /// (Phase 9 sustain; 0 disables regeneration).</summary>
    public double NodeRegenPerTick { get; init; } = 0.02;

    // ---- Phase 11 organic excavation (ALL INVENTED, in grid cells — the
    // design doc's inch-based ranges translated by judgment for this grid
    // scale; see PHASE11_REPORT for reasoning) ----

    /// <summary>Distance from the parent room's floor anchor to a new
    /// chamber's target center, in cells.</summary>
    public double RoomBranchMinDistance { get; init; } = 12;
    public double RoomBranchMaxDistance { get; init; } = 20;

    /// <summary>Half-angle (radians) of the downward cone new rooms branch
    /// into (±from straight down).</summary>
    public double RoomBranchAngleSpread { get; init; } = 1.0;

    /// <summary>Connecting-tunnel corridor width bounds, in cells.</summary>
    public double TunnelWidthMin { get; init; } = 2.0;
    public double TunnelWidthMax { get; init; } = 3.0;

    /// <summary>Per-step heading noise (radians ≈ 8.6°).</summary>
    public double TunnelTurnJitter { get; init; } = 0.15;

    /// <summary>Max heading drift from the (re-aimed) bias direction
    /// (radians ≈ 31.5°).</summary>
    public double TunnelMaxDeviation { get; init; } = 0.55;

    /// <summary>Chamber footprint bounds, in cells.</summary>
    public int ChamberMinArea { get; init; } = 40;
    public int ChamberMaxArea { get; init; } = 70;

    /// <summary>Probability an edge-adjacent seed cell joins the chamber seed
    /// before CA smoothing (breaks circular symmetry).</summary>
    public double ChamberEdgeNoise { get; init; } = 0.4;

    public int CaGenerations { get; init; } = 4;
    public int CaThreshold { get; init; } = 5;

    /// <summary>Organic generation attempts before the guaranteed rect
    /// fallback (Part C hardening).</summary>
    public int MaskRetryAttempts { get; init; } = 6;

    /// <summary>Buffer margin (cells) kept between new masks and existing
    /// rooms, except at the deliberate tunnel connection.</summary>
    public int RoomOverlapBuffer { get; init; } = 1;

    public double HaulSize(double distanceFromHome)
        => Math.Max(GatherChunkMin, GatherChunkBase - GatherDistanceFalloff * distanceFromHome);
}
