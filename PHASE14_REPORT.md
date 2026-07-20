# Phase 14 Report — Real Tuned Values from Colony Builder

Ports Colony Builder's actual tuned `game.js` CONFIG into `ColonyConfig`,
closing the "every constant is invented" caveat open since Phase 6. Source:
`C:\Users\Kevin\Desktop\Leaf Cutter\game.js` (read-only reference, untouched).

**Result: 94/94 tests, smoke clean, pacing shift measured and modest at the
milestone level — but one behavioral flip found and flagged (Nursery now
completes before the Garden in the app world; see Part D).**

---

## Part A — conversion bases (two of them, not one)

**Time: 1 sim second = 30 ticks.** The handoff asked me to confirm the
renderer's actual default before assuming 30 — and that check mattered: the
live renderer constructs its clock with `TicksPerSecond = 60`
(`MainWindow.xaml.cs:25`), not the core `TickClock` default of 30. But the
Phase 8 report documents that 60 was chosen explicitly as a **watchability
fast-forward** ("founding takes ~40 s to watch"), user-adjustable 2–240 at
runtime — a playback-speed choice, not a redefinition of the simulation
second. The engine's canonical second is the core default, 30 ticks. All
time conversions below use 30 tps; at the renderer's default speed the app
therefore plays Colony Builder's pacing at 2×, exactly as it played the
invented pacing at 2×.

**Distance: 1 grid cell = 8 px.** Not in the handoff, but required: several
CONFIG values are per-*pixel*, and GroundSim's distances are in cells.
Colony Builder's grid is `cell: 8` px, world 1600 px = 200 columns;
GroundSim's app world is also 200 columns wide (`ColonyScenario.Width`).
Same world, same column count → 1 cell = 8 px. Rate-per-px values are
multiplied by 8 to become rate-per-cell.

## Part B — the ports (before → after, conversion shown)

| Constant | Invented | Real | Conversion | Substantial? |
|---|---|---|---|---|
| `StarterResource` | 10 | **14** | direct mass | minor |
| `EggLayIntervalTicks` | 90 | **165** | 5.5 s × 30 tps | ⚠️ queen lays ~2× slower |
| `EggMaturationTicks` | 600 | **165** | 5.5 s × 30 tps | ⚠️ eggs mature 3.6× faster |
| `TendedMaturationSpeed` | 2 | **2** | dimensionless (2.0) | unchanged |
| `EggSurvivalChance` | 0.35 | **0.3** | direct probability | minor |
| `MajorChance` | 0.10 | **0.2** | direct probability | ⚠️ Major share doubled |
| `ForagerShareOfRemainder` | 0.50 | **0.6** | direct probability | moderate (Tender share 0.45 → 0.32 of survivors) |
| `GatherChunkBase` | 8 | **15** | direct mass | ⚠️ hauls ~2× bigger |
| `GatherDistanceFalloff` | 0.04 | **0.16** | 0.02/px × 8 px/cell | ⚠️ 4× steeper — and the px→cell conversion is why; a naive copy of 0.02 would have been 8× too shallow |
| `GatherChunkMin` | 1 | **5** | direct mass | ⚠️ haul floor 5× |
| `ProcessTicks` | 20 | **9** | 30 tps ÷ 3.2 mass/s = 9.375 → 9 (int; ~4% fast) | ⚠️ processing ~2× faster |
| `GardenTriggerThreshold` | 30 | **45** | direct mass | moderate — garden later |
| `NurseryBroodPressureThreshold` | 25,000 | **4,200** | 140 egg·s × 30 tps; cross-checked via the handoff's ratio route: (140/45) × 45 × 30 = 4,200 — the two derivations agree because the garden threshold ported 1:1 | ⚠️ nursery ~6× earlier — see Part D |

Sanity check on the falloff conversion: haul reaches its floor at
(15−5)/0.16 = **62.5 cells**, identical to Colony Builder's (15−5)/0.02 =
500 px = 62.5 cells. The invented values floored at 175 cells — the real
tuning is much steeper with a much higher floor.

**Not ported, still invented (game.js has no analog):** all Phase 11/12
excavation-geometry constants (chamber areas, tunnel width/jitter, shaft,
mound — Colony Builder carves rooms as fixed ellipses on timers, it has no
cell-by-cell digging), `NodeRegenPerTick` (its nodes are `amount:
Infinity`), and `WorkerDiggers` (its `maxGatherers: 4` caps concurrent
gathering, a different concept). Each is now explicitly marked `STILL
INVENTED` in `ColonyConfig` with the reason.

## Part C — structural gaps (systems Colony Builder has, GroundSim doesn't)

Documented in a "known but unbuilt" block at the bottom of
`ColonyConfig.cs` so future phases start from real numbers. On record here
too, per the handoff:

1. **Population-gated caste rolls** — `foragerUnlockPopulation: 4`,
   `majorUnlockPopulation: 7`. GroundSim rolls all castes unconditionally
   from the first egg; Colony Builder's early colony is Tender-only until
   workers exist. This phase ports the *fractions* only — early-game caste
   mix genuinely differs from Colony Builder's.
2. **Soldier caste** — `soldierUnlockPopulation: 5`, `soldierFraction: 0.15`.
3. **Queen/nuptial flight** — `matureWorkerPopulation: 20` (plus all four
   room types), `queenFraction: 0.03` (rolled before Major),
   `newColonyMinDistance: 350` px ≈ 44 cells.
4. **Waste system** — `wasteSystemUnlockRooms: 2`, `wasteFromDecayFraction:
   0.6`, `wasteTriggerThreshold: 20`, `wasteCapacity: 40`,
   `wasteOverflowPenalty: 3.0`, `wasteDrainRate: 0.5/s`.
5. **Contamination/grooming** — `contaminationRate: 0.15/s`, `groomRate:
   1.0/s`, `groomThreshold: 3`, `contaminationCapacity: 25`,
   `contaminationPenalty: 2.0` (stacks with waste overflow).
6. **Pupa Chamber** — `pupaBroodPressure: 300` egg·s (= 9,000 egg·ticks),
   `pupaStageFraction: 0.6`.
7. **Two-speed egg laying** — `offspringIntervalFast: 3.2 s` (= 96 ticks)
   once resource > `offspringBoostThreshold: 30`. Base rate only is ported.
8. **Trail/pheromone field** — `trailDepositRate: 16.0/s`, `trailDecayRate:
   0.5/s`, `trailMax: 8.0`, `trailFloor: 0.01`, `trailBaselineWeight: 1.0`.

**Two additional gaps observed while porting (not in the handoff's list),
flagged rather than skipped:**

9. **Resource decay** — `decayRate: 0.18 mass/s` passive drain. GroundSim
   has no decay at all. Note the dependency: the waste and contamination
   *penalties* (items 4–5) are multipliers **on** this decay term, so those
   systems can't be built meaningfully until decay exists.
10. **Egg cap and recycling** — `maxOffspring: 6` (pacing cap on eggs alive
    at once) and `recycleGain: 1.5` (resource refund per recycled egg).
    GroundSim has neither. The missing cap matters for the brood-pressure
    integral's growth rate (see Part D's ordering flip).

## Part D — pacing re-verification

Because the last e2e medians on record (Phase 13 rock-mining report) predate
dirt friction, I measured a fresh baseline myself at unmodified HEAD
(`git stash` → run → `git stash pop`) so the comparison is truly
like-for-like — same code, same seeds, only the constants differ:

| 10-seed e2e median (ticks) | invented (HEAD) | real values | shift |
|---|---|---|---|
| first worker | 4,897 | 4,863 | −0.7% |
| garden excavated | 10,247 | 11,320 | +10.5% |
| nursery excavated | 14,282 | 14,368 | +0.6% |

The large individual swings mostly cancel at the milestone level (founding
excavation, unchanged by this phase, dominates time-to-first-worker; faster
maturation offsets slower laying; bigger hauls offset the steeper falloff
against the higher garden threshold). **No existing test's tick bounds
needed adjusting** — the e2e's 60k budget absorbs the +10% garden shift
with huge margin.

Full suite:

```
Passed!  - Failed:     0, Passed:    94, Skipped:     0, Total:    94, Duration: 3 s
```

App-world smoke (200×120, 40k ticks):

```
colony smoke ok: 40000 ticks in 2787 ms (0.070 ms/tick), stage Expansion,
workers T:17 F:31 M:10, milestones: home=4072 worker=4731 gardenDone=16344
nurseryDone=11001, max dirty cells/frame 72 (0.30% of grid)
```

Performance is unchanged (value-only change; 0.070 ms/tick is in line with
the Phase 13-DF smoke). Founding (`home=4072`) is tick-identical to the
Phase 13-DF smoke — expected, since founding excavation touches nothing
ported.

### ⚠️ Flagged: the Nursery now completes BEFORE the Garden in the app world

Pre-port smoke order: garden ~9.8k, then nursery. Post-port: **nursery
11,001, garden 16,344.** Two ported values move in opposite directions
(garden trigger 30→45 = later; nursery integral 25,000→4,200 = much
earlier), and in the app world — where surface nodes are farther and the
steeper falloff bites hardest — they cross. In the small e2e world the
garden still finishes first (11,320 vs 14,368), so this is world-geometry
dependent, not a fixed ordering.

I am deliberately **not** "fixing" this: no code or test has ever asserted
room order, and the numbers are Colony Builder's own. But Colony Builder's
comment says the nursery was tuned "to land just after the garden," and its
dynamics include systems GroundSim lacks (egg cap bounding the integral's
growth, two-speed laying, resource decay slowing the garden trigger — gaps
1, 7, 9, 10 above). So the flip is plausibly an artifact of porting real
numbers into a model missing those systems, not real intended behavior.
**Recommend:** Kevin eyeballs whether nursery-before-garden reads
acceptably in the live app; if not, the principled fix is building the
missing systems (egg cap first — it directly bounds the integral), not
re-inventing the threshold we just made real.

## Test changes (disclosed per project rule)

- `SurvivalAndCasteRolls_MatchConfiguredDistribution` failed after the port
  — its bounds hardcoded the old invented distribution (survival 0.32–0.38
  vs real 0.3). This is the "is the test outdated for a good reason?" case:
  yes — changing these exact values is the phase's purpose. Rather than
  re-pinning new magic numbers, the test now **derives** its expected
  distribution from the live `ColonyConfig` (±0.03 over 20k trials), so it
  keeps verifying the roll *logic* against config and survives future
  retunes. It can still fail on any real roll-logic bug (wrong branch
  order, un-normalized shares).
- The e2e's output label and comment no longer claim "INVENTED constants."

No other test was touched. No tick bound was changed anywhere.

## The standing "constants invented" warning — narrowed, not removed

The blanket warning is no longer accurate, but it can't be dropped either.
Replacement wording (now in `ColonyConfig`'s class doc):

> Caste/egg/gather/room-trigger values are REAL, ported from Colony
> Builder's tuned game.js at 30 ticks/sec and 8 px/cell. Excavation
> geometry (chambers, tunnels, shaft, mound), node regen, and digger caps
> remain INVENTED — game.js has no analog for cell-based digging. Behavioral
> parity is NOT implied: ten tuned systems game.js has are not built yet
> (see the known-but-unbuilt block in ColonyConfig.cs).

## Files changed

- `GroundSim/Colony/ColonyConfig.cs` — the port, the narrowed warning, the
  known-but-unbuilt reference block. No signature changes; values only.
- `GroundSim.Tests/ColonyCasteTests.cs` — distribution test derives from
  config.
- `GroundSim.Tests/RoomAndStageTests.cs` — comment/label wording only.

Out of scope confirmed untouched: excavation/movement/camera code, all
Part C systems, `game.js` itself.
