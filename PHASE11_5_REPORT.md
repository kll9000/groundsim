# GroundSim — Phase 11.5 Report (rebuild the safety net + two test fixes)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 83/83 tests passing. Test-integrity work only: zero
production-code changes (the one non-test file touched is nothing — all changes
are in `GroundSim.Tests`). `IsSupported`'s unification stays, per the handoff.

---

## Item 1 — the safety net, rebuilt (Option A **and** Option B)

I did both halves, because they answer different needs:

### Option A: the new independent net — trajectory plausibility

`TrajectoryAuditor` (in `TrajectoryAuditTests.cs`) judges the **physical
plausibility of observed agent movement over time**, a categorically different
principle from comparing two grid predicates:

- **No teleports:** every per-tick transition moves ≤1 cell on each axis.
- **Airborne ⇒ falling:** an agent that starts a tick in fully-open air (direct
  grid reads: all 8 neighbors Air, not bottom row) must move exactly (0,+1) —
  never hover, never sidestep, never climb.

Two colony-scale audit runs (seeds 4 and 9, 9,000 ticks each, every worker
transition judged against the grid state captured *before* each tick): **zero
violations**.

**Showing the work on independence, as required:** the requirement is that the
new check can fail where the predicate comparison cannot. The demonstration is
concrete, not argued from code-reading: the failure domains are *disjoint by
construction* — the pins compare two static functions; the auditor constrains
observed dynamics. The canonical scenario: **if fall-application were removed
from movement code tomorrow, both predicates would be unchanged (pins pass) while
agents hover in open air (auditor fails)** — and that exact scenario is a unit
test (`Auditor_Flags_Hover_TheFailureMode_PredicatePinsCannotSee`), alongside
tests proving the auditor flags teleports, lateral/upward airborne motion, and
accepts legitimate walking/falling/climbing/idling. The auditor also cannot be
an accidental complement of `IsSupported`: it doesn't map cells to booleans at
all — its inputs are (state-before, transition) pairs.

### Option B: the old tests, honestly relabeled

`RuleVsOracleAuditTests.cs` → `SupportRulePinTests.cs`, class
`SupportRuleImplementationPinTests`, with a prominent comment stating exactly
what the Verifier proved: since the Phase 11 unification, `IsSupported` and
`IsVisiblyFloating` are exact complements (verified over 16,078 air cells), so
these tests are **regression pins on the two implementations staying in
lockstep** — they catch an accidental edit to either (the source stays
deliberately separate), and can no longer verify correctness. Assertion messages
now say "fell out of lockstep," not "false-support shape exists." The
Phase 9.5-era colony floating test kept its behavioral value (agents don't
persist in open-air cells) and got the same honest caveat in its comment.

## Item 2 — irregularity metric rebased on a same-area disc

Reproduced the Verifier's finding with data before changing anything: perfect
digital discs at chamber areas score **0.052–0.076** radial deviation (the old
absolute 0.08 threshold sat *inside* pixelation noise), and blob-vs-disc ratios
across 12 seeds measured **1.17–1.97, mean ≈1.6**. The test now asserts the real
claim — "less circular than a circle of the same size": each blob must exceed
its same-area disc's score by ≥1.05×, and the 12-seed mean ratio must exceed
1.35 (headroom below the measured minimum/mean respectively; ratio rather than
additive gap because pixelation noise scales both scores together at a given
area). Helpers live in `ShapeMetrics.cs`.

## Item 3 — the fallback test now digs for real

The old scenario carved *all* cone-reachable ground, so the fallback rect was
born complete — "no stall with nothing to dig." New scenario: an air band from
y=36 down defeats every organic attempt (chamber targets clamp to y≥36 and fail
the ≥90% fresh-ground check deterministically), while **rows 34–35 — where the
fallback rect glues below the Home Room — remain real, undug dirt**. The test
verifies: the planner exhausts retries and falls back; the fallback site
contains ≥8 genuinely diggable cells; diggers excavate it to completion within
8,000 ticks; material count measurably decreased; `ActiveDigSite` clears. That
is Part C's actual guarantee — no stall *while genuinely excavating a degraded
room*.

## Test output (83 tests, all passing)

```
Passed!  - Failed:     0, Passed:    83, Skipped:     0, Total:    83, Duration: 2 s - GroundSim.Tests.dll (net10.0)
```

76 → 83: +4 auditor self-tests, +2 colony trajectory audits (Theory ×2 seeds),
+1 multi-seed disc-baseline irregularity test; the sweep/occupancy tests were
relabeled in place, the single-seed chamber test and fallback test rewritten as
described. Ready for the independent verification pass the handoff says this
will get.
