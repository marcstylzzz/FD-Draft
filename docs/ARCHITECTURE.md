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
- **Erase** (Del, or the ERASE command) and **MOVE** / **ROTATE** transform
  the selected entities in place via `ACadSharp.Entities.Entity.ApplyTransform`.
  ROTATE takes the pivot by pick and the angle by typing degrees, clockwise
  (survey convention); the entity math is verified in
  `tests/FdDraft.Tests` against a real `CadDocument`, both about the origin
  and about an arbitrary pivot.
- **Undo/redo** (Ctrl+Z / Ctrl+Y): a linear `UndoStack` of `IEditCommand`s
  (`FdDraft.Cad.Editing`) - `AddEntitiesCommand`, `RemoveEntitiesCommand`,
  `TransformEntitiesCommand`. New commands truncate any redo history past
  them, like every other editor. This lives in `FdDraft.Cad`, not the WPF
  app, precisely so it can be unit-tested on the Linux build box.
- **LINE**: pick or type an E,N start point, then type `BEARING DISTANCE`
  legs (`N45-30-00E 125.50`, DMS or decimal, or a plain azimuth) - it chains
  like a data collector; blank ends it. Each leg is its own undo step.
- **ARC**: pick three points on the arc (start, a point on it, end); fit via
  the same `Arc.ThroughThreePoints` the drafting engine itself uses.
- **TEXT** / **LEADER**: pick a point (LEADER: two - the feature, then the
  text), then type the text, optionally prefixed with a height
  (`"0.25 LOT 5"`; default 0.2). LEADER draws a shaft, a small arrowhead
  aimed back along it, and the text as one undo step.
- New entities pick up the toolbar's **current layer**, created on the fly
  if the template doesn't have it yet.
- Bearing/leg parsing (`Cogo`) lives in `FdDraft.Core.Geometry` for the same
  testability reason as the edit commands.
- **Codes** panel lists the job's `codes.csv` (key, description, layer).
  **Properties** panel summarises the current selection (layer, handle, and
  type-specific geometry: a line's bearing/distance, an arc or circle's
  radius, a text's content). When exactly one TEXT or MTEXT is selected, its
  content is also editable right there (Enter or **Apply text**) via
  `EditTextCommand` - an ordinary undo step. **Set Layer** on the toolbar
  reassigns the whole selection to the current layer via
  `ChangeLayerCommand`, also undoable.
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

### Next in the app

- Relabel courses after a point moves (MOVE currently moves the linework;
  bearing/distance/area labels are not yet re-derived).
- Flip labels; multi-point (not just Del-all) partial erase of a polyline
  vertex; a dedicated sheet setup panel (rather than reusing Draft FD-Pro
  job) for scale-only changes without re-running the whole pipeline.
- Real leaders/dimensions as ACadSharp `Leader`/`Dimension` entities instead
  of plain lines, so they read back as leaders in AutoCAD/MSCAD too.
- Properties editing so far covers layer and text content; still read-only
  for everything else (a line's endpoints, an arc/circle's radius, a text's
  height or rotation).
- A STRETCH tool to move a single vertex/endpoint (rather than a whole
  entity) and drag its connected lines with it, which is the actual
  prerequisite for relabelling courses - today MOVE/ROTATE only transform
  whole selected entities rigidly, so a course's true bearing/distance
  never changes under the current toolset and its label never goes stale.

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
