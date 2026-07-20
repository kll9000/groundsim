# GroundSim — Phase 13 Report (rock mining, bigger chambers, finer resolution)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 92/92 tests passing, smoke reaches Expansion, window
verified running at the new resolution.

---

## Part A — rock is diggable

**The three design decisions, with reasoning:**

1. **Cost:** mining rock takes `Agent.RockDigTicks = 4` ticks of chipping per
   cell vs 1 for everything else (INVENTED constant, same status as all
   others). Implemented as multi-tick progress inside `Agent.TickDig`, so the
   one-unit-of-work-per-tick contract holds; `Grid.Dig()` itself stays a
   single mutation (its API is used directly by tests and physics scenarios).
2. **Caste:** **any digger can mine rock.** Restricting it to Majors would
   recreate the waits-on-a-caste stall class this project has already paid
   for twice (rooms are a stage-4 phenomenon, Majors stage-5) — and Majors
   still speed excavation the way they always have, as extra diggers.
3. **Completion semantics (the key decision): rock now COUNTS.** Completion
   remains "no frontier-accessible diggable cell remains," and since nothing
   is undiggable anymore, that includes rock — rooms come out genuinely
   clean (Kevin's pockmark complaint), and the sealed-rock-pocket tolerance
   is obsolete. Dug terrain Rock converts to `LooseRock` rubble (the Phase 2
   material built for exactly this), so mined rock uses the existing
   loose-rock carry/drop/settle physics unchanged — and visibly ends up
   speckling the surface mound.

**The ⚠️ latent AirFraction stall: now unreachable by construction.** The
Verifier's scenario required a genuinely-complete site sitting above ~70%
Rock. Under the new semantics a complete site has cleared its rock too, so
its air fraction is ~1.0 — the `≥ 0.3` gate is trivially satisfied at every
true completion, while still blocking the born-dead race it was built for
(a spill-sealed never-started site has air fraction ~0). No persistence-check
replacement needed; re-checked rather than assumed.

**The 70% reachability threshold, re-examined as instructed:** with rock
passable, `ReachableChamberFraction` no longer measures rock-sealing — it
degenerates to a pure mask-connectivity guard (rejects malformed/clipped
masks). Kept for that purpose, documented as such. The junction validation
and fallback-anchor checks similarly dropped their rock exclusions.

**Conservation invariant updated:** digging rock transforms material
(Rock → LooseRock) but never destroys a cell, so the conserved quantity in
the Phase 4/6 conservation tests is now *all solid cells + carried +
in-flight* — a cleaner invariant than the old rock-excluding count. Two
Phase 1/2 tests updated for the deliberate behavior change (dig-rock now
returns rubble), both flagged in-code.

**Performance:** no new per-tick cost — chipping is a counter inside the
existing dig cycle. Smoke: 0.222 ms/tick at 40k ticks with 118 workers.

## Part B — bigger chambers

`ChamberMinArea/MaxArea`: 80–130 (was 40–70); `HomeChamberMin/MaxArea`: 40–55
(was 25–35) — sized to read as real rooms at the doubled resolution.
Downstream effects handled: one planner test's blocker was resized (the old
blocker made organic placement geometrically impossible in the small test
world — the test's point is avoidance, not impossibility); the founding-shape
test's chamber bound widened; the e2e's existing 60k budget absorbed the
slower digs (verified, no change needed); the smoke run was extended
8k → 40k ticks since founding alone now takes ~8k in the app world. New
10-seed medians (INVENTED constants): first worker 6,961 · garden 13,290 ·
nursery 17,287.

## Part C — doubled render resolution

`GridRenderer.CellSize` 5 → **2** px (exact halving of an odd size is
impossible in integer pixels; 2 is slightly finer than double — flagged).
Confirmed a pure rendering change: `Grid.Width/Height` and the scenario
world are untouched. Camera rechecked point by point: all camera math is in
world-bitmap pixels derived from `CellSize` (follow centers, hit-test radius
`max(2 cells, 10px/zoom)`, initial centering) so it scales automatically;
`MaxZoom` raised 8 → 20 so the deepest zoom still reaches ~40 px/cell
(matching the old 5×8 ceiling). One artifact found and fixed: the egg
marker's 1px inset collapses to a zero-size rect at CellSize 2 — the inset
now only applies when cells are ≥4 px. Headless smoke exercises the full
pipeline at the new size (max dirty 105 cells/frame, 0.44% of grid).

## What it looks like together (seed 6, 35,000 ticks)

```
o#ooo##o##o#oo##ooo#########o.....o######oo######o#o###oo#oo##o
##o#####ooo#o###o############.....o#################o#######o##   ← mound with mined
#############################o....o####################o#######     rock chunks (o)
###############################...#############################
###########@#######@###########...###########@#@##############   ← shaft
##########@####@####@#@#####...#......####@####@########@@####
####################@#@####....####....#@###@#@#@########@@###
######@#@####@######..##@#...@######....###@####@########@####
@##@###@##@#######......#...#####@##......@####@####@#####@###
#############@####.........##@@@####........#@########@####@##
##########@###@#..........#########...........##@######@#####@   ← two big clean
#####@##@#@##@#@...........########...........#######@@##@####     chambers, no
######@##@#####............#####@#.............#@###@#########     rock inside
```

The mound is speckled with mined-rock rubble, the shaft runs clean, and the
chambers are visibly larger with rock-free interiors — every `@` in view is
untouched surrounding terrain, none inside a room. In the live window at
CellSize 2 the same scene renders at ~2.5× the former detail density; zoom
to 20× to watch individual ants chip through rock bands (4 ticks per cell).

## Test output (92 tests, all passing)

```
Passed!  - Failed:     0, Passed:    92, Skipped:     0, Total:    92, Duration: 6 s - GroundSim.Tests.dll (net10.0)
```

+3 new (`RockMiningTests`): mined rock behaves as LooseRock through the full
dig→carry→drop→settle pipeline; an agent takes ≥`RockDigTicks` ticks per rock
cell; an excavated garden contains zero terrain-Rock cells. Updated (all
in-code-flagged): 2 dig-rock behavior tests, 2 conservation counters (solid-
cell invariant), planner blocker resize, founding chamber bound, smoke tick
budget.
