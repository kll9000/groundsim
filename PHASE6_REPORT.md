# GroundSim — Phase 6 Handoff Report (Arc 2 begins: caste roster & colony data model)

**Date:** 2026-07-18
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` — **continuing the existing branch**
(the handoff left this to me; the linear history through Arc 1 is coherent and nothing
about Arc 2 needed isolation).
**Status:** ✅ Complete — builds clean (0 warnings), 45/45 tests passing
**Prerequisite check:** Arc 1 base verified at 32/32 before starting.

---

## 1. Scope

Ported Colony Builder's caste roster and colony data model into C# on the GroundSim
engine, per the content outline as behavioral spec. Headless only — zero
rendering/UI changes this phase. In scope: Queen, Tender, Forager, Major (narrowed),
eggs/survival/caste rolls, role purity. Out of scope and verified absent (see §7):
Soldier, Pupa Chamber, grooming/contamination, New Queen.

New core code lives in `GroundSim/Colony/`: `Colony`, `ColonyConfig`, `Queen`,
`Tender`, `Forager`, `Major`, `Egg`, `ResourceNode`, `PathWalker`.

## 2. The Queen and the founding transition (item 1 — flagged decision)

`Queen` is **not** an `Agent` subtype. During `QueenState.Founding` she *composes a
temporary `Agent`* whose dig region is the Home Room rect — so the founding
excavation is genuine cell-by-cell digging with spoil physically hauled out and
dropped (real physics, real conservation). When the chamber is fully excavated and
the agent is empty-handed, **the agent is discarded entirely**, she settles at the
chamber center, deposits the starter resource, and enters `QueenState.Laying` — a
state from which no code path moves, digs, or carries. Egg-laying is a simple timer
(`EggLayIntervalTicks`).

This "compose an Agent only for the founding act, then throw it away" approach is
the flagged design choice: it reuses proven machinery for the one moment she needs
it, and makes "never acts as a digger again" structurally true afterward (verified
behaviorally too — see §7).

## 3. Castes (items 2–4)

All worker castes compose **`PathWalker`** — a new core class extracting the
movement half of `Agent`'s machinery (one step/tick, replan on blocked path,
buried-push-up) — rather than the full dig-cycle `Agent`, because their work loops
aren't dig-carry-drop. **Exception: Major composes the full `Agent`**, because its
job *is* dig-carry-drop. Flagged: this is my interpretation of Phase 4's
"wrap or compose, don't fork" — machinery reused, `Agent` untouched.

- **Tender** — processes 1 raw → 1 farmed per `ProcessTicks` while standing at the
  colony's processing site; otherwise tends the nearest egg (doubles maturation
  speed). The processing site is a **delegate on `Colony`**
  (`ProcessingSiteProvider`, defaulting to Home Room center) so Phase 7 can point
  it at the Fungus Garden without touching Tender code — the anti-hardcoding
  requirement from the handoff.
- **Forager** — paths to the nearest surface `ResourceNode`, takes a haul sized by
  `HaulSize(distance) = max(GatherChunkMin, GatherChunkBase − falloff × distance)`
  (the outline's shrinks-with-distance rule in Colony Builder's
  base/DistanceFactor/Min shape), hauls it home as **raw** material. Deposit is the
  only code path that increases `RawMaterial`.
- **Major** — narrowed definition only: when `Colony.ActiveDigSite` is set, it
  spins up an internal `Agent` for that site and excavates alongside (spoil
  physically conserved); when the site clears it finishes delivering any carried
  spoil, then goes fully idle. **No guard behavior exists** — that's the deferred
  Soldier's.

## 4. Eggs and caste assignment (item 5)

Queen lays on her timer; eggs mature over `EggMaturationTicks` (2× while tended).
At maturation: survival roll (`EggSurvivalChance` = 0.35 — "most don't survive"),
then **rarity-ordered caste rolls, rarest first**: Major (0.10), then
Forager-vs-Tender split of the remainder (0.50/0.50, Tender the most-common
default). Survivors spawn as real caste instances at the egg's cell.

## 5. Tuned values: ALL INVENTED — none from game.js

The handoff offers `game.js` as ground truth for exact constants, but it wasn't
available to me this session, so **every number below is invented** and lives in
one place (`ColonyConfig`) for painless replacement:

| Constant | Invented value |
|---|---|
| StarterResource | 10 |
| EggLayIntervalTicks | 90 |
| EggMaturationTicks | 600 (tended: 2× speed) |
| EggSurvivalChance | 0.35 |
| MajorChance | 0.10 |
| ForagerShareOfRemainder | 0.50 |
| GatherChunkBase / DistanceFalloff / Min | 8.0 / 0.04·per-cell / 1.0 |
| ProcessTicks (1 raw → 1 farmed) | 20 |

Send me the real values (or `game.js`) and it's a one-file diff; the statistical
test tolerances would need matching adjustment.

## 6. Test Suite (45 tests, all passing)

```
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 622 ms - GroundSim.Tests.dll (net10.0)
```

32 Arc 1 tests unchanged, plus 13 new in `GroundSim.Tests/ColonyCasteTests.cs`:

- **Queen:** founds the chamber via real excavation (every cell verified Air,
  starter deposited exactly once), then position bit-identical across 3,000
  further ticks; lays exactly 10 eggs in 10 intervals.
- **Tender:** processes 5 raw into 5 farmed on the real grid; tended egg matures
  measurably faster than an identical untended control colony.
- **Forager:** hauls from a node — with a conservation check (node depletion ==
  raw at home + carrying, exactly); haul formula shrinks with distance and clamps
  at min.
- **Major:** excavates a real dig site with **exact whole-world material
  conservation** (initial diggable == remaining + carried + in-flight); with no
  dig site, provably inert (position frozen, no resource or node changes over
  3,000 ticks).
- **Role purity (item 6, behavioral not structural):** Tenders run 4,000 ticks
  beside full, reachable resource nodes — nodes end untouched and gathered-total
  is zero. Foragers run 4,000 ticks beside 50 units of unprocessed raw — farmed
  resource never grows, processed-total is zero. Supporting invariant: node
  depletion can only equal forager-gathered, farmed growth can only equal
  tender-processed (+ the starter) — the same conservation-style discipline as
  Phases 2/4.
- **Offspring statistics:** 20,000 seeded rolls — survival in [0.32, 0.38],
  Major share of survivors in [0.07, 0.13], Forager and Tender each in
  [0.40, 0.50]; and a full pipeline test (survival 1.0, fast maturation) where
  matured eggs spawn as real working caste members.
- **Scope boundary (item 7):** reflection over the core assembly asserts no type
  matching Soldier / Pupa / Groom / Contamination / NewQueen / Alate / Nuptial
  exists.

## 7. Unspecified decisions made (flag for course-correction)

1. **Queen founding via composed-then-discarded Agent** (§2) — the handoff's
   item-1 flag.
2. **PathWalker extraction** (§3) — movement machinery composed; `Agent` class
   untouched; Major composes full `Agent`.
3. **Resources are colony-level scalars** (raw, farmed) — matches the outline's
   own note that raw material "is just an abstract colony-wide number," not
   physical cells. `ResourceNode`s are entities at surface cells, not grid
   material; gathering doesn't modify terrain.
4. **Home Room in Phase 6 is the founding excavation rect** — an open-pit chamber
   dug from the surface. Enclosed, labeled room *regions* (and "is this Tender in
   the garden" membership) are Phase 7 per the roadmap; nothing here assumes a
   shape.
5. **No farmed-resource decay, waste, or trail system yet** — decay is entangled
   with waste/contamination (deferred) and trails belong to the Establishment
   stage work in Phase 7; including partial versions now seemed worse than a
   clean absence. Flag if you want passive decay earlier.
6. **Queen has no buried-recovery behavior** — workers push up out of settling
   spoil; the stationary queen doesn't. Spoil drop columns are placed away from
   home, and Phase 7's enclosed rooms make burial impossible; until then a
   pathological drop directly over the chamber could bury her silently.
7. **Egg positions are data, not cells** — eggs occupy Air cells for Tender
   pathing but don't block movement or physics.

## 8. Suggested Phase 7 considerations

- `Colony.ActiveDigSite` + `ProcessingSiteProvider` are the deliberate seams for
  room-trigger logic and the Garden relocation — both already test-exercised.
- `Colony.CreateFounded` (instant-founded factory) exists for tests; Phase 7's
  end-to-end run should use `Colony.Found` (real founding) as its stage 1.
- The Establishment-stage trail system will want its own field over the grid;
  `DirtyTracker`'s event-subscription pattern is the template.
- Room membership checks should be cheap (`is cell in region`) — the rect tuples
  used everywhere here are ready to become a small `Room` record with a type tag.
