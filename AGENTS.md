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
- Rust toolchain for rebuilding Sparrow: `%USERPROFILE%/.cargo`. See BUILDING.md.
- Do not kill the user's running SheetNest without asking. Test instances started from `bin/` are fine to stop.

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

Last verified (2026-10-14, 1.1.38): core 1078/1080; app 487/487 with the engine (SPARROW_EXE + bench DXF dir), common-line benches included.

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
- **Cut sheets, nest the rest** (`CutSheets.DeductParts/KeepConsumed`): ticked sheets' parts come off the order (mirrored from the mirrored count, others from quantity, then from spares) and the sheets stay consumed. A part quantity may be 0; such rows are not sent to the engine.
- **Move a part between sheets** (`DxfViewer.MoveSelectedTo`): only between single-copy sheets without common cutting. The part goes to the first free spot bottom-left, or stays red in place. An emptied sheet returns to stock (`SheetRemoved`). One undo step.

**Parts and sheets**
- Flip ↔/↕ mirrors the part itself (`PartMirror`): ticked in Edit Part ("…") and applied on OK; a copy of the drawing goes in `%TEMP%\sheetnest_flip` and is rewritten when a project opens. `DetailLoadInfo.Flip/UnflippedPath` are saved.
- Parts table (user's layout, 2026-10-15): sketch, name, quantity, **Mirrored** (copies on top of the quantity), size, priority, actions "…" and ✕. No colour column: clicking the sketch changes the colour. No delete/clear buttons: Delete removes the selected rows (Ctrl/Shift for several, asked first when more than one).
- Shapes (`AddShapeWindow`, `PartShapes`): a catalogue of `ShapeKind`s (rectangle, circle, washer, perforated tape), each with its parameters (label key + default in mm), optional presets (values they set and lock) and `Draw`. The dialog builds its fields from the kind, so a new shape is one class plus its texts. Files go to `%LOCALAPPDATA%\SheetNest\shapes`; adding the same shape again adds to that row. Values are in drawing units; defaults and presets are converted from mm in inch mode.
  - Washers: DIN 125-A M8/M10/M12 (hole 8.4/10.5/13, outside 16/20/24) and DIN 9021 (outside 24/30/37), or custom.
  - Perforated tape (`PerforatedTapeShape`): blank L × W cut into whole strips of width B; per strip a big hole every pitch with four small ones at ±offset along and ±rowGap/2 across, the groups centred along the length. Presets: B25 Ø8 (the shop's tape: small holes on a 10 mm square round each big one, groups every 20 mm, so a small hole every 10 mm along each row; the exact spacing was ours to choose, the user said any square-ish layout serves the fitters) and B17 Ø6.2 (the drawing the user sent: 10 × 8 round it, every 15). Small holes Ø3.2 in both. Shared cuts between strips get 0.5 mm bridges about every 400 mm (both editable).
- **Parts library and filling offcuts** (`DeepNestLib.Core/IO/PartLibrary.cs`, `FillRemnantWindow`, `SparrowNestService.Fill`).
  - The library is a folder (`SessionState.LibraryFolder`, default `Documents\SheetNest\Library`, set in Advanced Settings). Each subfolder is a group; loose files form a group of their own.
  - Items are DXF/CHF parts, or `*.pattern.json` patterns: a `ShapeKind` id plus its values in mm, keyed by label key, without the fitted length/width (`ShapeKind.Fit`).
  - A new library is seeded with the two tape presets. "To library..." in + Shape saves a pattern (fit kinds) or a DXF (others).
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
- Standard metric sheets: 2000×1000, 2500×1250, 1250×500. Anything else via "Custom size".
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
