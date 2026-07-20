# GroundSim — Phase 9.5b Report (geometry-based regression gate)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 70/70 tests passing
**Scope:** test-only. Zero changes to `IsSupported`, `IsSurfaceOpen`,
`IsVisiblyFloating`, movement logic, or room geometry.

---

## What was added

`FalseSupportShape_ExistsNowhereInTheWorld_FullGridSweep` (in
`RuleVsOracleAuditTests`, alongside — not replacing — the agent-occupancy
test): 5 seeds × full colony runs (12,000 ticks each, through both room
excavations), sweeping **every cell in the grid** at checkpoints every 400
ticks (30 checkpoints per seed, matching the Verifier's own sweep scale) and
asserting that no cell anywhere is simultaneously visibly floating (no 3×3
contact, per the independent oracle) and claimed supported by the production
rule.

This closes the gap the handoff describes: the existing test could only catch
a worker *standing in* a bad cell; this one catches the bad shape *existing*,
walked-into or not. It is the designated safety net for Phase 11's organic
room/tunnel shapes, whose failure message names the enclosed/roof
false-support shape explicitly.

## Sweep result

**Zero disagreements** across all 5 seeds × 30 checkpoints × 7,200 cells
(~1.08M cell evaluations) — matching the Verifier's independent one-off sweep
for current open-pit geometry.

## Performance / placement confirmation (as the handoff requires)

- The sweep runs at **sparse checkpoints** (every 400 ticks), never per-tick.
- It exists **only in the test suite** — `Colony.Tick()` and every other
  production path are untouched; no production code gained any cost. The only
  file changed in this phase is the test file (plus this report).
- Cost in practice: the whole suite, sweep included, runs in ~1 s (the sweep
  itself is a fraction of that).

## Test output

```
Passed!  - Failed:     0, Passed:    70, Skipped:     0, Total:    70, Duration: 1 s - GroundSim.Tests.dll (net10.0)
```

69 prior tests unmodified; 1 added.
