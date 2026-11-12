//! `rnest-sparrow` — our engine behind the command line of `sparrow.exe`, so
//! ACVSheetNest (SheetNest) can run either engine on the same job.
//!
//! In: `-i job.json` (a jagua-rs strip-packing instance: `strip_height` and
//! `items` with `id`, `demand`, `shape.data`, optional `allowed_orientations`).
//! Out: `output/final_<name>.json` in the working directory, the shape of
//! sparrow's solution (`solution.layout.placed_items[].transformation`,
//! `solution.layout.density`). A `dens: NN.N%` line goes to stdout, where the
//! caller reads its progress.
//!
//! The strip of jagua has a fixed height and grows along X. Our engine fills a
//! sheet in columns from the left edge, so the strip is simply a very long sheet.

use nest_engine::geom::{bounds_of, rotate_points};
use nest_engine::{nest, Config, DetailLoadInfo, PlacementDirection, SheetLoadInfo};
use nest_geom::SvgPoint;
use nest_io::RawDetail;
use std::collections::HashMap;

#[derive(serde::Deserialize)]
struct Job {
    #[serde(default)]
    name: String,
    strip_height: f64,
    items: Vec<Item>,
}

#[derive(serde::Deserialize)]
struct Item {
    id: usize,
    demand: u32,
    shape: Shape,
    /// Degrees. Absent = any angle in sparrow; here the four right angles.
    #[serde(default)]
    allowed_orientations: Option<Vec<f64>>,
}

#[derive(serde::Deserialize)]
struct Shape {
    data: Vec<[f64; 2]>,
}

struct Args {
    input: String,
    max_iterations: Option<u64>,
    seed: u64,
}

/// sparrow's flags. Those that only steer its own search are accepted and ignored.
fn parse_args() -> Args {
    let mut a = Args { input: String::new(), max_iterations: None, seed: 0 };
    let mut it = std::env::args().skip(1);
    while let Some(flag) = it.next() {
        match flag.as_str() {
            "-i" | "--input" => a.input = it.next().unwrap_or_default(),
            "--max-iterations" => a.max_iterations = it.next().and_then(|v| v.parse().ok()),
            "-s" | "--rng-seed" => a.seed = it.next().and_then(|v| v.parse().ok()).unwrap_or(0),
            "-x" | "--early-termination" => {}
            "--workers" | "--shrink-step" | "--max-fails" | "--compress-share" | "--compress-step"
            | "--no-improve-limit" | "--strike-limit" | "-t" | "--global-time" | "-e" | "--exploration"
            | "-c" | "--compression" => {
                it.next();
            }
            _ => {}
        }
    }
    if a.input.is_empty() {
        eprintln!("usage: rnest-sparrow -i <job.json> [--max-iterations N] [-s SEED] [-x] [--workers N]");
        std::process::exit(2);
    }
    a
}

fn area(p: &[SvgPoint]) -> f64 {
    let n = p.len();
    (0..n).map(|i| p[i].x * p[(i + 1) % n].y - p[(i + 1) % n].x * p[i].y).sum::<f64>().abs() / 2.0
}

fn env_u32(name: &str) -> Option<u32> {
    std::env::var(name).ok().and_then(|v| v.trim().parse().ok())
}

/// How far the vertices of `a` lie from those of `b` at worst.
fn deviation(a: &[SvgPoint], b: &[SvgPoint]) -> f64 {
    a.iter()
        .map(|p| b.iter().map(|q| (p.x - q.x).hypot(p.y - q.y)).fold(f64::INFINITY, f64::min))
        .fold(0.0, f64::max)
}

fn main() {
    let args = parse_args();
    let started = std::time::Instant::now();
    let text = std::fs::read_to_string(&args.input).unwrap_or_else(|e| {
        eprintln!("cannot read {}: {e}", args.input);
        std::process::exit(1);
    });
    let job: Job = serde_json::from_str(&text).unwrap_or_else(|e| {
        eprintln!("cannot parse {}: {e}", args.input);
        std::process::exit(1);
    });

    // The items as the strip has them.
    let items: HashMap<usize, Vec<SvgPoint>> = job
        .items
        .iter()
        .map(|i| (i.id, i.shape.data.iter().map(|p| SvgPoint::new(p[0], p[1])).collect()))
        .collect();
    let details: HashMap<String, RawDetail> = items
        .iter()
        .map(|(id, pts)| {
            let name = id.to_string();
            (name.clone(), RawDetail { name, outer: pts.clone(), children: Vec::new(), bends: Vec::new() })
        })
        .collect();

    let parts: Vec<DetailLoadInfo> = job
        .items
        .iter()
        .filter(|i| i.demand > 0)
        .map(|i| DetailLoadInfo {
            name: i.id.to_string(),
            quantity: i.demand,
            allowed_rotations: i.allowed_orientations.clone().unwrap_or_else(|| vec![0.0, 90.0, 180.0, 270.0]),
            ..Default::default()
        })
        .collect();

    // The search budget. sparrow's iteration count has no meaning here; it only
    // tells a quick request from a thorough one.
    let thorough = args.max_iterations.map_or(true, |n| n >= 400);
    let config = Config {
        spacing: 0.0, // the items arrive already grown by half the gap
        placement_direction: PlacementDirection::BottomLeftToTopRight,
        populations: env_u32("RNEST_POPULATIONS").unwrap_or(if thorough { 2 } else { 1 }),
        generations: env_u32("RNEST_GENERATIONS").unwrap_or(if thorough { 3 } else { 1 }),
        ..Config::default()
    };
    let _ = args.seed; // the engine is deterministic: every seed gives the same nest

    // The strip has no end, but the engine nests a sheet and lays its rows along
    // the long side, so an endless sheet comes back as one line of parts. Find the
    // shortest sheet of the strip's height that still takes every part: start
    // from the length a dense pack would need and let it out until all fit, then
    // close in between the last length that failed and the first that did not.
    let total_area: f64 = job.items.iter().map(|i| area(&items[&i.id]) * i.demand as f64).sum();
    let total_qty: usize = job.items.iter().map(|i| i.demand as usize).sum();
    let end_to_end: f64 = job
        .items
        .iter()
        .map(|i| {
            let b = bounds_of(&items[&i.id]);
            b.map(|b| b.width().max(b.height())).unwrap_or(0.0) * i.demand as f64
        })
        .sum::<f64>()
        + 1.0;
    let extent_of = |r: &nest_engine::NestResult| -> f64 {
        r.sheets.iter().flat_map(|s| &s.placements).filter_map(|p| bounds_of(&p.outer)).map(|b| b.max_x).fold(0.0, f64::max)
    };
    let run = |length: f64| -> Option<nest_engine::NestResult> {
        if std::env::var("RNEST_TRACE").is_ok() { eprintln!("[rnest] try length {length:.2}"); }
        let sheets = vec![SheetLoadInfo { width: length, height: job.strip_height, quantity: 1, ..Default::default() }];
        let r = nest(&sheets, &parts, |name: &str| details.get(name).cloned().unwrap_or_default(), config.clone()).ok()?;
        let placed: usize = r.sheets.iter().map(|s| s.placements.len()).sum();
        (r.unplaced.is_empty() && r.sheets.len() <= 1 && placed == total_qty).then_some(r)
    };
    let mut best: Option<(f64, nest_engine::NestResult)> = None;
    let mut failed = 0.0f64;
    let mut density = 0.95;
    while density >= 0.2 {
        let length = (total_area / (job.strip_height * density)).min(end_to_end);
        match run(length) {
            Some(r) => {
                best = Some((extent_of(&r), r));
                break;
            }
            None => failed = length,
        }
        if length >= end_to_end {
            break;
        }
        density -= 0.05;
    }
    if best.is_none() {
        best = run(end_to_end).map(|r| (extent_of(&r), r));
    }
    if let Some((first, _)) = &best {
        let (mut lo, mut hi) = (failed, *first);
        for _ in 0..env_u32("RNEST_REFINE").unwrap_or(4) {
            if hi - lo < 1.0 {
                break;
            }
            let mid = (lo + hi) / 2.0;
            match run(mid) {
                Some(r) => {
                    let e = extent_of(&r);
                    hi = e.min(mid);
                    if best.as_ref().map_or(true, |b| e < b.0) {
                        best = Some((e, r));
                    }
                }
                None => lo = mid,
            }
        }
    }
    let Some((_, result)) = best else {
        eprintln!("nest error: the parts do not fit the strip");
        std::process::exit(1);
    };

    // The engine fills a sheet in columns from the left, which is the strip of jagua.
    let w = job.strip_height;
    let mut placed = Vec::new();
    let mut polys: Vec<Vec<SvgPoint>> = Vec::new();
    let (mut max_x, mut used_area) = (0.0f64, 0.0f64);
    for p in result.sheets.iter().flat_map(|s| &s.placements) {
        let Ok(id) = p.name.parse::<usize>() else { continue };
        let Some(item) = items.get(&id) else { continue };
        let in_strip: Vec<SvgPoint> = p.outer.clone();
        let Some(sb) = bounds_of(&in_strip) else { continue };

        // The rotation the engine reports, checked against the contour it placed;
        // if they disagree, the allowed angle that reproduces the contour wins.
        let mut candidates = vec![p.rotation];
        if let Some(i) = job.items.iter().find(|i| i.id == id) {
            candidates.extend(i.allowed_orientations.clone().unwrap_or_else(|| vec![0.0, 90.0, 180.0, 270.0]));
        }
        let mut best: Option<(f64, f64, [f64; 2])> = None;
        for rot in candidates {
            let r = rotate_points(item, rot);
            let Some(rb) = bounds_of(&r) else { continue };
            let t = [sb.min_x - rb.min_x, sb.min_y - rb.min_y];
            let moved: Vec<SvgPoint> = r.iter().map(|q| SvgPoint::new(q.x + t[0], q.y + t[1])).collect();
            let dev = deviation(&moved, &in_strip);
            if best.map_or(true, |b| dev < b.0) {
                best = Some((dev, rot, t));
            }
            if dev < 1e-6 {
                break;
            }
        }
        let Some((dev, rot, t)) = best else { continue };
        if dev > 1e-3 {
            eprintln!("warning: item {id} is off by {dev:.4} after mapping back");
        }
        max_x = max_x.max(sb.max_x);
        polys.push(in_strip.clone());
        used_area += area(item);
        placed.push(serde_json::json!({
            "item_id": id,
            "transformation": { "rotation": rot, "translation": t },
        }));
    }

    let min_x = 0.0;
    let strip_width = (max_x - min_x).max(1e-9);
    let density = (used_area / (strip_width * w)).clamp(0.0, 1.0);
    let name = if job.name.is_empty() { "job".to_string() } else { job.name.clone() };
    let out = serde_json::json!({
        "name": name,
        "strip_height": job.strip_height,
        "solution": {
            "strip_width": strip_width,
            "layout": { "container_id": 0, "placed_items": placed, "density": density },
            "density": density,
            "run_time_sec": started.elapsed().as_secs_f64(),
        },
    });

    println!("[rnest] placed {} of {}, width: {:.3}, dens: {:.1}%", placed.len(), job.items.iter().map(|i| i.demand).sum::<u32>(), strip_width, density * 100.0);
    if let Err(e) = std::fs::create_dir_all("output") {
        eprintln!("cannot create output: {e}");
        std::process::exit(1);
    }
    write_svg(&format!("output/final_{name}.svg"), &polys, strip_width, w);
    let path = format!("output/final_{name}.json");
    if let Err(e) = std::fs::write(&path, serde_json::to_string(&out).unwrap_or_default()) {
        eprintln!("cannot write {path}: {e}");
        std::process::exit(1);
    }
}

/// The layout as an SVG next to the JSON, as sparrow writes one: strip frame.
fn write_svg(path: &str, polys: &[Vec<SvgPoint>], width: f64, height: f64) {
    let mut s = format!(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width:.1} {height:.1}\"><rect width=\"{width:.1}\" height=\"{height:.1}\" fill=\"#fff\" stroke=\"#000\" stroke-width=\"2\"/>"
    );
    for p in polys {
        let pts: Vec<String> = p.iter().map(|q| format!("{:.2},{:.2}", q.x, height - q.y)).collect();
        s.push_str(&format!("<polygon points=\"{}\" fill=\"#9cf\" stroke=\"#024\" stroke-width=\"1\"/>", pts.join(" ")));
    }
    s.push_str("</svg>");
    let _ = std::fs::write(path, s);
}
