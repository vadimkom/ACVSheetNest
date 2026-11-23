# SheetNest (acvent.bg fork): notes for coding agents

## Quick start

SheetNest is a Windows nesting program for sheet metal, grown out of DeepNestSharp. It imports DXF and SC2000 CHF drawings, nests parts onto stock sheets with the Sparrow engine, allows manual editing, and exports DXF and a PDF production report. C# / .NET 10 / WPF.

This repository is the acvent.bg fork, used at a laser-cutting shop. The shop PC is usually a Ryzen 5600, occasionally a Celeron J4125. The operator nests almost always with the fast setting (now the **▶ Nest** button) and works in millimetres.

When resuming:
1. Run `git status --short --branch` and `git log -5 --oneline`.
2. Read this file; open other documents only as the task needs them.
3. The user asked to keep an eye on the originals. Check, and mention anything worth taking:
   - `git log 7dca814..main` on ManuelMRosa/SheetNest (7dca814 was its head on 2026-11-04; fetch it by URL, see Remotes);
   - our pull requests there (`gh pr list --repo ManuelMRosa/SheetNest --author vadimkom --state all`);
   - `git ls-remote https://github.com/JeroenGar/sparrow.git HEAD` for the engine (we build on `961ec31`; its head was `6067f00` on 2026-11-04, 47 commits ahead: 0.3.0 on jagua-rs 1.1.0, a new input format, Rust 1.90; our patch and job writer would need porting).
4. Keep existing changes and work on the current branch (`main`).
5. If the user only asks to work on SheetNest, summarise the state and ask what they want. Earlier commits are not pending tasks.

## Remotes and publishing

This checkout (`C:/src/ACVSheetNest`, checked 2026-11-18) has two remotes, `origin` and `forgejo`, and none for upstream.
- `origin` = https://github.com/vadimkom/ACVSheetNest. This is ours: push `main` there (`git push origin main`) and publish releases with `gh release create`.
  - GitHub CLI: `C:/Program Files/GitHub CLI/gh.exe`, signed in as `vadimkom`.
  - Ask the user before overwriting published history.
- Upstream = https://github.com/ManuelMRosa/SheetNest. Not ours: never push there. No remote here; read it by URL (`git fetch https://github.com/ManuelMRosa/SheetNest main`, then `git log 7dca814..FETCH_HEAD`). `C:/src/SheetNest` is an old clone of it (origin = ManuelMRosa, last commit 2026-09-07), not the working copy.
  - Upstream has ~30 commits we do not have (colour marking, names from labels, common-cut seams); they were never merged.
  - Fixes worth sharing go there as focused pull requests from `pr/*` branches on our GitHub repo. Ask the user before opening one.
  - Build those branches in a worktree based on upstream main (`C:/src/sn-upstream` was used; it no longer exists).
  - Upstream targets .NET 6. Build it with the SDK 10 here and run its tests with `DOTNET_ROLL_FORWARD=Major`.
  - Baseline: core 1034/1037; the app suite fails `PartCardSizeFixture` on upstream main too.
  - Taken on 2026-11-04: #13 elliptical arcs, #14 file dialog on a pulled drive, #15 smaller `.dnest`, and of #12 the `FlatDxfJoiner` half (7dca814; #12 closed, the reader overlaps a grid `DxfParser` got in upstream 1.1.13). None open. Fill-up was not offered: `SparrowNestService` has diverged too far.
- `forgejo` = https://g.vkom.top/vadim/ACVSheetNest: the user's own Forgejo on FreeBSD (behind Caddy, by IP list), a second copy of `main` and the tags since 2026-11-04. Releases and the update check stay on GitHub. Remote URL is SSH, git@g.vkom.top:vadim/ACVSheetNest.git (key ~/.ssh/id_ed25519, user git; set 2026-11-17), so git push forgejo main --tags works from here.
- Historic upstream: https://github.com/9swampy/DeepNestSharp.
- Commit identity: `git -c user.name="vadim" -c user.email="komarov.v@gmail.com" commit ...`. No global identity is configured.
- Every MSI handed to the user gets a new version number. Releases on the fork feed the in-app update check (`UpdateChecker`, `releases/latest`).
  - Since 1.1.58 the program installs updates itself (`MainWindow.ShowUpdateWindow`): status-bar notice or Help → check, then "Download and install".
  - It downloads the release's `.msi`, only from `UpdateChecker.DownloadPrefix`, into `%TEMP%\SheetNest-update`.
  - It writes `install-update.cmd` (UTF-8 with `chcp 65001`; `find.exe` by full path, since Git's `find` can come first in PATH) and closes, through the usual save prompt.
  - On `Closed` it runs the script hidden: wait for the PID, `msiexec /i … /passive`, start `SheetNest.exe` again from `HKCU\Software\SheetNest\InstallFolder`.
  - **A release must carry the MSI as an asset**, or the button stays disabled and only "What's new" works.
  - Verified: the live check and download of 1.1.57 (54.7 MB), the script text, and the registry lookup. The full install-and-restart was not run here.
- **Never publish a release before the user has tested the local MSI and said "выложи".** Build it, tell them where it is, and wait. A release reaches other PCs through the update check; the laser PC is rarely online, so the user carries the MSI there on a flash drive (2026-11-20). Keep the MSI installable by hand.

## Paths on this machine

- Code: `C:/src/ACVSheetNest`. Installers land in the repo root as `SheetNest-x.y.z-win-x64.msi`; publish output goes to `publish-x.y.z/`. After building a new MSI, delete the older `publish-*` folders and MSIs and keep only the newest (the user, 2026-11-23; published ones are on GitHub releases).
- Runtime data: `%LOCALAPPDATA%/SheetNest`:
  - `session.json` (language, recent projects, last material);
  - `nest-profiles.json` (Draft/Nest engine settings);
  - `shapes/`, `autosave/`, crash logs.
- The user's CHF drawings: `E:/chf` (578 files, used to verify the CHF reader).
- Toolchains (2026-11-17): Rust 1.99 (msvc) with cargo, Go 1.27, MinGW gcc, Node 24, .NET 10; no Python. See BUILDING.md for rebuilding Sparrow. Sparrow sources at 961ec31 are cloned in `C:/src/sparrow`.
- Do not kill the user's running SheetNest without asking. Stop only the test instances you started, by their own process id. The MSI scripts up to 1.1.40 closed every running SheetNest (which killed the user's open job on 2026-10-15); from 1.1.41 they do not.
- Each running copy autosaves to `%LOCALAPPDATA%/SheetNest/autosave/recovery-{pid}.dnest`; recovery is offered only for a copy that is no longer running. Screenshot automation: `assets/demo/take-screenshots.ps1` (-language, -width, -height).

## Code map

- `DeepNestSharp/`: the WPF app.
  - `Ui/Views/MainWindow.xaml(.cs)`: four steps — Parts, Sheets, Nesting, Layout & Report.
  - `Ui/UserControls/DxfViewer`: layout view, manual editing, thumbnails, move between sheets.
  - `Ui/Themes/SheetNestTheme.xaml`: palette and keyed styles. Since 1.1.79 the standard controls are WPF Fluent (`App.ThemeMode="Light"`, WPF0001 suppressed); `FluentTweaks.xaml` adds what the theme lacks: Xceed number fields as Fluent text boxes, a compact menu bar (the theme's top-level template has 4 + 10 px margins), `CompactColumnHeader` (6 px instead of 12; set per GridView, the theme ignores an implicit header style). Keyed button styles must be BasedOn `{x:Type Button}`, or they lose Fluent.
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
- DeepNestSharp/DeepNestSharp.csproj: the declared version (`Version` + `AssemblyVersion`; file and informational versions follow `Version` since 1.1.81, when About still read 1.1.65). Do not infer the published version from MSI or publish folder names.

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
- `SHEETNEST_BENCH_SVG=<folder>` writes a picture of every sheet from the bench; `SHEETNEST_BENCH_NO_FILL=1` treats fill rows as counted.
- `SHEETNEST_FILL_UP=0` turns fill-up off.
- `SHEETNEST_MAX_CONC` overrides parallel searches.
- `SHEETNEST_NEST_*` overrides the profile file.
- `NestSpeedBenchFixture` benches a project (`SHEETNEST_BENCH_PROJECT`, `SHEETNEST_SPARROW_ARGS`, `SHEETNEST_DUMP_JOBS`).

Last verified (2026-11-23, 1.1.86): app 561/561 with the engine (8.6 min). Earlier (2026-11-20, 1.1.83): app 551/551. Earlier (2026-11-17, 1.1.74): app 534/534 with the engine. Earlier (2026-11-10): app 518/518 with the engine (tidy offcut, nesting direction); earlier that day app 515/515 with the engine (free room, shared reads; Porta then read the copied drawings, ~7 of the 14.7 min; now rectangles, 15 s); 2026-11-04: core 1090/1092; 2026-11-03, 1.1.63: app 513/513 with the engine (SPARROW_EXE + bench DXF dir), common-line benches included.

## How things work (decisions worth knowing)

**Nesting**
- One split button (since 1.1.66): the main part runs the mode last picked in its ▾ menu, Nest or Draft; picking in the menu only picks, the button runs (1.1.78, the user), Draft first, then Nest per engine (`MainWindow.OnNestModeMenu`, `SessionState.NestMode`, per machine). The menu also picks the engine (1.1.73, the user): Nest for Sparrow, for RNest when `rnest-sparrow.exe` is beside the app (`RNestInstalled`) and for each other engine; Draft for Sparrow only (1.1.77, the user: "Sparrow only"; a draft is for the report's area and cut length; at Draft settings on the kit's 16 jobs Sparrow came out +4.4 % on average against the best layout in ≤ 2.8 s, opus +1.0 % in ≤ 16 s, sonnet +3.0 %, RNest +9.2 % with failures on arcs, dsh +12.7 %). A Draft remembered with another engine runs on Sparrow. The button names the engine. The variants list has an Engine column (1.1.76). Saved as `NestMode` + `NestEngine`, which older builds read the same way. RNest quick/careful items (population 1/5) were tried in 1.1.67 and taken out in 1.1.68.
  - **⚡ Draft** (`NestEffort.Draft`): a rough nest for a quote. 100 iterations, 1 try, no last-chance pass, coarse engine flags, fill-up up to 2 packs. Its variants name the engine "Черновик" (1.1.89, the user: no mark, call the engine Draft; `NestRunHistoryItem.IsDraft`).
  - **▶ Nest** (`NestEffort.Fast`, the old Fast numbers).
  - Both read `nest-profiles.json` on every press. The file is written with defaults on first run; nonsense values fall back to defaults; an unreadable file gives defaults plus a message.
  - Normal/Best still exist in code for tests but are not in the UI.
- **Fill-up** (`SparrowNestService.FillUp`): after the best sheet size is chosen, it offers waiting large parts (≥5% of the sheet, area permitting) one at a time, largest first, up to 4 packs. A part stays only if the whole set still fits. Sheets with common-line parts are left alone. It took `test1` from 3 sheets to 2.
- **Free room** (`RasterNest/FreeRoom.cs`, 2026-11-10): the sheet's empty metal on a grid of cells (part boxes grown by the spacing). Fill-up first offers parts whose box fits that room, whatever their size; small ones without room are not offered. After fill-up, `PutIntoFreeRoom` puts waiting parts into the room without a search. It also tries taking off the small parts (< 5 %, at most 50) and laying them again with the waiting ones, under four rules (from the bottom or the left; ties to the nearer far side along or across), and keeps whichever puts the most on. The door job (`PortaFixture`, rectangles drawn by the test: eight strips 1915 long and eight panels 290 × 425, on two 1250 × 2500 sheets) placed 13 of 16; both buttons now place all 16 on every seed, four strips and four panels on each sheet as by hand (`PortaFixture`). The cause: the panels are 4 % of the sheet, below fill-up's 5 %, and the engine lays them on their side because that shortens its strip.
- **Rows** (`RasterNest/RowsLayout.cs`, 1.1.74, the user on the uprights job: "I don't like this layout" / "I like this", a hand layout in `Flat-Stoika M-50…chf`). `Nest` lays the job in rows (shown at once as a candidate), still runs the engine, and `RowsBeatEngine` picks: for fill jobs rows win at ≥ 95 % of the engine's good parts; for counted jobs rows win when the engine left parts off or too close, else when the sheet area is no larger and the run ends within 5 % of the engine's. So rows save no time; on L parts (`PortaFixture`) the engine wins. Rows apply to jobs of at most four kinds, no common cutting, no free rotation, and rectangular sheets of the best priority. It covers two cases:
  - counted parts that all go on one sheet (the smallest size that holds them);
  - a kit of two or more fillers on a single stock size: the most kits that go on, by halving, with the same layout on every sheet.
  - How rows are laid: each kind is a block of rows (`RowMotif`): every allowed angle, every other part / row / both turned 180° when the part may turn so, straight or half-shifted rows, with steps bisected on outlines grown by half the spacing. Blocks are stacked along the sheet. The strips beside a block, and the end of a short last row, are filled by the next kind first. Every order of kinds is tried, with the first kind's six shortest blocks. The layout ending nearest the start wins, and is checked with `CountUnfit`.
  - Uprights on 1250 × 2300 at 5 mm from the right: 28 kits with 5–7 pairs too close (RNest, 26 s) became 30 kits, none too close, in 0.4 s (`RowsLayoutFixture`, `test-data/stoika`).
  - Limit: blocks meet box to box. The operator's hand layout (36 kits on 1250 × 2500) interlocks tapered parts across block edges and uses about 2.5 mm spacing; rows give 34 there.
  - `SHEETNEST_ROWS=0` turns rows off.
- **Part priority is kept** (`SparrowNestService.PriorityFirst`, since 1.1.60, the user: "if I set a priority, it matters"). The engine is priority-blind and a sheet keeps what lands inside the strip, so after a pack, when a part of a better priority waits while a worse one is on the sheet, the sheet is packed again with all parts of that priority and better, and the worse copies on it less enough room (worst priority and biggest first; 10 %, then +30 % a try, up to 4 packs). Fill-up then adds back what fits. The cap job (`test-data/priority`, 125 % of one 1250×1250) left a side of priority 1 off with lamellas of 5 on; now all four sides and all four uprights (3) go on (`PriorityFirstFixture`, fails without it).
- Sheet priority: higher-priority stock (offcuts) is consumed first, listed first, and shown first in the viewer, report and export. A worse-priority size is not run through Sparrow once a better one has packed.
- **Nesting direction** (Advanced Settings → "Раскладка: слева направо / справа налево", `SessionState.NestFromRight`, per machine; 2026-11-10, the user: "our laser cuts from right to left"). From the right the job is mirrored for the engine (`RasterNest/CutDirection.cs`: outlines and shaped sheets flipped, angles negated) and the result mirrored back: each part is its own drawing turned by −a, never mirrored, the layout packed from the bottom-right with the offcut on the left. The offcut line mirrors too (`OffcutOptions.FromRight`, `OffcutGeometry.Remnants`, used by the viewer and the export). Applies to Nest, Draft, fill from an offcut and the virtual sheets; common-line jobs are nested as they lie. Tested: `PortaFixture` (door job from the right; an L comes out as drawn, no overlaps, against the right edge; fails if the angle is not negated). Not clicked through in the running app.
- **Engine statistics** (1.1.78, `RasterNest/EngineStats.cs`, the user: "how many times best, run, DXF saved, job size; then we will see"; 1.1.80: "just save it, we will look at it some day", no window): `%LOCALAPPDATA%/SheetNest/engine-stats.json` (per key, e.g. `Sparrow`, `Sparrow/draft`: runs, failed, compared, best, DXF saved, pieces/kinds sums, max pieces, seconds) and `engine-runs.csv` (a line per run, `;`-separated, with the project file name). "Best" counts a job once its list is done with (cleared, another job, closing) and only when two engines or more were run on it. DXF counts the engine of the exported result (`ExportLayoutsAsync` now returns whether it wrote). Files only, no window or menu item (the 1.1.78 window was removed in 1.1.80); tested by `EngineStatsFixture`.
- Parallel searches: Auto = cores / threads per search, at most 8. `sparrow.exe` runs at BelowNormal priority. The result does not depend on concurrency.
- **Other engines** (2026-11-18, the user: "just a notice and a log to fix it; try another"): every `*.exe` in `SparrowEngine/engines/` ships to `<app>\engines\` and gets its own Nest item in the ▾ menu, by file name (`MainWindow.OtherEngines`, saved as `NestEngine` = 2 + `NestEngineName`). Now `opus55-high.exe` (v1), `sonnet55-high.exe` (v1), `dsh.exe` (v3), and from 1.1.84 `opus55-rust.exe` (tag opus55-rust-v1; v2 from 1.1.87: small jobs refined by overlap minimisation, the 1_3 elbow job 584 → 523–525, all 4 on the strip, as sonnet55-rust; app suite 551/556 with 3 `ShapedSheetFixture` time limits under load, 9/9 alone; v3 from 1.1.88: 12 % faster, the same layouts byte for byte on the kit) and `sonnet55-rust.exe` (tag sonet55-rust-v1, file renamed). App suite 2026-11-20: sonnet55-rust 551/551; opus55-rust 550/551 (no common edges on the mixed job; the round-offcut pair failed only while other runs loaded the CPU). Not taken: haiku-go-v2, haiku-rust-v1 (answer depends on the clock), hy4-go-v1 (`C:/opencode/Hy4`, OpenCode; porta at RNest, stoika 31 s, Kolena 6). A run with no answer (crash, no output, watchdog) keeps `job.json`, `params.txt` (command line, reason, rerun command) and `log.txt` (last 500 lines) in `%LOCALAPPDATA%\SheetNest\engine-failures\<engine>-<time>` (newest 30; `EngineFailures`). A sheet no try could lay is collected per nest (`AsyncLocal`) and `OnNest` shows which engine failed on which sheet, "pick another engine", and offers the folder. No fallback to Sparrow. Tested with where.exe as the crashing engine (`EngineFailuresFixture`); the menu and the message not clicked through in the running app.
- Watchdog: Sparrow and RNest get `WatchdogSeconds` (minutes; a kill loses the race). Any other engine (`SHEETNEST_SPARROW`/`SPARROW_EXE` pointing elsewhere) gets 120 s (`ThirdPartyLeashSeconds`), and once a try of the same sheet answered in t s the others are stopped at max(60, 3t) (`WaveLeash`; `EngineLeashFixture`). Not for Sparrow: its seeds differ up to 20× in time.
- The variants list holds the best finished nest of each engine run on the job (1.1.78, the user: "I ran them all and could see only one"; a Draft is its own row, engine "Черновик") (1.1.80, the user: only the engine name, no Runs column): a run no better than its engine's row is dropped. The best is starred and on top (`MainWindow.CompareRuns`, 1.1.89, the user: "utilisation does not show density", "speed does not count", "no sorting"): all parts placed, then fewest sheets, then utilisation (if more than 0.1 % apart), then the larger offcut (if more than 0.2 % of the sheet apart), then the quicker run (if more than 0.5 s apart; a saved nest has no time and loses the tie). Utilisation is part area over used sheet area, so equal jobs often tie on it. Column "Остаток" = the largest clean rectangle W × H on the used sheets (`RasterNest/LargestOffcut.cs`: outlines rasterised on 512 cells along the long side, grown by the spacing, histogram stack; shaped sheets give "—"). A header click sorts (first direction, other, back to ranked; ▲/▼ in the header). The saved row's "Сохранено" follows the language. Tested: `LargestOffcutFixture`; the column, the widened panel (540 px) and a sort by time looked at in a UIA screenshot of a test instance (two Nest runs on demo.dnest), the ranking with several engines not clicked through. Per-sheet attempts only draw in the preview.
- **Sheet quantities stay as entered** (since 1.1.51). A nest only reports what it used: `ConsumeStock` returns counts by size and the badge reads "used / in stock".
  - Up to 1.1.50 a nest took its sheets off the quantities and gave them back on the next nest. That summed up: a sheet added after a nest came on top of the ones it held, and the user saw "2/3".
  - Projects saved that way carry `SheetsKeptWhole = false`. On opening they get the sheets back once (`ReturnStock`).
- **Not enough material → virtual sheets** (since 1.1.60/61, the user's request). When the stock runs out (no unlimited size) the parts left over (`LeftOverJob.Owed`: populations less what the result placed, by drawing and hand) are laid by a Draft nest on unlimited virtual 1250×2500 mm (48×96 in) sheets, cached per result (`MainWindow.virtualNests`, made in the background for a result shown later). They go at the end of the strips of layouts, red, "Нет материала N" (`DxfViewer.VirtualSheetsFor`, `SheetGroup.IsVirtual`): browsed like the rest, read-only, never in `GetProductionPlan`/`GetDistinctLayoutSheets` (so not in the export or the report's plan), not tickable as cut, not in `FindUnfitLayouts` (`VirtualSheetsInTheStripFixture`). The "not enough sheets" message says how many; the report adds "Докупить: N шт. 1250 × 2500" (`ReportShortage`). "Сохранить на потом…" on the Layout step writes the project with only what is owed (spares folded in, mirrored apart, fill rows dropped), the virtual sheets as stock, no nest (`LeftOverJob.ProjectJson`; `LeftOverJobFixture`). Not clicked through in the running app.
- **Cut sheets, nest the rest** (`CutSheets.DeductParts/TakeCutSheets`): ticked sheets' parts come off the order (mirrored from the mirrored count, others from quantity, then from spares). The cut sheets come off the quantities for good, the only place sheets leave the stock. A part quantity may be 0; such rows are not sent to the engine.
- **Move a part between sheets** (`DxfViewer.MoveSelectedTo`): only between single-copy sheets without common cutting. The part goes to the first free spot bottom-left, or stays red in place. An emptied sheet returns to stock (`SheetRemoved`). One undo step.
- **Fill the sheets** (`IDetailLoadInfo.FillSheets`; "Fill the sheets" in + Shape and in the part's "…"): a row with no count, e.g. a sheet of washers. The operator picks sheets as usual; the nest puts on as many as fit after every counted part.
  - Engine (`SparrowNestService.Nest`): a filler is offered ceil(stock area / spaced outline area), at priority 11. Unlimited sizes count as their box number, at least 1. `SelectBatch` keeps fillers out while counted parts wait. `TopUpWithFillers` adds them around a packed sheet after `DeferUnfit`. Unplaced fillers are not reported as missing.
  - One filler goes in rows (`LatticeFill`): every allowed angle, straight or half-shifted rows, steps bisected on the spaced outline. Sparrow took 2.5 min over 900 washers on 1250×500; rows take 0.2 s and seat 798 Ø24. A rows-only sheet skips `DeferUnfit`, being clear by construction. `CommonEdgeClearance.FindUnfit` now skips pairs whose boxes are apart (a 2652-washer sheet: 25 s → 0.5 s, in the viewer too).
  - When rows seat 40 or fewer (`LatticeSearchMax`), the chosen engine searches the sheet too, judged by `DeferUnfit`, and the sheet keeps whichever seats more (1.1.83, the user on the elbow sides, `test-data/kolena`: "I put 7 on by hand"). Rows give 6 on 320 × 2000 at 5 mm; Sparrow and sonnet55-high give 7, RNest, dsh and opus55-high 6 (`KolenaFillFixture`, 3 s). Until then a single filler never reached the engine, so every engine showed the same layout.
  - The same around counted parts (`TopUpWithFillers`, 1.1.86, the user on `1_fill`: one counted elbow side R100/200 and the 150 side filling 500 × 530): rows around them seat 40 or fewer, so the engine packs the counted parts with the filler too, judged by `DeferUnfit`, all counted parts kept, else the rows stay. Sparrow offered more than goes on seats fewer (2 of 4 offered, 3 of 3), so a few large fillers are offered one less at a time while that can win. 1 filler became 3, as by count (`KolenaFillFixture`).
  - **Several fillers are one kit** (an air-conditioner bracket of two parts) by default, as many of each (`Loaded.Kit`). From 1.1.54 the Parts step shows a switch once two included rows fill the sheets, "Заполнение: поровну / максимум" (`ProjectInfo.FillToMaximum`, saved with the project, `fillAsKit` to the engine). At "максимум" each filler is offered by its own footprint and tops up on its own, biggest first, nothing is evened out (`FillSheetsFixture.TwoFillersToTheMaximumAreNotEvenedOut`: more area covered). As a kit:
    - each is offered ceil(stock area / kit footprint);
    - `SelectBatch` offers whole kits on a fillers-only sheet;
    - `TopUpWithFillers` tops up by kits;
    - `EvenOutKit` drops the extra copies, last sheets first, after holes are filled.
    - Tested: `FillSheetsFixture.TwoFillersComeOutAsManyOfEach`.
  - After a nest the row's Quantity = what went on (`CountFilledRows`); the cell shows "fill: N". Fill parts get no names on the layout or in the DXF (`DxfExporter.UnnamedParts`, set in `ApplyPartColours`).

**Parts and sheets**
- **Default rotation is the four sides, 90° steps** (`RotationCodes.DefaultRotations` = 4; the user, 2026-11-17: the 0° + 90° default of 1.1.68–1.1.71 "was my mistake").
  - A newly added part gets it as its own (`DetailLoadInfo.NewPartRotations`, set in `NestProjectViewModel.AddPartRows`, since 1.1.69), so "Rot_4Way" (4 sides) is selected in Edit Part rather than "Job default". The model still starts at -1, so saved parts without the field follow the job (cf. issue #2). Parts added in 1.1.69–1.1.71 carry 1002 in their projects and keep it.
  - The job's code (`cfg.Rotations`) defaults to 4 again from 1.1.72; `SessionState.JobRotations` turns a stored 1002 from a settings file older than version 4 (`FourSidesAgainVersion`) into 4 once and writes the 4 back at start (`RotationInheritsTheJobFixture`; 1.1.72 did not write it back, so a later save stamped version 4 over the 1002 and kept it).
- Flip ↔/↕ mirrors the part itself (`PartMirror`): ticked in Edit Part ("…") and applied on OK; a copy of the drawing goes in `%TEMP%\sheetnest_flip` and is rewritten when a project opens. `DetailLoadInfo.Flip/UnflippedPath` are saved.
- **Part names** (`IDetailLoadInfo.DisplayName`; null = the file's name). Set in "…" or by double-clicking a part on a layout (`DxfViewer.PartRenameRequested` → `MainWindow.OnPartRenameRequested`). It shows in the parts table (`ShownName`), on layouts, in the DXF and the report, via `DxfExporter.PartNames` (set in `ApplyPartColours`; `GetPartNameLabel` and the report's `DisplayName` read it). The drawing file is not renamed.
- Both viewers get the part colours (the Nesting step's preview was grey until 1.1.49). The Nesting step has no ◀ n/m ▶ bar (`ShowSheetNav = false`): its thumbnails move between layouts. No back/next buttons on the steps since 1.1.81 (the user: they repeat the step bar at the top); the empty Layout step keeps its "go to nesting" button.
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
- Standard sheets stand (since 1.1.52, the user's request): 1000×2000, 1250×2500, 500×1250 mm; the inch list likewise short side first; Custom size opens at 1500×3000. Anything else via "Custom size".
- **Standing sheets are packed bottom-up** (`RasterNest/StandingSheets.cs`, since 1.1.63). Sparrow shortens its strip along X, so from 1.1.52 a standing sheet came out in columns from left to right over its whole height (the shop's 360 × 1890 job, `test-data/alexi`: two columns, 1860 mm long). When most of the stock stands (by area) and no part is common-cut, the job is nested turned a quarter turn clockwise (sizes swapped, sheet outlines turned, each part's geometry turned −90° so its angles mean the same), turned back, and the parts slid left then down (`RasterCompact`, not on shaped sheets). That job now ends at 859 mm on every seed, no part hanging over empty metal (`CornerPackFixture`, six seeds per effort). The user (2026-11-03): the direction does not matter, they turn the sheet with ⟲ to try the other way; what matters is no empty metal in the middle of the sheet. After the slide the columns are stood tallest first from the left (`TallColumnsFirst`, 2026-11-10, the user's request on the door job: "tall, short, short, tall" left a comb of gaps; tall ones first leaves a staircase, a more usable piece). A column is a run of parts whose boxes overlap across; columns are split only where boxes stand a spacing apart, so any order stays clear (`PortaFixture` checks the tops never rise). In 1.1.62 columns a part wider than its column (lamel_top, 90 mm) hooked on a neighbour and left 230 mm empty under it, which no down-left slide can fix. Then each column of several parts is laid again by boxes (`TidyOffcut`, at most 30 parts on the sheet): widest first or tallest first, under the four free-room rules, kept when the largest empty rectangle grows by more than 5 % and the column does not rise above its old top (two grid cells allowed, then slid again). On the door job the second sheet now comes out as the user laid it by hand: two strip pairs, two panels in a row at the bottom and the two short strips over them, one large clean offcut.
- **Turn a sheet** ("⟲" beside the size on a stock row (moved there in 1.1.86: the Actions column was cut off at 1080 px), `MainWindow.OnRotateSheet`, since 1.1.62): a rectangle swaps W and H; a sheet from a drawing keeps `ISheetLoadInfo.ShapeTurns` (quarter turns CCW) and `SheetShape.Load(path, turns)` turns its outline and holes before moving the box to 0,0; the sketch binds the turns too. A nest on screen is cleared. Tested: `ShapedSheetFixture.AnOffcutTurnedAQuarterTurnLiesTheOtherWay`.
- **Sheets from a drawing** (Add sheet → "Sheet from a drawing (DXF, CHF)…", `SheetShape.Load`, `ISheetLoadInfo.ShapePath`): an offcut of any shape. The largest closed contour is the edge, closed contours inside it are holes, anything outside is dropped; moved so its box starts at 0,0. The row's W×H is the rounded box (rounded like the "used / in stock" and cut-sheet matching), priority 1, quantity 1; the file is read again on every nest, as parts are.
  - The Sheets table has a sketch column (`SheetPreviewConverter`, since 1.1.53): every row in proportion, a drawn sheet with its outline and holes, cached by path and time written.
  - **Round sheets by diameter** (Add sheet → "Круглый лист (диаметр)…", since 1.1.59): `AddSheetWindow.UseRound` asks the diameter (priority 1), `SheetShape.RoundSheetFile` writes a circle once to `%LOCALAPPDATA%/SheetNest/sheets/Ø500 mm.dxf` and the row is a sheet from that drawing. Its size is fixed by the drawing: to change a diameter, add a new round sheet.
  - Packed by `SparrowNestService.PackShapedSheet` (`RasterNest/ShapedSheetPacker.cs`), not by Sparrow (a strip packer; patching it needs Rust): no-fit polygons with Clipper Minkowski sums, per part and angle (poses built in parallel). Three gravities are packed and the one with the most part area kept: along the long side, along the short side, outward against the edge (rings a round offcut). Counted parts by priority and size first, fillers after; the counted copies are also tried in turn (one of each size per round), and a kit of fillers always goes on in turn and ends when one member no longer fits (bad2: filled as a kit it gave 1+1 where 3+2 went on by count, now 2+2; `RoundOffcutFilledAsAKitHoldsWhatACountDoes`). 3+3 does not go on at 6.35 nor at 4 mm (bad2a gives 3+2 in every order tried; 4+1 also fits at 4). Fill-up, top-up, last chance and the common-line planner are skipped on such a sheet; `DeferUnfit` and `DropOutsideShape` still judge it. A drawing that is a plain rectangle goes to Sparrow as usual.
  - Every candidate position is checked on the real shapes (`ShapedFits`) before it is taken: Clipper leaves zero-area slivers where two no-fit polygons meet, and on the shop's round offcut (`test-data/bad`, 2026-10-23) a part went on top of another, `DeferUnfit` took it off, and 3 of 4 parts were placed with half the circle empty. Outlines are reduced by Douglas-Peucker to 2.5e-4 of the sheet diagonal (grown first, the sheet shrunk first): that job went from 50 s to 5 s.
  - Measured (`ShapedSheetFixture`): the round offcut takes the order (2 + 2 elbow sides) and the operator's own layout (4 + 1) in 3–5 s; the L-shaped test offcut 8 squares where the first version found 7.
  - One part filling what is left of a shaped sheet (washers) is laid in rows (`ShapedRows`): every orientation, straight and half-shifted rows at the `LatticeSteps` the rectangle lattice uses, the grid slid in eighths of a step; a position counts when it is in the pose's free region. Kept when it seats at least as many as the part-by-part pack. BAD3 (Ø638, M8 washers, 5.5): 665 scattered, the operator's straight rows (bad4.chf) 659, shifted rows 739 in 1.3 s (`WashersFillARoundOffcutInShiftedRows`).
  - The layout, thumbnails and report draw the outline and holes (`SheetShape.IsShaped`), utilisation is over the metal, and the editor paints a part red off the metal or inside the margin of the edge or a hole (`IsOutsideShape`). The cut DXF carries the outline and holes on Background (`DxfExporter.AppendSheetOutline`), never cut.
  - Not done: the offcut line (`OffcutOptions`) still measures the box; a shaped and a rectangular sheet with the same rounded box share the "used / in stock" count.
- Part sizes load in `Task.Run` once, and the notification goes back to the asking thread's context. A report of missing sizes on the user's PC could not be reproduced; ask for a screenshot if it recurs.

**Import**
- `DrawingFile.Load` recognises CHF by content. An empty file gives a clear error. Drawings are read with `DxfPartSplitter.ReadAllBytesShared`, so one still open in the CAD program reads too (import, export, inner cuts, the common-line catalogs; upstream baaf56d plus `DrawingFile`, 2026-11-04).
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
- Every sheet is drawn lying in the report and its preview (1.1.82, the user: "turn the picture to lie, whatever the real position of the sheet"): a standing sheet turns a quarter turn clockwise, its bottom edge on the left (`NestReportPdf.LiesTurned/Lying`, numbers too); the caption keeps the real W × H (`NestReportLyingSheetFixture`). Not looked at in a real report.
- **Pictures in the report** (1.1.85, the user: "pictures from files and from the clipboard that go into the report"): "🖼 Картинки..." on the Layout step opens `PicturesWindow` (add files, drop files, paste button or Ctrl+V; caption, order, remove). Kept in the .dnest as base64 (`ProjectInfo.Pictures`, `ProjectPicture`), not in the undo history (`ProjectHistory.Rows` drops them, `Restore` keeps them). `ReportPicture.Import`: EXIF turn applied, shrunk to 2400 px, small upright JPEG kept, else PNG (JPEG q90 above 1.5 MB); clipboard order: files, "PNG", bitmap (alpha dropped). Each prints on its own page after the layouts, caption as title (or "Рисунок N из M"), never enlarged past 1.5 pt/px; in the PDF as a DCTDecode JPEG flattened on white (no zlib). `ReportPicturesFixture`; the PDF rendered by Windows.Data.Pdf, and paste → caption → OK → preview page clicked through in a test instance. Add files and drop not clicked.

**Saving**
- `.dnest` writes sheets through `SheetPlacementJsonConverter`, so computed members (PolygonsForExport, Hull, Simplify) are not saved: 4.4 MB → ~1.4 MB. `SvgPoint.Marked` is still written, because the golden JSON tests need it.
- Projects are not zipped (native zlib is unreliable on the shop PC).
- **Drawings found beside the project** (`ProjectPaths.Relocate`, called by `ProjectInfo.Load`, since 1.1.61): a part, its unflipped or source drawing, or a sheet's ShapePath missing at its path is looked for by file name in the folder the .dnest was opened from and every folder below (unreadable ones skipped); of several, the one sharing the most trailing folders with the old path. The saved nest's strings equal to a moved path are rewritten too (the DXF export reads drawings back by Part.Name). Paths that exist, or match nothing, stay (`ProjectPathsFixture`).
- **Save with drawings** (File → "Сохранить с чертежами...", `MainWindow.OnSaveWithDrawings`, 1.1.70, to carry a job to another PC): a copy of the project (the open one keeps its paths and file) goes through `ProjectPaths.Pack`, which copies every part, unflipped and source drawing, sheet ShapePath and `.shape.json` record into "<name> чертежи" beside the .dnest (same names apart as "name (2).dxf", read shared) and rewrites the paths and the saved nest; unreadable files are listed and keep their path. On the other PC `Relocate` finds them (`ProjectPathsFixture`). Not zipped (zlib).
- **Undo and revert** (1.1.70, `MainWindow.History.cs`, `ProjectHistory`): Ctrl+Z on the Parts and Sheets steps (not in a text field) and File → "Отменить" step back through the job's rows, material and fill choice, 50 deep. A state is the project JSON without `Config` and the nest; noted 400 ms after changes stop (collections, rows and ProjectInfo hooked), restored by `ObservableProjectInfo.Restore`, which keeps the nest on screen. The history starts at ApplicationIdle after a document becomes active. File → "Вернуть сохранённое" asks, then reopens `FilePath` (`ProjectHistoryFixture`). Not clicked through in the running app.
- **The last project** (1.1.71, `LastProject`, `MainWindow.LastProject.cs`, the user: "save the last project to _last automatically and add an item to load it"): `%LOCALAPPDATA%/SheetNest/_last.dnest` (+ `_last.meta.json`: its own path, when) is written on close (whatever was answered about saving), when another project replaces the active one, and on each autosave of a dirty job, with the nest on screen; the document is not touched. A job with no parts and no sheets does not overwrite it. File → "Открыть последний проект" (tooltip: path and time) opens it like a recovered autosave: pointed back at its own file, unsaved. Files under the SheetNest data folder never go into Recent Projects (`LastProjectFixture`). Not clicked through in the running app.
- **Reload drawings from disk** (File → "Перечитать чертежи с диска", F5, "⟳ Перечитать" on the Parts step; `MainWindow.OnReloadDrawings`, since 1.1.62): parts split out of a drawing are cut out again (`DxfPartSplitter.Rebuild`), flipped parts rewritten from the original (`PartMirror.Write`), each row forgets its shape (`ObservableDetailLoadInfo.Reload`: sketch, size, area) and the thumbnails are redrawn (`PartPreviewConverter.Forget`); sheet sketches follow the file's time. A nest on screen is cleared after asking: the export reads drawings back, so new shapes would be cut where old ones were laid. Nesting itself always reads the files afresh; the common-line catalogs are content-addressed (`ReloadDrawingsFixture`).
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

## RNest: the second engine

`RNestEngine/` is our own nesting engine in Rust, a separate Cargo workspace. It builds `rnest-sparrow.exe`, a drop-in for `sparrow.exe` (same command line, same `job.json` and `final_job.json`). The operator picks the engine in the Nest button's ▾ menu (Advanced Settings until 1.1.72) (`SparrowNestService.UseRNestEngine`, saved as `SessionState.NestEngine`); with RNest one try runs instead of a race of seeds. Sparrow is 0.1 to 13 % denser, RNest 1.5 to 30 times faster (2026-11-12, five jobs).

- Build: `pwsh RNestEngineuild.ps1` (needs Rust), which copies the exe to `SparrowEngine`; the app project ships it when it is there and falls back to Sparrow when it is not.
- Description: `RNestEngine/README.md`. State, what was not checked and what to improve: `RNestEngine/HANDOVER.md`.
- Not checked: the switch in the window, and the CI fixtures through RNest.
- `engine-kit/` (2026-11-17, for a third-party engine): `TASK.md`, `ENGINE_PROTOCOL.md` (the `job.json` / `final_job.json` protocol), four real jobs, `bin/` with both engines, their answers at 300 iterations in `reference/`, `tools/check.mjs` (Node, no deps: demand, angles, strip height, overlaps deeper than 0.01, SVG) and `run-all.ps1` (runs an exe over the jobs, checks, compares the width with Sparrow's best; `-Set stress` runs `stress/`, two test jobs that hung engines, 60 s each). `compare-agents.ps1` measures every `agent/*` branch in one table.
  - The kit lives in `ssh://git@g.vkom.top/vadim/sheetnest-engine.git` (clone `C:/src/sheetnest-engine`; `engine-kit/` here is the source copy). Agents work on this PC, each in `C:/src/engines/<name>` on branch `agent/<name>`, and tag `<name>-vN`.
  - 2026-11-17 on this PC: qwen4-max-preview (Qoder) and dsh (DeepSeek), both written from scratch in Go, met the minimum (stoika 2519.5 by qwen4, shorter than Sparrow) in ≤ 8 s; iceman (RNest-based) failed stoika-free. Through the app suite: dsh 524/527 (L parts and a two-filler sheet laid looser), qwen4 hung on two jobs (now `stress/`). 2026-11-18: dsh-v2 540/541 (no common edges on the mixed job), kimi-k3-1-pro-v1 (qwen4's code plus fixes) 534/541 (crash on mixed orientation counts, only right angles for free rotation, door job 15/16); both answer all of `stress/`, which now has `cl30` (the mixed common-cut job). Later that day, all 543/544 and all of `stress/`: opus55-high-v1 (Go, from scratch; stoika 2526.7, stoika-free 1257.3, shorter than Sparrow on 9 of 12 stress jobs; overlaps of 2.45e-4 make SheetNest take an L off on the door job), sonnet55-high-v1 (Go; stoika 2466.0, the best of all, ≤ 9 s; free rotation only in 15° steps), dsh-v3 (still no common edges: other pairs closer than the spacing), qwen4-max-preview-v2 538/541 (crash on `cl30`).
  - `check.mjs` passes overlaps up to 0.01 deep; SheetNest rejects far smaller ones (opus, 2.45e-4).
  - `check.mjs` false-flagged concave parts until 2026-11-17 (the vertex average of an L lies outside it); no saved verdict changed.

## Open items

- The mixed common-line bench gives 31 edges instead of the reference 21. Decide whether to update the reference or find the cause.
- Tail repack (`SparrowNestService.ShrinkTail`) is written but off (`TailRepackEnabled = false`) and has not been measured.
- Grid compaction on very perforated parts still takes about 20–40 s in total.
- Native speakers' review of the de/fr/es/pt texts.
- Suggested to the user, not requested: price per metre / m² / sheet in the report.

## Keeping this file current

After relevant work, update the sections above in place (what changed, what was actually verified, what is still open) rather than appending a diary. Keep it short and put technical detail in the matching document. Never store credentials or whole conversations. Search with rg in the relevant module first; avoid walking `bin`, `obj`, `publish-*` and the whole profile.
