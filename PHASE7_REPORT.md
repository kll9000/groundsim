# GroundSim — Phase 7 Handoff Report (rooms + end-to-end stages 1–4)

**Date:** 2026-07-18
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — builds clean, 52/52 tests passing, 10-seed end-to-end verified
**Prerequisite check:** Phase 6 base verified at 45/45 before starting.

> **⚠️ Constants still invented.** Per the open item in the handoff: every
> `ColonyConfig` number (including this phase's new `GardenTriggerThreshold = 30`
> and `NurseryBroodPressureThreshold = 25 000`) is an invented placeholder —
> `game.js` remains unretrieved. The milestone timings in §5 are *measured*, but
> measured **on placeholders**: treat them as pipeline-works evidence, not pacing
> truth.

---

## 1. `Room` as a first-class type (item 1)

`Room` (type tag, inclusive rect, `Excavated` flag, `Contains`, `Center`) with
`RoomType { Home, Garden, Nursery }` — Waste/Pupa stay deferred.
`Colony.HomeRoom`/`HomeCenter` now delegate to `Rooms[0]`, so all Phase 6 call
sites work unchanged. One deviation from the handoff's sketch, flagged: `Room`
is a small mutable **class**, not a `record`, because `Excavated` flips in place
when digging completes.

Excavation-completion rule: a room is complete when **no diggable cell remains**
— terrain Rock inside the rect is tolerated as a natural pillar rather than
blocking completion forever.

## 2. Room triggers (item 2)

- **Garden:** fires the tick `FarmedResource >= GardenTriggerThreshold` (tested
  to not fire at threshold − ε over 1,000 ticks, and to fire on the very next
  tick at the threshold).
- **Nursery:** a **brood-pressure integral**, as the handoff steered:
  `BroodPressure += Eggs.Count` every post-founding tick, triggering at a
  threshold. The mechanism test holds egg count constant at 5 and asserts the
  trigger fires within [1000, 1005] ticks at threshold 5000 — i.e. exactly
  `threshold / count`, which an instantaneous "N eggs now" check could not
  reproduce (it would fire immediately or never).
- On trigger, the planned room becomes a real dig job via the existing
  `Colony.ActiveDigSite` — no second excavation pathway. One site digs at a
  time; a second triggered room queues until the first completes.

**Who digs (flagged decision):** rooms appear in stage 4 but Majors are a
stage-5 phenomenon, so baseline excavation must not depend on them. Excavation
is **communal**: up to `WorkerDiggers = 2` idle Foragers are assigned to assist
via `DigAssist` (the same composed-Agent machinery Majors use; Major itself was
refactored onto it, behavior unchanged). Tenders never dig — they keep
processing/tending. My reading: digging is communal colony work, not a
caste-exclusive job, so the gather/process purity invariants are untouched — the
Phase 6 purity tests all still pass unmodified.

## 3. Behavior relocation (item 3)

- **Processing → Garden:** on Garden excavation, `OnRoomExcavated` repoints
  `ProcessingSiteProvider` at the Garden's center. **Zero Tender code changes
  were needed** — the Phase 6 seam worked as designed. The relocation test
  observes a Tender physically standing inside the Garden with processing
  completing there (position-verified, not just "the provider returns a new
  value").
- **Eggs → Nursery:** `LayEgg` targets the Nursery's cells once it's excavated;
  before that, Home Room behavior is exactly Phase 6's (the
  "before-the-room-exists = prior phase's behavior" principle). Tested: five
  post-Nursery eggs, all inside the Nursery rect.

## 4. A real bug found and fixed: leaked dig claims

The first 10-seed run failed on seed 2: the Nursery sat one cell short of
completion forever. Probing showed `DigClaims` held a stale entry — when digger
assignment shifts between Foragers (haul states reorder who's idle), the
displaced Forager's internal `Agent` was discarded while still holding a claimed
dig target. The claim leaked, permanently blocking that cell for every digger.

Fix: `Agent.ReleaseClaims()` (releases the held claim), called by `DigAssist`
whenever it discards an agent on stand-down or site change
([Agent.cs](GroundSim/Agent.cs), [DigAssist.cs](GroundSim/Colony/DigAssist.cs)).
This was a genuine latent bug in the Phase 6 Major pattern too (site change
leaked the same way) — surfaced only under Phase 7's realistic assignment churn.
Exactly what the spread-of-runs discipline is for.

## 5. End-to-end: stages 1–4 across 10 seeded runs (item 4)

Each seed runs one continuous headless sim from `Colony.Found` (real founding
excavation) with two surface resource nodes, asserting per stage: (1) queen
founds and lays; (2) a first worker matures; (3) the gather loop genuinely ran
(gathered > 0 AND processed > 0); (4) Garden and Nursery both trigger, excavate
for real, processing relocates to the Garden. All 10 seeds pass within the
40,000-tick bound. Measured milestone medians (ticks; **invented constants**):

```
first worker:      median 3067  (min 2977, max 4417)
garden excavated:  median 4536  (min 3982, max 5434)
nursery excavated: median 6658  (min 6515, max 6798)
```

## 6. Test suite (52 tests, all passing)

```
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52, Duration: 495 ms - GroundSim.Tests.dll (net10.0)
```

45 prior tests pass **unmodified**, plus 7 new in
`GroundSim.Tests/RoomAndStageTests.cs`: Room geometry; Garden threshold
exactness; Nursery integral mechanism; communal excavation completing a
triggered room and clearing the site; Tender-processes-inside-Garden
(position-observed); eggs-laid-in-Nursery; and the 10-seed end-to-end.

## 7. Unspecified decisions made (flags)

1. **Communal excavation, Foragers only** (§2) — the biggest one. If Colony
   Builder has a different baseline-digging model, this is the thing to correct.
2. **Room placement geometry (invented):** Garden 6×3 directly below the Home
   Room; Nursery 5×3 directly beside it — both share an open edge with existing
   Air so the dig frontier reaches them. No collision/overlap checking between
   planned rooms and terrain features beyond the Rock-pillar tolerance.
3. **One dig site at a time**, queued in trigger order.
4. **`Room` as mutable class** rather than the suggested record (§1).
5. **Nursery threshold 25 000 tick-eggs** — chosen so it fires after the Garden
   under the invented economy (~5,000–7,000 ticks in practice); it's shape-
   correct (integral), scale-invented.
6. **No trail/pheromone system** — Establishment verified without it (raw
   flows, farmed grows); it wasn't needed for correctness, so per the handoff it
   stayed out rather than being quietly added.

## 8. Arc 2 castes+rooms scope: closing statement

The scope this pass committed to is genuinely done: castes with verified role
purity (Phase 6), rooms as real excavated regions with trigger conditions and
behavior relocation, and stages 1–4 running end-to-end on real physics across a
spread of seeds — with the same no-overclaiming caveat throughout: **pacing
numbers are placeholders until `game.js` constants arrive** (Kevin's tracked
open item). Phase 8 (packaged, launchable .exe) is next; nothing in this phase
blocks it. Worth noting for Phase 8: the renderer demo still shows the Phase 4
quarry scenario — pointing it at a live `Colony` would make the packaged app
show the actual colony loop, which is probably what a double-clickable demo
should show.
