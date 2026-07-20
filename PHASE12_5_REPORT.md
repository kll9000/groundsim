# GroundSim — Phase 12.5 Report (Garden-abandonment fix + test hardening)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 89/89 tests passing; the Phase 7 guarantee is strictly
restored and measured at **10/10 seeds, 95–100% of processing in the Garden,
zero unusable sampled instants** across ~5,000 checks.

---

## Item 1 — the decision, stated plainly: NOT acceptable, and it wasn't burial

**Decision:** a room becoming permanently unusable is a regression against the
Phase 7 guarantee, not acceptable behavior. But probing before deciding
overturned the diagnosis itself: the three bad seeds were **not** spill-buried
gardens — they were gardens **born dead**. Instrumented evidence: in seeds
3/4/7 the garden had *zero air cells from the moment it was marked excavated*,
0% in-garden processing for the entire run, "unusable" from the very first
sample. Three distinct root causes were then isolated and fixed:

1. **Completion race:** a just-planned site whose single air junction was
   transiently covered by spill had no frontier for one tick → marked
   "excavated" with zero cells dug. Fix: completion now also requires the site
   to be substantially open (`DigSite.AirFraction ≥ 0.3`); a frontier-less
   *closed* site simply stays active and resumes when maintenance re-opens the
   junction.
2. **Single-cell junctions:** the tunnel origin was the parent cell *nearest
   the chamber* — a deep corner (seed 4: literally the Queen's seat), whose
   sealing orphaned the entire 84-cell site. Fix: origins are chosen from
   currently-**air** parent cells, the tunnel mouth is widened to a multi-cell
   junction, and plans are rejected unless ≥2 diggable tunnel cells touch
   parent air at plan time.
3. **Fallback anchored on rock:** the fallback rect glued to the parent's
   bounding box (which can touch no actual chamber cell), and after the first
   correction, under a floor cell that sat directly on Rock (seed 3's map:
   the one junction cell was `@`). Fix: the anchor is now a parent air cell
   whose below-neighbor is verified *diggable*.

The Home fallback in the processing provider **stays** as tracked
defense-in-depth: it now increments `Stats.ProcessingFellBackToHome`, and the
e2e accounts for burial explicitly (below) instead of excusing it.

**Measured after the fixes (10 seeds × 60k ticks):** every seed ends with a
usable garden, 0 unusable samples out of ~500 per seed, in-garden processing
95–100% (previously seeds 3/4/7: 0%).

## Item 1's test accounting — conditional removed

The e2e escape-hatch conditional is gone. It now asserts, per seed: the
processing site is an air cell **inside the garden**; the garden has a usable
floor cell at end; and — the explicit accounting — the garden was unusable at
**≤10% of sampled instants** during the run (sampled every 200 ticks), so any
future regression toward abandonment fails loudly rather than being silently
excused.

## Item 2 — founding-completeness assertion restored

`OrganicFounding_QueenCompletes_AndPhase6GuaranteesHold` now asserts
`!colony.Rooms[0].HasRemainingDiggable(grid)` on transition to `Laying` — the
project's own completion definition (frontier-accessible, rock-pocket-
tolerant), confirming the founding dig actually finished, not just that the
state flipped.

## Item 3 — founding-fallback test hardened

The Phase 11.5 pattern applied: `diggableBefore >= 20` pinned before founding
runs (the fallback chamber must hold real undug material), `diggableBefore >
diggableAfter` after completion (material genuinely removed), plus the
completion-definition assert. A future carve-depth or geometry shift can no
longer silently degenerate this into a nothing-to-dig test.

## Item 4 — scope departures, named plainly

Acknowledged as standing practice going forward. For the record, **this phase
also touches Garden/Nursery planning logic** (`OrganicPlanner.Plan`: origin
selection, junction widening, junction validation; the fallback anchor;
`ManageExcavation`'s completion gate) — the handoff's out-of-scope list
protects the blacklist, the 70% reachability threshold, and the mound
distribution, none of which changed; the junction/completion changes are the
fix item 1 demanded and are called out here rather than left to a diff-reader.

## Test output (89 tests, all passing)

```
Passed!  - Failed:     0, Passed:    89, Skipped:     0, Total:    89, Duration: 2 s - GroundSim.Tests.dll (net10.0)
```

Same count as Phase 12 — this phase strengthened assertions inside existing
tests (e2e strict + accounting, founding completeness, fallback material pins)
rather than adding new test methods. Ready for the independent verification
pass the handoff calls for.
