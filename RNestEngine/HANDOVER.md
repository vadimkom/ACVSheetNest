# RNest engine: handover

State on 2026-11-12. What the engine is and how it is called is in
[README.md](README.md); this file is what is done, what was not checked, and
what to improve.

## State

- The engine builds and its 95 tests pass in this folder (`cargo test --release --workspace`).
- `rnest-sparrow.exe` runs the same `job.json` as `sparrow.exe` and writes the
  same solution file. Compared on five jobs (table in the README): Sparrow is
  0.1 to 12.7 % denser, RNest 1.5 to 30 times faster.
- SheetNest builds with the switch (Advanced Settings → Nesting engine), and
  both exes land in the output folder. WiX harvests the whole publish folder,
  so the installer takes `rnest-sparrow.exe` along when it is in
  `SparrowEngine` at build time.
- Nothing is committed.

## Not checked

- **SheetNest end to end through RNest.** The fixtures `FillSheetsFixture`,
  `LeftOverJobFixture`, `NestExtentFixture` and `CornerPackFixture` were started
  with `SPARROW_EXE` pointing at `rnest-sparrow.exe`; the first run hung: the
  genetic search waited forever on a job of one part (fixed, with a test). The
  rerun passed, 11 of 11 in 3.9 minutes (two `NestExtentFixture` tests ended in
  under a millisecond, so they likely did not nest anything). Those fixtures race eight
  seeds, which is eight identical runs for this engine.
- **The switch in the window** was not opened or clicked. The saved setting,
  the fallback to Sparrow when the exe is missing, and the single try were only
  read in the code.
- **Common-line cutting, fill-up, shaped sheets, tail repack with RNest**: not
  run. The common-line benches pin values that came from Sparrow layouts and
  will not hold for another engine.
- **Free rotation and holes**: no job with free rotation was compared.
- The comparison used outlines without the spacing shell and one seed of
  Sparrow at 600 iterations. SheetNest itself races several seeds and keeps the
  best, so in the app Sparrow's lead is likely a little larger than in the table.
- Installer and update: no MSI was built with the new exe.

## What to improve

Ordered by what it would buy.

### 1. A strip mode inside the engine

Today the adapter runs the whole engine 2 to 14 times to find the shortest
sheet that takes all parts, because the engine lays rows along the long side of
a sheet and has no notion of "as short as possible". A native mode (fixed
height, minimise the length used, score a layout by its extent) would cut most
of RNest's time on hard jobs and should also pack tighter, since the search
would optimise the real target instead of "does it fit".

### 2. Close the density gap

The gap is small on parts with straight sides (0.1 to 3 %) and large on shaped
parts that interlock (11 to 13 %). What Sparrow does and RNest does not:

- **Compaction as a search.** RNest slides parts along the axes and re-inserts
  single parts. Sparrow shrinks the strip and resolves the overlaps that
  appear. A pass of that kind after placement (shrink, separate, repeat until
  it fails) is the most direct way to the missing percent.
- **Pairs.** Two shaped parts turned 180° into each other often make a near
  rectangle. Find such pairs once per shape and place them as one unit.
- **More candidates along edges.** Candidates are NFP vertices and crossings;
  sliding a part along an NFP edge to the best point is not searched.

### 3. A random seed

The engine is deterministic and ignores `-s`, so SheetNest's race of seeds
buys nothing and is cut to one try. With a seed that changes the part order or
the genetic search, several tries would give different nests and the existing
best-of-N machinery in `PackOneSheet` would work for RNest as it does for
Sparrow. Keep each seed reproducible.

### 4. Free rotation

A part with no `allowed_orientations` may take any angle; RNest tries 0, 90,
180 and 270. Add at least the angles that lay the part's longest edge along
the strip, and a finer step (15° or so) at the thorough effort.

### 5. Honest effort and progress

- `--max-iterations` only switches between two search sizes. Map SheetNest's
  three efforts (Fast, Normal, Best) to real budgets, measured on the shop
  jobs, so that Best is worth its time.
- `--workers` is ignored; rayon takes every core. SheetNest runs several
  engines at once and counts on each keeping to its threads.
- One `dens:` line is printed at the end. Print one per improvement so the
  progress bar moves during a long run.

### 6. Use the engine's own sheet logic

Through Sparrow's interface RNest is only a strip packer; SheetNest does the
sheets, priorities, offcuts, fill-up and hole filling around it. The engine
has its own: several sheets with stock priority, offcuts of any shape with
holes, parts already on a sheet, part priority, mirrored copies, row filling.
A second, native job format (parts with holes, the real stock, the settings →
placements per sheet) would let SheetNest hand RNest the whole job. This is a
large change on the C# side (`SparrowNestService` is 6700 lines built around a
strip packer) and is only worth it once RNest wins on quality.

### 7. Holes in the job

The job carries outlines only, so RNest never puts a part into another part's
hole; SheetNest fills holes itself afterwards. If the job format gains holes
(point 6), the engine can do it during placement.

### 8. Smaller things

- The label "Nesting engine" and its tooltip are English only; add them to the
  seven `Strings.*.resx`.
- Show which engine made a nest (status bar or report), so two results can be
  told apart.
- The Auto text of "Parallel searches" describes Sparrow's race; with RNest
  one engine runs and uses all cores.
- `build.ps1` is a manual step. Either call it from `package.ps1` or commit the
  built exe as `sparrow.exe` is committed.
- Dead-code warnings in `nest-nfp` (`PolyPoint`, `Poly`).
- A regression test for the adapter: a small `job.json`, checked for all parts
  placed, no overlaps, and the mapped contours matching the placed ones.
- A bench script that runs a folder of jobs through both engines and prints
  the table in the README, so a change to either engine can be measured.

## Ideas kept from the Rust rewrite

- Full edge-slide and contact heuristics as in `PartPlacementWorker`.
- Feasible region updated incrementally instead of rebuilt per placement.
- NFP cache keyed by a full geometry hash; today it is a quantised signature.
- Compaction is the largest cost on big jobs (about 30 s of CPU on 50 parts).
- Merged cut length and common-line placement inside the engine.
