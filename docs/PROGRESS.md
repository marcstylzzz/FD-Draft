# FD-Draft progress log

**Read this file first in any new chat or account picking up this project.**
It's the up-to-date status; `docs/ARCHITECTURE.md` next to it explains the
design and reasoning behind what's built. Between the two, a fresh Claude
session should be able to continue this project with no other context.

- **Repo:** `github.com/marcstylzzz/FD-Draft` (clone with
  `git clone --recurse-submodules ...`, then `git pull --recurse-submodules`
  to update - ACadSharp is a git submodule).
- **Current version:** 0.6.1 (`Directory.Build.props`).
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

- **Commands for Marc**: he runs them in a fresh Command Prompt, which opens
  in `C:\Users\marcs` - always write Windows commands to work from there
  (the repo is `C:\Dev\FD-Draft`). To update: close FD-Draft, then
  `C:\Dev\FD-Draft\update.cmd` (pulls, builds, starts the app).

- Every commit: bump `<Version>` in `Directory.Build.props`, run both
  scripts above clean, update `README.md` and `docs/ARCHITECTURE.md` (the
  feature list and the "Next in the app" section), then commit and push.
- Commit trailer:
  ```
  Co-Authored-By: <the model named in that session's attribution note> <noreply@anthropic.com>
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
  101 tests as of v0.6.1, all passing.

## History this project (chronological, most recent last)

- **v0.1-v0.3**: drafting engine (FD-Pro reader, DraftBuilder, SheetPicker,
  Annotator), DWG/DXF/PDF/SVG output, template inspector, then the WPF
  desktop app shell (canvas, sheets, snaps, inverse, save/plot).
- **v0.4.0**: selection, erase, undo/redo, MOVE/ROTATE, and COGO drafting
  (LINE by bearing/distance, ARC by three points, TEXT, application icon).
- **Bug investigation**: Marc opened a real MSCAD-authored DWG
  (`17 Empire Blvd Wellington.dwg`) and reported (a) "Draft" picking the
  demo job instead of the open job, and (b) sheets appearing empty. (a) is
  by design (Draft always drafts from a raw FD-Pro job folder). (b) was
  diagnosed at the time as the file's sheets being genuinely blank - that
  turned out to be wrong; see v0.4.22 (lone plan viewports were being
  skipped as the paper background). This led directly to the next two features below, so the
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
- **v0.4.19**: DIMDIA (diameter) and DIMANG (3-point angle in
  D°MM'SS", arc placement picks the angle). All five dimension kinds share
  `DimensionBuilder`. DWG round-trip tested.
- **v0.4.20**: VXDEL/VXADD and the Properties vertex buttons now
  work on Polyline2D too, by swapping in a rebuilt polyline
  (`ReplacePolyline2DCommand`) - ACadSharp's HashSet-backed vertex
  collection scrambles order if vertices are removed and re-added.
- **v0.4.21**: MVIEW - add a plan viewport to a sheet (two
  corners + scale, Enter = fit at the next standard scale). Prompted by
  Marc re-reporting the "17 Empire Blvd Wellington.dwg" 11X17 tab as empty
  while model space has the survey: still correct (that tab has no
  viewport; the plan is on 17X22/RPLAN-22X34 in paper space), but there was
  no way to put model space onto a blank tab. Marc was also running a
  pre-v0.4.2 build (no Stretch/Set Layer buttons, leaders "not drawn yet"),
  so the v0.4.1 blank-sheet log line wasn't showing for him - remind him to
  `git pull --recurse-submodules` and rebuild.
- **v0.4.22**: **correction** to the "17 Empire Blvd" diagnosis
  above. Marc's screenshot of RPLAN-22X34 showed no plan either, so "the
  plan is drawn in paper space on 17X22/RPLAN-22X34" was wrong - that was
  inferred from primitive counts, which title-block/schedule tables inflate
  too. Real cause (most likely): ACadSharp numbers viewports by position,
  so a tab whose only viewport is its plan reads back with it as #1 =
  "represents paper", and SceneBuilder skipped it on every tab. Fixed with
  `FdDraft.View.ViewportRules` (a lone #1 viewport looking at survey
  coordinates is the plan). Not yet confirmed against the real file - it
  wasn't available in this session; ask Marc to re-open it after pulling.
  FdDraft.Cad now references FdDraft.View (for that rule).
- **v0.4.23**: Marc re-opened the file on v0.4.22 and the per-
  sheet counts were unchanged (11X17 21, 17X22 219, RPLAN-22X34 143, the
  rest 32/51) - so no viewport shows model space there even with the new
  rule; the lone-#1 theory did NOT explain this file. Root cause still
  open. Added VPINFO (lists every viewport on a sheet: number, verdict,
  centre/size, view centre/height, status, layer) and replaced the
  misleading "the plan looks drawn on:" log line (prim counts can't tell a
  plan from a title block) with "shows model space" per sheet. Next step:
  get Marc's VPINFO ALL output, or the DWG itself, and look.
- **17 Empire Blvd - settled** (Marc uploaded the DWG): every one of the ten
  sheet tabs has exactly one viewport, and it is genuinely the paper
  background (it looks at sheet coordinates ~450,320, not the survey at
  309 600 / 4 869 200). Everything on the tabs is on `TitleBlock-*` layers
  and full of template placeholders (PART OF XXXXX, JOB 24-0XX) - 17X22 is
  the SRPR title block + legend, RPLAN-22X34 the R-plan schedule. The
  survey (755 entities) is only in model space and was never put on a
  sheet. FD-Draft displays it correctly; the fix for Marc is MVIEW.
  Verified MVIEW's code on the real file: 17X22 at 1:250 shows 979 survey
  items and survives DWG save + reopen. (The v0.4.22 lone-viewport rule is
  still correct in general, just not this file's cause.)
- **v0.4.24**: MTEXT word-wraps to its box width (found plotting
  that file: SRPR notes ran off the sheet). Known leftovers there: a
  heading using an inline \H scale code runs a little long, and notes that
  indent with spaces sized for the SHX font overlap their heading slightly.
- **v0.4.25**: Marc (drafting job 46 Jenland Way S) couldn't move
  overlapping point labels. Real bug: ACadSharp's TEXT transform ignores
  AlignmentPoint, and all drafted labels are aligned, so MOVE/ROTATE/COPY
  did nothing visible (COPY stacked the copy on the original). Fixed via
  `EntityTransform` (also MTEXT rotation, block attributes). Also: drag-to-
  move in the canvas, text picked by its box instead of its anchor, and a
  selection highlight drawn on the real box. Possible next: an automatic
  "spread overlapping point labels" tool.
- **v0.4.26**: the window no longer opens taller than the screen
  (Marc's laptop: the command line and status bar were below the taskbar).
  Opens maximized the first time, remembers size/position/maximized, and
  `WindowFit` pulls a saved window back on screen above the taskbar.
- **v0.4.27**: Marc asked that clicking anything tied to a point
  (node, elevation, code, icon) highlight and centre it in the Points list.
  Draft now tags point entities with their point number (XData "FDDRAFT"),
  with a by-position fallback for untagged drawings; the Points list and
  the plan select each other. Jobs drafted before this need a re-draft
  (Ctrl+D) to get tags - until then a label dragged far from its point
  won't be recognised.
- **v0.4.28**: Marc asked for AutoCAD's Print dialog "to select
  the right .ctb and other settings". Built: PlotDialog (printer or PDF,
  paper, copies, .ctb, area incl. picked window, scale/fit, offset/centre,
  lineweight + style options, orientation, Preview, Apply to Layout), with
  .ctb reading (`PlotStyleTable`), page composition (`PlotComposer`),
  per-line pen widths in the PDF, Windows printing (`PlotPrinters`), and
  page setups read from / saved to the DWG (`LayoutPlotSetup`). Verified on
  17 Empire Blvd (monochrome and fit-to-Letter plots look right). Not yet
  run against a real .ctb from Marc's PC or a real printer - ask him to try
  his firm's .ctb and plotter. Left out on purpose: shaded viewports, print
  stamp, background printing, .stb named plot styles.
- **v0.4.29**: the right-docked, hover-to-slide-out tool palette
  Marc asked for (tabs Text Styles / Useful Tools / Line Styles, from his
  MSCAD screenshots), driven by `%APPDATA%\FD-Draft\palette.ini`. The
  default entries' layers/heights are guesses except where the ProVision
  template has them - Marc should correct palette.ini to his firm's layers.
  MSCAD "Useful Tools" with no FD-Draft equivalent yet (RTS Command, Surveyor
  View, Xref Manager, Text to Multiline Text, Add Sheet and Title Block,
  Layers Off, Symbol Librarian) were left off; FD-Draft's own tools fill
  that tab instead.
- **v0.4.30**: Marc's follow-ups on the point sync - selection
  shows in blue (not WPF's pale unfocused grey); it no longer switches to
  the Points tab, it updates whichever list is open; and it now also
  follows on the Layers tab (the item's layer) and the Codes tab (the
  item's code - Draft tags figure linework with its code).
- **v0.4.31**: Layers/Codes list selection now blue too (their
  Aero2 item templates hard-code a grey unfocused selection, so they got
  their own `ListItemStyle`); model space black / sheets white; right-click
  edit menu (and right-click = Enter during a command); View toolbar
  (Regen, Pan, Zoom Window/Previous/In/Out, Extents) and Object Snap
  toolbar (End/Mid/Int/Cen/Quad/Perp/Near/Node toggles, None).
- **v0.4.32**: the Annotate toolbar - Marc named screenshot 3's
  tools: auto split bearing (on centre of line), auto bearing off line,
  auto distance, auto distance off line, auto bearing/distance, auto
  bearing-distance, auto bearing/distance // line, auto distance/bearing //
  line. Built as BRGON/BRGOFF/DISTON/DISTOFF/BRGDIST/BRGDASH/BRGDISTL/
  DISTBRGL with a pick loop. His screenshot 3 had 15 icons; the last 7
  (after the eight he named) are unidentified.
- **v0.5.0**: the full toolbar set from Marc's MSCAD `icad.cui`
  (he uploaded it; it lives only in that chat - `ToolbarCatalog.cs` now holds
  everything needed from it). Marc approved the dark icon style from a sample
  sheet, then asked for all of it and for FD-Draft names ("FD Labels", not
  "MS Labels"). Built:
  - 186 original vector icons (tools/icons/icons.py -> ToolIconData.g.cs),
    replacing every text button. Not MicroSurvey's artwork - drawn fresh.
  - Catalog-driven dark toolbars in 6 rows, View > Toolbars show/hide/reset,
    layout remembered. Bars: Standard, Draw, Modify, Layer, View, Object Snap,
    Survey + FD Labels (15), FD Ties (13), FD Text Edit (17), FD Layer (11),
    Layer Tools (18), Dimensioning (30), Text (5), FD Main Control (18),
    FD Calcs (15), FD Coordinate (18) - button for button with icad.cui.
  - ~60 new commands (HELP lists them by bar). FD Labels corrected against
    the CUI: "Auto Split Bearing" (split across the line) and "Place Bearing
    on centre of line" are separate buttons; Distance - Bearing was missing.
  - 25 of 205 buttons have no FD-Draft equivalent and show dimmed with the reason
    (arc-length/ordinate dims, jog/break/spacing, tolerance/inspection,
    oblique, reassociate, grips, assistant, hot toggles, traverse editor,
    scale Z, ROW design, reverse curve, renumber/transfer/stakeout/Helmert -
    FD-Pro owns the point database and raw data).
  - Not run on Windows yet: Marc should try the bars, especially ties,
    tables, text on arc, dimension text moves, layer lock/freeze/states.
- **v0.5.1**: Marc ran v0.5.0 (it works) - the 24 px toolbars in
  six rows took too much of the drawing area; he asked for half-size icons that
  stay legible. At 12 px (on his 150%-scaled 1920 screen) the numbers inside
  the label icons turn to dots, so the default is now 16 px with tight button
  padding, and the rows are packed first-fit to the window's width
  (`ToolbarLayout.Pack`): 5 thin rows, about half the old toolbar height.
  View > Toolbars > Icon size switches Small 12 / Medium 16 / Large 24.
  A layout saved at another size (or by 0.5.0) is re-packed on start.
- **v0.5.2**: EXPLODE, typed X (Marc: "X for explode, J for join, on
  the right click menu"). Polylines -> lines and arcs (keeping their FD-Pro code
  tag), blocks -> their parts placed as shown (layer 0 / ByBlock take the
  insert's; attributes become text), dimensions -> their picture. `Exploder` in
  FdDraft.Cad. J already ran JOIN; both are now on the right-click menu, the
  Modify menu, and Explode is on the Modify toolbar.
- **v0.5.3**: Surveyor View - Marc looked for it on the palette
  (it had been left off in v0.4.29 for want of a command) and said it must turn
  the paper layout and the north arrow too, as MSCAD's does. SV turns the plan
  (pick a line to run level, or type a bearing to point up, or degrees): Model
  on screen (`ViewTransform.Twist`, `DrawingCanvas.ModelTwist`), and every
  sheet's plan viewport (`TwistAngle`, keeping the same model point centred)
  with its north arrow turned by the same amount (`SurveyorView` in
  FdDraft.Cad) - one undo step. WV = north up, RSV = back to the last one.
  A drawing opened in surveyor view shows turned. Fixed on the way:
  SceneBuilder took a twisted viewport's view centre as a world point;
  AutoCAD (per ezdxf) keeps it in the twisted frame - now matches. Box
  select and zoom window work on a turned Model view; MVIEW turns new
  viewports to match. palette.ini gains Surveyor View / World View / Return,
  Text to Multiline Text and Layers Off in Useful Tools (added once to an
  existing file).
- **v0.5.4**: labels follow Surveyor View. Marc: "all labels must
  follow; elevation is always at NE45 of the plan, codes and symbols by default
  follow the same plane, unless changed; point numbers the same".
  `SurveyorView.Relabel` (part of the same undo step as the turn): a point's
  labels and symbol (tagged with its point number) turn about the point so they
  keep their place round it and read level on the plan - a label moved by hand
  keeps its spot; the elevation is always put at `elevation_angle` (45° =
  up-right on the plan); untagged text that read level stays level (unless it
  lies along a parallel line - then it's a bearing/distance); text along lines
  keeps its angle and is turned end for end where it would read upside down;
  FD-Draft's dimensions redraw. New labels read in the turned view:
  `Angles.ViewTwist` (set by the app) feeds `ReadableRotation`, and new TEXT /
  MTEXT / leader text / imported point numbers are level on the plan.
  Drafting default changed to match: elevation at 45° up-right, point number at
  -45° down-right (was elevation below-right, number above-right) -
  `[labels] elevation_angle / point_number_angle` in the standards file.
- **v0.5.5**: Marc's screenshot: elevations drafted "just to the
  right" of the node - they were at 45° but only ~0.5 mm out, next to 1.5 mm text.
  Now `Annotator.PointLabelDistance` = symbol clearance + 0.8 text height (and
  at least 1.3 text heights when Surveyor View re-places one), so the corner is
  plainly on the diagonal. ELEV45 re-places every elevation in an existing
  drawing. Draft now opens in Model space (Marc: "default view is model
  space"); the sheet is a tab below.
- **v0.5.6**: I had "45°" wrong. Marc rotated one elevation by hand
  to show it: the elevation TEXT ITSELF runs up the 45° line from its point
  (rotation 45° on the plan, left end at the point, centred on the line) - not
  level text placed up-right. `Annotator.ElevationPlace` does that for Draft,
  ELEV45 and Surveyor View (an angle that would read upside down runs back
  toward the point instead). Point numbers stay level (down-right).
- **v0.5.7**: "north arrow when moved around gets distorted and
  inverts". ACadSharp's `Insert.ApplyTransform` turns the scale factors like a
  vector, so any block with a rotation (the north arrow, once Surveyor View had
  turned it) came out squashed and mirrored from a plain move (3x3 at 0.5 rad ->
  -0.90 x 4.15). `EntityTransform.TransformFlatInsert` now works a plan block's
  rotation and scale out from where its own axes land (move, rotate, copy, drag,
  scale all go through it). NORTHARROW - and every SV/WV/RSV - sets each
  sheet's north arrow to true north at an even, unmirrored scale, mending ones
  already damaged.
- **v0.5.8**: toolbar wiring pass while writing the user guide
  (Claude Doc "FD-Draft Toolbars - User Guide"). Fixed: ADDANGLE text and
  CURVEOFF lines follow the view twist; curve/line tables rotate to the view;
  the Layers panel ticks turn on and thaw off/frozen layers (names show
  (off)/(frozen)/(locked)); Baseline/Continue only chain onto a dimension in the
  current drawing; PTSONOBJ uses the polyline span nearest the pick (arcs too);
  PTIMPORT number/description text follows the view.
- **v0.5.9**: "tried line function didnt work" - Marc keyed a leg as
  `ne30.0030 125.5` (quadrant first, angle in DD.MMSS, as in MSCAD) and LINE
  only knew N45-30-00E. `Cogo.ParseBearing` now also takes NE/SE/SW/NW + angle
  (DD.MMSS when a plain number, or D-M-S), with the quadrant typed attached or
  apart ("NE 30.0030 125.5"). N45.5E stays decimal degrees.
- **v0.6.0**: MSCAD's INFO -> "CAD Line Computations" -> Traverse, from
  Marc's screenshots. INFO on a line (or straight polyline span) opens
  `LineInfoDialog` (bearing, rotated bearing, from/to NEZ, horizontal/slope and
  the same scaled for output = x GridToGround, % grade, dZ; Traverse, Turned
  Angle, Tangent to Arc, Curve Calcs, List Line; Angle/Angle, Deflection,
  Proportioning dimmed). Traverse (also the TRAVERSE command) picks a start
  point, then the modeless `TraverseDialog` ("Traverse or Side Shots"): bearing
  (N73.1010E / NE73.1010 / N73-10-10E / DD.MMSS azimuth), distance x the job's
  scale factor when Input scale is on (ground in, grid drawn -
  `Cogo.TraverseLegFrom`), bearing correction, Traverse vs Side Shot, next
  point number (point + number text), bearing/distance labels (printed back
  as ground), pan/zoom pad with undo-last-leg. Each leg is one Ctrl+Z.
  Scale direction assumed: typed ground x SF = grid (the inverse of what the
  labels print) - confirm with Marc against an MSCAD result.
- **v0.6.1** (current): "why did this happen?" - a fan of black lines from the
  title-block logo ("ONTARIO LAND SURVEYORS" / GRAD SURVEYING, splines) to one
  point below the sheet. ACadSharp's `Spline.PolygonalVertexes` samples from the
  first knot; on a closed, unclamped spline (exploded text / logo outlines) the
  curve isn't defined there and `c()` returns XYZ.Zero - (0,0) in block space,
  i.e. the block's insertion point, one ray per glyph. `View.SplinePoints` now
  tessellates over [knot[p], knot[n]] by de Boor (wrapping unwrapped closed
  control points, uniform knots when missing); SceneBuilder and
  TemplateInspector use it.
- **Open**: Marc's feedback on v0.5 in use; whether DELPOINTS should also
  delete from the FD-Pro job; then the dimmed buttons worth
  doing (arc-length dimension first - ACadSharp has `DimensionArc`).

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
  insert/delete covers both polyline kinds since v0.4.20.
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
