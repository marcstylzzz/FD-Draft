# FD-Draft

A Windows survey-drafting program, in the spirit of MicroSurvey CAD, that drafts
plans from **FD-Pro job folders** onto the firm's own **.dwt** template. Firms
already have their templates set up, so FD-Draft uses those as they are. It opens
the .dwt itself, drafts into it and saves a real DWG. No AutoCAD, MSCAD or other
CAD program is involved.

Status: **v0.4**. The desktop application (**FD-Draft.exe**) opens, drafts,
views, inverses, saves DWG and plots PDF - and now edits: select, erase,
move, rotate, retype text, reassign layers, undo/redo, and draw lines by
bearing and distance, arcs, text and leaders. The command-line tool runs the
same engine. The document assistant (plans, PINs, deeds) comes next. See
`docs/ARCHITECTURE.md` for how it's built, and **`docs/PROGRESS.md`** for
current status and what's next - read that one first in a new chat/session.

```
FD-Draft/
  src/FdDraft.Core/        drafting engine: FD-Pro reader, geometry, labels, sheet/scale picker, title-block rules
  src/FdDraft.Cad/         DWG layer: open the .dwt, draft into it, save DWG; template inspector
  src/FdDraft.View/        what the app draws: DWG -> display list (model and sheets), snaps, inverse, PDF
  src/FdDraft.App/         FD-Draft.exe - the Windows app (WPF)
  tools/FdDraft.Cli/       fddraft.exe - the engine from the command line
  tools/wpf-compile-check/ compiles the app against WPF's public API on a non-Windows box
  tests/FdDraft.Tests/     plain console test runner
  external/ACadSharp/      DWG/DXF library (MIT), git submodule at a pinned commit
  standards/               provision-2024.standards.ini - rules for the example template
  samples/demo-lot/        a synthetic FD-Pro job
```

## Update

Double-click **`update.cmd`** in the FD-Draft folder: it pulls the latest
version, builds it and starts the app (close FD-Draft first). By hand, from
the FD-Draft folder: `git pull --recurse-submodules`, then
`dotnet build FD-Draft.sln`.

## Build

Needs the **.NET 10 SDK** (the bundled ACadSharp source uses C# 13). FD-Draft
itself targets .NET 8.

```powershell
git submodule update --init --recursive     # if you cloned rather than unzipped
dotnet build FD-Draft.sln
dotnet run --project tests\FdDraft.Tests
```

Set `FDDRAFT_TEST_DWT` to a template path to include the DWG tests.

## The app

Run `src\FdDraft.App\bin\Debug\net8.0-windows\FD-Draft.exe` (or F5 on the
FdDraft.App project in Visual Studio).

- **Draft job** (Ctrl+D): pick the FD-Pro job folder, the firm .dwt and its
  standards file, and the plan type. Every sheet is ranked with the scale it
  fits at and why. Accept the top one, or pick another sheet or scale. The plan
  opens on its sheet, and the job's points fill the **Points** panel
  (double-click a point to zoom to it).
- **Model space is black, sheets are white**, as in AutoCAD/MSCAD (colour 7
  draws white on black; plots are unaffected).
- **View toolbar**: Regen, Pan (left-drag pans until you click it again or
  press Esc), Zoom Window, Zoom Previous, Zoom In/Out, Extents - also typed:
  REGEN, PAN, ZW, ZP, ZI, ZO, ZOOM W/P/I/O.
- **Object Snap toolbar**: End, Mid, Int (intersection), Cen, Quad, Perp,
  Near, Node - each on/off (orange = on), ✕ None; remembered between
  sessions. F3 still switches all snapping off/on.
- **Right-click** an item for the edit menu (Undo, Redo, Erase, Select all,
  Select same layer, plus Move, Copy, Rotate, Mirror, Change to current
  layer, Properties, zooms); while a command is waiting, right-click is
  Enter, as in AutoCAD.
- **Canvas**: the wheel zooms at the cursor. Middle-drag or Shift-drag pans, and
  a middle double-click zooms to extents. Tabs along the bottom switch between
  Model and the sheets. On a sheet, the status bar shows the model N/E under the
  cursor through the viewport.
- **Snaps** (F3) to surveyed points, line ends and midpoints, and arc centres.
- **Inverse** (INV): pick points for bearing, distance, dN and dE. It chains
  from point to point like a data collector, and works on Model or through a
  sheet's viewport. Esc ends it.
- **Layers** panel: display on/off for each layer. Layers frozen in the drawing
  are shown greyed.
- **Save** (Ctrl+S) writes the DWG. **Print** (Ctrl+P) opens the plot dialog,
  laid out like AutoCAD's: printer (FD-Draft's own vector PDF, or any
  installed Windows printer/plotter), paper size, copies, **plot style
  table (.ctb)**, what to print (layout, extents, display, or a picked
  window), scale or fit to paper, offset or centre, lineweights on/off,
  portrait/landscape/upside-down, a **Preview**, and **Apply to Layout**,
  which saves the setup into the DWG's layout the way AutoCAD does (so
  the sheet opens with it next time, in FD-Draft, AutoCAD or MSCAD).
  .ctb files are found in FD-Draft's own `%APPDATA%\FD-Draft\Plot Styles`
  folder, the template's folder, AutoCAD/MicroSurvey "Plot Styles" folders,
  or wherever you browse to with the … button; monochrome and grayscale
  are built in. A .ctb maps each colour number to a pen colour,
  lineweight and screening, just as it does in AutoCAD.
- **Tool palette** (right edge): hover a tab - Text Styles, Useful Tools,
  Line Styles - and its buttons slide out over the drawing; they tuck away
  when the mouse leaves (📌 keeps them open). A Text Styles button makes its
  layer current and starts TEXT in its style at its paper height, scaled to
  the sheet (PART NUMBER, PIN NUMBER, ROAD NAME...); a Line Styles button
  makes its layer current and starts LINE; Useful Tools are one-click
  FD-Draft commands. It's all one editable file, `%APPDATA%\FD-Draft\palette.ini`
  (✎ on the palette opens it; saving it updates the palette).
- **The list on the left follows the plan**: click something on the plan
  and whichever tab is open follows it, selected in blue and scrolled to
  the middle - **Points**: its survey point (node, symbol, number,
  elevation or code label); **Layers**: its layer; **Codes**: its FD-Pro
  code (drafted linework carries its figure code). It never switches tabs.
  Click a row in the Points list and that point's entities are highlighted
  on the plan; double-click still zooms to it.
- **Drag to move**: press on any entity - a label, a line, a symbol - and
  drag it where you want it; if it's part of the selection the whole
  selection goes with it. One Ctrl+Z undoes it. Clicking anywhere on a
  label's text selects it (it wins over a point marker or line under it).
- **Select**: click an entity (Ctrl+click adds), or drag a box - left to
  right takes what is wholly inside it (window), right to left anything it
  touches (crossing); Ctrl+drag adds. **Ctrl+A** selects everything in
  view, **SELLAYER** everything on a layer (or on the selection's own
  layers). **Del** erases; **Ctrl+Z** /
  **Ctrl+Y** undo/redo. **Move** and **Rotate** act on the selection (Rotate
  picks the pivot, then type the angle in degrees, clockwise). **Stretch**
  moves just one vertex - select the line(s) or polyline sharing it, pick
  the vertex (snap helps) then its new spot, and every one of them stays
  joined there, like a grip edit.
- **Copy** repeats the selection from a base point to as many destinations
  as you pick. **Mirror** reflects it across a picked line (optionally
  erasing the originals) - text is mirrored the way a plan needs it: in the
  right place, but still reading forwards. **Offset** makes a parallel copy
  of lines, arcs, circles and polylines at a typed distance toward a picked
  side - polyline corners are mitred and curves stay concentric, so an
  offset boundary or road allowance comes out right.
- **Area** reports the area (m² and ha, or ft² and acres for a feet job)
  and perimeter of each selected closed polyline or circle, arcs included;
  with nothing closed selected, pick the corners and it keeps a running
  total.
- **Annotate toolbar** - MSCAD's eight auto labels: split bearing (on the
  centre of the line, the line broken around it), bearing off line, split
  distance, distance off line, bearing/distance, bearing-distance (one
  line), bearing/distance // line and distance/bearing // line (stacked on
  the picked side). Pick lines one after another; for the off-line styles,
  pick on the side the label should go. Esc or right-click ends. Commands:
  BRGON, BRGOFF, DISTON, DISTOFF, BRGDIST, BRGDASH, BRGDISTL, DISTBRGL.
- **Label** adds bearing and distance (or radius/arc/chord for a curve) to
  the selected lines, arcs and polyline spans, by exactly the rules Draft
  uses - the firm's text heights, layers, styles and bearing format - at
  the sheet's own scale.
- **Flip** moves the selected bearing/distance (or curve-data) labels to
  the other side of their course, at the same gap and still reading the
  same way - select both labels of a course to swap them.
- **Join** turns selected lines, arcs and open polylines that meet end to
  end into one polyline (closed when it closes, arcs kept as true curves),
  so a boundary drawn in pieces can then be measured with Area, offset or
  labelled as one figure. **ID** reads off the N/E of picked points (and the
  survey point's number and elevation when you snap to one).
- **Trim** and **Extend** cut lines back at, or run them out to, the
  selected edges (or every line, arc, circle and polyline in view when
  nothing is selected) - pick each line on the part to cut / near the end
  to lengthen. **Fillet** rounds the corner between two lines with a typed
  radius (a corner rounding or daylighting curve), cutting both back to
  their tangent points; radius 0 closes them to a sharp corner.
- **VXDEL** removes one polyline vertex (either polyline type) (pick it) without erasing the
  whole polyline; **VXADD** adds one where you pick on a span - on an arc
  it lands on the curve and splits the arc exactly.
- **Line** draws by bearing and distance: pick or type an E,N start point,
  then type each leg (`N45-30-00E 125.50`), chaining like a data collector -
  blank ends it. Type **C** to close back to the start: it draws the
  closing course and reports the misclosure (dN, dE), the precision
  (1:n over the traverse length) and the closed area; **U** takes back
  the last leg. **Arc** fits three picked points. **Text** and **Leader**
  place labels (type `height text`, e.g. `0.25 LOT 5`, or just the text for
  the default height) - Leader places a real DWG leader entity, with an
  arrowhead, not just plain lines, so it reads back as one in AutoCAD/MSCAD
  too. **Dim** places a real aligned DIMENSION: pick the two points,
  then where the dimension line goes, then the text height - it shows the
  measured distance with extension lines and arrowheads, and reads back as
  a dimension in AutoCAD/MSCAD. **DimLin** measures just the dE or dN
  (horizontal when you place it above/below the points, vertical beside
  them, or type H, V or an angle), **DimRad** / **DimDia** label an arc's
  or circle's radius / diameter, and **DimAng** dimensions an angle in
  degrees, minutes and seconds (where you place its arc picks which of the
  two angles). New linework goes on the toolbar's **Layer** box, and lands on the
  current sheet correctly even when that sheet has no real viewport of its
  own (a real MSCAD job commonly draws straight onto paper on the sheet it
  actually used).
- **Codes** panel lists the job's codes; **Properties** summarises the
  current selection and, for a single entity, grows editable fields for it:
  a TEXT/MTEXT's content (Enter or **Apply text**), plus, depending on the
  entity, its text height, rotation, a Circle/Arc's radius, an Arc's start
  and end angle, a Line's endpoint coordinates, or - for an
  LwPolyline/Polyline2D - one vertex's E,N, picked by typing its number
  (**Apply**), with **Delete vertex** / **Insert after** buttons beside
  it for a polyline. Editing several fields at once and clicking Apply undoes
  them together as one step. **Set Layer** on the toolbar reassigns the
  whole selection to the current layer.
- **Add viewport to sheet** (MVIEW, View menu): on a sheet tab that shows
  only its title block - a legacy job's unused sheet-size tabs are like
  this - pick two corners and a scale (Enter fits the whole survey at the
  next standard scale), and model space shows through it, locked at scale,
  on the standards' viewport layer. Opening a DWG logs which tabs actually
  have a plan on them.
- **Sheet scale** (VPSCALE, View menu): change the current sheet's scale
  in place - type `1:250` and the plan viewport re-zooms about its centre,
  the title block's `1:n` and the scale-bar ticks are rewritten, and
  (unless you answer N) the model's labels, symbols and dimension text are
  resized to keep their size on paper. One Ctrl+Z undoes it all. For a full
  relayout of the labels at the new scale, re-draft with Ctrl+D.
- Opening any DWG logs which sheets actually have a plan drawn on them - a
  legacy DWG commonly carries several unused blank sheet-size layouts, and
  they look identical in the tab strip until you check.
- **Command line**: typing anywhere goes there. Commands are DRAFT, OPEN, NEW,
  SAVE, SAVEAS, PDF, INV, ID, AREA, JOIN, LINE, ARC, TEXT, LEADER, DIM, DIMLIN, DIMRAD, DIMDIA, DIMANG, MOVE, ROTATE, STRETCH,
  COPY, MIRROR, OFFSET, TRIM, EXTEND, FILLET, VXDEL, VXADD, LABEL, FLIP, ERASE, LAYER, UNDO, REDO, CLAYER, MVIEW, VPSCALE, SELALL, SELLAYER, ZE, SNAP, MODEL, LAYOUT <name>, and HELP.

## Draft a plan from the command line

```powershell
fddraft C:\...\FD-PRO\Project\24-012 --template ProVisionTemplate-2024.dwt --standards standards\provision-2024.standards.ini
```

It ranks every sheet in the plan type with the scale that fits and why, then
writes `<job>\export\fd-draft\`:

- **`<job>.dwg`** is the plan on a copy of the template:
  - linework on the codes' layers (boundary codes go on `PLAN-SubjectBoundary`), with true arcs;
  - the template's own symbol blocks sized for the scale, and a node at each point's elevation;
  - bearings and distances (`PLAN-Bearing` / `PLAN-Distance`) and curve data; an arc through a monument is split in two;
  - point numbers, elevations, monument text and the lot area;
  - a plan viewport in the chosen layout, locked at scale on `Defpoints`, plus the north arrow;
  - the title block filled in and the scale bar relabelled;
  - the plan type's layers frozen and the unused layouts removed.

  The template wins every tie. Anything it lacked is created and listed in the report.
- **`<job>.pdf`** is the sheet as a true-scale vector PDF.
- **`<job>.svg`** is a preview of the plan area on the chosen sheet.
- **`<job>.report.txt`** holds the ranking, parcel areas and perimeters, and what was created or missing.

Options: `--family rplan`, `--layout RPLAN-22X34`, `--scale 500`, `--dxf`.
The .dwt is only read. FD-Pro's job files are never written.

## A new firm's template

```powershell
fddraft inspect C:\Standards\FirmTemplate.dwt
```

This lists the template's layouts, paper sizes, drawing frames and title-block
areas, and prints a starter `[sheet.*]` block for that firm's standards file. A
drafter checks it once. The rest of the standards file (code→layer/block
mapping, text styles, which codes are boundary, title-block rules) starts from
`provision-2024.standards.ini`.
