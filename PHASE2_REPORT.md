# GroundSim — Phase 2 Handoff Report

**Date:** 2026-07-18
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (Phase 2 commit `395d4ff`, pushed to GitHub)
**Status:** ✅ Complete — builds clean, 16/16 tests passing, demo verified visually
**Prerequisite check:** Phase 1 + 1.5 base was verified at 10/10 tests before starting.

---

## 1. Scope

Phase 2 extended the Phase 1 single-material, one-particle-at-a-time simulation to:

1. **Loose rock** as a carryable material with distinct (steeper-piling) physics,
   separate from undiggable terrain Rock.
2. **Sticks** as a material that never slides — stacks where it lands.
3. **Mixed-material piles** with a no-clipping guarantee across all combinations.
4. **Many simultaneous particles** in flight (Phase 1 built the machinery but never
   exercised more than one at a time).

Still headless / no renderer; ASCII output via `AsciiRenderer`.

## 2. Concrete Rules Chosen

| Material | Rule when blocked below | Result |
|---|---|---|
| Dirt | Always slides to a diagonal if the diagonal + same-side cell are Air (unchanged from Phase 1.5) | Smooth ~45° slopes |
| LooseRock | Attempts a slide only with probability **0.25 per tick** (`Simulation.RockSlideChance`); otherwise settles immediately. When it does slide, same side-cell corner check as dirt. | Measurably narrower + taller piles than dirt |
| Stick | Never slides; settles exactly where it lands | Vertical stacks, no slope |

Rationale: one tunable constant for rock reuses all of dirt's slide machinery and gives
a quantitative, testable shape difference. Sticks use the handoff's suggested minimal
rule. **Multi-cell angled sticks were deliberately NOT scoped in** — they'd need
multi-cell occupancy, orientation state, and support checks (a real subsystem);
flagged as a Phase 3+ option rather than silently attempted.

`Dig()` needed no change for loose rock: it already refuses only Air and terrain
Rock, so `LooseRock` (and `Stick`) are carryable as-is. Terrain Rock remains
undiggable, unchanged.

## 3. Concurrent-Particle Mechanism (the important new code)

In-flight particles are **invisible to the grid** — only settled material is solid.
Particles may therefore share a cell mid-fall, and with 50 particles in flight two
could try to settle into the same cell. `Simulation.Settle()` now refuses to
overwrite settled material: a particle whose target cell was claimed by another
particle **bumps up to the first Air cell and stays active**, re-evaluating
fall/slide from there next tick. Consequences:

- **Conservation guarantee:** every dropped chunk settles into exactly one cell —
  no overwrites, no loss, no embedding inside settled cells (tested).
- **Performance property preserved:** tick cost is still O(active particles),
  independent of grid size. The new perf test (100 concurrent particles, 200×200
  grid, <1 s limit) exists specifically to catch an accidental O(grid) regression;
  the full 16-test suite runs in ~150 ms.

**Discovered behavior worth knowing for Phase 4 agent design:** because particles
don't collide mid-air, dropping N particles from the *same cell on the same tick*
makes them arrive as one burst and smear into a flat layer instead of a pile. A
*stream* (one drop every few ticks) forms natural piles. When agents dump carried
loads later, drops should be staggered over ticks, not batched into one tick.

## 4. Verified Behavior (demo)

`dotnet run --project GroundSim` — 20 drops of each material streamed concurrently
(one per material every 3 ticks), plus a mixed pile settled batch-by-batch:

```
Legend: '.' air  '#' dirt  '@' rock(terrain)  'o' loose rock  '/' stick

................................................................./............................
................................................................./............................
........................................o......................../............................
........................................o......................../............................
........................................o......................../.................../........
...............#.......................oo......................../.................../........
.............####......................ooo......................./...................oo.......
............######.....................ooo......................./..................ooo.......
...........#########..................oooo......................./...............########.....
###############################################################################################
```

Four visually distinct formations: dirt slope (x=20), steep narrow rock pile (x=45),
pure 20-high stick column (x=70), layered mixed pile — dirt base, rock middle,
sticks on top (x=90).

## 5. Test Suite (16 tests, all passing)

```
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 154 ms - GroundSim.Tests.dll (net10.0)
```

10 Phase 1/1.5 tests unchanged and passing, plus 6 new in
`GroundSim.Tests/MaterialBehaviorTests.cs`:

- `LooseRock_IsDiggable_UnlikeTerrainRock` — loose rock digs and returns; terrain rock still refuses.
- `RockPile_IsMeasurablySteeperAndNarrower_ThanDirtPile` — identical seeded 15-drop
  runs; asserts rock occupies **fewer columns** AND has **greater center height**
  than dirt (quantitative, not "looks different").
- `Sticks_NeverSlide_TheyStackInASingleColumn` — 10 stick drops → exactly 1 occupied
  column, exactly 10 high.
- `MixedDrops_LayerInDropOrder_WithoutClipping` — dirt→rock→stick drops; asserts
  exact per-material cell counts (conservation) and top-down layer order
  stick/rock/dirt in the drop column.
- `ManyConcurrentDrops_AllSettle_WithNoOverlapOrClipping` — 50 particles in flight
  at once (verified via `ActiveParticleCount`), settled once at the end; asserts
  exactly 50 cells settled and every settled cell rests on something solid.
- `HundredConcurrentParticles_SettleQuickly` — <1 s for 100 concurrent particles on
  200×200. Rationale in-code: ~10k particle-steps is microseconds of real work; 1 s
  is huge CI headroom but fails hard if concurrent handling becomes O(grid cells)
  per tick.

## 6. Unspecified Decisions Made (flag for course-correction)

1. **No mid-air particle collision** (particles can share cells while falling).
   Simplest model that preserves O(active) cost; the burst-smear consequence and
   its stagger-drops mitigation are documented in §3.
2. **Rock slide probability = 0.25** — arbitrary but tunable single constant;
   changing it only affects pile steepness, and the shape test compares
   relative-to-dirt, not absolute numbers.
3. **Bumped particles stay active** rather than force-settling where the collision
   happened — costs a few extra ticks under heavy concurrency, keeps piles natural.
4. **Loose-rock debris seeding** is done manually in the demo (a strip of
   `LooseRock` cells on the surface), not in `Grid.CreateTestWorld` — world-gen
   placement of debris felt like a Phase 3/4 worldbuilding decision.

## 7. Suggested Phase 3 Considerations

- Multi-cell angled sticks (deliberately deferred, see §2).
- Debris placement in world generation.
- A real-time tick loop / renderer will need a drop-staggering convention (§3).
- `RockSlideChance` and material rules may want a per-material properties table
  once a 4th behavior appears, rather than branches in `Tick()`.
