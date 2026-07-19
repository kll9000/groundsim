# GroundSim — Phase 1 Handoff Report

**Date:** 2026-07-18
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (commit `710a92e`)
**Status:** ✅ Complete — builds clean, 9/9 tests passing, demo verified visually

---

## 1. What GroundSim Is

GroundSim is a standalone C# prototype of a 2D falling-sand-style ground simulation,
intended to eventually plug into a separate ant-colony game. Instead of pre-authored
tunnel/room shapes, the world is a dense 2D grid where every cell has a material.
Agents dig cells (Dirt → Air, material is "carried"), and dropped material becomes a
falling particle that falls, slides diagonally, and settles into realistic piles —
in the style of Noita / classic falling-sand toys. No rigid-body physics.

Phase 1 is a **headless console app** — no renderer, ASCII output only.

## 2. Deliverables

| Component | File | Purpose |
|---|---|---|
| Solution | `GroundSim.slnx` | Console app + xunit test project, cross-referenced |
| Materials | `GroundSim/CellMaterial.cs` | `Air, Dirt, Rock, Grass, Fungus` (byte enum, extensible) |
| World grid | `GroundSim/Grid.cs` | Dense grid, `Dig()`, test-world generator |
| Particle | `GroundSim/Particle.cs` | A chunk of loose material in flight |
| Physics | `GroundSim/Simulation.cs` | Falling-sand tick loop, `Drop()`, `RunUntilSettled()` |
| Test agent | `GroundSim/TestAgent.cs` | Dig → carry → walk → drop → settle cycle |
| Visualization | `GroundSim/AsciiRenderer.cs` | ASCII window rendering for eyeballing terrain |
| Demo | `GroundSim/Program.cs` | 60-cycle agent run printing trench + pile |
| Tests | `GroundSim.Tests/SimulationTests.cs` | 9 xunit tests (see §5) |

**Run it:** `dotnet run --project GroundSim` · **Test it:** `dotnet test` (requires .NET 10 SDK)

## 3. Architecture Decisions (and why)

1. **Flattened 1D array storage** — `Grid` stores cells as `CellMaterial[width * height]`
   indexed `[y * Width + x]`, hidden behind an `[x, y]` indexer. One contiguous
   allocation, better cache locality on row scans, trivially amenable to span/SIMD
   bulk operations later. Tradeoff: slightly uglier internal index math, fully
   encapsulated.
2. **Coordinate convention** — Y grows downward; row 0 is the sky. Matches console
   render order. All future code must follow this.
3. **Active-particle list (the key performance property)** — only in-flight particles
   are processed each tick. When a particle settles it is written into the grid as
   static material and removed from the list. Settled terrain costs **zero** per tick;
   tick cost is O(active particles), independent of grid size. Any Phase 2 feature
   (e.g. cave-ins, erosion) must preserve this — never scan the whole grid per tick.
4. **Physics rules per tick** — fall 1 cell if below is Air; else slide to
   diagonal-down-left/right if open (random tie-break via seeded RNG, so no
   directional bias in pile shapes and runs are reproducible); else settle. Bottom
   row settles in place.
5. **Rock is not diggable** — `Dig()` returns `null` for Rock and Air, so agents
   can't tunnel through rock. Deliberate; revisit if mining is wanted.
6. **Drop lands on top of piles** — `Simulation.Drop()` walks upward to the first Air
   cell if the target is occupied. This is what prevents dirt clipping into terrain.
7. **No fixed tick rate** — `RunUntilSettled()` loops until quiescent. Real-time
   pacing is deferred to game integration.

## 4. Verified Behavior

Demo output (60 dig+carry+drop cycles, dig at x=30–34, drop at x=70): a trench forms
at the dig site and a pile with a natural ~45° slope forms at the drop site:

```
..................................................#........
.................................................###.......
................................................#####......
..............................................########.....
.............................................###########...
............................................#############..
...........................................###############.
##########.....#############################################
##########.....#############################################
```

56/60 cycles completed — the other 4 digs hit undiggable rock at depth (expected).

## 5. Test Suite (9 tests, all passing)

```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 116 ms
```

- Dig converts Dirt → Air and returns the dug material; Air and Rock refuse (rock stays).
- Test world has air above ground level and fully solid ground at/below it.
- A dropped particle falls until it rests on the floor.
- A particle blocked directly below slides diagonally and settles beside the obstacle.
- 15 drops at one spot form a pile wider than 1 column with center height well under
  the drop count (a vertical tower fails this test).
- Agent cycles form a trench at the dig site and a pile **above** the original surface
  at the drop site, with no dirt clipped into solid ground.
- **Performance:** 500 dig+drop cycles on a 200×200 grid must finish in < 2 s.
  Observed: well under 100 ms (~20× headroom — loose enough for CI jitter, tight
  enough to catch an accidental O(all-cells-per-tick) regression).

## 6. Known Limitations / Suggested Phase 2 Scope

- **Single particle in flight per agent cycle.** The simulation supports many
  concurrent particles, but the test agent settles each drop before the next dig.
  Phase 2 should exercise many simultaneous particles.
- **Diagonal slide checks only the diagonal cell,** not the adjacent side cell —
  particles can slip through diagonal one-cell gaps. Acceptable for Phase 1; tighten
  if it looks wrong at scale.
- **No tunnel-stability / cave-in rules.** Dug tunnels of any shape stay open forever.
  If ant tunnels should collapse without support, that's new physics.
- **Materials beyond Dirt are inert.** Grass/Fungus exist in the enum only; no growth,
  spread, or distinct particle behavior.
- **No renderer** — ASCII only. Real-time visualization and a fixed tick rate are the
  natural bridge to game integration.
- **Grid size/format** — test world is 200×200; nothing precludes larger, but there is
  no chunking, so memory is `width × height` bytes (trivial) and world bounds are hard.
- **Agent is not an ant.** No pathfinding, no AI — it teleports between dig and drop
  columns. Phase 2+ should replace it with real agent movement.
