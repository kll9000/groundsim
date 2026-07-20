# GroundSim — Phase 12 Report (organic Home Room + mound-around-shaft spoil)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 89/89 tests passing, app verified running, layout verified
against the mockup (ASCII below). **Four real bugs found and fixed during the phase**
— this turned out to be much more than a shape change, exactly the kind of thing the
handoff's "flag it as such if it needs more than anticipated" clause anticipated.

---

## Part A — organic founding

**Shaft-vs-tunnel choice (reasoning requested):** reused `MaskGenerator.Tunnel`
parameterized, not a new generator — jitter 0.03 / max deviation 0.08 rad
(≈1.7°/4.6°) vs the lateral corridors' 0.15/0.55. One algorithm, two personalities;
the shaft-shape test pins the difference (per-row width ≤3, total wander ≤4 columns
over 10+ rows). Chamber: the existing CA-blob generator at a smaller footprint
(`HomeChamberMin/MaxArea` 25–35 — the Queen digs alone). `PlanFounding` follows
Phase 11's planner pattern: 6 shrinking attempts, then the guaranteed rect fallback
(tested: fallback engages and founding still completes). The Queen's state machine is
untouched — she now takes a `DigSite` + settle point, lazily composes her agent, and
completes via the same frontier-accessible rule (Phase 9.5b semantics). All Phase 6
guarantees re-verified by test, including on the three seeds that exposed bug #4 below.

## Part B — the mound

`SpoilDropX` (single fixed column) → `NextSpoilDropX()`: deliveries alternate sides
of the entrance at `(opening half-width + 1 + rand(0..5))` columns, colony-wide —
founding Queen, room crews, and Majors all feed the same mound (confirmed: room
spoil converges on the entrance mound, no separate exits). The fixed column
survives as a nullable test override. Physics untouched — `Simulation`/`Particle`
unchanged, as required.

## The four bugs (all measured, all fixed, all now tested)

1. **Mound-drain equilibrium.** Fixed near-shaft drops let the inner slope drain
   back down the shaft as fast as crews dug — garden remaining-count oscillated
   51–58 forever. Fix: the adaptive spread the handoff hinted at — columns already
   piled `MoundMaxHeight` (4) above the original surface are skipped outward, so
   the mound widens instead of feeding the hole.
2. **Re-plugged arteries.** Spill refilling the shaft or a completed room's tunnel
   belonged to no dig site — the colony's passages sealed permanently (measured
   twice: shaft, then the garden tunnel, which froze processing at farmed=67
   forever). Fix: every completed excavation joins `MaintenanceSites`, and
   maintenance preempts room digs in `ManageExcavation`. Also: room work-sites
   (processing, egg cells) are now **live-computed** (`Room.FloorSite`) with
   home-fallback resilience, because spill can bury any fixed cell.
3. **Rock-severed plans.** A rock band across the tunnel let the frontier-accessible
   completion rule declare the garden "done" with **zero chamber cells dug**
   (sealed behind rock — `siteRock=18`, every chamber cell air-adjacent to nothing).
   Rock is known at plan time, so `OrganicPlanner` (rooms *and* founding) now
   validates ≥70% of the chamber is reachable from the origin through non-Rock site
   cells before accepting a mask; seed 1's rocky depths correctly fall back to a
   rect garden and the economy flows.
4. **Unreachable-nearest livelock.** Deterministic nearest-first target selection
   re-picked the same unpathable cell (a sealed air pocket in partially-refilled
   ground) forever — 3 of 10 founding seeds stranded the Queen on the mound.
   Fix in `Agent`: failed-plan targets go on a blacklist (skipped by the scan),
   cleared on any successful dig or full exhaustion. All 10 seeds now found in
   3,946–7,573 ticks.

   Plus the **chimney**: the 3-wide column above the entrance joins the founding/
   maintenance site, so spill capping the mouth (cells above the original surface,
   previously in *no* site) gets cleared — which is also what keeps the entrance
   hole open through the growing mound.

## What it looks like (seed 6, 30,000 ticks — compare to the mockup)

```
..............#######...........#######.....................
.............#########.........##########...................
............###########.......############..................
..........##############.....##############.................
#########################...################################  ← symmetric mound,
#########################...################################    entrance kept open
#########################...################################
#########################...################################
######@#######@##########...############@#@#################  ← near-vertical shaft
#######@#################...########@#######################
##############@##@######......###@#############@#######@##@#  ← chamber cluster
#@###@##################.....#########@@#@@##@####@#########
##################@##@#@..@..###########@###################
###############@#@########.....####@###@#@#@########@@######
#@#@####@##########@####@#........@###@####@##############@#
########@####@###@@#####@@@####....@####@####....@####@#####  ← garden + nursery
#####@###@##@####################....@.####@......@#####@###    lobes at depth
```

Two mound wings rise ~4 cells on each side of the opening; the shaft drops
near-vertically to a lumpy home chamber (queen at its floor), with the garden and
nursery as separate lobes below — the ant-hill cross-section from the mockup.

## Tests (89, all passing) and measurements

```
Passed!  - Failed:     0, Passed:    89, Skipped:     0, Total:    89, Duration: 2 s - GroundSim.Tests.dll (net10.0)
```

+6 new: founding shape (connectivity, narrow/straight shaft vs lateral tunnels,
chamber at depth), organic-founding Phase-6-guarantee regression ×3 seeds (the
former livelock seeds), founding fallback engages-and-completes, spoil-on-both-
sides distribution (≥20% each side, ≥8 columns, no column holding half). Existing
tests updated: e2e/pins/trajectory audits migrated to organic founding (the product
path; the rect overload remains covered by the Phase 6 Queen tests as the fallback
shape), e2e bound 40k→60k and its processing-site assert now recognizes the
resilience fallback. The 9.5b-lineage full-grid sweep runs against the new founding
geometry and stays clean; new constants (`Shaft*`, `HomeChamber*`, `MoundDropRange`,
`MoundMaxHeight`) are INVENTED like everything else.

E2E medians (10 seeds, organic founding + mound): first worker 5,917 · garden
9,244 · nursery 11,339 — later than Phase 11 (founding is ~80 cells dug by one
queen vs 36, plus real spill churn), pacing still placeholder pending `game.js`.
