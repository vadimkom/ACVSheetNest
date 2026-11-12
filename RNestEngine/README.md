# RNest engine

Our own nesting engine, written in Rust, shipped next to Sparrow as a second
engine the operator can pick in **Advanced Settings → Nesting engine**.

It lives here as a separate Cargo workspace. The C# solution does not compile
it; it only ships the built `rnest-sparrow.exe`, which speaks the command line
and the files of `sparrow.exe`.

For what is unfinished and what to improve, see [HANDOVER.md](HANDOVER.md).

## What it is for

Sparrow packs tighter but is slow. RNest is much faster and a little less
dense. Neither wins outright, so SheetNest keeps both and lets the operator
choose.

Measured on 2026-11-12, one strip-packing run each, 600 iterations, four
rotations, parts without the spacing shell:

| Job | Parts | Sparrow, mm | RNest, mm | RNest longer by | Sparrow, s | RNest, s |
|---|---|---|---|---|---|---|
| box_02 (three flat patterns) | 12 | 6145 | 6302 | 2.6 % | 27.5 | 0.9 |
| test1 (`test-data/test1`) | 18 | 20124 | 20147 | 0.1 % | 10.5 | 6.8 |
| alexi (`test-data/alexi`) | 20 | 547 | 565 | 3.2 % | 60.5 | 1.7 |
| duct (`test-data/dxf/Flat-Pattern*`) | 16 | 2868 | 3180 | 10.9 % | 62.8 | 2.1 |
| mix (six small shaped parts × 6) | 36 | 2080 | 2346 | 12.7 % | 117.8 | 21.6 |

The number is the length of strip used; shorter is better. Times for alexi and
duct were taken while other tests were running and are rough.

## Build

Needs Rust (rustup, stable toolchain, MSVC).

```
pwsh RNestEngine\build.ps1
```

This builds the release exe and copies it to `SparrowEngine\rnest-sparrow.exe`.
`DeepNestSharp.csproj` copies that file to the output folder when it exists, so
build the engine before the app. Without the file the app still works and uses
Sparrow whatever the setting says.

Tests: `cargo test --release --workspace` in `RNestEngine` (95 tests).

## How SheetNest calls it

Nothing in the call differs from Sparrow. `SparrowNestService.PackOneSheet`
writes `job.json`, starts the engine once per try in its own folder, and reads
`output/final_job.json`.

| | |
|---|---|
| Switch | `SparrowNestService.UseRNestEngine`, saved as `SessionState.NestEngine` (0 Sparrow, 1 RNest), per machine |
| Exe choice | `MainWindow.ResolveSparrowExe`: `SHEETNEST_SPARROW` wins, then `rnest-sparrow.exe` if chosen and present, then `sparrow.exe` |
| Tries | one instead of a race of seeds (`NestTries`): the engine has no random seed, every seed returns the same nest |
| Tests | the `SPARROW_EXE` variable of the CI fixtures can point at `rnest-sparrow.exe` |

### Command line

```
rnest-sparrow -i <job.json> [--max-iterations N] [-s SEED] [-x] [--workers N] [...]
```

| Flag | What RNest does with it |
|---|---|
| `-i`, `--input` | the job file |
| `--max-iterations N` | only tells quick from thorough: below 400 the search is one pass, otherwise a small genetic search |
| `-s`, `-x`, `--workers`, `--shrink-step`, `--max-fails`, `--compress-share`, `--compress-step`, `--no-improve-limit`, `--strike-limit`, `-t`, `-e`, `-c` | accepted and ignored |

Environment, for calibration: `RNEST_POPULATIONS`, `RNEST_GENERATIONS` (search
size), `RNEST_REFINE` (steps of the length search, default 4).

### Job in (`job.json`, the jagua-rs strip-packing instance)

```json
{ "name": "job", "strip_height": 1240.0,
  "items": [ { "id": 0, "demand": 4,
               "allowed_orientations": [0.0, 90.0, 180.0, 270.0],
               "shape": { "type": "simple_polygon", "data": [[x, y], ...] } } ] }
```

- `shape.data` is the outline only, already grown by half the part spacing.
  Holes are not in the job.
- `allowed_orientations` absent means any angle in Sparrow. RNest uses 0, 90,
  180 and 270.

### Solution out (`output/final_<name>.json`)

```json
{ "name": "job", "strip_height": 1240.0,
  "solution": { "strip_width": 6302.2, "density": 0.90, "run_time_sec": 0.9,
    "layout": { "container_id": 0, "density": 0.90,
      "placed_items": [ { "item_id": 0,
        "transformation": { "rotation": 90.0, "translation": [x, y] } } ] } } }
```

A placed item is the job's outline rotated about the origin by `rotation`
degrees counter-clockwise, then moved by `translation`. SheetNest reads
`placed_items` and `layout.density` only. A line with `dens: NN.N%` goes to
stdout, where SheetNest reads its progress, and `output/final_<name>.svg` shows
the layout.

## How it works

### The adapter (`crates/nest-sparrow`)

Sparrow's strip has a fixed height and no end. RNest nests a sheet, and lays
its rows along the sheet's long side, so an endless sheet comes back as one
line of parts. The adapter therefore looks for the shortest sheet of the
strip's height that still takes every part:

1. Try the length a 95 % dense pack would need, then 90 %, 85 % and so on,
   until all parts are placed.
2. Close in between the last length that failed and the one that worked
   (bisection, four steps by default), keeping the shortest result.

That is 2 to about 14 engine runs per job, which is where most of RNest's time
goes on the jobs it found hard.

Each placement is then turned into Sparrow's rotation and translation. The
rotation the engine reports is checked against the contour it actually placed;
if they disagree, the allowed angle that reproduces the contour is used and a
warning is printed.

`rnest-make-job` builds a `job.json` from drawings, to run one job through both
engines:

```
rnest-make-job <out.json> <strip_height> <copies> <rot: 0|2|4|free> <drawing>...
```

### The engine (`crates/nest-engine`)

A port of the DeepNest/SVGnest line (`DeepNestLib.Core`), reworked for speed.

- **Placement by no-fit polygons.** For each part the free positions are the
  inner NFP of the sheet minus the outer NFPs of the parts already placed.
  Candidates are the vertices of that region, crossings of NFP edges (where an
  exact-fit slot lies) and corner anchors, tried best first.
- **NFPs.** An orbiting NFP (`nest-nfp`), checked by its box and a least-area
  bound; when it fails, a Minkowski sum built from edge-pair parallelograms
  (`nest-clip`), slower and always right. NFP(B, A) is NFP(A, B) mirrored, so
  half the pairs are computed. They are cached and warmed up in parallel.
- **Search.** Several orders of the parts (by area, by longest side, as given)
  and orientation locks (all standing, all lying) run in parallel; the best is
  kept. `populations` and `generations` add a genetic search on top.
- **Compaction and re-insertion.** After placement parts slide towards the
  preferred corner, then single parts are taken out and put back where they
  fit better (`reinsertion_passes`).
- **Spacing** is an outline grown by half the gap. Through the adapter it is
  zero, because the job's outlines are already grown.
- **Direction.** Four packing corners (`PlacementDirection`); the adapter uses
  bottom-left.
- **Deterministic.** Parallel attempts are folded in a fixed order; the same
  job always gives the same nest on any machine.

Not reachable through the Sparrow interface, but present in the engine: several
sheets and stock priority, offcuts of any shape with holes, parts already lying
on a sheet, mirrored copies, part priority, filling a sheet with one part in
rows (`fill_sheet`), bend lines carried through placement.

### The other crates

| Crate | What it holds |
|---|---|
| `nest-geom` | points, bounds, area, point-in-polygon, simplification |
| `nest-nfp` | orbiting no-fit polygon, rectangle special case |
| `nest-clip` | clipper2 wrapper: boolean operations, offset, Minkowski NFP |
| `nest-io` | DXF and CHF reading, DXF export, parametric shapes, bend lines (grey or red lines, layer 9, phantom linetypes) |
| `nest-engine` | the nest itself |
| `nest-sparrow` | `rnest-sparrow.exe` and `rnest-make-job.exe` |

## Where it came from

Copied on 2026-11-12 from `C:\src\rustnesting` (the Rust rewrite of SheetNest
with its own egui window). The window (`nest-app`) and the plain runner
(`nest-cli`) stayed there. This copy is the one to develop from now on: the C#
app is the main version.
