//! Golden smoke tests against the reference DXF assets (`ACVSheetNest` demo
//! parts). Guards nesting correctness and quality against regressions.
//!
//! Skipped silently when the reference checkout is not present, so the test
//! stays green on machines without `/Users/vadim/src/claude_code/acv_ref`.

use nest_engine::geom::{details_overlap, inside_sheet};
use nest_engine::{nest, Config, DetailLoadInfo, PartPlacement, SheetLoadInfo};
use nest_geom::{polygon_area, SvgPoint};
use nest_io::{load_detail, RawDetail};
use std::path::PathBuf;

fn demo_dir() -> Option<PathBuf> {
    // The demo drawings live in the repository (shared with nest-io's tests).
    let dir = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../nest-io/tests/data");
    if dir.is_dir() { Some(dir) } else { None }
}

fn demo_dxf_files() -> Vec<(String, PathBuf, RawDetail)> {
    let Some(dir) = demo_dir() else { return Vec::new() };
    let mut out = Vec::new();
    let mut entries: Vec<_> = std::fs::read_dir(&dir)
        .expect("read demo dir")
        .filter_map(|e| e.ok())
        .map(|e| e.path())
        .filter(|p| p.extension().map(|x| x.eq_ignore_ascii_case("dxf")).unwrap_or(false))
        .collect();
    entries.sort();
    for path in entries {
        let name = path.file_stem().unwrap().to_string_lossy().to_string();
        match load_detail(&path) {
            Ok(detail) => out.push((name, path, detail)),
            Err(e) => panic!("failed to load {}: {e}", path.display()),
        }
    }
    out
}

/// All parts must be inside the sheet, pairwise non-overlapping (hole-aware).
fn assert_valid(placements: &[PartPlacement], sheet_w: f64, sheet_h: f64) {
    for p in placements {
        assert!(
            inside_sheet(&p.local_outer, (p.x, p.y), sheet_w, sheet_h),
            "part {} escapes the sheet",
            p.name
        );
    }
    for i in 0..placements.len() {
        for j in (i + 1)..placements.len() {
            let (a, b) = (&placements[i], &placements[j]);
            let overlaps = details_overlap(
                &a.local_outer, &a.local_holes, (a.x, a.y),
                &b.local_outer, &b.local_holes, (b.x, b.y),
            );
            assert!(!overlaps, "parts {} and {} overlap", a.name, b.name);
        }
    }
}

fn utilization(placements: &[PartPlacement], sheet_w: f64, sheet_h: f64) -> f64 {
    let placed: f64 = placements
        .iter()
        .map(|p| {
            let outer = polygon_area(&p.local_outer).abs();
            let holes: f64 = p.local_holes.iter().map(|c| polygon_area(c).abs()).sum();
            (outer - holes).max(0.0)
        })
        .sum();
    let sheet = sheet_w * sheet_h;
    if sheet <= 0.0 { 0.0 } else { placed / sheet }
}

#[test]
fn golden_demo_parts_nest_validly_and_deterministically() {
    let files = demo_dxf_files();
    if files.is_empty() {
        eprintln!("skipping: reference demo assets not found");
        return;
    }
    eprintln!("golden smoke over {} demo files", files.len());

    for (name, path, detail) in &files {
        let b = nest_engine::geom::bounds_of(&detail.outer)
            .unwrap_or_else(|| panic!("{} has no bounds", name));
        let w = b.width();
        let h = b.height();
        assert!(w > 0.0 && h > 0.0, "{} empty bounds", name);

        // One sheet sized to host 2 copies of the part (plus margin).
        let sheet = SheetLoadInfo {
            width: (w * 2.0 + 20.0).max(w + 1.0),
            height: (h * 2.0 + 20.0).max(h + 1.0),
            quantity: 1,
            source: None,
            ..Default::default()
        };
        let parts = vec![DetailLoadInfo {
            name: name.clone(),
            quantity: 2,
            allowed_rotations: vec![],
            mirrored_quantity: 0, mirror_allowed: false,
            is_priority: false,
            ..Default::default()
        }];

        let catalogue = |_n: &str| -> RawDetail { detail.clone() };
        let mut cfg = Config::default();
        cfg.generations = 1;
        cfg.populations = 1;

        let r1 = nest(&[sheet.clone()], &parts, catalogue, cfg.clone()).expect("nest 1");
        let r2 = nest(&[sheet], &parts, catalogue, cfg).expect("nest 2");

        let count1: usize = r1.sheets.iter().map(|s| s.placements.len()).sum();
        let count2: usize = r2.sheets.iter().map(|s| s.placements.len()).sum();
        assert_eq!(count1, 2, "{}: expected both copies placed, got {count1} (unplaced={:?})", name, r1.unplaced);
        assert_eq!(count1, count2, "{}: non-deterministic placement count", name);
        for s in &r1.sheets {
            assert_valid(&s.placements, s.sheet_width, s.sheet_height);
        }
        // Same geometry sequence across runs (deterministic engine).
        let sig1: Vec<_> = r1
            .sheets
            .iter()
            .flat_map(|s| s.placements.iter().map(|p| (p.name.clone(), p.x, p.y, p.rotation)))
            .collect();
        let sig2: Vec<_> = r2
            .sheets
            .iter()
            .flat_map(|s| s.placements.iter().map(|p| (p.name.clone(), p.x, p.y, p.rotation)))
            .collect();
        assert_eq!(sig1, sig2, "{}: non-deterministic layout", name);

        eprintln!("  {:>10}: ok, util {:.1}%", name, 100.0 * utilization(&r1.sheets[0].placements, r1.sheets[0].sheet_width, r1.sheets[0].sheet_height));
        let _ = path;
    }
}

    #[test]
    fn ga_search_is_deterministic_and_places_all_parts() {
        // populations=1 + generations=2 triggers the GA path with a 10-individual
        // population. Must be reproducible run-to-run and place everything.
        let files = demo_dxf_files();
        if files.is_empty() {
            eprintln!("skipping: reference demo assets not found");
            return;
        }
        let sheets = vec![SheetLoadInfo { width: 600.0, height: 400.0, quantity: 3, source: None, ..Default::default() }];
        let parts: Vec<DetailLoadInfo> = files
            .iter()
            .map(|(name, _, _)| DetailLoadInfo {
                name: name.clone(),
                quantity: 1,
                allowed_rotations: vec![],
                mirrored_quantity: 0, mirror_allowed: false,
                is_priority: false,
                ..Default::default()
            })
            .collect();
        let catalogue = |name: &str| -> RawDetail {
            files.iter().find(|(n, _, _)| n == name).map(|(_, _, d)| d.clone()).expect("known part")
        };
        let mut cfg = Config::default();
        cfg.generations = 2;
        cfg.populations = 1;

        let r1 = nest(&sheets, &parts, catalogue, cfg.clone()).expect("nest ga 1");
        let r2 = nest(&sheets, &parts, catalogue, cfg).expect("nest ga 2");
        let expected: usize = parts.iter().map(|p| p.quantity as usize).sum();
        let n: usize = r1.sheets.iter().map(|s| s.placements.len()).sum();
        assert_eq!(n, expected, "GA must place all parts (unplaced={:?})", r1.unplaced);
        let sig = |r: &nest_engine::NestResult| -> Vec<(String, i64, i64)> {
            r.sheets
                .iter()
                .flat_map(|s| s.placements.iter().map(|p| (p.name.clone(), (p.x * 1000.0).round() as i64, (p.y * 1000.0).round() as i64)))
                .collect()
        };
        assert_eq!(sig(&r1), sig(&r2), "GA runs must be reproducible");
        for s in &r1.sheets {
            assert_valid(&s.placements, s.sheet_width, s.sheet_height);
        }
    }

    #[test]
    fn clipper_scale_100_matches_scale_10() {
        // Higher clipper resolution must produce an equivalent nesting.
        let sheets = vec![SheetLoadInfo { width: 30.0, height: 30.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![DetailLoadInfo {
            name: "sq".into(),
            quantity: 6,
            allowed_rotations: vec![],
            mirrored_quantity: 0, mirror_allowed: false,
            is_priority: false,
            ..Default::default()
        }];
        let catalogue = |_n: &str| -> RawDetail {
            RawDetail {
                bends: Vec::new(),
                name: "sq".into(),
                outer: vec![
                    SvgPoint::new(0.0, 0.0),
                    SvgPoint::new(10.0, 0.0),
                    SvgPoint::new(10.0, 10.0),
                    SvgPoint::new(0.0, 10.0),
                ],
                children: vec![],
            }
        };

        let mut cfg10 = Config::default();
        cfg10.clipper_scale = 10.0;
        let mut cfg100 = Config::default();
        cfg100.clipper_scale = 100.0;

        let r10 = nest(&sheets, &parts, catalogue, cfg10).expect("nest scale 10");
        let r100 = nest(&sheets, &parts, catalogue, cfg100).expect("nest scale 100");

        let n10: usize = r10.sheets.iter().map(|s| s.placements.len()).sum();
        let n100: usize = r100.sheets.iter().map(|s| s.placements.len()).sum();
        assert_eq!(n10, 6, "scale 10: all placed");
        assert_eq!(n100, 6, "scale 100: all placed");
        assert!(r100.unplaced.is_empty(), "scale 100 unplaced={:?}", r100.unplaced);
        for s in &r100.sheets {
            assert_valid(&s.placements, s.sheet_width, s.sheet_height);
        }
        // Same packing quality at both resolutions (rounding differs slightly:
        // scale 10 snaps to a 0.05 grid, scale 100 to 0.005).
        let util = |r: &nest_engine::NestResult| -> f64 {
            r.sheets
                .iter()
                .map(|s| {
                    let placed: f64 = s.placements.iter().map(|p| {
                        let outer = nest_geom::polygon_area(&p.local_outer).abs();
                        outer.max(0.0)
                    }).sum();
                    placed / (s.sheet_width * s.sheet_height)
                })
                .sum()
        };
        let (u10, u100) = (util(&r10), util(&r100));
        assert!(
            (u10 - u100).abs() < 0.05,
            "utilization differs too much across scales: {u10} vs {u100}"
        );
    }

    #[test]
    fn packing_quality_squares_fill_sheet() {
    // 6 squares of 10x10 on a 30x30 sheet must pack with >= 60% utilization
    // (ideal is 2/3). Guards compaction/candidate quality regressions.
    let sheets = vec![SheetLoadInfo { width: 30.0, height: 30.0, quantity: 1, source: None, ..Default::default() }];
    let parts = vec![DetailLoadInfo {
        name: "sq".into(),
        quantity: 6,
        allowed_rotations: vec![],
        mirrored_quantity: 0, mirror_allowed: false,
        is_priority: false,
        ..Default::default()
    }];
    let catalogue = |_n: &str| -> RawDetail {
        RawDetail {
            bends: Vec::new(),
            name: "sq".into(),
            outer: vec![
                SvgPoint::new(0.0, 0.0),
                SvgPoint::new(10.0, 0.0),
                SvgPoint::new(10.0, 10.0),
                SvgPoint::new(0.0, 10.0),
            ],
            children: vec![],
        }
    };
    let result = nest(&sheets, &parts, catalogue, Config::default()).expect("nest");
    assert!(result.unplaced.is_empty(), "all squares must fit: unplaced={:?}", result.unplaced);
    let placed: usize = result.sheets.iter().map(|s| s.placements.len()).sum();
    assert_eq!(placed, 6);
    for s in &result.sheets {
        assert_valid(&s.placements, s.sheet_width, s.sheet_height);
        let util = utilization(&s.placements, s.sheet_width, s.sheet_height);
        assert!(util >= 0.59, "utilization {util:.3} below floor 0.59");
    }
}

/// The shop's door job: six strips 290 x 1915, two 225 x 1775 and eight panels
/// 290 x 425 on two sheets 1250 x 2500 at 4 mm. Everything goes on (four strips
/// and four panels a sheet) only when the panels stand; left to lie on their
/// side, one of the 16 stayed off. Also guards `pick_better`: a nest that
/// leaves a part off must not win for using fewer sheets.
#[test]
fn door_job_places_all_sixteen_on_two_sheets() {
    let rect = |name: &str, w: f64, h: f64| RawDetail {
        bends: Vec::new(),
        name: name.into(),
        outer: vec![
            SvgPoint::new(0.0, 0.0),
            SvgPoint::new(w, 0.0),
            SvgPoint::new(w, h),
            SvgPoint::new(0.0, h),
        ],
        children: vec![],
    };
    let catalogue = |name: &str| -> RawDetail {
        match name {
            "strip" => rect(name, 290.0, 1915.0),
            "short" => rect(name, 225.0, 1775.0),
            _ => rect(name, 290.0, 425.0),
        }
    };
    let part = |name: &str, quantity: u32| DetailLoadInfo {
        name: name.into(),
        quantity,
        allowed_rotations: vec![0.0, 90.0],
        mirrored_quantity: 0, mirror_allowed: false,
        is_priority: false,
        ..Default::default()
    };
    let parts = vec![part("strip", 6), part("short", 2), part("panel", 8)];
    let sheets = vec![SheetLoadInfo { width: 1250.0, height: 2500.0, quantity: 2, source: None, ..Default::default() }];

    let nest_cfg = Config { spacing: 4.0, ..Config::default() };
    let draft_cfg = Config { spacing: 4.0, populations: 0, generations: 0, reinsertion_passes: 0, ..Config::default() };
    for (label, cfg) in [("nest", nest_cfg), ("draft", draft_cfg)] {
        let r = nest(&sheets, &parts, catalogue, cfg).expect("nest");
        assert!(r.unplaced.is_empty(), "{label}: unplaced={:?}", r.unplaced);
        assert_eq!(r.sheets.len(), 2, "{label}: sheets");
        for s in &r.sheets {
            assert_eq!(s.placements.len(), 8, "{label}: parts on a sheet");
            assert_valid(&s.placements, s.sheet_width, s.sheet_height);
        }
    }
}
