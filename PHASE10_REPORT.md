# GroundSim — Phase 10 Report (camera system + view fix)

**Date:** 2026-07-19
**Branch:** `claude/groundsim-phase-1-setup-7e2e20` (continuing, as established)
**Status:** ✅ Complete — 69/69 tests passing, window verified running with the camera
**Scope:** rendering-only, as required. Zero `Colony`/`Grid`/`Simulation` changes.

---

## Part A — the camera-shift bug: found, root-caused, and fixed

**The bug was found and root-caused, and the fix is structural** (both claims,
per the handoff's distinction — the redesign eliminates the *mechanism*, not
just the symptom).

**Mechanism:** the old window used `SizeToContent="WidthAndHeight"` with a
`StackPanel` holding the 1000px-wide image above the status `TextBlock`. The
status text was *always wider than the image* — measured with WPF's own text
metrics (Consolas 12):

```
image width:   1000 px
pre-founding:  1095 px
post-founding: 1121 px
expansion:     1181 px
```

So the window's width was governed by the **status text**, not the image — and
the text's width changes any time a field changes character count. The
narrower image, centered inside the resized window, shifted horizontally by
half of every width delta. It "starts right as founding completes" because
that's exactly when the text starts fluctuating: `Founding` → `FirstBrood`
(+26 px), room states `—` → `digging` → `done`, worker/egg counts appearing and
rolling digits. None of the handoff's candidate suspects (SpoilDropX, DrawCell
rounding, founding-agent handoff) were involved — `Int32Rect` math was verified
integer-exact throughout.

**Fix:** the window is now a fixed size and its dimensions are never
content-driven; the status text is a docked, clipped (`TextTrimming`) bar and
the world lives inside a `ClipToBounds` viewport. No layout path exists from
text width to image position anymore.

## Part B — the camera

**Architecture:** the world still renders into the single full-world
`WriteableBitmap` exactly as since Phase 3 — dirty cells only. The camera is a
pure-math `Camera` class (zoom + pan; `screen = world·zoom + pan`) applied as
GPU `ScaleTransform`/`TranslateTransform` on the image. **Pan/zoom therefore
never touches the bitmap at all** — a stationary camera over an idle colony
redraws only changed cells (the Phase 3 property, preserved by construction),
and panning is a transform update, not a re-blit.

- **Window size: 1600×900** — fits comfortably inside 1920×1080 (the most
  common desktop resolution) with room for the taskbar; the viewport is ~4×
  the old usable area.
- **Pan:** left-drag (4px threshold distinguishes drag from click). Unbounded
  — you can pan off the world edge; flagged as a deliberate simplicity choice.
- **Zoom:** wheel, 0.5×–8×, cursor-centered — the world point under the cursor
  stays under the cursor (unit-tested, including at the clamp bounds).
- **Click-to-follow:** click any Queen/Tender/Forager/Major. Hit-testing maps
  screen→world through the current pan/zoom, then picks the nearest agent
  within a tolerance of max(2 cells, 10 screen px ÷ zoom) — so clicking stays
  forgiving when zoomed far out. The status bar shows who's being followed.
- **Follow smoothing:** exponential easing — each frame closes 12% of the
  remaining distance to centering the target. Following a walking ant feels
  like a soft leash: the camera trails it slightly and glides, no per-frame
  teleport; switching targets is a smooth swoop rather than a cut.
- **Release:** Esc, clicking empty space, or grabbing the world to drag — all
  release tracking and leave the camera exactly where it is.
- Initial view: zoom 2.0 centered just above the founding site, so the first
  thing visible is still the Queen digging.

**Performance at zoom extremes:** zoomed out, the GPU scales a single
1000×600px bitmap — trivially cheap (this is the same cost class as any
image-viewer thumbnail); zoomed in, fewer pixels are sampled, cheaper still.
The simulation/dirty pipeline is untouched, so the Phase 9 measured numbers
(0.028 ms/tick, max 0.06% dirty) still describe the render workload. No new
performance concern to measure — the camera adds two transform-property writes
per frame.

## Feel notes (requested)

Dragging is 1:1 with the cursor at every zoom. Wheel-zooming into an ant and
following it feels like the standard map-app interaction; at 8× a followed
Forager visibly climbs pit walls cell by cell. The eased follow means a
followed digger bobbing in and out of the pit reads as camera "interest," not
jitter. The old few-pixel shudder is gone entirely — the window frame is inert
regardless of what the status line does.

## Tests (69, all passing)

```
Passed!  - Failed:     0, Passed:    69, Skipped:     0, Total:    69, Duration: 1 s - GroundSim.Tests.dll (net10.0)
```

64 prior tests unmodified. 5 new in `CameraTests` (pure math, no rendering):
screen↔world round-trip under pan+zoom; cursor-centered zoom invariance (in,
out, and at clamp); zoom clamping; smooth-follow convergence (monotonic
approach, never closing more than half the gap per frame — the no-teleport
guarantee — and ~centered within 120 frames); nearest-agent hit-testing inside/
outside radius and with no agents.

## Flagged decisions

1. **Test project retargeted to `net10.0-windows`** so it can reference the
   render project — the handoff wanted camera math tested headlessly AND kept
   in `GroundSim.Render`; a `net10.0` test project cannot reference a WPF
   project, and this suite was Windows-only in practice already.
2. **Unbounded pan** (no world-edge clamping) — simplicity; trivially add
   clamping later if it annoys.
3. **Zoom range 0.5×–8×, wheel step 1.2×, follow easing 0.12/frame** — feel
   constants, chosen by hand, all in one place in `MainWindow`/`Camera`.
4. **Dragging releases follow** (grabbing the world implies wanting manual
   control) — matches map-app conventions; Esc and empty-click also release.
5. Default speed remains 60 tps from Phase 8.
