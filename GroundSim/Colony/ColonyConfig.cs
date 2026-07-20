namespace GroundSim;

/// <summary>
/// All tunable colony constants in one place.
///
/// Phase 14: the caste/egg/gather/room-trigger values below are now REAL,
/// ported from Colony Builder's tuned game.js CONFIG. Conversion basis:
/// 1 sim second = 30 ticks (the core TickClock default; the renderer's 60
/// tps is a deliberate Phase 8 2× watchability fast-forward, not a sim-rate
/// definition), and — since Phase 15's finer grid (see GridScale) —
/// 1 grid cell = 8 px / GridScale = 4 px (game.js cell: 8; its world is
/// 200 old-cells = 400 new-cells wide, matching the app world). Values NOT
/// covered by game.js remain invented and are
/// still marked as such (organic-excavation geometry, node regen, digger
/// caps, mound tuning) — see the "known but unbuilt" section at the bottom
/// for tuned values whose systems GroundSim doesn't have yet.
/// </summary>
public sealed class ColonyConfig
{
    /// <summary>Phase 15: linear grid-fineness factor relative to the
    /// Phase 6–14 grid. Each old cell is now a GridScale×GridScale block of
    /// new cells covering the same physical world footprint, so
    /// 1 cell = 8 px / GridScale = 4 px of Colony Builder space. Every
    /// cell-denominated constant below is re-derived at this scale
    /// (distances ×GridScale, areas ×GridScale², per-cell rates re-derived
    /// from their original px/mass basis — never compounded from the
    /// Phase 14 cell values). Fractions, probabilities, angles, masses,
    /// and tick durations are scale-invariant and unchanged.</summary>
    public const int GridScale = 2;

    /// <summary>Farmed resource the founding queen deposits (the fungal
    /// pellet). game.js starterResource: 14 (mass, no conversion).</summary>
    public double StarterResource { get; init; } = 14;

    /// <summary>Ticks between eggs once the queen is laying.
    /// game.js offspringIntervalBase: 5.5 s × 30 tps = 165. Colony Builder
    /// also has a two-speed fast rate (offspringIntervalFast: 3.2 s = 96
    /// ticks once resource mass > offspringBoostThreshold: 30) that
    /// GroundSim's single-rate model doesn't implement — structural gap,
    /// flagged in the Phase 14 report, base rate only ported here.</summary>
    public int EggLayIntervalTicks { get; init; } = 165;

    /// <summary>Untended ticks for an egg to mature.
    /// game.js gestation: 5.5 s × 30 tps = 165.</summary>
    public int EggMaturationTicks { get; init; } = 165;

    /// <summary>Maturation speed multiplier while a Tender tends the egg.
    /// game.js tendSpeedup: 2.0 (dimensionless).</summary>
    public int TendedMaturationSpeed { get; init; } = 2;

    /// <summary>Fraction of matured eggs that survive ("most don't survive").
    /// game.js survivalFraction: 0.3 (probability, no conversion).</summary>
    public double EggSurvivalChance { get; init; } = 0.3;

    /// <summary>Rarest-first caste rolls: Major first, then Forager vs Tender
    /// (Tender is the most-common default). game.js majorFraction: 0.2 /
    /// foragerFraction: 0.6 (probabilities, no conversion). NOTE: Colony
    /// Builder gates these rolls behind population thresholds
    /// (majorUnlockPopulation: 7, foragerUnlockPopulation: 4); GroundSim
    /// rolls unconditionally from the start — structural gap, flagged in
    /// the Phase 14 report, fractions only ported here.</summary>
    public double MajorChance { get; init; } = 0.2;
    public double ForagerShareOfRemainder { get; init; } = 0.6;

    /// <summary>Forager haul size: base minus distance falloff, floored.
    /// game.js gatherChunkBase: 15 / gatherChunkMin: 5 (mass, no
    /// conversion — masses are scale-invariant);
    /// gatherChunkDistanceFactor: 0.02 per px × 4 px/cell (Phase 15 grid)
    /// = 0.08 per cell. Re-derived from the ORIGINAL px basis, not by
    /// scaling Phase 14's 0.16/cell (same result, but the px basis is the
    /// ground truth). Haul hits the floor at (15−5)/0.08 = 125 cells =
    /// 500 px — identical physical distance to Colony Builder's.</summary>
    public double GatherChunkBase { get; init; } = 15.0;
    public double GatherDistanceFalloff { get; init; } = 0.08;
    public double GatherChunkMin { get; init; } = 5.0;

    /// <summary>Ticks a Tender spends converting 1 raw material into 1
    /// farmed resource. game.js processRate: 3.2 mass/s → 30 tps / 3.2 =
    /// 9.375, rounded to 9 (int; ~4% faster than the true rate — the
    /// nearest-integer error is smaller than one tick's worth).</summary>
    public int ProcessTicks { get; init; } = 9;

    /// <summary>Farmed resource mass at which the Fungus Garden triggers.
    /// game.js gardenCrowdedThreshold: 45 (mass, no conversion).</summary>
    public double GardenTriggerThreshold { get; init; } = 45;

    /// <summary>Brood-pressure integral (sum of egg count per tick since
    /// founding) at which the Nursery triggers. An integral, not an
    /// instantaneous egg count — matching Colony Builder's own documented
    /// finding that instantaneous checks near a cap almost never fire.
    /// game.js nurseryBroodPressure: 140 egg·seconds × 30 tps = 4,200
    /// egg·ticks. Cross-check via the nursery:garden ratio the Phase 14
    /// handoff prescribed: 140/45 ≈ 3.11× the garden threshold per second
    /// → 3.11 × 45 × 30 = 4,200 — both routes agree because the garden
    /// threshold ported 1:1.</summary>
    public double NurseryBroodPressureThreshold { get; init; } = 4_200;

    /// <summary>Max idle Foragers assigned to assist room excavation.
    /// STILL INVENTED — game.js has no analog (its maxGatherers: 4 caps
    /// concurrent gatherers, a different concept; rooms there are carved
    /// on timers, not excavated by workers).</summary>
    public int WorkerDiggers { get; init; } = 2;

    /// <summary>Resource-node regrowth per tick toward each node's cap
    /// (Phase 9 sustain; 0 disables regeneration). STILL INVENTED —
    /// game.js nodes have amount: Infinity, so it has no regen concept
    /// at all.</summary>
    public double NodeRegenPerTick { get; init; } = 0.02;

    // ---- Phase 11 organic excavation (ALL INVENTED, in grid cells — the
    // design doc's inch-based ranges translated by judgment for this grid
    // scale; see PHASE11_REPORT for reasoning) ----

    /// <summary>Distance from the parent room's floor anchor to a new
    /// chamber's target center, in cells. Phase 15: 12–20 × GridScale
    /// (linear distance) = 24–40, same physical spacing.</summary>
    public double RoomBranchMinDistance { get; init; } = 24;
    public double RoomBranchMaxDistance { get; init; } = 40;

    /// <summary>Half-angle (radians) of the downward cone new rooms branch
    /// into (±from straight down).</summary>
    public double RoomBranchAngleSpread { get; init; } = 1.0;

    /// <summary>Connecting-tunnel corridor width bounds, in cells.
    /// Phase 15: 2–3 × GridScale = 4–6, same physical width.</summary>
    public double TunnelWidthMin { get; init; } = 4.0;
    public double TunnelWidthMax { get; init; } = 6.0;

    /// <summary>Per-step heading noise (radians ≈ 8.6°). Phase 15:
    /// UNCHANGED — angles are dimensionless. Steps are per-cell, so the
    /// finer grid wiggles at a finer spatial wavelength within the same
    /// ±MaxDeviation envelope; that finer-scale texture is exactly the
    /// added detail this phase is for, not an error to compensate.</summary>
    public double TunnelTurnJitter { get; init; } = 0.15;

    /// <summary>Max heading drift from the (re-aimed) bias direction
    /// (radians ≈ 31.5°).</summary>
    public double TunnelMaxDeviation { get; init; } = 0.55;

    /// <summary>Chamber footprint bounds, in cells (Phase 13: enlarged so
    /// rooms read as real rooms, tuned together with the doubled render
    /// resolution). Phase 15: 80–130 × GridScale² (AREA scales by the
    /// square of the linear factor) = 320–520, same physical footprint.</summary>
    public int ChamberMinArea { get; init; } = 320;
    public int ChamberMaxArea { get; init; } = 520;

    /// <summary>Probability an edge-adjacent seed cell joins the chamber seed
    /// before CA smoothing (breaks circular symmetry).</summary>
    public double ChamberEdgeNoise { get; init; } = 0.4;

    /// <summary>Phase 15: UNCHANGED, deliberately. CA smoothing reach is
    /// ~CaGenerations cells, so at the finer grid it smooths over half the
    /// physical radius — chamber edges keep proportionally more small-scale
    /// irregularity. That is the finer detail Kevin asked for, but it's an
    /// aesthetic judgment his live visual check should confirm; if blobs
    /// read as too rough, raising CaGenerations is the knob (Phase 16
    /// visual-polish territory, flagged in the Phase 15 report).</summary>
    public int CaGenerations { get; init; } = 4;
    public int CaThreshold { get; init; } = 5;

    /// <summary>Organic generation attempts before the guaranteed rect
    /// fallback (Part C hardening).</summary>
    public int MaskRetryAttempts { get; init; } = 6;

    // ---- Phase 12: founding shaft + home chamber + spoil mound (INVENTED) ----

    /// <summary>Entrance-shaft length bounds (cells) from the surface to the
    /// founding chamber. Phase 15: 8–12 × GridScale = 16–24, same physical
    /// depth.</summary>
    public int ShaftMinLength { get; init; } = 16;
    public int ShaftMaxLength { get; init; } = 24;

    /// <summary>Shaft wobble — near-zero so the entrance reads as a direct
    /// vertical hole (≈1.7°/4.6°), unlike the winding lateral corridors.</summary>
    public double ShaftTurnJitter { get; init; } = 0.03;
    public double ShaftMaxDeviation { get; init; } = 0.08;

    /// <summary>Founding-chamber footprint (cells) — smaller than worker-dug
    /// rooms; the Queen digs it alone. (Phase 13: enlarged with the rest.)
    /// Phase 15: 40–55 × GridScale² = 160–220, same physical footprint.
    /// Phase 16 Part C: retuned 160–220 → 240–320 (a deliberate PHYSICAL
    /// size increase per Kevin's live observation, NOT a scale conversion —
    /// avg 280 ≈ 2/3 of the Garden/Nursery average of 420, up from ~45%,
    /// so Home reads as a real roomy founding chamber while worker-dug
    /// rooms stay the larger ones). INVENTED, like all excavation
    /// geometry.</summary>
    public int HomeChamberMinArea { get; init; } = 240;
    public int HomeChamberMaxArea { get; init; } = 320;

    /// <summary>Spoil-mound drop offsets: deliveries alternate sides of the
    /// entrance at (opening half-width + 1 + rand(0..MoundDropRange)) columns,
    /// so the pile builds symmetrically around the hole.
    /// Phase 15: 5 × GridScale = 10, same physical spread.</summary>
    public int MoundDropRange { get; init; } = 10;

    /// <summary>Adaptive mound spreading: when a candidate drop column's pile
    /// is already this many cells above the original surface, the drop point
    /// walks outward to the next lower column. Without a cap, the inner slope
    /// grows until it continuously drains back down the entrance shaft and
    /// excavation reaches equilibrium with the refill (measured stall).
    /// Phase 13-DF: raised 4 → 7 — the cap was tuned for frictionless dirt;
    /// with DirtSlideChance friction, slopes hold and the mound can build
    /// real height without re-plugging the shaft (re-measured).
    /// Phase 15: 7 × GridScale = 14 — a height in cells, linear scale,
    /// same physical mound cap.
    /// Phase 16 Part B: 14 → 20, a deliberate retune (NOT a scale
    /// conversion). Root cause of the "flat mound" look: the adaptive
    /// spread makes the equilibrium shape a PLATEAU at exactly this cap,
    /// so the cap alone decides the mound's aspect; measured at the finer
    /// grid, dirt piles hold ~2.6× steeper slopes than at the old grid
    /// (grain run-length is fixed in cells = half the physical distance),
    /// and 4-seed 100k-tick app-world runs at 20 show peaked two-winged
    /// mounds with the entrance chimney essentially never plugged (0-2 of
    /// 200 samples, transient). COUPLING: must stay comfortably BELOW the
    /// entrance chimney's maintained height (12 × GridScale = 24) or the
    /// mound tops out above what maintenance keeps open and can seal the
    /// colony — 20 leaves a 4-cell margin.</summary>
    public int MoundMaxHeight { get; init; } = 20;

    /// <summary>Buffer margin (cells) kept between new masks and existing
    /// rooms, except at the deliberate tunnel connection.
    /// Phase 15: 1 × GridScale = 2, same physical margin.</summary>
    public int RoomOverlapBuffer { get; init; } = 2;

    public double HaulSize(double distanceFromHome)
        => Math.Max(GatherChunkMin, GatherChunkBase - GatherDistanceFalloff * distanceFromHome);

    // ------------------------------------------------------------------
    // Known tuned values for systems GroundSim does NOT have yet
    // (Phase 14, Part C). Recorded here so future phases that build these
    // systems start from Colony Builder's real numbers instead of
    // inventing new placeholders. Rates are per SECOND in game.js —
    // convert at 30 ticks/sec (and px→cell at 8/GridScale = 4 px/cell
    // since Phase 15) when porting.
    //
    //  1. Population-gated caste rolls: foragerUnlockPopulation: 4,
    //     majorUnlockPopulation: 7 (workers must exist before the caste
    //     can be rolled at all; GroundSim currently rolls unconditionally).
    //  2. Soldier caste: soldierUnlockPopulation: 5, soldierFraction: 0.15.
    //  3. Queen/nuptial flight: matureWorkerPopulation: 20 (plus all four
    //     room types present), queenFraction: 0.03 (rolled before Major —
    //     rarest), newColonyMinDistance: 350 px ≈ 88 cells at GridScale 2.
    //  4. Waste system: wasteSystemUnlockRooms: 2, wasteFromDecayFraction:
    //     0.6, wasteTriggerThreshold: 20, wasteCapacity: 40,
    //     wasteOverflowPenalty: 3.0, wasteDrainRate: 0.5/s.
    //  5. Contamination/grooming: contaminationRate: 0.15/s, groomRate:
    //     1.0/s, groomThreshold: 3, contaminationCapacity: 25,
    //     contaminationPenalty: 2.0 (stacks with waste overflow).
    //  6. Pupa Chamber: pupaBroodPressure: 300 egg·s (= 9,000 egg·ticks),
    //     pupaStageFraction: 0.6.
    //  7. Two-speed egg laying: offspringIntervalFast: 3.2 s (= 96 ticks)
    //     once resource mass > offspringBoostThreshold: 30.
    //  8. Trail/pheromone field: trailDepositRate: 16.0/s, trailDecayRate:
    //     0.5/s, trailMax: 8.0, trailFloor: 0.01, trailBaselineWeight: 1.0.
    //  Also observed while porting (not in the handoff's list):
    //  9. Resource decay: decayRate: 0.18 mass/s passive drain — GroundSim
    //     has no decay at all; the waste/contamination penalties above are
    //     multipliers ON this term, so those systems depend on it.
    // 10. Egg pacing cap and recycling: maxOffspring: 6 (eggs alive at
    //     once), recycleGain: 1.5 (resource per recycled egg) — GroundSim
    //     has neither an egg cap nor recycling.
    // ------------------------------------------------------------------
}
