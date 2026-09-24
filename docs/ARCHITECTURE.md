# FD-Draft architecture

FD-Draft is a standalone drafting program. Its only borrowed piece is the firm's
.dwt template, which every firm already maintains. It never runs inside another
CAD program.

## Shape

```
 FD-Pro job folder           firm .dwt              firm standards .ini
 (points, figures, codes)    (layers, blocks,        (code->layer/block, labels, scales,
          |                   styles, layouts,        sheet areas, title-block rules)
          |                   title blocks)                    |
          v                        |                           |
   FdJobReader -> DraftBuilder -> SheetPicker -> Annotator     |      FdDraft.Core
                        \___________________________________/        (no CAD library)
                                       |
                                 DraftDocument
                                       |
          +----------------------------+-------------------------+
          v                            v                         v
   TemplateDrafter (DWG)          SvgPreview               DxfWriter (template-free)
   opens the .dwt, drafts,                                                  FdDraft.Cad
   viewport, title block, saves DWG      TemplateInspector (new templates)  (ACadSharp)
          |
   desktop app (next) - renders the DWG, edits, re-drafts, plots
```

## Decisions

**DWG without a CAD program.** ACadSharp (MIT) reads and writes DWG R14 to 2018
formats. It reads the example template fully, including its 10 layouts and page
setups. Objects it doesn't model (display settings, AEC data) are carried
through untouched. The output keeps the template's DWG version. It is checked
by reading it back with ACadSharp and with LibreDWG, an independent reader, and
by rendering the sheet. ACadSharp is a pinned git submodule, so a library update
can never change a plan silently.

**Reading FD-Pro.** The formats follow FD-Pro's `storage/format` code exactly:
columns are found by header name, bare PNEZD files are accepted, skipped rows
are reported, figures are split into pieces with FD-Pro's own rule (arc spans in
pairs, an odd leftover span is straight).

**Layers.** A figure's layer comes from `[line-layers]` first (monument codes →
`PLAN-SubjectBoundary`), otherwise from the code's `LINE LAYER` in the job's
`codes.csv`. Point layers follow the template's `MSPOINT-` / `POINTNUMBER-` /
`ELEVATION-{feature}` scheme.

**Sheet and scale.** Each layout is a frame minus keepouts (the title column and
the schedule box). FD-Draft finds the maximal free rectangles. For each layout
it takes the largest standard scale that fits the survey plus a label margin. It
prefers the smallest paper where at least 90 % of labelled courses are long
enough to hold their bearing, and returns the full ranking with reasons. The
template's layouts have no plan viewport, so FD-Draft creates one.
`fddraft inspect` measures frames and keepouts for a new template.

**Labels.** Text heights are set in paper mm. Bearings are quadrant DMS and
never print 60". A label slides along its course when a point sits under it. A
course shared by two lots is labelled once.

**Title block.** The example title blocks are plain text, so FD-Draft fills
them with pattern rules (`#` = a number, `*` = rest of the text). Scale-bar
ticks are recomputed from the template's "SCALE 1:n" text. The layout that owns
`*Paper_Space` is kept because the DWG format needs it.

## The desktop application (v0.3)

FdDraft.App is WPF on .NET 8, with the UI built in code (no XAML) so mistakes
are compile errors. It is a thin shell:

- **FdDraft.View** builds the display list from the DWG. Model space is drawn in
  model units. A sheet is drawn in paper mm, with model space shown through each
  viewport (scaled, clipped, with per-viewport frozen layers), blocks expanded,
  ByLayer/ByBlock colours resolved, layer-0-in-blocks inheritance applied, and
  MTEXT formatting stripped. The same list feeds the canvas, the SVG preview and
  the PDF plot, so the screen, the preview and the PDF always agree.
- **DrawingCanvas** paints that list with culling, cached text and frozen pens.
  It handles zoom at the cursor, pan and snap markers.
- **PDF plotting** is FD-Draft's own writer: one page the size of the sheet at
  1:1, vector linework, and the PDF base font Helvetica with real metrics for
  alignment. No PDF library, and no printer driver involved.

The app cannot be built on the Linux build box, so
`tools/wpf-compile-check/check.sh` compiles it against WPF's public reference
API, with warnings treated as errors. That catches C# and API mistakes; running
it on Windows is the real test.

### Editing and drafting tools (v0.4)

- **Select**: click an entity to select it (its DWG handle drives it, from
  `Prim.Handle`); Ctrl+click adds or removes. Highlighted in the canvas and
  summarised in the **Properties** panel.
- **Box selection**: a left-button drag with no tool active becomes a
  selection box once it passes 5 px (a shorter one is still a click).
  `FdDraft.View.BoxSelect.Handles` decides from the scene's own prims -
  window (dragged left to right) needs every prim of an entity inside;
  crossing (right to left) takes any entity with a prim inside or a segment
  cutting the box edge (circles by nearest/farthest box point against the
  radius). Text and nodes go by their anchor. On a sheet the box is clipped
  to each viewport first, so only what shows through it can be boxed.
  SELALL / SELLAYER work from the handles drawn in the current view.
- **Erase** (Del, or the ERASE command) and **MOVE** / **ROTATE** transform
  the selected entities in place via `ACadSharp.Entities.Entity.ApplyTransform`.
  ROTATE takes the pivot by pick and the angle by typing degrees, clockwise
  (survey convention); the entity math is verified in
  `tests/FdDraft.Tests` against a real `CadDocument`, both about the origin
  and about an arbitrary pivot.
- **STRETCH** moves one vertex - a Line endpoint or an LwPolyline/Polyline2D
  vertex - rather than a whole entity. Select the line(s)/polyline sharing
  it, pick the vertex (a snap lands on it exactly; otherwise within 6 screen
  pixels), then its new position: `VertexEditing.FindCoincident` finds every
  matching endpoint among the selection and `StretchVertexCommand` moves them
  together, so lines that meet at a survey point stay joined. This is the
  actual "grip edit" a course correction needs, as opposed to MOVE/ROTATE
  which only ever transform whole entities rigidly.
- **COPY / MIRROR / OFFSET** build brand-new entities with
  `FdDraft.Cad.Editing.EntityOps` and add them beside their sources
  (`AddBesideSources`: same owner block - Model or a paper-native sheet - as
  one undo step), so undo is just removing them; the source is never touched
  (MIRROR's optional "erase originals" is a `RemoveEntitiesCommand` in the
  same `CompositeCommand`). `EntityOps.Duplicate` re-points ACadSharp's
  cloned Layer/LineType back at the document's own table entries. MIRROR
  reflects each type exactly rather than through `ApplyTransform` with a
  reflection matrix, because a reflected Arc would come back with a -Z
  normal that nothing downstream (FD-Draft's own renderer included) reads:
  arcs keep their centre and swap ends, bulges change sign, a block gets
  rotation 2·axis − r and a negative Y scale. TEXT/MTEXT follow AutoCAD's
  MIRRTEXT 0 - anchor reflected, top/bottom anchoring swapped, and turned
  180° with left/right swapped if it would otherwise read upside down, so it
  covers the mirrored area but still reads forwards. OFFSET's math is
  `FdDraft.Core.Geometry.Construct.OffsetPolyline`: each span is shifted
  (lines) or made concentric (arcs, radius ∓ d by bulge sign), then
  consecutive spans are re-joined at their carriers' intersection nearest
  the naive joint (line-line, line-circle, circle-circle) - a mitred survey
  corner, and a tangent curve stays tangent. The side comes from the span
  nearest the pick. Dimensions are left out of COPY for now (their picture
  block is per-dimension).
- **LABEL** (`CourseLabelling`): the Annotator's straight-course and curve
  label construction is factored out as `Annotator.StraightCourseLabels` /
  `ArcCourseLabels` (the pipeline calls them too, so the two can't drift),
  and LABEL runs them at the midpoint of each selected Line, Arc or
  polyline span, converting each `DraftText` to a DWG TEXT aligned exactly
  as `TemplateDrafter` writes it. The scale comes from the current sheet's
  plan viewport (or, on Model, the first sheet with one), paper-native
  sheets label in paper units, and the standards are the drafted job's,
  else the Draft dialog's last standards file, else the built-in defaults.
  No collision sliding (that needs the job's points) - FLIP or MOVE a label
  that lands on something.
- **FLIP** (`LabelFlip` in `FdDraft.Cad.Editing`): for each selected
  TEXT/MTEXT, finds the nearest course span (Lines, Arcs and every span of
  the polylines in the label's own block, within 10 text heights), mirrors
  the label's anchor across it - across the line for a straight course,
  radially through the curve for an arc - and swaps its top/bottom
  anchoring. Annotator places a bearing bottom-anchored just above its
  course and a distance top-anchored just below, so that lands each at the
  same gap on the other side with the rotation untouched; flipping both
  swaps them. All labels flip as one undo step (`SetPropertyCommand` over a
  tuple of the fields that change). This needs no course↔label link: it is
  purely geometric, one label at a time, and never re-derives any text - the
  deferred relabel-after-STRETCH problem is unaffected.
- **JOIN**: each Line, Arc and open LwPolyline span becomes a
  `Construct.Piece` (a→b with a bulge); `Construct.JoinPieces` grows chains
  from both ends by matching endpoints within 1 mm, reversing a piece
  (swap ends, negate bulge) where it runs the wrong way, and marks a chain
  closed when its ends meet. Every chain built from two or more source
  entities (or a lone open polyline whose ends meet) becomes an LwPolyline
  on the first piece's layer/linetype/colour, replacing its sources in one
  `CompositeCommand`; pieces in different blocks never join.
- **TRIM / EXTEND / FILLET** (lines as targets): `EntityOps.SpansOf` turns
  any Line, Arc, Circle (two half arcs) or polyline into
  `Construct.Span`s, and `Construct.LineParamsOn` gives where a line's
  carrier crosses each span (on the segment, or on the arc's sweep only).
  TRIM removes the part around the pick between the nearest cuts either
  side (the line is shortened, split - the second piece a `Duplicate` added
  to the same block - or erased); EXTEND moves the end nearer the pick to
  the first crossing beyond it. FILLET (`Construct.Fillet`) intersects the
  two lines, keeps each one's end on its picked side of the corner, sets
  back r/tan(θ/2) to the tangent points and adds a CCW Arc on the first
  line's layer; r = 0 just runs both to the corner. All are single undo
  steps. `LabelFlip` now uses `SpansOf` too.
- **Polyline vertex insert/delete** (LwPolyline): `DeleteVertexCommand`
  removes one vertex and straightens the span that arrived at it (merging
  two arcs has no exact answer, and a straight join is what a drafter
  expects), restoring both the vertex and that bulge on undo;
  `InsertVertexCommand` splits the span after a vertex - a point on an arc
  span keeps the curve exactly (`Construct.Span.SplitBulges` gives each
  piece its own bulge), anything else gives two straight pieces. VXDEL/VXADD
  pick with `VertexEditing.NearestVertex` / `NearestSpan` (the latter
  projects onto the curve itself for an arc); the Properties panel's
  vertex rows grow **Delete vertex** and **Insert after** (at the span's
  arc-true midpoint) buttons. Polyline2D is left out: ACadSharp's
  `SeqendCollection` has no insert-at-index.
- **Undo/redo** (Ctrl+Z / Ctrl+Y): a linear `UndoStack` of `IEditCommand`s
  (`FdDraft.Cad.Editing`) - `AddEntitiesCommand`, `RemoveEntitiesCommand`,
  `TransformEntitiesCommand`. New commands truncate any redo history past
  them, like every other editor. This lives in `FdDraft.Cad`, not the WPF
  app, precisely so it can be unit-tested on the Linux build box.
- **LINE**: pick or type an E,N start point, then type `BEARING DISTANCE`
  legs (`N45-30-00E 125.50`, DMS or decimal, or a plain azimuth) - it chains
  like a data collector; blank ends it. Each leg is its own undo step.
- **LINE closure**: LINE keeps the list of points the traverse visited.
  `C` runs `FdDraft.Core.Geometry.ClosureReport.Of` over them - misclosure
  (end minus start, as dN/dE and a distance), precision 1:(traverse length
  ÷ misclosure), and the area once closed - then draws the closing course
  as an ordinary leg. `U` pops the last leg off both that list and the undo
  stack, like a data collector's "back up one".
- **AREA**: `FigureMeasure.Area/Perimeter` over a closed LwPolyline or
  Polyline2D's spans (arc segments included, via `Polygon.SignedArea` and
  `Construct.Spans`), or a circle; with no closed figure selected it is a
  pick-the-corners tool with a running total. Areas are printed the way the
  plan's own area labels are (`Annotator.FormatArea`, so a feet job gets
  ft² and acres).
- **ARC**: pick three points on the arc (start, a point on it, end); fit via
  the same `Arc.ThroughThreePoints` the drafting engine itself uses.
- **TEXT** / **LEADER**: pick a point (LEADER: two - the feature, then the
  text), then type the text, optionally prefixed with a height
  (`"0.25 LOT 5"`; default 0.2). LEADER places a real `ACadSharp.Entities.Leader`
  (not hand-drawn lines) - `ArrowHeadEnabled`, `Style` set to the default
  dimension style (resolved against the document's own `DimensionStyles`
  table once added, registering "Standard" if it isn't there yet) - plus a
  separate TEXT entity for the annotation, as one undo step. `SceneBuilder`
  draws a Leader's own vertices and, when its arrowhead flag is set, a small
  triangle at the first vertex aimed back along the shaft, so it still shows
  up in FD-Draft's own canvas/PDF/SVG, not just in AutoCAD/MSCAD. Covered by
  an in-memory test and a real DWG write/read round trip (no firm .dwt
  needed for the latter, unlike most DWG tests).
- **DIM** places a real `ACadSharp.Entities.DimensionAligned`
  (`FdDraft.Cad.Editing.DimensionBuilder`): first/second points, the
  definition point on the dimension line, override text = the measured
  distance to the standards' distance decimals. A DWG dimension's visible
  "picture" is its own anonymous `*D` block; ACadSharp's generator for it
  puts both arrowheads at the first end and never turns the text to the
  line, so FD-Draft calls `UpdateBlock()` only to register/clear that block
  and then draws the picture itself (`DrawPicture`): extension lines with an
  offset gap and overshoot, the dimension line, two filled SOLID arrowheads
  (one at each end), an MTEXT turned readable along the line, and the three
  definition points on `defpoints`. The picture is in world coordinates, so
  `TransformEntitiesCommand` redraws it after MOVE/ROTATE and
  `AddEntitiesCommand` draws it whenever an aligned dimension is (re)added
  (undo/redo detaches the block). COPY/MIRROR rebuild an FD-Draft dimension
  from its moved/reflected definition points (`DimensionBuilder.Rebuilt`)
  rather than cloning it, since a clone would share its source's block.
  Only the exact `DimensionAligned` type gets this - `DimensionLinear`
  derives from it, and a template's other dimension kinds keep their own
  picture. Covered by a real DWG write/read round trip.
- New entities pick up the toolbar's **current layer**, created on the fly
  if the template doesn't have it yet, and land in the right block: Model
  space, unless the current sheet is a layout with no working viewport at
  all, in which case they go straight into that layout's own block in paper
  coordinates (`MainWindow.CurrentEntityOwner`) - the same paper-native case
  `Scene.ModelAt`'s fallback above exists for.
- Bearing/leg parsing (`Cogo`) lives in `FdDraft.Core.Geometry` for the same
  testability reason as the edit commands.
- **Codes** panel lists the job's `codes.csv` (key, description, layer).
  **Properties** panel summarises the current selection (layer, handle, and
  type-specific geometry: a line's bearing/distance, an arc or circle's
  radius, a text's content). When exactly one entity is selected, the panel
  also grows editable fields for that type - TEXT/MTEXT get their content
  (Enter or **Apply text**) via `EditTextCommand`. Numeric fields are built
  per entity type and applied together via the generic
  `SetPropertyCommand<T>` (a setter delegate, so one command class covers
  any scalar property rather than a bespoke class per field): a Circle's
  radius; an Arc's radius, start angle and end angle (degrees); a
  TextEntity's height and rotation (degrees); an MText's height and rotation
  (MText.Rotation is computed, read-only, from `AlignmentPoint` treated as a
  direction vector, so "setting" it means writing
  `AlignmentPoint = (cos, sin, 0)` instead); a Line's start/end E,N; and, for
  an LwPolyline/Polyline2D, one vertex's E,N via a `VertexRef` (the same type
  `StretchVertexCommand` uses) - a "Vertex # (0..N-1), Enter to jump" box
  picks which vertex the E/N fields below it edit, since a polyline can have
  many. Changing more than one field and clicking Apply pushes a
  `CompositeCommand` wrapping every changed field's `SetPropertyCommand`, so
  they undo/redo together as one step rather than one Ctrl+Z per field.
  **Set Layer** on the toolbar reassigns the whole selection to the current
  layer via `ChangeLayerCommand`, also undoable.
- **VPSCALE** (`FdDraft.Cad.Editing.SheetScale`): the scale-only sheet
  setup. The current scale is read from the sheet's own "SCALE 1:n" text
  (the standards' scale-bar anchor), falling back to the plan viewport's
  ViewHeight/Height as metres on a mm sheet. The plan viewport (the sheet's
  largest working one - detail viewports keep their scale) gets ViewHeight
  × new/old, keeping its ViewCenter; every sheet text containing "1:old"
  gets "1:new"; tick labels 3-10 mm above the anchor are relabelled with
  the same rule and `TitleBlockFiller.RelabelTick` the drafter uses; and,
  optionally, every model-space TEXT/MTEXT height, block scale and
  FD-Draft dimension text height is multiplied by the same ratio so it
  keeps its paper size. One `CompositeCommand`. Label *positions* are not
  moved, so a full relayout at the new scale is still Ctrl+D.
- Re-running the sheet/scale choice on an already-open job is Ctrl+D again -
  Draft FD-Pro job re-opens the ranked list pre-filled with the last job,
  template and standards.
- **Opening any DWG** (not just FD-Draft's own output) logs, per sheet, how
  much it actually has drawn on it - a real firm's legacy DWG commonly
  carries several unused blank sheet-size layouts (leftover template
  options) alongside the one actually plotted, and they look identical in
  the tab strip. FD-Draft renders each sheet's own display list and counts
  the primitives; a sheet at or under the title-block's own handful is
  flagged blank, so the drafter isn't left guessing which tab has the plan.
  Draft (Ctrl+D) is unrelated to whichever DWG is open - it always drafts
  from a raw FD-Pro job folder onto the template - so opening a legacy DWG
  with no FD-Pro data behind it correctly leaves nothing to draft; view,
  edit and plot it directly instead. If the opened file does look like
  FD-Draft's own prior output (`<job>\export\fd-draft\<job>.dwg`), its job
  folder is picked up automatically so Ctrl+D re-drafts that same job
  rather than whatever was drafted last.
- **`Scene.ModelAt`** falls back to the paper point itself on a layout with
  no working viewport at all, rather than returning null - a real MSCAD job
  commonly draws its plan directly onto paper space on the one sheet it
  actually used (the mandatory background "represents paper" viewport is
  never a real one; see the empty-layout note above), and without this
  fallback none of INV/LINE/ARC/TEXT/LEADER/MOVE/ROTATE/STRETCH could pick a
  point on that content at all. A sheet that does have a real viewport is
  unaffected: a pick outside it is still ambiguous and stays null.

### Next in the app

- Relabel courses after a STRETCH (the vertex moves and connected lines stay
  joined, but their bearing/distance/area labels are not yet re-derived -
  there is still no persisted link between a course and its label text).
- A sheet setup panel for page size/layout changes (VPSCALE now covers
  scale-only changes in place).
- LEADER's annotation is still a separate, unassociated TEXT entity next to
  a real Leader, not linked as its `AssociatedAnnotation` (that setter is
  `internal` to ACadSharp - not reachable from FD-Draft) or an MTEXT with a
  real dimension-style-driven landing gap. Aligned DIMENSIONs exist (DIM);
  linear (rotated), angular and radial ones don't yet.
- Properties editing now covers layer, text content, a text's height and
  rotation, a Circle/Arc's radius, an Arc's start/end angle, a Line's
  endpoint coordinates, and one LwPolyline/Polyline2D vertex's E,N at a time
  (multi-field edits undo as one step), and LwPolyline vertices can be
  inserted/deleted (VXADD/VXDEL, or the Properties buttons). Still missing:
  the same for Polyline2D, and a way to renumber which vertex is which on a
  closed polyline.

## After that: the document assistant (hybrid)

It reads the job's research folder: R-plans, registered plans, parcel register /
PIN pages, and deeds.

1. **Pages.** Text PDFs are read directly. Scans go through Windows' built-in
   offline OCR first, with the Claude API (vision) as an opt-in for hard scans.
2. **Research pack.** Extraction produces record courses per plan (P1, P2 …),
   monuments, PINs, parts, instruments and easements, and lot/concession.
3. **Use.** Record courses are matched to surveyed ones and the bearing rotation
   between them is computed, reusing the boundary-reconciliation fit. Record
   labels go on `PLAN-BDComparison`, and discrepancies are flagged. The R-plan
   schedule and the title block are filled, and the plan type is suggested. The
   assistant explains or overrides the deterministic sheet picker; it never
   replaces it.
4. **Guardrails.** Every value carries its source page and a confidence.
   Nothing reaches a plan without the drafter confirming it. Documents leave the
   PC only when the cloud switch is on.

## Known limits (v0.2)

- North-up viewports only (no twist to fit a rotated lot).
- Splines are written as dense polylines through the fitted curve.
- Label collisions are handled locally (sliding along the course), not by a full solver.
- Long MTEXT notes are only rewritten when a rule matches their raw contents.
- The DWG has been checked with two independent readers; opening it in AutoCAD
  and MSCAD is the remaining check.
