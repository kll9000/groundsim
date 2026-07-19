# GroundSim — Phase 4 Handoff Report (Arc 1 Capstone)

**Date:** 2026-07-18
**Branch:** `claude/groundsim-phase-1-setup-7e2e20`
**Status:** ✅ Complete — builds clean, 32/32 tests passing, live demo verified running
**Prerequisite check:** Phase 1+1.5+2+3 base was verified at 24/24 tests before starting.

---

## 1. Scope

Phase 4 replaced the blocking, teleporting `TestAgent` with real agents:

1. Non-blocking per-tick agent state machine (the Phase 3 §9.4 fix).
2. A* pathfinding over the grid, with re-planning when terrain changes mid-route.
3. Multiple concurrent agents with no grid corruption or material loss.
4. A capstone demo: agents quarry a pit and haul spoil to growing settled piles.
5. Colony-scale performance measurement (40 agents, 400×300 grid).

## 2. Agent State Machine (item 1)

`Agent` (core project) with `AgentState { Idle, PathingToDig, Digging,
PathingToDrop, Dropping }`. **Contract: `Tick()` performs at most one unit of
work** — one cell of movement, one dig, one drop, or one plan/replan — and never
loops to completion internally. `TestAgent` remains untouched for blocking
headless scenarios, per the handoff.

Cycle: Idle finds the nearest unclaimed diggable frontier cell (diggable = not
Air/Rock, touches Air) → claims it → paths to an adjacent Air cell → digs (one
tick) → paths to the drop column → drops (one tick, spawning a falling particle)
→ Idle. A workless agent re-scans its region only every 15 ticks (idle cooldown),
so idle agents cost ~nothing.

## 3. Pathfinding (item 2)

`Pathfinder.FindPath` — standard A*, 4-connected, Manhattan heuristic, over Air
cells (start cell exempt; the agent stands there). Pure core logic, no rendering
dependency. An expansion cap (default 50k) bounds worst-case cost of provably
unreachable goals; capped searches are treated as unreachable.

**Re-planning:** when the next path step is no longer Air (settled particle, pile
growth, another agent's dig), that tick is spent re-planning. The drop approach
cell is *recomputed at every plan* as "the Air cell above the drop column's
current surface" — so a growing pile naturally moves the drop point up. If no
path exists, the agent releases its claim and goes Idle (with cooldown), retrying
later.

Movement is one cell per tick, as the handoff suggested.

## 4. Concurrent Agents (item 3)

- **Claims set** (shared `HashSet`) prevents two agents targeting the same cell;
  even without it, `Grid.Dig` returns the material exactly once, so double-digging
  cannot duplicate material — the second digger just gets null and re-idles.
- **Agents are positions, not matter:** they never occupy grid cells, don't
  collide with each other (explicitly out of scope), and a particle may settle
  into an agent's cell — the agent then pushes up one cell per tick until back in
  Air (same spirit as Phase 2's particle bump-up).
- Conservation test: 8 agents, 4,000 ticks — settled + carried + in-flight
  exactly equals the initial diggable-cell count.

## 5. Capstone Demo (item 4)

`dotnet run --project GroundSim.Render`: 200×120 world, 8 agents quarrying a
31-cell-wide pit below the surface (with loose rock and sticks scattered in the
dig region so spoil is mixed-material), hauling to drop columns flanking the
site. Red = empty-handed agent, orange = carrying, yellow = falling particle;
piles visibly grow and settle in real time. Status bar adds agent/carrying
counts. No new UI beyond that.

## 6. Performance at Colony Scale (item 5) — measured numbers

Headless colony smoke (`dotnet run --project GroundSim.Render -- --smoke`), which
runs the **full render pipeline** (agents + sim + dirty tracking + bitmap writes):

```
colony smoke ok: 2000 ticks, grid 400x300 (120000 cells), 40 agents (23 carrying at end),
elapsed 627 ms (0.314 ms/tick), max dirty cells/frame 82 (0.07% of grid),
active particles at end 0
```

- **0.314 ms/tick** with 40 agents on a 120,000-cell grid — ~30× real-time at the
  default 30 tps, including rendering work.
- **Max 82 dirty cells/frame (0.07% of grid)** — the dirty-cell property held at
  scale (Phase 3 measured 0.20% on a smaller grid with no agents).
- The xunit colony test (sim-only, no rendering) runs 40 agents × 2,000 ticks in
  ~500 ms against a 5,000 ms threshold — rationale documented in-code: even
  1 ns-per-cell O(grid) work per tick would cost 0.24 s of pure overhead, so any
  realistic O(grid) regression blows far past the limit.

## 7. Test Suite (32 tests, all passing)

```
Passed!  - Failed:     0, Passed:    32, Skipped:     0, Total:    32, Duration: 996 ms - GroundSim.Tests.dll (net10.0)
```

24 prior tests unchanged, plus 8 new in `GroundSim.Tests/AgentPathfindingTests.cs`:

- Pathfinder: valid path (adjacent steps, Air-only, threads a 1-cell wall gap),
  null for a sealed-off goal, empty path for start==goal.
- Agent: never moves more than one cell per `Tick()` across 500 instrumented
  ticks; completes a full dig→carry→drop cycle (spoil settles above the original
  surface at the drop column, hole exists in the dig region); a route walled off
  mid-traversal → agent re-plans, never enters solid cells, never crosses the
  wall, ends Idle.
- Concurrency: 8 agents × 4,000 ticks conserve material exactly (see §4).
- Colony scale: the §6 performance test.

## 8. Unspecified Decisions Made (flag for course-correction)

1. **Agents are climbers** — any Air cell is walkable; agents have no gravity.
   Chosen because ants climb tunnel walls, and it avoids a second gravity system
   for what Arc 2 will replace with real ant movement rules anyway. If Arc 2
   wants surface-walkers, walkability is one predicate in `Pathfinder`.
2. **Buried-agent rule** — a particle settling into an agent's cell pushes the
   agent up one cell/tick. Agents can't die or be trapped in Arc 1.
3. **Dig frontier definition** — diggable cells must touch Air; sealed cells
   become available as digging exposes them. This makes agents excavate an open
   quarry pit top-down rather than teleport-mining enclosed cells.
4. **Claims released on dig or abandonment**, held during pathing — at most one
   claim per agent at any time (asserted in the concurrency test).
5. **Idle cooldown 15 ticks** after a failed work scan — bounds idle-agent cost;
   makes agents resume up to 15 ticks late when work reappears. Tunable.
6. **`DemoScript` (Phase 3) deleted** — replaced by `DemoWorld` + real agents in
   both the window and the smoke run; keeping a dead scripted demo invited drift.
7. **A* expansion cap 50k** treated as unreachable — bounds pathological searches;
   on these grid sizes real paths never approach it.

## 9. Arc 1 Exit Criteria — honest closing assessment

> "a standalone demo where several agents dig tunnels into a large grid, carry
> material outside, and produce visually convincing, performant dirt/rock/stick
> piles — with test coverage and no rendering hacks blocking Arc 2."

**Met, with one wording caveat:**

- ✅ Several agents (8 in the demo, 40 in the perf check) dig, carry outside, drop.
- ✅ Dirt/rock/stick piles form with visibly distinct, physically-settled behavior.
- ✅ Performant: 0.314 ms/tick at colony scale, dirty-rendering at 0.07% of grid.
- ✅ Test coverage: 32 tests including conservation, re-planning, and perf gates.
- ✅ No rendering hacks: renderer is read-only; every core hook is observational.
- ⚠️ **"Tunnels" caveat:** the frontier rule produces open-pit *quarries*, not
  enclosed *tunnels*. Agents CAN dig enclosed shapes (the physics and pathing
  support it — a dug cell becomes walkable Air immediately); the demo's
  target-selection just never chooses to. If Arc 2's ant behavior needs
  tunnel-shaped digging, that's a target-selection policy change (dig
  depth-first along a line), not an engine change. Flagging rather than
  claiming it demonstrated.
- Known deferred items unchanged: no cave-in physics, no mid-air particle
  collision, no agent-agent collision — all documented in prior reports and
  none load-bearing for the Arc 2 integration decision.

**Verdict: Arc 1 is genuinely complete for its stated purpose** — the physics,
rendering, and agent layers are proven, measured, and headlessly testable. The
one honest gap (tunnel-shaped vs. pit-shaped excavation) is a demo-policy gap,
not an engine gap, and should take hours, not days, if Arc 2 wants it
demonstrated first.

## 10. Suggested Arc 2 Considerations

- Decide the integration architecture before adding ant behavior (per roadmap).
- A tunnel-digging target policy (see §9 caveat) would make a stronger visual
  for any stakeholder demo.
- If agent counts grow past ~100, the per-Idle region scan (currently bounded
  by cooldown) and per-plan surface scans are the first things to profile.
- `Agent` is deliberately generic; ant-specific behavior (castes, pheromones,
  leaf-carrying) should wrap or compose it, not fork it.
