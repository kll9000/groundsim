# GroundSim — Phase 13-DF Report (dirt friction / slide chance)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 94/94 tests passing; the mound now builds real height.

> **⚠️ Numbering collision, flagged plainly:** this handoff renumbers itself
> Phase 13 and defers rock-mining/chambers/resolution to Phase 14 — but that
> work had already landed and been pushed as Phase 13 (`2be85a1`,
> PHASE13_REPORT.md) before this handoff arrived; the handoffs reached me out
> of order. Nothing was renumbered retroactively (pushed history stays);
> this phase is labeled **13-DF** and lands *after* the rock/chamber/
> resolution work. The PM's "land friction first" rationale was about
> re-verifying dependents — that concern is fully covered, since every
> dependent check below was re-run against the *newer* base (friction + rock
> mining + big chambers together), which is strictly the stronger test.

---

## The change

`Simulation.DirtSlideChance = 0.6` — a blocked Dirt particle attempts the
diagonal slide with probability 0.6, else settles in place, using the exact
branching pattern `RockSlideChance` (0.25) has had since Phase 2. **Value
reasoning:** deliberately between rock's 0.25 (dirt is looser than rock
chunks — the rock-vs-dirt relative shape contract must keep holding) and the
old effective 1.0 (frictionless "slippery balls"); 0.6 measurably builds
height while still forming natural slopes. INVENTED constant, flagged like
everything pending `game.js`. Global physics, applied everywhere — no
surface-only scoping, per the handoff. Rock's chance and all Phase 9/11
movement rules untouched.

## The checks the handoff required

- **Phase 2 pile-shape tests:** pass *unmodified* — rock is still narrower
  and steeper than dirt in relative terms across all three theory seeds. One
  Phase 1 test updated (flagged in-code): the single-particle "slides when
  blocked" test now drops several particles, since one particle legitimately
  settling atop the obstacle is correct friction behavior; the slide
  *mechanic* is still asserted.
- **Mound shape:** transformed. Before friction the peak was pinned at
  Phase 12's `MoundMaxHeight = 4` cap — which had been tuned *for
  frictionless dirt* as the anti-shaft-drainage measure. With friction
  holding slopes, that tuned value (not logic) was raised **4 → 7**, and
  re-measurement confirms the shaft stays clear. The mound now shows two
  sloped wings rising 7 cells around a kept-open entrance, rubble speckled
  through (ASCII below).
- **Phase 12.5 garden measurement, re-run (10 seeds × 60k ticks):**
  **zero unusable samples on all 10 seeds and `ProcessingFellBackToHome = 0`
  everywhere** — the defense-in-depth fallback never even fires now.
  In-garden processing 89–99%. On connecting the observations: Kevin's
  "spoil sliding back into the dig" was the same *physical* mechanism that
  drove 12.5's churn, but the 12.5 planning bugs (completion race, one-cell
  junctions) were logic faults friction couldn't have fixed; friction
  reduces the *inflow* those fixes then handle. Distinguishable and
  complementary — both were needed.
- **Bonus, measured:** excavation got *faster* — smoke milestones improved
  (founding 8,205 → 4,072; garden 14,564 → 9,767) because less spoil drains
  back into holes, so far less re-digging churn.
- **Performance:** the friction branch is one `NextDouble()` comparison in
  the already-existing per-particle blocked path — the same cost class as
  rock's existing check. Smoke: 0.254 ms/tick at 40k ticks with 131 workers.

## What the mound looks like now (seed 6, 35,000 ticks)

```
.....#o###o#o#o#o#o####o.....#o#o######o#######o......   ← peak 7 cells,
....o#o######o#####o###o.....######oo######o#o#o......     two sloped wings
....#o###oo####o#o######.....##o##############o#......
...########o#o##########.....############oo#o####.....
...#####oo#####o#####o###....########o###########o....
..#oo####################....######################...
.########################....#########o#####o#o####...
##########################...###############################  ← entrance kept open
```

Previously (frictionless, cap 4): a flat 4-cell embankment that could only
spread. Now: a real ant-hill profile with height, slope, and the entrance
crater — the visual Kevin asked for.

## Tests (94, all passing)

```
Passed!  - Failed:     0, Passed:    94, Skipped:     0, Total:    94, Duration: 6 s - GroundSim.Tests.dll (net10.0)
```

+2 new (`DirtFrictionTests`): a 400-trial statistical test pinning the slide
rate to `DirtSlideChance` ± 0.08 (the pattern the handoff asked for), and a
3-seed comparative pile test asserting dirt now builds height ≥ 4 while
staying at-least-as-wide and no-taller than rock. All other suites —
including Phase 12's mound-distribution test and the 12.5-hardened e2e with
its burial accounting — pass without modification.
