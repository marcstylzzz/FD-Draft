# FD-Draft

Drafts a survey plan from an **FD-Pro job folder** on the firm's own **.dwt**
template. It picks the sheet and scale, places bearings, distances, curve data,
monuments, point numbers, elevations and areas on the template's layers, fills in
the title block, and saves a DWG and a PDF.

Status: **v0.1, phase 1** (FD-Pro job → drafted plan). The document assistant
(plans, PINs, deeds) is phase 2 — see `docs/ARCHITECTURE.md`.

```
FD-Draft/
  src/FdDraft.Core/       drafting engine - no AutoCAD, no packages; shared by every front end
  src/FdDraft.AutoCAD/    AutoCAD plugin: FDDRAFT, FDDRAFTSETUP
  tools/FdDraft.Cli/      fddraft.exe - same engine, writes DXF + SVG preview + report
  tests/FdDraft.Tests/    21 tests (plain console runner)
  standards/              provision-2024.standards.ini - firm rules for ProVisionTemplate-2024.dwt
  samples/demo-lot/       a synthetic FD-Pro job to try it on
  docs/ARCHITECTURE.md    how it works and what comes next
```

## Build

Needs the .NET 8 SDK (and Visual Studio 2022 if you prefer the IDE: open `FD-Draft.sln`).

```powershell
# engine, CLI and tests
dotnet build tools\FdDraft.Cli
dotnet run --project tests\FdDraft.Tests

# the AutoCAD plugin - point it at YOUR AutoCAD
dotnet build src\FdDraft.AutoCAD -c Release -p:AcadYear=2025 -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025"
```

`AcadYear` 2025 or later builds for .NET 8; 2024 or earlier builds for .NET
Framework 4.8 (AutoCAD's own requirement). The plugin references AutoCAD's
`accoremgd/acdbmgd/acmgd.dll` straight from the install folder.

## Use in AutoCAD

1. `NETLOAD` → `src\FdDraft.AutoCAD\bin\Release\<framework>\FdDraft.AutoCAD.dll`
2. `FDDRAFTSETUP` → pick `ProVisionTemplate-2024.dwt`, then `standards\provision-2024.standards.ini` (remembered).
3. `FDDRAFT` → pick the job's `job.ini` or `points.csv` → plan type `Topo`/`Rplan`
   → FD-Draft lists every sheet with the scale it fits at and why → `Accept`, or
   `Layout` / `Scale` to override.

It opens a new drawing from the template and:

- draws linework on the codes' layers (boundary codes go on `PLAN-SubjectBoundary`)
  with true arcs and splines;
- inserts the template's symbol blocks (found/set monuments, manholes, hydrants ...)
  sized for the scale, plus a point node at its true elevation;
- labels boundary courses with bearing (`PLAN-Bearing`) and distance
  (`PLAN-Distance`), and curves with R / A / C / chord bearing; an arc through a
  monument is split in two;
- adds point numbers and elevations on `POINTNUMBER-*` / `ELEVATION-*`,
  monument text (`SIB`, `IB` ...) and the lot area;
- creates the plan viewport in the chosen layout's free area (the template's
  layouts have none), locked at scale, and adds the north arrow;
- rewrites the title block (job number, drawing file, scale, intended plot size)
  and relabels the scale bar ticks;
- freezes the family's layers (point numbers on topo plans; numbers and
  elevations on R-plans), removes the unused layouts;
- saves `<job>\export\fd-draft\<job>.dwg` and plots `<job>.pdf`.

It never writes into the FD-Pro job's own files.

## Without AutoCAD

```powershell
fddraft samples\demo-lot --standards standards\provision-2024.standards.ini
fddraft <job> --standards <ini> --family rplan --layout RPLAN-22X34 --scale 500
```

Writes `<job>.dxf` (no template applied — for checking in any CAD program),
`<job>.svg` (the plan area on the chosen sheet) and `<job>.report.txt`
(sheet ranking, parcel areas and perimeters, warnings).

## The standards file

Everything firm-specific that the .dwt doesn't say lives in one INI file:
which codes are boundary, layer and block mapping, text heights and styles, the
scale list, where the plan may go on each layout, and the title-block rules.
`standards/provision-2024.standards.ini` was written from a read of the template
and FD-Pro's ProVision 2024 code list. Lines marked **VERIFY** are defaults a
drafter should confirm (monument abbreviations, north-arrow size).
