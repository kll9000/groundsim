# GroundSim — Phase 3 Handoff Report

**Date:** 2026-07-18
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (Phase 3 commit `d37bcce`, pushed to GitHub)
**Status:** ✅ Complete — builds clean, 24/24 tests passing, renderer verified running
**Prerequisite check:** Phase 1+1.5+2 base was verified at 16/16 tests before starting.

---

## 1. Scope

Phase 3 added a real 2D windowed renderer on top of the headless simulation:

1. A lightweight WPF renderer (no engine dependency), strictly read-only over the sim.
2. Dirty-cell rendering — settled terrain costs zero per-frame draw time.
3. A debug overlay visually distinguishing awake (falling) particles from settled terrain.
4. A fixed simulation tick rate decoupled from render frame rate.

No agent AI (Phase 4), no game integration (Arc 2), minimal controls only.

## 2. Rendering Approach Chosen

**WPF + `WriteableBitmap`**, new project `GroundSim.Render` (net10.0-windows):

- Native on Windows, zero external packages, no game engine — appropriate
  proof-of-concept weight, per the handoff.
- 5 px per cell; 200×120 demo world → 1000×600 px window.
- `GridRenderer` only ever **reads** `Grid`/`Simulation` — physics stays fully
  headless and testable, as required. The core projects have no rendering reference.
- Run with `dotnet run --project GroundSim.Render`. Controls: **Space** = pause,
  **Up/Down** = double/halve speed (2–240 tps). Nothing more, per item 5.
- `--smoke` flag runs the entire render pipeline (ticks + dirty tracking + bitmap
  writes) headless for 600 ticks and exits — usable in CI / by agents.

## 3. Dirty-Cell Tracking (item 2) — full tracking, not the viewport fallback

The full grid is drawn **once** at startup. Every frame after redraws **only**:

- Cells written in the grid this frame (settles, digs) — captured via a new
  `Grid.CellChanged` event.
- Active particles' previous positions (redrawn as background) and current
  positions (particle overlay) — captured via `DirtyTracker.MarkParticles`.

Measured result from the smoke run: **600 ticks on 200×120 (24,000 cells), max 48
dirty cells in any frame — 0.20% of the grid.** Settled interior terrain is never
touched, matching the simulation's O(active particles) property.

## 4. Debug Overlay (item 3)

Active particles draw in bright yellow over material colors; settled terrain keeps
its material color. The status bar shows live `active` and `dirty` counts every
frame — if dirty count ever balloons relative to active particles, the performance
regression is visible immediately, which is the sanity check this item wanted.

## 5. Fixed Tick Rate (item 4)

`TickClock` (in the core project — pure logic, no rendering dependency):

- Configurable `TicksPerSecond` (default 30), fully decoupled from frame rate.
- Fractional ticks accumulate across frames (a 60 fps frame at 10 tps yields 0
  ticks most frames, 1 every ~6th — never truncated away).
- `MaxTicksPerAdvance` cap (default 8): a long stall (breakpoint, window drag)
  causes slow-motion catch-up, not a burst that teleports particles.

## 6. Core Changes — the flag item 6 asked for

Dirty-tracking requires *observing* changes, so two **additive, observational**
members were added to the simulation layer:

1. `Grid.CellChanged` — event fired from the indexer setter on any cell write.
2. `Simulation.ActiveParticles` — read-only `IReadOnlyList<Particle>` view.

No simulation logic reads either; no existing behavior changed; **all Phase 1–2
tests pass unmodified.** The alternative (renderer-side snapshot diffing) would be
O(grid) per frame — strictly worse. Flagged here so it can be vetoed before
Phase 4 builds on it; `DirtyTracker` and `TickClock` also live in the core project
(pure logic, unit-tested, no rendering types).

## 7. Test Suite (24 tests, all passing)

```
Passed!  - Failed:     0, Passed:    24, Skipped:     0, Total:    24, Duration: 84 ms - GroundSim.Tests.dll (net10.0)
```

18 prior tests unchanged (the rock-pile test expanded, see §8), plus new in
`GroundSim.Tests/RenderSupportTests.cs`:

- `IdleSimulation_ProducesZeroDirtyCells` — the item-6 test: ticks with no active
  particles produce **zero** dirty cells; directly protects the perf property.
- `FallingParticle_MarksOnlyItsOwnCells` — one particle falling one cell dirties
  exactly its old + new cell, nothing else.
- `GridWrites_AreMarkedViaCellChanged` — settles/digs flow into the dirty set.
- `TickClock` ×3 — rate correctness (~30 ticks over 1 s of 60 fps frames),
  fractional accumulation (no truncation), pause + stall-cap behavior.

## 8. Item 7 (Phase 2 test-rigor note) — resolved

Confirmed the reviewer's analysis: a shared seed does **not** make dirt/rock runs
RNG-identical, because `LooseRock` rolls `RockSlideChance` on every blocked tick in
addition to the tie-break roll dirt uses — the streams diverge regardless of seed.
Rather than imply false isolation, `RockPile_IsMeasurablySteeperAndNarrower_ThanDirtPile`
is now a `[Theory]` over seeds {3, 7, 11}, asserting narrower + taller for each.
All three pass. The in-code comment documents the RNG-consumption caveat for
whoever next changes `RockSlideChance` or tie-break logic.

## 9. Unspecified Decisions Made (flag for course-correction)

1. **Demo world 200×120 at 5 px/cell** (1000×600 window) — arbitrary comfortable
   size; the renderer takes any grid dimensions.
2. **Default 30 tps**, range 2–240 via keys — chosen so individual particle falls
   are watchable at default speed.
3. **Stall handling drops excess ticks** (slow-motion catch-up) instead of owing
   them — prioritizes watchability over wall-clock sim fidelity; revisit if Arc 2
   ever needs deterministic realtime replay.
4. **Scripted demo digs directly** (`DemoScript` streams digs/drops a few ticks
   apart) instead of using `TestAgent`, whose `DigCarryDrop` blocks on
   `RunUntilSettled` — fine headless, but it would freeze animation. **Phase 4
   note:** agent AI needs a non-blocking act-per-tick pattern, not
   `RunUntilSettled` inside an action.
5. **One test correction:** the first TickClock accumulation test expected a tick
   after exactly 6 × (1/60 s) frames at 10 tps; 1/60 isn't binary-exact so the sum
   is fractionally under 1.0 and the tick correctly arrives on frame 7. The test
   expectation was fixed (documented in-code); the clock was right.

## 10. Suggested Phase 4 Considerations

- Agent actions must be per-tick state machines (dig this tick, walk next ticks,
  drop later) — see §9.4.
- The `DirtyTracker` before/after `MarkParticles` calls are the renderer's
  responsibility; a Phase 4 game loop wrapper could own that pairing to avoid
  each caller re-implementing it.
- If more overlay layers appear (pheromones, agent paths), consider layering the
  bitmap rather than multiplying overlay draws into the cell loop.
