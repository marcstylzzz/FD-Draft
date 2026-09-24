# FD-Draft progress log

**Read this file first in any new chat or account picking up this project.**
It's the up-to-date status; `docs/ARCHITECTURE.md` next to it explains the
design and reasoning behind what's built. Between the two, a fresh Claude
session should be able to continue this project with no other context.

- **Repo:** `github.com/marcstylzzz/FD-Draft` (clone with
  `git clone --recurse-submodules ...`, then `git pull --recurse-submodules`
  to update - ACadSharp is a git submodule).
- **Current version:** 0.4.19 (`Directory.Build.props`).
- **Owner:** Marc, Vaughan Land Surveyors (Colborne, Ontario). This is his
  standing instruction: *put in as many features as possible, he'll give the
  app a run once it's substantially built.* There is no fixed spec beyond
  that - use judgment on what a working land surveyor would need next, and
  keep going without waiting for sign-off on each feature.

## What FD-Draft is

A standalone Windows land-survey drafting application (WPF, .NET 8) that
drafts FD-Pro job folders onto a firm's own `.dwt` template and saves a real
DWG - no AutoCAD or MSCAD involved. Reads/writes DWG via ACadSharp (MIT,
vendored). See `README.md` for the user-facing feature list and
`docs/ARCHITECTURE.md` for how it's built.

## How to build and verify (Linux sandbox - no Windows/WPF runtime here)

```bash
bash build-offline.sh              # builds everything, runs tests/FdDraft.Tests
bash tools/wpf-compile-check/check.sh   # compiles FdDraft.App against a WPF stand-in
```

`wpf-compile-check` depends on freshly-built `FdDraft.Cad`/`Core`/`View`
DLLs under `tests/FdDraft.Tests/bin/Debug/net8.0/` - always run
`build-offline.sh` first after touching those projects, or it checks against
stale DLLs and gives false failures. Neither script proves runtime behavior
on Windows; that's still Marc's job when he runs a build.

## Working conventions (established this project, keep following them)

- Every commit: bump `<Version>` in `Directory.Build.props`, run both
  scripts above clean, update `README.md` and `docs/ARCHITECTURE.md` (the
  feature list and the "Next in the app" section), then commit and push.
- Commit trailer:
  ```
  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: <this session's claude.ai/code/session_... URL>
  ```
  (a new session uses its own URL here - that's expected and fine).
- `FdDraft.Cad.Editing` (undo/redo commands) and `FdDraft.Core.Geometry`
  (bearing/leg parsing) deliberately live outside the WPF project so they
  stay unit-testable on Linux. New editing features should follow that
  pattern: build the undoable logic in `FdDraft.Cad`, wire it into
  `MainWindow.cs` last.
- `Arc` must be matched before `Circle` in any C# switch - `Arc` derives
  from `Circle` in ACadSharp.
- Add a `tests/FdDraft.Tests/Program.cs` test for new non-UI logic (it's a
  plain reflection-based runner - any public static void `Test*` method).
  61 tests as of v0.4.19, all passing.

## History this project (chronological, most recent last)

- **v0.1-v0.3**: drafting engine (FD-Pro reader, DraftBuilder, SheetPicker,
  Annotator), DWG/DXF/PDF/SVG output, template inspector, then the WPF
  desktop app shell (canvas, sheets, snaps, inverse, save/plot).
- **v0.4.0**: selection, erase, undo/redo, MOVE/ROTATE, and COGO drafting
  (LINE by bearing/distance, ARC by three points, TEXT, application icon).
- **Bug investigation** (no code fix needed): Marc opened a real
  MSCAD-authored DWG (`17 Empire Blvd Wellington.dwg`) and reported (a)
  "Draft" picking the demo job instead of the open job, and (b) sheets
  appearing empty. Both root-caused as correct existing behavior, not bugs:
  Draft always drafts from a raw FD-Pro job folder regardless of what DWG is
  open (by design), and that file's 11x17 layout is genuinely blank in the
  source DWG. This led directly to the next two features below, so the
  drafter isn't left guessing.
- **v0.4.1**: log which sheets actually have a plan drawn on them (a legacy
  DWG often carries several blank unused layouts); auto-detect and reuse a
  job's own `<job>\export\fd-draft\` folder when reopening its output.
- **v0.4.2**: layer reassignment (**Set Layer**) and inline text editing in
  the Properties panel, both undoable.
- **v0.4.3**: STRETCH (grip-edit a single shared vertex, keeping connected
  lines joined) - required fixing `Scene.ModelAt` to fall back to the paper
  point itself on a layout with no working viewport, because a real MSCAD
  job commonly draws its finished plan straight onto paper space rather
  than through a floating viewport. Without that fix, no pick-based tool
  could target paper-native content at all.
- **v0.4.4**: LEADER now places a real `ACadSharp.Entities.Leader` (arrowhead,
  dimension style) instead of hand-drawn lines; fixed new linework (LINE,
  ARC, TEXT, LEADER) landing in Model space even on a paper-native sheet -
  it now goes into the correct owner block via `MainWindow.CurrentEntityOwner()`.
- **v0.4.5**: Properties panel grew a numeric field for a text's height and
  a Circle/Arc's radius (`SetPropertyCommand<T>`, a generic setter-delegate
  command).
- **v0.4.6**: extended that to Arc start/end angle, TEXT/MTEXT rotation
  (MText.Rotation is read-only in ACadSharp - it's derived from
  `AlignmentPoint` treated as a direction vector, so "setting" it means
  writing `AlignmentPoint = (cos v, sin v, 0)`), and Line endpoint E/N.
  Added `CompositeCommand` so editing several fields at once and clicking
  Apply undoes them together as one step, not one Ctrl+Z per field.
- **v0.4.7**: added a type-in vertex field to Properties for
  LwPolyline/Polyline2D - a "Vertex #" box picks which vertex, then E/N
  fields edit it via the same `VertexRef` type STRETCH uses. This is the
  type-in alternative to STRETCH's pick-and-drag.
- **Build box note**: the Linux sandbox may not have .NET preinstalled and
  dot.net / Microsoft CDNs may be blocked. Ubuntu 24.04's own archive carries
  it: `apt-get install dotnet-sdk-10.0 dotnet-runtime-8.0
  dotnet-targeting-pack-8.0 aspnetcore-targeting-pack-8.0` is enough for
  both scripts.
- **v0.4.8**: COPY (multi-destination), MIRROR (exact per-type
  reflection, plan-readable text, optional erase of originals) and OFFSET
  (lines, arcs, circles, LwPolylines with mitred corners and concentric
  curves). Entity construction in `FdDraft.Cad.Editing.EntityOps`, the math
  in `FdDraft.Core.Geometry.Construct`.
- **v0.4.9**: polyline vertex insert/delete - VXDEL (pick a
  vertex to remove; the joined span goes straight), VXADD (pick a spot on a
  span; an arc is split on its curve exactly), and matching Delete vertex /
  Insert after buttons in Properties. LwPolyline only.
- **v0.4.10**: FLIP - selected bearing/distance/curve labels move
  to the other side of their nearest course (mirrored across the line, or
  radially through an arc) with top/bottom anchoring swapped. Purely
  geometric; no course<->label link needed.
- **v0.4.11**: LINE gets `C` (close to start: misclosure, 1:n
  precision, area, then the closing course) and `U` (undo last leg); new
  AREA command (closed polylines/circles, or picked corners). Core types
  `ClosureReport` and `FigureMeasure` (not "Figure" - that name is FD-Pro's
  job figure in FdDraft.Core.Job, and "Measure" clashes with WPF's
  UIElement.Measure inside MainWindow).
- **v0.4.12**: drag-box selection in the canvas (window
  left-to-right, crossing right-to-left, Ctrl adds; logic in
  `FdDraft.View.BoxSelect`), plus SELALL (Ctrl+A) and SELLAYER.
- **v0.4.13**: DIM - a real aligned `DimensionAligned` whose
  picture block FD-Draft draws itself (ACadSharp's own generator puts both
  arrows at one end). Redrawn after MOVE/ROTATE and on undo/redo; COPY and
  MIRROR rebuild it from its definition points. DWG round-trip tested.
- **v0.4.14**: VPSCALE - change the current sheet's scale in
  place (viewport zoom, title-block "1:n", scale-bar ticks, optional resize
  of model labels/symbols/dimension text), one undo step.
- **v0.4.15**: LABEL - bearing/distance/curve labels for hand-
  drawn linework, built by the pipeline's own rules (Annotator's label
  construction factored into public `StraightCourseLabels`/`ArcCourseLabels`,
  which the pipeline now calls too) at the sheet's scale.
- **v0.4.16**: TRIM, EXTEND (lines, against any linework or the
  selection) and FILLET (corner rounding with a typed radius, 0 = sharp
  corner). Geometry in `Construct.TrimSegment/ExtendSegment/Fillet`.
- **v0.4.17**: JOIN (lines/arcs/open polylines meeting end to
  end -> one LwPolyline, closed when it closes) and ID (pick-to-read N/E,
  with the survey point's number/elevation when snapped to one).
- **v0.4.18**: DIMLIN (linear: auto horizontal/vertical, or H/V/
  typed angle) and DIMRAD (radius of an arc/circle), both through the
  generalised `DimensionBuilder` (IsOurs / DrawPicture / Transform /
  Remapped). DWG round-trip tested.
- **v0.4.19** (current): DIMDIA (diameter) and DIMANG (3-point angle in
  D°MM'SS", arc placement picks the angle). All five dimension kinds share
  `DimensionBuilder`. DWG round-trip tested.

## Known limits / deliberately deferred (don't re-litigate these)

- **Course relabeling after STRETCH**: moving a vertex keeps connected
  lines joined, but their bearing/distance/area labels don't update.
  Deliberately not attempted - there's no persisted link between a course
  and its label text yet, and a heuristic guess risks silently mislabeling
  a legal survey document. Needs a real course<->label data model first.
- **LEADER<->MTEXT association**: `Leader.AssociatedAnnotation`'s setter is
  `internal` to ACadSharp, not reachable from FD-Draft. The leader's text is
  a separate, unassociated TEXT entity next to a real Leader. Likely not
  solvable without a change on the ACadSharp side.
- **Dimensions**: aligned, linear, radius, diameter and 3-point angular.
  No ordinate or 2-line angular (pick two lines) yet.
- No sheet-size/layout change without re-drafting (scale-only changes
  are VPSCALE since v0.4.14). Vertex
  insert/delete covers LwPolyline only (not Polyline2D).
- **North-up viewports only** (no twist for a rotated lot); splines written
  as dense polylines; label collisions handled by local sliding, not a
  solver; long MTEXT notes only rewritten when a rule matches their raw
  contents.

## Suggested next steps (in roughly the order they'd naturally come up)

1. ~~Flip labels~~ - done in v0.4.10.
2. ~~Partial polyline-vertex erase~~ - done in v0.4.9 (LwPolyline).
3. ~~Scale-only sheet setup~~ - VPSCALE in v0.4.14. Still open: moving a
   plan to a different sheet size/layout without re-drafting.
4. ~~A real DIMENSION entity~~ - aligned (v0.4.13), linear and radius
   (v0.4.18). Angular and diameter in v0.4.19 - done.
5. The course-relabeling-after-STRETCH problem - but only once a real
   course<->label link is designed; don't guess at this with heuristics.
6. **Phase 2**: the in-app document assistant (reads R-plans, registered
   plans, PIN/parcel-register pages, deeds; matches record courses to
   surveyed ones; fills the R-plan schedule and title block). Not started.
   Design sketch already in `docs/ARCHITECTURE.md`'s "After that" section.

## What a new session should do on arrival

1. Clone the repo (`--recurse-submodules`) and read this file, then skim
   `docs/ARCHITECTURE.md` and `README.md`.
2. Run `bash build-offline.sh` and `bash tools/wpf-compile-check/check.sh`
   to confirm the starting point is clean.
3. Pick the next item above (or whatever Marc asks for), build it following
   the conventions section, verify both scripts stay clean, update
   README/ARCHITECTURE/this file, commit with the trailer, push.
4. Append a new bullet to the History section above for what was done -
   keep this file current the same way the others are.
