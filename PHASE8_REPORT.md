# GroundSim — Phase 8 Report (packaged executable — Arc 2 castes+rooms scope closed)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — builds clean, 53/53 tests passing, published `.exe` verified launching
**Prerequisite check:** Phase 7 base verified at 52/52 before starting.

> **⚠️ Constants still invented** — `game.js` values remain unretrieved. Per the
> handoff's suggestion, the running app now says so on screen: the status bar
> leads with **"PROTOTYPE (untuned constants)"**, so the packaged build can't
> create a false impression of validated pacing.

---

## 1. The window now shows the real colony (item 1)

`MainWindow` no longer runs Phase 4's leftover quarry demo (`DemoWorld` is
deleted). It runs `ColonyScenario`: a 200×120 world, `Colony.Found(...)` — the
**real founding**, so the first thing a viewer sees is the Queen digging the
Home Room cell-by-cell and hauling spoil out — plus two surface `ResourceNode`s
for Foragers. `Colony.Tick()` is wired into the exact same
`TickClock`/`DirtyTracker` per-tick loop from Phase 3; nothing new was built
there. Default speed is 60 tps (founding takes ~40 s to watch; Up-arrow to
240 tps makes it ~10 s — flagged as a watchability choice).

## 2. Colony rendering (item 2) — flat colors, no art assets

- **Queen:** pink — unmistakable and permanently visible once settled.
- **Castes:** Tender green, Forager blue, Major red.
- **Eggs:** small pale inset dots (read as objects in a cell, not terrain).
- **Rooms:** excavated rooms tint their Air cells — Home slate-violet, Garden
  dark green, Nursery warm brown — so "this is the Garden" is visually distinct
  from plain tunnel. A newly-excavated room's area is marked dirty once for the
  repaint; no per-frame cost afterward (the Phase 3 dirty-cell property holds:
  the headless smoke measured **max 31 dirty cells/frame, 0.13% of the grid**).
- Falling particles stay yellow. The theme-sheet sprite art stays out, per the
  handoff.

## 3. Status bar (item 3)

`PROTOTYPE (untuned constants)  stage: Expansion  T:14 F:8 M:3 eggs:6  raw 3.2
farmed 87.0  garden:done nursery:digging  tps 60  active 2  dirty 31  [Space]
pause  [Up/Down] speed`

Stage comes from a new read-only `Colony.CurrentStage` derived property
(Founding → FirstBrood → Establishment → Expansion, from milestones). **This is
the one Colony-side change this phase**, flagged per item 5: it's
presentation-supporting derived state, reads existing milestones, mutates
nothing, and is unit-tested through all four transitions (the 53rd test). No
other `Colony`/`Room`/caste logic was touched, and no bugs surfaced in items
1–3 requiring inline fixes.

## 4. Packaging (item 4)

- **Publish mode: self-contained, single-file, win-x64**
  (`dotnet publish -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true -o publish`). Chosen because the goal of this phase
  is an app that can be handed to a machine that has nothing installed —
  framework-dependent would silently require the .NET 10 Desktop Runtime. Cost:
  **~125 MB** exe (plus a few native WPF DLLs alongside, ~8 MB). The `publish/`
  folder is gitignored.
- **Icon:** `groundsim.ico` generated for the project (32 px flat-color motif:
  terrain cross-section with tunnel, red worker + pink queen dots, spoil pile) —
  not the stock .NET icon; set via `<ApplicationIcon>`, visible in the title
  bar and Explorer.
- **Title:** "GroundSim — Colony Prototype".
- **Launch verification:** the published `GroundSim.Render.exe` was launched
  via the Windows shell (the same code path as an Explorer double-click, no
  `dotnet run`, no terminal dependency) and confirmed alive after 25 s; a
  screen capture shows the window up with the custom icon and title. One note:
  the exe is named `GroundSim.Render.exe` — setting `<AssemblyName>GroundSim`
  collided with the core project's name at solution restore, and renaming
  wasn't worth destabilizing the build for a prototype.

## 5. What the running window shows

Sky over a dirt terrain cross-section with scattered rock. The pink Queen digs
the Home Room out cell by cell, yellow spoil particles pile up to the right of
the entrance. Eggs appear as pale dots in the slate-tinted Home Room; green
Tenders and blue Foragers emerge; Foragers stream to the surface nodes and
back. At ~30 farmed, the Garden dig starts below the Home Room (blue Foragers
and red Majors digging); its cells turn green-tinted when done and Tenders
relocate there. The Nursery (warm tint) follows beside the Home Room, and new
eggs appear inside it. Headless smoke of this exact scenario: **stage Expansion
reached by tick 8,000 (0.049 ms/tick)** — milestones home=2288, first
worker=3067, garden=4675, nursery=6878, consistent with Phase 7's 10-seed
medians.

## 6. Test suite (53 tests, all passing)

```
Passed!  - Failed:     0, Passed:    53, Skipped:     0, Total:    53, Duration: 1 s - GroundSim.Tests.dll (net10.0)
```

52 prior tests pass unmodified; the one addition is the `CurrentStage`
transition test (§3). Rendering/packaging themselves are not unit-tested, per
the handoff's (and Phase 3's) stance; the `--smoke` flag still runs the full
render pipeline headlessly for CI-style verification.

## 7. Arc 2 castes+rooms scope: closed

All eight phases are done with the discipline held throughout: real test output
every phase (9 → 53 tests), physical conservation invariants, role purity
verified behaviorally, a spread-of-runs end-to-end, one genuine latent bug
(claim leak) found and fixed by that discipline, and a double-clickable
Windows executable showing the actual colony loop. The open item remains:
**`ColonyConfig` constants are invented placeholders until `game.js` is
retrieved** — tracked in every report since Phase 6 and now on-screen in the
app itself. Future scope (Soldier, Pupa Chamber, hygiene, New Queen, Arc 3) is
deliberately not started, per the handoff.
