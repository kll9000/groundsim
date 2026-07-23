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

    /// <summary>Maturation speed multiplier while a Minim tends the egg (Phase 18: was Tender).
    /// game.js tendSpeedup: 2.0 (dimensionless).</summary>
    public int TendedMaturationSpeed { get; init; } = 2;

    /// <summary>Fraction of matured eggs that survive ("most don't survive").
    /// game.js survivalFraction: 0.3 (probability, no conversion).</summary>
    public double EggSurvivalChance { get; init; } = 0.3;

    /// <summary>Phase 19: Soldier replaces Major, using Colony Builder's
    /// REAL tuned values, unused-but-known since Phase 14's Part C gap
    /// list: soldierFraction: 0.15, soldierUnlockPopulation: 5 (workers
    /// that must exist before a Soldier can roll; below it the roll falls
    /// through to the commoner castes). The retired MajorChance (0.2, also
    /// real) leaves with its caste.</summary>
    public double SoldierChance { get; init; } = 0.15;
    public int SoldierUnlockPopulation { get; init; } = 5;
    public double ForagerShareOfRemainder { get; init; } = 0.6;

    /// <summary>Phase 18 (new outline): of the caregiver remainder (the old
    /// Tender share, 0.32 of survivors), the fraction rolled as Gardener
    /// once unlocked — the rest are Minims. INVENTED 50/50 split: the new
    /// outline has no numbers and Colony Builder's game.js never had this
    /// caste distinction at all; flagged pending any better source.</summary>
    public double GardenerShareOfCaregivers { get; init; } = 0.5;

    /// <summary>Phase 18: workers that must exist before a Gardener can be
    /// rolled at all (below it the roll falls through to Minim). The
    /// outline ties Gardener appearance to the garden operation scaling up;
    /// a population gate is used rather than a Garden-room-excavated gate
    /// because the Garden's own trigger REQUIRES farmed resource, which
    /// requires processing, which only Gardeners do — an excavation-gated
    /// Gardener could never exist (deadlock by construction). Population
    /// gating is also exactly how Colony Builder's real values gate
    /// Forager/Major (Phase 14 Part C gap #1, first instance now built).
    /// INVENTED value: 4, mirroring game.js foragerUnlockPopulation: 4.</summary>
    public int GardenerUnlockPopulation { get; init; } = 4;

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

    /// <summary>Ticks a Gardener spends converting 1 raw material into 1
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

    /// <summary>Phase 18.5: max ticks a triggered room may wait in the
    /// planning queue for the previous room's excavation to finish before
    /// being planned anyway (accepting cone contention — the pre-18.5
    /// behavior as the bounded-wait fallback, so a satisfied trigger can
    /// never starve). INVENTED: ~2× a typical room excavation.</summary>
    public int MaxRoomPlanDeferralTicks { get; init; } = 60_000;

    /// <summary>Phase 18 Part B (new outline): cumulative raw material
    /// gathered by Foragers at which the Food-storage room triggers — the
    /// room appears once a real foraging economy exists, in the same
    /// trigger spirit as Garden/Nursery. INVENTED value (the outline gives
    /// no numbers; game.js never had this room).</summary>
    public double FoodStorageTriggerThreshold { get; init; } = 60;

    /// <summary>Phase 18 Part C (new outline): mean worker lifespan in
    /// ticks, with uniform ±jitter per individual. 0 or negative DISABLES
    /// death entirely (used by tests that pin fixed-workforce mechanics —
    /// excavation tests spawn exactly two workers and must not lose them
    /// mid-dig). INVENTED values: 40,000 ± 10,000 ticks ≈ 22 ± 5.5 sim-
    /// minutes at 30 tps — long enough that no fixed-workforce behavior
    /// test window (≤12k ticks) ever sees a natural death, short enough
    /// that long e2e/smoke runs exercise real population turnover
    /// (equilibrium ≈ lifespan / ~550 ticks-per-surviving-birth ≈ 70
    /// workers). The Queen is EXEMPT — her death/succession is explicitly
    /// deferred scope.</summary>
    public int WorkerLifespanMeanTicks { get; init; } = 40_000;
    public int WorkerLifespanJitterTicks { get; init; } = 10_000;

    /// <summary>Phase 21: ticks before a dead body disappears — applies
    /// both to unburied corpses (where they fell) and to settled Remains
    /// material (in the graveyard or from emergency lay-downs). Decay is a
    /// DELIBERATE, DISCLOSED exception to this project's conservation
    /// discipline: decayed matter is destroyed, tracked in
    /// Stats.CorpsesDecayed / Stats.RemainsDecayed so the death ledger
    /// still closes. 0 disables decay (tests). INVENTED value: 15,000
    /// ticks = 8.3 sim-minutes (~1 min of watching at the 240 tps
    /// default) — long enough that burials visibly accumulate, short
    /// enough that nothing grows without bound.</summary>
    public int RemainsDecayTicks { get; init; } = 15_000;

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
    /// and 4-seed 100k-tick app-world runs at 20 show a taller, narrower
    /// flat-topped mound with tapered flanks (Phase 16.5 correction: the
    /// top is still flat at the cap — ~84% of mound columns sit AT it —
    /// because this skip-at-cap rule truncates every peak at ANY cap
    /// value; a genuinely peaked silhouette needs a profiled/tapered
    /// height limit, a future-phase mechanism, not a cap retune), with
    /// the entrance chimney essentially never plugged (0-2 of
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

    /// <summary>Phase 24 item 3: candidates whose distance is within this
    /// factor of the true nearest count as "tied" and are chosen among
    /// with the colony's seeded RNG (exact-equality ties essentially never
    /// occur on a Manhattan grid, so strict-tie-only randomness would have
    /// changed nothing — the live scenario's two nodes sit at 132 vs 148
    /// from the nest and would still be a deterministic pick; this is the
    /// minimal widening that makes tie-break randomness actually fire,
    /// flagged in the Phase 24 report as a judgment call).
    /// 1.0 restores exact-nearest-only. INVENTED constant.</summary>
    public double NearTieToleranceFactor { get; init; } = 1.25;

    // ------------------------------------------------------------------
    // Phase 25: day-scale caste-emergence gates. ALL FOUR ARE INVENTED,
    // deliberately-tunable starting points for Kevin to react to (same
    // status as RemainsDecayTicks / CasteCircleScale). Day N in the
    // status-bar calendar = tick (N-1) × 43,200 (SimCalendar's
    // 1-based display). Each is a SOFT gate, the same shape as
    // SoldierUnlockPopulation: before the tick, that caste's roll falls
    // through to the commoner castes exactly like a failed population
    // gate; after it, the existing roll proceeds untouched, so
    // seed-to-seed variance around each target is preserved. Set to 0
    // to disable a gate (tests that exercise the roll mechanics
    // unrelated to pacing do this).
    // ------------------------------------------------------------------

    /// <summary>~day 2 — nominal: first eggs don't mature until ~day 2.05
    /// anyway (Phase 23 measurement), and Minim is the roll's ungated
    /// fall-through floor regardless (an egg that survives must become
    /// SOMETHING), so this constant documents the target rather than
    /// enforcing it. INVENTED.</summary>
    public int MinimMinEmergenceTick { get; init; } = 43_200;

    /// <summary>~day 4. INVENTED.</summary>
    public int GardenerMinEmergenceTick { get; init; } = 129_600;

    /// <summary>~day 2, matching Minim — RETUNED in Phase 25.5 from day 6
    /// (216,000) on Kevin's call: Forager is the colony's ONLY
    /// raw-gathering caste, i.e. the economic bottleneck, not a specialist
    /// — gating it to day 6 stalled the whole resource pipeline and the
    /// app-world garden never formed within a watchable window (Phase 25
    /// report §4.1). Minim and Forager re-clustering is the accepted
    /// trade-off; Gardener and Soldier still carry the day-scale spread.
    /// INVENTED.</summary>
    public int ForagerMinEmergenceTick { get; init; } = 43_200;

    /// <summary>~day 9.5 (stretching Soldier's existing last-to-appear
    /// margin, not inventing a new relationship). INVENTED.</summary>
    public int SoldierMinEmergenceTick { get; init; } = 367_200;

    // ------------------------------------------------------------------
    // Phase 27: trail-system constants. ALL INVENTED, single-sourced here
    // per convention. Exponential decay chosen over linear (one factor
    // gives a scale-free half-life, can't overshoot below zero).
    // ------------------------------------------------------------------

    /// <summary>Per-tick multiplicative decay. 0.9995 → half-life ≈ 1,386
    /// ticks (~46 sim-s; ~5.8 real-s at the 240 tps default) — long enough
    /// to outlive a typical forage round trip (~200–600 ticks), short
    /// enough that an abandoned route visibly fades. INVENTED.</summary>
    public double TrailDecayFactor { get; init; } = 0.9995;

    /// <summary>Strength added per reinforcement event. INVENTED.</summary>
    public double TrailReinforcePerVisit { get; init; } = 1.0;

    /// <summary>Ceiling on any one cell's strength — heavily-trafficked
    /// cells saturate instead of growing without bound. INVENTED.</summary>
    public double TrailMaxStrength { get; init; } = 12.0;

    /// <summary>Entries decaying below this are removed outright (sparse
    /// cleanup) — nothing lingers at near-zero forever. INVENTED.</summary>
    public double TrailCullThreshold { get; init; } = 0.05;

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
