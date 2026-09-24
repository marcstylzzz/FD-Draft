# FD-Draft architecture

## Shape

```
 FD-Pro job folder            firm .dwt            firm standards .ini
 (points, figures, codes)     (layers, blocks,      (code->layer/block, labels,
          |                    title blocks)         scales, sheet areas, TB rules)
          v                         |                        |
   FdJobReader ------> DraftBuilder (geometry, layers, courses, parcels)
                              |
                         SheetPicker  <---- sheet areas (frame minus keepouts)
                              |
                          Annotator (labels at the chosen scale)
                              |
                        DraftDocument  (CAD-neutral, the single source of truth)
            ______________/   |    \________________
           v                  v                     v
   AutoCAD plugin        DxfWriter             SvgPreview
   (template, viewport,  (R12, any CAD)        (sheet preview)
    title block, PDF)
```

`FdDraft.Core` has no AutoCAD reference and no package dependencies. The same
`DraftDocument` goes to every writer, so the CLI preview shows what AutoCAD gets.
It builds for net8.0 and netstandard2.0, which covers AutoCAD 2025+ (.NET 8),
AutoCAD 2021–2024 (.NET Framework 4.8), and the standalone app to come.

## Decisions

**Reading FD-Pro.** Formats follow FD-Pro's `storage/format` code exactly:
columns found by header name, bare PNEZD accepted, skipped rows reported rather
than silently dropped, `job.ini` escaping, figures split into pieces with the
same round-199 rule (arc spans in pairs, odd leftover = straight). FD-Pro's
on-screen spline is a cosmetic curve; FD-Draft draws a real fit-point spline.

**Layers.** A figure's layer comes from `[line-layers]` (e.g. monument codes to
`PLAN-SubjectBoundary`), else the code's `LINE LAYER` from the job's
`codes.csv`. Point layers follow the template's `MSPOINT-/POINTNUMBER-/ELEVATION-
{feature}` scheme; `{feature}` is the code's layer without dashes, with a
`[feature-map]` for the template's exceptions. The template wins every tie;
anything created is listed in the report.

**Sheet and scale.** The template's layouts come without a model viewport, so
each layout is described by its frame and keepouts (title column, schedule box).
FD-Draft finds the maximal free rectangles, then for every layout the largest
standard scale at which survey plus label margin fits. It prefers the smallest
paper where ≥90 % of labelled courses are long enough to hold their bearing, and
returns the full ranking with reasons.

**Labels.** Heights are paper mm; the model height follows from the scale.
Bearings are quadrant DMS with a whole-second carry (never 60"). A label slides
along its course when a surveyed point sits under it. Shared courses between
lots are labelled once. An arc through a monument becomes two curves.

**Title block.** The template's title blocks are plain TEXT/MTEXT, not
attributes, so they are filled by regex rules (`[titleblock-replace]`). Scale-bar
ticks are recomputed from the template's own "SCALE 1:n" text.

## Phase 2 — the document assistant (hybrid)

Goal: read the job's research folder (R-plans, registered plans, PIN / parcel
register pages, deeds / instruments) and help draft.

1. **Ingest.** Each PDF or image is split into pages. Text-layer PDFs are read
   directly. Scans go to local OCR first (Windows' built-in `Windows.Media.Ocr`,
   offline, no install), with the Claude API (vision) as the optional switch for
   hard scans and handwritten plans. Per-page results are cached in the job folder.
2. **Extract** to a research pack (`research.json` next to the job):
   record courses per plan (P1, P2 ...) with bearing, distance and the monuments
   at each end; PINs and parts; instrument numbers and easements; lot/concession/
   township; plan numbers and dates; the bearing reference note.
3. **Use it in the plan.**
   - Match record courses to surveyed courses and compute the rotation between
     record and grid bearings (the template's "FOR BEARING COMPARISONS, A
     ROTATION OF ..." note) - best-fit through the existing boundary
     reconciliation method.
   - Place record labels on `PLAN-BDComparison` (e.g. `N45°12'30"E (P1)`), and
     flag discrepancies beyond tolerance.
   - Fill the title block and R-plan parts schedule (lot, concession, PIN, area)
     and the legend of plans referred to (P1 ... Pn).
   - Pick the plan type (an R-plan job needs the parts schedule → `rplan`
     family) and explain the sheet and scale choice. The picker stays
     deterministic; the assistant explains and overrides it, never replaces it.
4. **Guardrails.** Every extracted value carries its source page and a
   confidence value, and nothing extracted goes on a plan without the drafter
   confirming it. Documents leave the PC only when the cloud switch is on.

## Phase 3 — standalone front end

A WPF app on the same core: opens the template through a DWG library (ACadSharp
to start, ODA Drawings SDK if full fidelity is needed), shows the plan, writes
DWG and PDF without an AutoCAD licence. The AutoCAD plugin stays as the
power-user path.

## Known limits (v0.1)

- North-up viewports only (no twist to fit a rotated lot).
- Label collision handling is local (slide along the course), not a full solver.
- Short courses below `min_labelled_course_mm` are reported, not tabulated yet.
- A standards file needs `[sheet.*]` areas per layout; a new firm template needs
  them measured once.
- The AutoCAD plugin was compiled against a stand-in of the AutoCAD API, not
  AutoCAD itself - first build on a machine with AutoCAD will confirm it.
