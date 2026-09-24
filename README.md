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
- **Save** (Ctrl+S) writes the DWG. **PDF** (Ctrl+P) plots the current sheet as
  a true-scale vector PDF.
- **Select**: click an entity (Ctrl+click adds); **Del** erases; **Ctrl+Z** /
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
- **VXDEL** removes one polyline vertex (pick it) without erasing the
  whole polyline; **VXADD** adds one where you pick on a span - on an arc
  it lands on the curve and splits the arc exactly.
- **Line** draws by bearing and distance: pick or type an E,N start point,
  then type each leg (`N45-30-00E 125.50`), chaining like a data collector -
  blank ends it. **Arc** fits three picked points. **Text** and **Leader**
  place labels (type `height text`, e.g. `0.25 LOT 5`, or just the text for
  the default height) - Leader places a real DWG leader entity, with an
  arrowhead, not just plain lines, so it reads back as one in AutoCAD/MSCAD
  too. New linework goes on the toolbar's **Layer** box, and lands on the
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
- Opening any DWG logs which sheets actually have a plan drawn on them - a
  legacy DWG commonly carries several unused blank sheet-size layouts, and
  they look identical in the tab strip until you check.
- **Command line**: typing anywhere goes there. Commands are DRAFT, OPEN, NEW,
  SAVE, SAVEAS, PDF, INV, LINE, ARC, TEXT, LEADER, MOVE, ROTATE, STRETCH,
  COPY, MIRROR, OFFSET, VXDEL, VXADD, ERASE, LAYER, UNDO, REDO, CLAYER, ZE, SNAP, MODEL, LAYOUT <name>, and HELP.

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
