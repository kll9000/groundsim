# Phase 15 Report — Finer Simulation Grid (GridScale = 2)

The simulation grid is now 2× finer per axis: every Phase-14 cell is a 2×2
block of new cells covering the same physical world. All 99 tests pass
(94 prior + 5 new conversion pins), the smoke runs clean at the new
density, and every conversion is shown below. **One big flagged
consequence up front: tick-denominated pacing inflated ~7-8×** (founding
4,072 → 31,224 ticks) because dirt digging is pinned at the engine's
1-cell-per-tick floor and cannot scale down with cell volume — see "The
pacing consequence" section for why this is inherent, what it means for
real-time feel, and the Phase-16-shaped options.

## Part A — scale factor 2×, same physical world

**Factor: 2× linear (4× total cells), chosen over 3×** because 2× already
doubles the pixel detail of every ant/terrain/mound feature at unchanged
`CellSize`, while 3× would be 9× the cells and — because of the 1-tick
dig floor explained below — ~18× the excavation ticks, which is pacing
territory nobody has asked for. 2× is also the factor whose conversions
stay exact in integers (no half-cell rounding anywhere).

**World-size decision: the world does NOT grow.** `Grid.Width/Height`
double (app world 200×120 → 400×240 cells) but they represent the *same
physical footprint* — 1600×960 Colony Builder pixels at 8 px per old
cell = 4 px per new cell. "A cell" now means half the physical distance
it used to; "the world" means exactly what it meant before. This is
pinned by a test (`AppWorld_SamePhysicalFootprint_FinerCells`).

The unit chain from Phase 14 extends cleanly:
**1 sim second = 30 ticks (unchanged); 1 cell = 8 px / GridScale = 4 px.**
`ColonyConfig.GridScale = 2` is now a real constant that production code
and tests derive from.

## Part B — every conversion, shown

### ColonyConfig — scaled

| Constant | Phase 14 | Phase 15 | Rule |
|---|---|---|---|
| `GatherDistanceFalloff` | 0.16/cell | **0.08/cell** | re-derived from the px basis: 0.02/px × 4 px/cell — NOT by dividing the old cell value (same result, but the handoff's anti-double-compounding concern is real and the conversion test pins the px route) |
| `RoomBranchMin/MaxDistance` | 12 / 20 | **24 / 40** | linear ×S |
| `TunnelWidthMin/Max` | 2 / 3 | **4 / 6** | linear ×S |
| `ShaftMin/MaxLength` | 8 / 12 | **16 / 24** | linear ×S |
| `MoundDropRange` | 5 | **10** | linear ×S |
| `MoundMaxHeight` | 7 | **14** | linear ×S (a height) |
| `RoomOverlapBuffer` | 1 | **2** | linear ×S |
| `ChamberMin/MaxArea` | 80 / 130 | **320 / 520** | area ×S² |
| `HomeChamberMin/MaxArea` | 40 / 55 | **160 / 220** | area ×S² |

Cross-check (same pattern as Phase 14's): the physical haul-floor
distance is invariant — (15−5)/0.08 = 125 cells × 4 px = **500 px**,
identical to Colony Builder's and to Phase 14's 62.5 cells × 8 px.

### ColonyConfig — deliberately unscaled, with reasoning

- **Masses, probabilities, tick durations** (`GatherChunkBase/Min`,
  `StarterResource`, egg/process/trigger values): dimensionless in cells.
  Pinned unchanged by `ScaleInvariants_DidNotChange`.
- **Angles** (`TunnelTurnJitter`, `MaxDeviation`, shaft variants,
  `RoomBranchAngleSpread`): dimensionless. Finer per-cell steps wiggle at
  a finer spatial wavelength inside the same deviation envelope — that
  finer texture is the point of the phase.
- **CA parameters** (`CaGenerations`, `CaThreshold`, `ChamberEdgeNoise`)
  and MaskGenerator's rim constants (1.4/1.0/2.5/0.12 cells): chamber
  edge texture. Unscaled, edges keep proportionally more small-scale
  irregularity — flagged for Kevin's live visual check; `CaGenerations`
  (and the rim pads) are the knobs if blobs read too rough.
- **Fractions/ratios** (`AirFraction ≥ 0.3`, the 0.7 reachability guard,
  the ≤10% already-air bar): scale-invariant by construction, confirmed
  by reading each use — they divide cell counts by cell counts.

### Where I diverged from the handoff's guesses — flagged explicitly

1. **Junction redundancy `≥ 2` DID need scaling (→ `≥ 2×S = 4`).** The
   handoff guessed counts are scale-invariant; this one is a physical
   opening width in disguise. Its purpose (Phase 12.5) is "a junction one
   settling particle can't seal" — particles are cell-sized, so 2 fine
   cells = 1 old cell = exactly the fragility the rule exists to prevent.
   Same for the 3×3 tunnel-mouth widening (now (2S+1)² = 5×5).
2. **`RockDigTicks` stays 4, NOT scaled to 1.** The handoff leaned toward
   scaling per-cell time cost down with cell volume, and that argument is
   real (4 ÷ 4 = 1 tick preserves rock mass/tick exactly). But dirt is
   already at the 1-tick floor and cannot follow — so scaling rock alone
   would make rock cost *identical to dirt per cell*, silently deleting
   Phase 13's designed "rock digs slower than dirt" property as a side
   effect of a resolution change. The designed semantic is the 4×-dirt
   RATIO; the ratio is what survives. Consequence: all excavation slows
   uniformly (next section), relative hardness preserved exactly.

### Hardcoded cell literals outside ColonyConfig (full sweep)

A dedicated sweep (production code, every `.cs`) found and scaled these —
each annotated in code with its rule:

- **OrganicPlanner**: degenerate-blob floor 12→12S² (area); margins
  4/3/4/5/6-cell offsets ×S; hardcoded shaft bore 2→2S; entrance
  half-widths 2→2S, 5→5S; both fallback rects 6×3→6S×3S and 9×4→9S×4S
  with their clamps; chimney height 12→12S and width ±1→±S (must clear
  the scaled mound cap and cover the scaled shaft bore — the old 12-vs-7
  and 3-covers-2 relationships are preserved as 24-vs-14 and 5-covers-4).
  Adjacency idioms (halo radius 1, +1 below-anchor) deliberately left.
- **Colony.NextSpoilDropX**: outward mound-spread span 40→40S.
- **Pathfinder `maxExpansions`** 50k→50k×S²=200k: the cap bounds explored
  nodes and the same physical route now has 4× the nodes — unscaled it
  silently reclassifies real routes as unreachable (stall, not crash).
- **MaskGenerator `maxSteps`** 400→400S: steps are ~1 cell; branch
  distances doubled; unscaled it truncates long tunnels into silent
  "never arrived" plan rejects.
- **MainWindow initial framing** −6→−6S cells (same physical framing).
- **Left alone, with reasoning**: `Grid.CreateTestWorld` (all fractions —
  rock density scales by relative depth; rock speckle is finer-grained
  now, which reads as detail); `Room`'s `y*1000` sort key (safe below
  1000-cell dimensions — noted as a latent assumption that would break at
  ~5× scale, fine at 2×); camera click-radius and zoom limits (screen-
  space feel, identical at equal zoom); `Program.cs` console demo (a
  self-contained Phase-2 fixture, decoupled from colony scale);
  `DirtSlideChance`/`RockSlideChance` (per-event probabilities — friction
  character at finer granularity is Phase 16's explicit territory).

## The pacing consequence — flagged, not hidden

The engine digs 1 cell per tick per digger (the load-bearing non-blocking
contract) and dirt is already at that floor. The same physical chamber is
now 4× the cells, and every haul walks 2× the cells at the unchanged
1 cell/tick agent speed (movement code is explicitly out of scope). So
excavation-dominated milestones inflate ~×8 (4× cells, ~2× per-cell haul
round-trip), while time-based systems (eggs, processing, triggers) are
unchanged — measured exactly that way:

| 10-seed e2e median (ticks) | Phase 14 | Phase 15 | ratio |
|---|---|---|---|
| first worker | 4,863 | 31,709 | 6.5× |
| garden excavated | 11,320 | 58,010 | 5.1× |
| nursery excavated | 14,368 | 72,700 | 5.1× |

App-world smoke milestones: founding 4,072 → 31,224 (7.7×), first worker
4,731 → 32,708, garden 16,344 → 56,708, nursery 11,001 → 68,560.

**Real-time feel at the renderer's default 60 tps: founding went from
~68 s to ~8.7 min.** I did NOT change the default tps — that's a
watchability decision Kevin should make live (the Up-arrow already
reaches 240 tps, which brings founding back to ~2.2 min). If the slower
feel is wrong, the options are (a) raise the renderer's default tps
(playback speed, zero sim-semantics impact — the Phase 8 precedent), or
(b) let diggers excavate multiple cells per tick at the finer grid — a
real change to the one-unit-of-work-per-tick contract that should be its
own deliberated phase, not a side effect here. Flagged for Phase 16
scoping.

**Ordering note:** the app-world Nursery-before-Garden flip that Phase 14
introduced (and Kevin accepted) has flipped back — the Garden completes
first again (56,708 vs 68,560) because excavation time now dominates
trigger time. Both rooms' *triggers* still fire in the Phase 14 order;
it's the dig queue that serializes them. Nothing asserts an ordering;
noting it so nobody treats either ordering as pinned behavior.

## Part C — measured performance at the new density

All numbers from the headless smoke (Release, 160k ticks, full render
pipeline), against Phase 14's smoke as the comparison base:

- **Per-tick cost: 0.070 → 0.210 ms/tick.** This is NOT a grid-size
  effect: the run is 4× longer in ticks, so the colony grows far larger
  before the sample window closes (245 workers at end vs 58). Per-agent
  cost actually *fell* slightly: 0.070/58 ≈ 1.2 µs vs 0.210/245 ≈ 0.86 µs
  per worker-tick. Cost tracks colony size and active particles — the
  O(active), not-O(grid) property held (the Phase 9 lesson about
  cross-scenario comparisons applies; this is the honest framing).
- **Dirty-cell rendering: max 208 dirty cells/frame = 0.22% of the
  96,000-cell grid** (Phase 14: 72 = 0.30% of 24,000). The dirty
  *fraction* went down — redraw work scales with activity, not grid
  size. Property intact.
- **Mask generation: still measured-cheap.** The standing 100-plan
  benchmark test passes at the 4×-area chambers (threshold 2 s, actual
  well under; it exists to catch O(grid²) regressions, and didn't fire).
- **Memory: trivial.** The cell array is 96,000 cells (~100 KB order) vs
  24,000; the world bitmap doubles per axis to 800×480 px (~1.5 MB) —
  both far below anything measurable in this app.
- **Camera:** all camera math derives from `CellSize` (unchanged at 2 px)
  and the bitmap dimensions, which scale automatically; the smoke drives
  the full render path headlessly. `MaxZoom` 20 still gives 40 px/cell at
  deepest zoom — and each physical feature now spans 2× the cells, so
  max-zoom shows *more* physical detail, which is the entire point.
  Kevin's live check remains the confirmation for pan/zoom/click feel.

## Tests — 99/99, every change disclosed

```
Passed!  - Failed:     0, Passed:    99, Skipped:     0, Total:    99, Duration: 1 m 26 s
```

**New: `GridScaleConversionTests` (5 tests)** — pins every conversion to
its Phase-14 basis by formula (distances ×S, areas ×S², falloff from the
px basis, invariants unchanged, world footprint unchanged), the
self-verification the handoff asked for.

**Changed tests, each with reasoning (nothing silently loosened):**

1. **Colony test worlds scaled ×2** (240×120, ground 60, entrance 112,
   node coordinates ×2) across the six colony-behavior test files. Node
   *amounts* (mass) unchanged. Pure-physics/pathfinding fixtures were NOT
   scaled — their geometry is self-contained and scale-invariant.
2. **Tick budgets ×8, not ×4** (founding 30k→180k, communal digs
   8k→64k, rock-clearing 25k→200k, spoil run 40k→240k, e2e 60k→360k,
   smoke 40k→160k): ×S² cells at the 1-tick floor times ~×S haul walks.
   All are early-exit loops — headroom costs nothing on passing runs.
3. **Cell-denominated assertion bounds scaled by their dimension**:
   shaft row-width ≤3→≤6, wander ≤4→≤8, row count ≥5→≥10 (linear);
   chamber-area windows 12–90→48–360, spoil volume ≥60→≥240, diggable
   floors ≥20→≥80 and ≥8→≥32 (area); spoil column-spread ≥8→≥16
   (linear). Fractional assertions (lopsidedness fifths, half-in-one-
   column, in-garden accounting) untouched.
4. **`Plan_AvoidsExistingRooms` seed 5→2.** At the new scale seed 5's
   draw sequence exhausts its retries near the (scaled) blocker. Measured
   before changing: 20/25 seeds place organically — the planner is
   healthy; the seed is simply unlucky, which the fallback design
   tolerates by contract. Seeded tests here have always been picked to
   exercise the intended branch; the fallback branch has its own test.
5. **`Plan_FallsBack` forcing device redesigned** — the one substantive
   test change, root-caused before touching it (details below).
6. **`SupportChecks` perf guard**: run 8k→60k ticks, limit 5s→30s,
   scaled together so the assertion still fails hard on an accidental
   O(grid) per-tick sweep; the "did real work" guard needed founding
   (now ~31k ticks) to complete inside the window.

### The one real investigation: the fallback test's synthetic void

The old test forced organic failure by carving everything below the Home
Room to air. At the new scale the dug fallback room ended up floating
over that synthetic abyss: its interior air had no 3×3 support, so no
walkable route to the last 12 cells existed. Measured precisely: workers
frozen for 60k straight ticks, path to home center = 17 steps, path to
EVERY supported approach cell = NULL. The room's own floor was the void —
a geometry real excavation cannot produce (real rooms sit in solid
ground; the planner's fresh-ground and reachability guards exist to
guarantee exactly that). The forcing device is now a giant pre-existing
blocker room filling the branch cone (every organic candidate overlaps
its forbidden halo → fallback), with solid ground under the rect — same
forced branch, same "genuinely dug to completion" assertions, no
unrepresentative physics.

**Flagged as a latent edge, not fixed here:** an agent whose dig target
becomes unreachable goes onto a blacklist that only clears when terrain
changes; in a world where *nothing* can change terrain (all diggers
blacklisted simultaneously), that's a permanent stall. It cannot arise
from planner-produced geometry (the guards above), only from synthetic
worlds — but it's now a known boundary of the dig machinery, on record.

## What Kevin should look at live

1. **Overall feel at 60 tps** — founding is ~8.7 min real-time now
   (Up-arrow to 240 tps ≈ 2.2 min). Decide whether the default playback
   speed should rise; that's a one-line watchability change with
   Phase 8 precedent.
2. **Chamber edge texture** — deliberately finer-grained now (CA and rim
   constants unscaled). If too rough: `CaGenerations` up from 4, or scale
   the MaskGenerator rim pads.
3. **Rock speckle** — test-world rock is per-cell noise, so pockets are
   finer-grained (reads as detail, but it's a look change).
4. **Camera feel** — math rescales automatically and the smoke exercises
   the pipeline, but pan/zoom/click-to-follow feel is a live check.

## Files changed

- `GroundSim/Colony/ColonyConfig.cs` — GridScale + all scaled/annotated values
- `GroundSim/Colony/OrganicPlanner.cs` — all planner literals scaled (S-derived)
- `GroundSim/Colony/Colony.cs` — mound spread span
- `GroundSim/Colony/MaskGenerator.cs` — step cap; rim constants annotated
- `GroundSim/Pathfinder.cs` — expansion cap
- `GroundSim/Agent.cs` — RockDigTicks reasoning (value unchanged)
- `GroundSim.Render/ColonyScenario.cs`, `App.xaml.cs`, `MainWindow.xaml.cs` —
  world dims, smoke budget, initial framing
- `GroundSim.Tests/*` — world scaling, budgets, bounds, seed, fallback
  device, new `GridScaleConversionTests.cs`

Out of scope confirmed untouched: movement/support rules, camera logic,
friction physics, `CellSize`, all Colony Builder source values.
