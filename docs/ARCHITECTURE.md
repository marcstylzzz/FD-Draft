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

### Next in the app

- Editing: move, rotate and flip labels; erase; undo. Relabel courses after a
  point moves.
- Drafting tools: line/arc by bearing and distance, text, leaders, building
  ties, dimensions.
- A sheet setup panel that re-runs the sheet/scale choice on an open plan.
- Properties panel for the selected entity; a code list panel.

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
