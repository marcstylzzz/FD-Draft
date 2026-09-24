# FD-Draft

A Windows survey-drafting program, in the spirit of MicroSurvey CAD, that drafts
plans from **FD-Pro job folders** onto the firm's own **.dwt** template. Firms
already have their templates set up, so FD-Draft uses those as they are. It opens
the .dwt itself, drafts into it and saves a real DWG. No AutoCAD, MSCAD or other
CAD program is involved.

Status: **v0.2**. The drafting engine and the DWG layer work end to end from the
command line. The desktop application (canvas, layers, point table, editing
tools) is the next milestone, and the document assistant (plans, PINs, deeds)
comes after it. See `docs/ARCHITECTURE.md`.

```
FD-Draft/
  src/FdDraft.Core/        drafting engine: FD-Pro reader, geometry, labels, sheet/scale picker, title-block rules
  src/FdDraft.Cad/         DWG layer: open the .dwt, draft into it, save DWG; template inspector
  tools/FdDraft.Cli/       fddraft.exe - the engine from the command line
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

## Draft a plan

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
