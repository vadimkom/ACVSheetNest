# SheetNest (acvent.bg fork): notes for coding agents

## Quick start

SheetNest is a Windows nesting program for sheet metal, grown out of DeepNestSharp. It imports DXF and SC2000 CHF drawings, nests parts onto stock sheets with the Sparrow engine, allows manual editing, and exports DXF and a PDF production report. C# / .NET 10 / WPF.

This repository is the acvent.bg fork, used at a laser-cutting shop. The shop PC is usually a Ryzen 5600, occasionally a Celeron J4125. The operator nests almost always with the fast setting (now the **▶ Nest** button) and works in millimetres.

When resuming:
1. Run `git status --short --branch` and `git log -5 --oneline`.
2. Read this file; open other documents only as the task needs them.
3. The user asked to keep an eye on the originals. Check, and mention anything worth taking:
   - `git fetch origin` and `git log 9d9c1cd..origin/main` for ManuelMRosa/SheetNest (9d9c1cd was its head on 2026-10-14);
   - our pull requests #12–#15 there (`gh pr list --repo ManuelMRosa/SheetNest --author vadimkom --state all`);
   - `git ls-remote https://github.com/JeroenGar/sparrow.git HEAD` for the engine (we build on `961ec31`; its head was `1fe3e31` on 2026-10-14).
4. Keep existing changes and work on the current branch (`main`).
5. If the user only asks to work on SheetNest, summarise the state and ask what they want. Earlier commits are not pending tasks.

## Remotes and publishing

- `fork` = https://github.com/vadimkom/ACVSheetNest. This is ours: push `main` there and publish releases with `gh release create`.
  - GitHub CLI: `C:/Program Files/GitHub CLI/gh.exe`, signed in as `vadimkom`.
  - Ask the user before overwriting published history.
- `origin` = https://github.com/ManuelMRosa/SheetNest. Not ours: never push there.
  - Upstream has ~30 commits we do not have (colour marking, names from labels, common-cut seams); they were never merged.
  - Fixes worth sharing go there as focused pull requests from `pr/*` branches on the fork. Ask the user before opening one.
  - Build those branches in the worktree `C:/src/sn-upstream`, based on `origin/main`.
  - Upstream targets .NET 6. Build it with the SDK 10 here and run its tests with `DOTNET_ROLL_FORWARD=Major`.
  - Baseline: core 1034/1037; the app suite fails `PartCardSizeFixture` on upstream main too.
  - Open pull requests: #12 DXF endpoint grid, #13 elliptical arcs, #14 file dialog on a pulled drive, #15 smaller `.dnest`. Fill-up was not offered: `SparrowNestService` has diverged too far.
- Historic upstream: https://github.com/9swampy/DeepNestSharp.
- Commit identity: `git -c user.name="vadim" -c user.email="komarov.v@gmail.com" commit ...`. No global identity is configured.
- Every MSI handed to the user gets a new version number. Releases on the fork feed the in-app update check (`UpdateChecker`, `releases/latest`).
- **Never publish a release before the user has tested the local MSI and said "выложи".** Build it, tell them where it is, and wait. Pushing a release is what reaches the laser PC through the update check.

## Paths on this machine

- Code: `C:/src/SheetNest`. Installers land in the repo root as `SheetNest-x.y.z-win-x64.msi`; publish output goes to `publish-x.y.z/`.
- Runtime data: `%LOCALAPPDATA%/SheetNest`:
  - `session.json` (language, recent projects, last material);
  - `nest-profiles.json` (Draft/Nest engine settings);
  - `shapes/`, `autosave/`, crash logs.
- The user's CHF drawings: `E:/chf` (578 files, used to verify the CHF reader).
- Rust toolchain for rebuilding Sparrow: not installed on this machine (checked 2026-10-23: no `.cargo`, no MSVC build tools). See BUILDING.md. Sparrow sources at 961ec31 are cloned in `C:/src/sparrow`.
- Do not kill the user's running SheetNest without asking. Stop only the test instances you started, by their own process id. The MSI scripts up to 1.1.40 closed every running SheetNest (which killed the user's open job on 2026-10-15); from 1.1.41 they do not.
- Each running copy autosaves to `%LOCALAPPDATA%/SheetNest/autosave/recovery-{pid}.dnest`; recovery is offered only for a copy that is no longer running. Screenshot automation: `assets/demo/take-screenshots.ps1` (-language, -width, -height).

## Code map

- `DeepNestSharp/`: the WPF app.
  - `Ui/Views/MainWindow.xaml(.cs)`: four steps — Parts, Sheets, Nesting, Layout & Report.
  - `Ui/UserControls/DxfViewer`: layout view, manual editing, thumbnails, move between sheets.
  - `Ui/Themes/SheetNestTheme.xaml`: palette; standard WPF controls, Segoe UI.
  - `SessionState.cs`.
- `DeepNestSharp/RasterNest/`:
  - `SparrowNestService.cs`: sheet-by-sheet packing, fill-up, races.
  - `NestProfiles.cs`: Draft/Nest settings file.
  - `CommonEdgeClearance.cs`, `RasterCompact.cs`.
- `DeepNestSharp/Reports/`:
  - `NestReportPdf.cs`: PDF, on-screen preview and print from one `IReportCanvas`.
  - `PdfTrueTypeFont.cs`: embedded Arial as Unicode; works without native zlib.
  - `ReportMaterial.cs`.
- `DeepNestSharp/Ui/Strings*.resx` + `Ui/Localization.cs`: translations (see Languages).
- `DeepNestLib.Core/`:
  - `IO/`: DXF reading/writing, `ChfReader.cs`, `DrawingFile.Load`, `PartMirror.cs`, `PartShapes.cs`, `DxfOutputLayers.cs`, `FlatDxfJoiner.cs`.
  - `Placement/`: geometry and common line.
  - `NestProject/`: persistence, `SheetMaterials.cs`.
- `DeepNestSharp.Domain/`: observable models and configuration. `Services/UiText` routes Domain texts to `Localization`.
- `SparrowEngine/`: `sparrow.exe` and `sheetnest-sparrow.patch`.
- `DeepNestSharp.CiTests/` (app) and `DeepNestLib.CiTests/` (core): tests. `NestBench/`, `GpuNestLab/`: benchmarks and lab.
- `installer/`: WiX MSI (`build-msi-x.y.z.ps1`, `SheetNest.wxs`), release policy in `RELEASING.md`.
- `test-data/`:
  - `dxf/`: SolidWorks flat patterns of duct parts, `test2`, `Hodgson`, `inhole` for the common-line benches;
  - `chf/sides.chf`;
  - `test1/`: a real three-sheet order with a 1048-hole front, used by `SavedNestSizeFixture` and the speed bench.

## Documents by task

- README.md: product features. The "About this fork" section lists what the fork adds.
- BUILDING.md: requirements, build, rebuilding Sparrow.
- COMMON_LINE_IMPLEMENTATION.md: architecture, invariants, limits and tests of common-line cutting. Read it before touching that feature.
- installer/RELEASING.md: testing and publishing.
- DeepNestSharp/DeepNestSharp.csproj: the declared version. Do not infer the published version from MSI or publish folder names.

## Commands (repository root, PowerShell)

```powershell
dotnet build .\DeepNestSharp\DeepNestSharp.csproj -c Release
dotnet test .\DeepNestSharp.CiTests\DeepNestSharp.CiTests.csproj -c Release
dotnet test .\DeepNestLib.CiTests\DeepNestLib.CiTests.csproj -c Release
& '.\DeepNestSharp\bin\Release\net10.0-windows\SheetNest.exe'
& .\installer\build-msi-x.y.z.ps1   # copy the previous script and bump the version
```

Run the suites per project: `dotnet test` on the whole solution can hang in restore. Pick the tests related to a change; do not build or run suites just to regain context.

Engine tests:
- They need `SPARROW_EXE` (and `SHEETNEST_MIXED_COMMON_LINE_DXF_DIR=test-data\dxf` for the benches). **Without `SPARROW_EXE` the native tests are SKIPPED and look exactly like passes**; five stayed broken for a day that way.
- With the engine, the app suite takes about 5.5 min.
- Long measurements and Normal-effort searches run only with `SHEETNEST_SLOW_TESTS=1` (`DeepNestSharp.CiTests/SlowTests.cs`). Run them before a release that changes the search.

Other switches:
- `SHEETNEST_FILL_UP=0` turns fill-up off.
- `SHEETNEST_MAX_CONC` overrides parallel searches.
- `SHEETNEST_NEST_*` overrides the profile file.
- `NestSpeedBenchFixture` benches a project (`SHEETNEST_BENCH_PROJECT`, `SHEETNEST_SPARROW_ARGS`, `SHEETNEST_DUMP_JOBS`).

Last verified (2026-10-23, 1.1.57): core 1087/1089; app 499/499 with the engine (SPARROW_EXE + bench DXF dir), common-line benches included.

## How things work (decisions worth knowing)

**Nesting**
- Two buttons:
  - **⚡ Draft** (`NestEffort.Draft`): a rough nest for a quote. 100 iterations, 1 try, no last-chance pass, coarse engine flags, fill-up up to 2 packs. Its variants are marked "draft".
  - **▶ Nest** (`NestEffort.Fast`, the old Fast numbers).
  - Both read `nest-profiles.json` on every press. The file is written with defaults on first run; nonsense values fall back to defaults; an unreadable file gives defaults plus a message.
  - Normal/Best still exist in code for tests but are not in the UI.
- **Fill-up** (`SparrowNestService.FillUp`): after the best sheet size is chosen, it offers waiting large parts (≥5% of the sheet, area permitting) one at a time, largest first, up to 4 packs. A part stays only if the whole set still fits. Sheets with common-line parts are left alone. It took `test1` from 3 sheets to 2.
- Sheet priority: higher-priority stock (offcuts) is consumed first, listed first, and shown first in the viewer, report and export. A worse-priority size is not run through Sparrow once a better one has packed.
- Parallel searches: Auto = cores / threads per search, at most 8. `sparrow.exe` runs at BelowNormal priority. The result does not depend on concurrency.
- The variants list holds only finished nests of the whole job, one per press, no duplicates. The best is starred: all parts placed, then fewest sheets, then highest utilisation. Per-sheet attempts only draw in the preview.
- **Sheet quantities stay as entered** (since 1.1.51). A nest only reports what it used: `ConsumeStock` returns counts by size and the badge reads "used / in stock".
  - Up to 1.1.50 a nest took its sheets off the quantities and gave them back on the next nest. That summed up: a sheet added after a nest came on top of the ones it held, and the user saw "2/3".
  - Projects saved that way carry `SheetsKeptWhole = false`. On opening they get the sheets back once (`ReturnStock`).
- **Cut sheets, nest the rest** (`CutSheets.DeductParts/TakeCutSheets`): ticked sheets' parts come off the order (mirrored from the mirrored count, others from quantity, then from spares). The cut sheets come off the quantities for good, the only place sheets leave the stock. A part quantity may be 0; such rows are not sent to the engine.
- **Move a part between sheets** (`DxfViewer.MoveSelectedTo`): only between single-copy sheets without common cutting. The part goes to the first free spot bottom-left, or stays red in place. An emptied sheet returns to stock (`SheetRemoved`). One undo step.
- **Fill the sheets** (`IDetailLoadInfo.FillSheets`; "Fill the sheets" in + Shape and in the part's "…"): a row with no count, e.g. a sheet of washers. The operator picks sheets as usual; the nest puts on as many as fit after every counted part.
  - Engine (`SparrowNestService.Nest`): a filler is offered ceil(stock area / spaced outline area), at priority 11. Unlimited sizes count as their box number, at least 1. `SelectBatch` keeps fillers out while counted parts wait. `TopUpWithFillers` adds them around a packed sheet after `DeferUnfit`. Unplaced fillers are not reported as missing.
  - One filler goes in rows (`LatticeFill`): every allowed angle, straight or half-shifted rows, steps bisected on the spaced outline. Sparrow took 2.5 min over 900 washers on 1250×500; rows take 0.2 s and seat 798 Ø24. A rows-only sheet skips `DeferUnfit`, being clear by construction. `CommonEdgeClearance.FindUnfit` now skips pairs whose boxes are apart (a 2652-washer sheet: 25 s → 0.5 s, in the viewer too).
  - **Several fillers are one kit** (an air-conditioner bracket of two parts) by default, as many of each (`Loaded.Kit`). From 1.1.54 the Parts step shows a switch once two included rows fill the sheets, "Заполнение: поровну / максимум" (`ProjectInfo.FillToMaximum`, saved with the project, `fillAsKit` to the engine). At "максимум" each filler is offered by its own footprint and tops up on its own, biggest first, nothing is evened out (`FillSheetsFixture.TwoFillersToTheMaximumAreNotEvenedOut`: more area covered). As a kit:
    - each is offered ceil(stock area / kit footprint);
    - `SelectBatch` offers whole kits on a fillers-only sheet;
    - `TopUpWithFillers` tops up by kits;
    - `EvenOutKit` drops the extra copies, last sheets first, after holes are filled.
    - Tested: `FillSheetsFixture.TwoFillersComeOutAsManyOfEach`.
  - After a nest the row's Quantity = what went on (`CountFilledRows`); the cell shows "fill: N". Fill parts get no names on the layout or in the DXF (`DxfExporter.UnnamedParts`, set in `ApplyPartColours`).

**Parts and sheets**
- Flip ↔/↕ mirrors the part itself (`PartMirror`): ticked in Edit Part ("…") and applied on OK; a copy of the drawing goes in `%TEMP%\sheetnest_flip` and is rewritten when a project opens. `DetailLoadInfo.Flip/UnflippedPath` are saved.
- **Part names** (`IDetailLoadInfo.DisplayName`; null = the file's name). Set in "…" or by double-clicking a part on a layout (`DxfViewer.PartRenameRequested` → `MainWindow.OnPartRenameRequested`). It shows in the parts table (`ShownName`), on layouts, in the DXF and the report, via `DxfExporter.PartNames` (set in `ApplyPartColours`; `GetPartNameLabel` and the report's `DisplayName` read it). The drawing file is not renamed.
- Both viewers get the part colours (the Nesting step's preview was grey until 1.1.49). The Nesting step has no ◀ n/m ▶ bar (`ShowSheetNav = false`): its thumbnails move between layouts.
- Parts table (user's layout, 2026-10-15): sketch, name, quantity, **Mirrored** (copies on top of the quantity), size, priority, actions "…" and ✕. No colour column: clicking the sketch changes the colour. No delete/clear buttons: Delete removes the selected rows (Ctrl/Shift for several, Ctrl+A for all, asked first when more than one).
- Shapes (`AddShapeWindow`, `PartShapes`): a catalogue of `ShapeKind`s (rectangle, circle, washer, perforated tape), each with its parameters (label key + default in mm), optional presets (values they set and lock) and `Draw`. The dialog builds its fields from the kind, so a new shape is one class plus its texts. Files go to `%LOCALAPPDATA%\SheetNest\shapes`; adding the same shape again adds to that row. Values are in drawing units; defaults and presets are converted from mm in inch mode.
  - Washers: DIN 125-A M8/M10/M12 (hole 8.4/10.5/13, outside 16/20/24) and DIN 9021 (outside 24/30/37), or custom.
  - Perforated tape (`PerforatedTapeShape`): blank L × W cut into whole strips of width B; per strip a big hole every pitch with four small ones at ±offset along and ±rowGap/2 across, the groups centred along the length. Shared cuts between strips get 0.5 mm bridges about every 400 mm (both editable).
    - Presets:
      - **B26 Ø8.2**, first: the shop's real tape, from its drawing `perfo1` (2026-10-15). Big Ø8.2 every 25 mm; small Ø3.5 pairs 18 apart across, offset 12.5 = half the pitch, so each pair is shared by two groups and drawn once (`Between`). 1237.8 mm gives 49 + 100 holes, as in the drawing.
      - B25 Ø8 (our guess before the drawing: 10 mm square, every 20, Ø3.2).
      - B17 Ø6.2 (another drawing of the user's: 10 × 8, every 15, Ø3.2).
    - In + Shape the tape's length × width are the **sheet**. The tape is drawn to `MainWindow.TapeRoom`: less 2 margins + one gap + 0.5, whole mm. Sparrow panics on an item whose spaced outline exactly fills the strip, hence the 0.5. The sheet is added to the job (one per blank, priority 1), and the nest starts.
      - The blank's row gets part priority 1, so in a job with other parts it lands on its own sheet first.
      - The fields read "Длина/Ширина листа" (`Shape_SheetLength/Width`; `SheetRoom` rebuilds the fields when set).
      - Double-clicking a shape's row opens the shape dialog on what it was made from (`ShapeRecord`, `name.dxf.shape.json` beside the drawing in `shapes/`). Apply redraws the row's drawing and moves the sheet it brought (same typed size, priority 1), then nests (`MainWindow.RunShapeDialog`). Turning, priority and the rest stay on "…". Shapes made before 1.1.48 have no record and open "…".
      - "Fill the sheets" is hidden for fit kinds. A tick left from the washers made the tape a filler: no sheet, no nest (the user's report on 1.1.46).
    - A 2474×1224 sheet of B26 (30k holes): split 3 s, nest 4.7 s. `DxfPartSplitter` used to compare every contour pair: 19 s for 5000 holes, minutes for a sheet. It now uses a grid (`ContainmentGrid`).
  - Profile (`ProfileShape`, presets B100 and B66), from the shop's `profilm.chf` (2026-10-16). Strips like tape, same sheet flow and `StripCuts` bridges. One row per strip: round Ø10 every 62.5; a slot 10 × 28 (obround along the strip) centred 32.5 after each round hole but the last. 2000 mm gives 32 + 31, as in the drawing.
    - Bridges default to 0.5 every ~350, i.e. 5 at 2000/6 as in the drawing.
    - The CHF keeps its bridges in `<PWM Control>` (count, then from/to fractions of the contour's length). `ChfReader` ignores that block, so imported CHF lines come in unbroken.
- **Parts library and filling offcuts** (`DeepNestLib.Core/IO/PartLibrary.cs`, `FillRemnantWindow`, `SparrowNestService.Fill`).
  - The library is a folder (`SessionState.LibraryFolder`, default `Documents\SheetNest\Library`, set in Advanced Settings). Each subfolder is a group; loose files form a group of their own.
  - Items are DXF/CHF parts, or `*.pattern.json` patterns: a `ShapeKind` id plus its values in mm, keyed by label key, without the fitted length/width (`ShapeKind.Fit`).
  - A new library is seeded with the tape presets (group "Перфолента") and the profiles (group "Профили"); an existing one is left alone. "To library..." in + Shape saves a pattern (fit kinds) or a DXF (others).
  - **▦ From offcut...** creates a new job: the offcut W×L × pieces as the only stock (priority 1), the ticked groups' parts, patterns realized to the offcut (inside 2×margin + spacing, floored to whole mm). Then `OnNest` runs in fill mode (`pendingFillMinShare`).
  - `Fill`: one `PackOneSheet` per part, biggest first. It offers as many as the area left allows and keeps the result if everything chosen before still goes on. Parts under the min share (default 2%) are skipped. The chosen counts × pieces then go through `RunNestBody`, so compaction, holes and replication are the normal ones, and unplaced parts are dropped.
  - `TakeFillQuantities` then sets the rows to what went on and removes the rest.
- **Cuts inside a part that enclose nothing** live on the source layer `SHEETNEST_INNER_CUT` (`DxfOutputLayers.InnerCutSourceName`).
  - The parser ignores them, so nesting sees the outline and holes.
  - The export writes them on CUT, turned with the part (`PartBendInfo.InnerCutEntities`); other open lines stay bends on PAINT.
  - The layout draws them inside each part's geometry (`DxfInnerCuts.Placed`, using `Part.Rotation` as the export does).
  - The report adds their length and pieces to cut length and pierces (`DxfInnerCuts.Measure`).
- Material and thickness are set per project (`ProjectInfo.Material/Thickness`) and remembered for the next job.
  - Materials: black, galvanized, stainless, aluminium; densities 7.85 / 7.85 / 7.93 / 2.70.
  - Thickness: 0.6, 0.7, 0.8, 1, 1.2, 1.5, 2, 3, 4, 5, or typed.
- Default part spacing in mm is 4 (since 1.1.56, the user's choice; was 6.35 = ¼"). A stored 6.35 from an older build becomes 4 once (`SessionState.SpacingFourMillimetresVersion`, settings version 2; the effort reset keeps its own `EffortResetVersion` = 1). Projects keep the spacing they were saved with, part rows too.
- Standard sheets stand (since 1.1.52, the user's request): 1000×2000, 1250×2500, 500×1250 mm; the inch list likewise short side first; Custom size opens at 1500×3000. Anything else via "Custom size". The engine packs along the longer side either way: on the flat patterns 1000×2000 gave the same 8 sheets as 2000×1000, 1250×2500 the same 4.
- **Sheets from a drawing** (Add sheet → "Sheet from a drawing (DXF, CHF)…", `SheetShape.Load`, `ISheetLoadInfo.ShapePath`): an offcut of any shape. The largest closed contour is the edge, closed contours inside it are holes, anything outside is dropped; moved so its box starts at 0,0. The row's W×H is the rounded box (rounded like the "used / in stock" and cut-sheet matching), priority 1, quantity 1; the file is read again on every nest, as parts are.
  - The Sheets table has a sketch column (`SheetPreviewConverter`, since 1.1.53): every row in proportion, a drawn sheet with its outline and holes, cached by path and time written.
  - Packed by `SparrowNestService.PackShapedSheet` (`RasterNest/ShapedSheetPacker.cs`), not by Sparrow (a strip packer; patching it needs Rust): no-fit polygons with Clipper Minkowski sums, per part and angle (poses built in parallel). Three gravities are packed and the one with the most part area kept: along the long side, along the short side, outward against the edge (rings a round offcut). Counted parts by priority and size first, fillers after; the counted copies are also tried in turn (one of each size per round), and a kit of fillers always goes on in turn and ends when one member no longer fits (bad2: filled as a kit it gave 1+1 where 3+2 went on by count, now 2+2; `RoundOffcutFilledAsAKitHoldsWhatACountDoes`). 3+3 does not go on at 6.35 nor at 4 mm (bad2a gives 3+2 in every order tried; 4+1 also fits at 4). Fill-up, top-up, last chance and the common-line planner are skipped on such a sheet; `DeferUnfit` and `DropOutsideShape` still judge it. A drawing that is a plain rectangle goes to Sparrow as usual.
  - Every candidate position is checked on the real shapes (`ShapedFits`) before it is taken: Clipper leaves zero-area slivers where two no-fit polygons meet, and on the shop's round offcut (`test-data/bad`, 2026-10-23) a part went on top of another, `DeferUnfit` took it off, and 3 of 4 parts were placed with half the circle empty. Outlines are reduced by Douglas-Peucker to 2.5e-4 of the sheet diagonal (grown first, the sheet shrunk first): that job went from 50 s to 5 s.
  - Measured (`ShapedSheetFixture`): the round offcut takes the order (2 + 2 elbow sides) and the operator's own layout (4 + 1) in 3–5 s; the L-shaped test offcut 8 squares where the first version found 7.
  - One part filling what is left of a shaped sheet (washers) is laid in rows (`ShapedRows`): every orientation, straight and half-shifted rows at the `LatticeSteps` the rectangle lattice uses, the grid slid in eighths of a step; a position counts when it is in the pose's free region. Kept when it seats at least as many as the part-by-part pack. BAD3 (Ø638, M8 washers, 5.5): 665 scattered, the operator's straight rows (bad4.chf) 659, shifted rows 739 in 1.3 s (`WashersFillARoundOffcutInShiftedRows`).
  - The layout, thumbnails and report draw the outline and holes (`SheetShape.IsShaped`), utilisation is over the metal, and the editor paints a part red off the metal or inside the margin of the edge or a hole (`IsOutsideShape`). The cut DXF carries the outline and holes on Background (`DxfExporter.AppendSheetOutline`), never cut.
  - Not done: the offcut line (`OffcutOptions`) still measures the box; a shaped and a rectangular sheet with the same rounded box share the "used / in stock" count.
- Part sizes load in `Task.Run` once, and the notification goes back to the asking thread's context. A report of missing sizes on the user's PC could not be reproduced; ask for a screenshot if it recurs.

**Import**
- `DrawingFile.Load` recognises CHF by content. An empty file gives a clear error.
- **CHF (SC2000)**: the whole format, verified against `E:/chf` (572/572 import, 6 empty).
  - Graph kinds: 8 contour, 9 group, 10 text.
  - Elements: 2 line, 3 arc, 4 circle, 6 polyline with bulges, 7 spline.
  - Layers per the user: 0 green = cut; 1 blue (bends) and 8 red (labels) = engrave → layer 9; 2–7 and 9 = cut with other modes; 10 = background, not read (`ChfReader.LayerFor`).
- **DXF chaining**: an endpoint grid, not a scan of every segment. Same choices as before, verified byte-identical on 324 drawings; 280 s → 1.4 s. `FlatDxfJoiner` uses the same grid.
- Elliptical arcs whose sweep crosses zero are short arcs, not whole ellipses. Mirrored ellipse arcs swap and negate their parameters.
- SolidWorks flat patterns: bend lines (layer 0 colour 8, or layer 9) are exported on PAINT, rotated with the part. Part labels drop the "Flat-Pattern - " prefix.

**Export and report**
- DXF layers are only three (`DxfOutputLayers`), all entities ByLayer, cut geometry first. Confirmed correct in SC2000 on 2026-09-29.
  - CUT = "0"/7: everything cut.
  - PAINT = "9"/1: bend lines and engraved names.
  - BACKGROUND = "Background"/8: display-only names.
- Non-ASCII names are written in the Windows codepage, not as `\U+XXXX` escapes, which SC2000 shows literally (`DxfExporter.SaveForCam`): 1251 when every such letter fits, else 1252, and the header `$DWGCODEPAGE` says so. A plain-ASCII file is unchanged.
- One file per layout, `name_N_WxH_xRepeats.dxf`. The offcut line is not written to DXF.
- Report:
  - "Material required" box: sheets, then material and thickness and the sheet and part weight (material + mm only); "Job summary": parts, cut length in metres, pierces, material used, layouts;
  - part tables add a weight column (one piece, kg) when the job has a material;
  - a size column;
  - part tables in one column, continued on following pages;
  - page numbers from a trial render pass.
- **No thousands separators anywhere**: the shop reads "1,913" as a decimal.
- The PDF embeds Arial as Unicode (Type0/Identity-H). If native zlib is missing, as on the shop PC, the font is written uncompressed.
- Report preview: "Whole page" is the default zoom; a fit mode holds on resize until the user zooms by hand.

**Saving**
- `.dnest` writes sheets through `SheetPlacementJsonConverter`, so computed members (PolygonsForExport, Hull, Simplify) are not saved: 4.4 MB → ~1.4 MB. `SvgPoint.Marked` is still written, because the golden JSON tests need it.
- Projects are not zipped (native zlib is unreliable on the shop PC).
- File dialogs skip a folder that no longer exists, such as a pulled flash drive, and retry without it if the shell refuses. A failed save shows a message instead of crashing.

**Languages**
- Seven: ru (default), bg, en, de, fr, es, pt. Texts live in `DeepNestSharp/Ui/Strings.resx` (English, neutral) and `Strings.xx.resx`.
  - `Localization.Get` uses a `ResourceManager`: a missing translation falls back to English; a missing key shows the key.
  - XAML uses `{ui:Tr Key}`, which follows live language changes.
- To add a text: add the key to every resx (`TranslationsFixture` checks keys and `{n}` placeholders). To add a language: a new resx, an entry in `Localization.Languages`, and a menu item.
- `Plural`: Russian has three forms, the others two; fr/pt count 0 as one.
- de/fr/es/pt are machine translations (pt leans Brazilian); native speakers should review them. Terms used:
  - de: Schachteln / Blech
  - fr: imbrication / tôle
  - es: anidado / chapa
  - pt: encaixe / chapa
- Long-label checks were done by screenshotting all four steps and the main dialogs in de/fr/es/pt.
- `SatelliteResourceLanguages=ru;bg` only filters the .NET runtime's own messages; our satellites are always built.

**Installer**
- Self-contained publish, WiX harvests the whole publish folder. About 52 MB.
- FreeCAD / 3D import was removed entirely; old projects with 3D fields still load.

## Common-line cutting is validated against TWO benches, never one

`Test2SamePartDirectRegressionFixture` uses **one part**; `MixedCommonLineRegressionFixture` uses **different parts**. They measure different things: a change can leave the first unchanged and empty the second. Removing the neighbour shift did exactly that, and an MSI shipped with mixed jobs producing zero shared cuts.

Values that must not move (with `SHEETNEST_BENCH_EFFORT=Fast`):

| bench | value |
|---|---|
| test2, 16/17/24/25 copies | 16/16 · 17/17 · 24/26 · 25/27 |
| test2, 26 copies | 26 placed, 27 edges, `isolated=1`, chain 16 |
| test2, 27 copies | 26 placed, 28 edges |
| mixed | 30 placed, 21 edges (the branch has shown 31 since 2026-09-27; cause not found) |
| all | `unsafePairs=0`, `minUnrecordedGap=0.24998` |

They need `SPARROW_EXE` and `SHEETNEST_MIXED_COMMON_LINE_DXF_DIR` with `test2.dxf`, `Hodgson.dxf` and `inhole.dxf`. The user has hardly used common cutting and has no feedback on it.

## Open items

- The mixed common-line bench gives 31 edges instead of the reference 21. Decide whether to update the reference or find the cause.
- Tail repack (`SparrowNestService.ShrinkTail`) is written but off (`TailRepackEnabled = false`) and has not been measured.
- Grid compaction on very perforated parts still takes about 20–40 s in total.
- Native speakers' review of the de/fr/es/pt texts.
- Suggested to the user, not requested: price per metre / m² / sheet in the report.

## Keeping this file current

After relevant work, update the sections above in place (what changed, what was actually verified, what is still open) rather than appending a diary. Keep it short and put technical detail in the matching document. Never store credentials or whole conversations. Search with rg in the relevant module first; avoid walking `bin`, `obj`, `publish-*` and the whole profile.
