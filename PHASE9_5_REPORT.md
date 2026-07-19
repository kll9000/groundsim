# GroundSim — Phase 9.5 Report (independent-oracle floating check)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 64/64 tests passing, no movement-rule changes made
**Scope discipline:** corner-cling/wall-cling untouched; the enclosed/roof rule
untouched, per the handoff. This phase changed diagnostics and test coverage only.

---

## Item 1 — does the false-support shape occur in real colony geometry?

**Answer: no occurrences found.** Evidence, both empirical and geometric:

**Empirical audit:** 5 seeds × 15,000 ticks of the full colony pipeline (real
founding, gathering, both room excavations), checking every worker every tick:

```
workers observed floating-by-oracle (tick-instances): 99
max CONSECUTIVE oracle-floating ticks for any worker: 6
rule-says-supported BUT oracle-says-floating instances: 0
```

Zero disagreements. The 99 oracle-floating instances (never more than 6
consecutive ticks) are agents **mid-fall** — cells the production rule *also*
calls unsupported, i.e. legitimate transient state while dropping to the ground,
not the bug shape.

**Geometric argument for why:** `IsSupported` checks contact first — the
enclosed/roof branch is only ever consulted for cells with *no* solid contact at
all. In this codebase's actual excavation shapes, that combination essentially
can't coexist with a roof: every room and founding chamber is an **open pit**
(the column above every dug cell is open to the sky), so contact-free interior
cells are surface-open and correctly fall. The only "roofs" that exist are
transient mid-dig shapes where a cell is dug sideways under a still-solid cell —
and there the solid cell is *directly overhead*, which is 3×3 contact (ceiling
cling), satisfying both rule and oracle. A contact-free cell under a *distant*
roof would require a tall enclosed cavern ≥3 cells wide, which nothing in
`PlanRoom` or the founding chamber can produce today.

**Disposition per the handoff:** documented as a **known theoretical edge case,
not an active bug** — pinned by a dedicated test
(`OracleAndProductionRule_DivergeOnDistantRoof_DocumentedEdgeCase`) that
reproduces the verifier's synthetic case (floor + overhang seven rows up →
`IsSupported` true, oracle floating) so the divergence is visible in the suite
and becomes the marker for re-opening this decision if room shapes ever gain
real roofs. No preemptive fix was made.

## Item 2 — the independent oracle

`Terrain.IsVisiblyFloating(grid, x, y)`: an Air cell with **no solid cell
anywhere in its 3×3 neighborhood** (grid edges count as walls; the world's
bottom row counts as ground). How it differs from `IsSupported` by design:

- It shares none of `IsSupported`'s reasoning — no column scans, no
  surface-open concept, no cling taxonomy. Pure local contact.
- It is deliberately *stricter about distance* (a roof seven rows up means
  nothing) and *looser about direction* (a ceiling directly overhead counts —
  an ant clinging under a one-cell ceiling is visibly attached, not floating).
- **It is used by zero movement code** — agents' fall/climb decisions are
  unchanged. It exists purely to audit.

Coverage changes:

1. `ColonyRun_NoWorkerEndsUpFloatingOverOpenSky` → replaced by
   `ColonyRun_NoWorkerEndsUpVisiblyFloating_ByIndependentOracle` — same
   persistent-floating window logic, but judged by the oracle, so it can now
   fail on a production-rule false-positive instead of only confirming the rule
   agrees with itself (the structural flaw the verification called out).
2. New `RuleVsOracleAuditTests` — the item-1 audit kept as a permanent
   regression gate: 3 seeds × 10,000 ticks, asserting **zero**
   supported-but-visibly-floating worker observations, with a failure message
   that names the enclosed/roof false-positive explicitly.
3. The documented-divergence test from item 1.

## Test suite (64 tests, all passing)

```
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 586 ms - GroundSim.Tests.dll (net10.0)
```

62 prior tests: 61 unmodified; 1 replaced as described above (same scenario and
window semantics, stronger judge). 2 net-new (divergence documentation, audit
gate). No changes to `Terrain.IsSupported`, `Pathfinder`, `Agent`, `PathWalker`,
or any colony logic — `IsVisiblyFloating` is the only production-code addition,
and nothing reads it outside tests.
