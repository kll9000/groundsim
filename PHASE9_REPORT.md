# GroundSim — Phase 9 Report (terrain-following movement + resource sustain)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — builds clean, 62/62 tests passing, live scenario verified headlessly
**Prerequisite note:** the parallel independent verification of Phase 8 has not sent
anything my way this session; if it surfaces findings they should be reconciled
against this phase's changes too.

> **⚠️ Constants still invented**, including this phase's new
> `NodeRegenPerTick = 0.02`. Same placeholder status as everything else pending
> `game.js`.

---

## Part A — terrain-following movement

### The support rule (with two flagged deviations from the handoff's sketch)

`Terrain` (core) defines the distinction the handoff asked for:

- `IsSurfaceOpen`: Air under an unbroken air column to the top of the grid.
  Per-query column scan, invoked per agent per tick — **no O(grid) cost**
  (and it's the *last* check tried; the O(1) contact checks below short-circuit
  almost every call).
- `IsSupported`: grid bottom, solid below, **solid side-neighbor (wall-cling)**,
  **solid diagonal-down neighbor (corner-cling)**, or enclosed (any roof above →
  free climbing exactly as before Phase 4-style).

**Deviation 1 — wall-cling:** the handoff's strict "surface-open needs solid
directly below" rule would strand every agent permanently: this project's rooms
and founding excavations are open pits under open sky (documented since Phase 4),
so pit interiors and shaft walls are "surface-open" — strictly applied, nobody
could climb out of their own excavation and founding deadlocks. Wall contact
keeps pits/shafts climbable while genuinely free air (no floor, no wall) falls.

**Deviation 2 — corner-cling:** discovered by the test suite failing en masse:
the cell at the *mouth* of a freshly dug 1-wide hole has air below and on both
sides — without diagonal-down contact, an agent digs its first cell, falls in,
and can never climb out (the founding queen traps herself in dig #1). Real ants
climb over a lip; diagonal-down contact models that.

### Pathfinding interaction — which approach and why

The handoff offered "support-aware paths" vs "fall-correction only." I
implemented **both**, because fall-correction-only livelocks: support-blind A*
deterministically re-proposes the same unsupported route (e.g. the home pit's
center column), so the agent steps up, falls back, and replans identically
forever. So:

- `Pathfinder.FindPath` now only traverses **supported** Air cells (paths hug
  floors, walls, tunnel surfaces — visually ant-like), and requires a supported
  goal.
- Per-tick fall correction stays in both movement systems (`Agent`,
  `PathWalker`) as the safety net for terrain changing underfoot, plus a
  "path desynced by a fall → replan, never teleport" guard.

### A third real bug this surfaced: the severed-route deadlock

The new sustain test froze at a real deadlock (probed to root cause, same
discipline as Phase 7's claim leak): digging the Nursery's first cells removes
the wall contact that made the home pit's east-wall route climbable, and an open
pit mouth cannot be crossed overhead (free air) — so **no path to the spoil
column exists**, both diggers stand carrying dirt forever, and the digging that
would re-open a route can't continue because carriers won't dig.

Fix: **emergency dump** — after 3 consecutive failed drop-path plans, an agent
drops its carried material where it stands (a normal conserved particle; worst
case it lands back in the dig region and is re-dug later). Verified: the frozen
scenario now completes the Nursery within ~1,000 ticks of triggering and
gathering resumes indefinitely.

### What didn't change

Tunnel/enclosed movement (regression-tested), the one-unit-of-work-per-tick
contract (falls are one cell per tick), and all conservation/role-purity
invariants (their tests pass unmodified).

## Part B — resource sustainability

**Chose option 1: regeneration** (no new-node spawning). Reasoning: it keeps
existing node positions meaningful, needs no placement-validity rules, and is
the smallest change that makes the world stop running dry. `ResourceNode` gains
`Cap` (its initial amount) and `Regenerate`; `Colony.Tick` regenerates all
nodes at `NodeRegenPerTick` (0.02 default, invented). The app scenario now uses
800-cap nodes so patches visibly deplete and regrow.

## Measured results

Colony smoke (window scenario, headless): stage Expansion by tick 8,000,
**0.028 ms/tick**, max dirty 15 cells/frame (**0.06%** of grid). End-to-end
10-seed medians under the new movement (INVENTED constants): first worker
3,087 · garden excavated 4,446 · nursery excavated 7,734 (nursery ~1,000 ticks
later than Phase 7's 6,658 — consistent with occasional emergency dumps being
re-dug; expected, not a regression).

## Test suite (62 tests, all passing)

```
Passed!  - Failed:     0, Passed:    62, Skipped:     0, Total:    62, Duration: 480 ms - GroundSim.Tests.dll (net10.0)
```

9 new tests: terrain rule classification (incl. the wall-cling deviation and
open-pit shaft climbability), one-cell-per-tick falling, enclosed-climb
regression, tunnel→surface transition without sticking, a 12,000-tick colony
run asserting no worker floats unsupported for 150 consecutive ticks, a
measured performance gate (8,000-tick colony run < 5 s; measured ~1 s),
node-regen-to-cap-and-no-further, and the long-run no-flatline gather test.

**Existing tests updated (called out per the handoff):**
1. `FindPath_ReturnsValidAdjacentAirOnlyPath` → rebuilt on a floor (its all-Air
   world with an aerial goal is *deliberately* illegal now); same wall-gap
   intent, now also asserts every step is supported.
2. `Tenders_ActuallyProcessInsideTheGarden` + end-to-end: processing site is
   now the Garden's **floor** center — with terrain-following, mid-air room
   centers are cells agents fall out of, so all fixed colony sites
   (`HomeCenter`, processing, queen's settle position, egg placement) moved to
   room floors.
3. `Forager_GathersAndHaulsRawHome`: explicitly sets `NodeRegenPerTick = 0` —
   its exact conservation equation requires depletion to have gathering as its
   only cause.

## Flagged decisions (summary)

1. Wall-cling + corner-cling amendments to the support rule (Part A above) —
   the biggest judgment calls of the phase; both exist because the strict rule
   is incompatible with open-pit excavation, and both are documented in
   `Terrain`'s comments and pinned by tests.
2. Support-aware pathfinding *plus* per-tick falls, not either alone.
3. Emergency dump after 3 failed drop plans (deadlock class fix).
4. All fixed colony sites moved to room floors.
5. Regeneration over node-spawning for Part B; scenario node caps 800.
6. Two small read-only introspection accessors (`DigAssist.ActiveAgent`,
   `Forager.DigAgent`) added during deadlock diagnosis and kept for future
   debugging — no behavior reads them.
