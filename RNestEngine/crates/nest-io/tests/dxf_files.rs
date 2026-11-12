use nest_geom::{polygon_area, SvgPoint};
use nest_io::{export_dxf, load_detail, parse_dxf, RawDetail};
use std::path::{Path, PathBuf};

fn fixture_path(name: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests/data")
        .join(name)
}

fn bounds(points: &[SvgPoint]) -> (f64, f64, f64, f64) {
    let (mut min_x, mut max_x) = (f64::INFINITY, f64::NEG_INFINITY);
    let (mut min_y, mut max_y) = (f64::INFINITY, f64::NEG_INFINITY);
    for point in points {
        min_x = min_x.min(point.x);
        max_x = max_x.max(point.x);
        min_y = min_y.min(point.y);
        max_y = max_y.max(point.y);
    }
    (min_x, max_x, min_y, max_y)
}

fn assert_close(actual: f64, expected: f64, tolerance: f64) {
    assert!(
        (actual - expected).abs() <= tolerance,
        "actual {actual}, expected {expected} ± {tolerance}"
    );
}

fn assert_relative(actual: f64, expected: f64, tolerance: f64) {
    let error = ((actual - expected) / expected).abs();
    assert!(
        error <= tolerance,
        "actual {actual}, expected {expected}, relative error {error}"
    );
}

fn net_area(detail: &RawDetail) -> f64 {
    polygon_area(&detail.outer).abs()
        - detail
            .children
            .iter()
            .map(|hole| polygon_area(hole).abs())
            .sum::<f64>()
}

#[test]
fn arm_demo_has_sector_outline_and_five_drilled_holes() {
    let detail = load_detail(&fixture_path("arm.dxf")).unwrap();

    // The file contains a 70-degree annular sector (R330/R270) and five R9
    // circles. Arc chords have at most a 6-degree step, whose area error is
    // below sin(6°)/(6° in radians), hence the conservative 0.2% bound.
    assert_eq!(detail.children.len(), 5);
    let sector_area = 0.5 * (330.0_f64.powi(2) - 270.0_f64.powi(2)) * 70.0_f64.to_radians();
    assert_relative(polygon_area(&detail.outer).abs(), sector_area, 0.002);

    // Only the four ARC ends are original vertices. All other outer points are
    // chord samples; CIRCLE contours consist entirely of chord samples.
    assert_eq!(detail.outer.iter().filter(|point| point.exact).count(), 4);
    assert!(detail.outer.iter().any(|point| !point.exact));
    assert!(detail
        .children
        .iter()
        .flatten()
        .all(|point| !point.exact));

    for hole in &detail.children {
        let (min_x, max_x, min_y, max_y) = bounds(hole);
        assert_close(max_x - min_x, 18.0, 0.05);
        assert_close(max_y - min_y, 18.0, 0.05);
    }

    let (min_x, max_x, min_y, max_y) = bounds(&detail.outer);
    assert_close(min_x, 270.0 * 80.0_f64.to_radians().cos(), 1e-5);
    assert_close(max_x, 330.0 * 10.0_f64.to_radians().cos(), 1e-5);
    assert_close(min_y, 270.0 * 10.0_f64.to_radians().sin(), 1e-5);
    assert_close(max_y, 330.0 * 80.0_f64.to_radians().sin(), 1e-5);
}

#[test]
fn hub_demo_has_central_bore_and_four_bolt_holes() {
    let detail = parse_dxf(include_str!("data/hub.dxf"), "hub.dxf").unwrap();

    assert_eq!(detail.children.len(), 5);
    let expected_net = std::f64::consts::PI * (45.0_f64.powi(2) - 10.0_f64.powi(2) - 4.0 * 7.0_f64.powi(2));
    // Subtracting independently tessellated circles can accumulate their area
    // errors, so the net-area bound is slightly wider than for one contour.
    assert_relative(net_area(&detail), expected_net, 0.003);
    assert!(detail.outer.iter().all(|point| !point.exact));

    let (min_x, max_x, min_y, max_y) = bounds(&detail.outer);
    for (actual, expected) in [(min_x, 0.0), (max_x, 90.0), (min_y, 0.0), (max_y, 90.0)] {
        assert_close(actual, expected, 0.05);
    }

    let mut diameters: Vec<f64> = detail
        .children
        .iter()
        .map(|hole| {
            let (min_x, max_x, min_y, max_y) = bounds(hole);
            (max_x - min_x).max(max_y - min_y)
        })
        .collect();
    diameters.sort_by(f64::total_cmp);
    assert_eq!(diameters.len(), 5);
    for diameter in &diameters[..4] {
        assert_close(*diameter, 14.0, 0.05);
    }
    assert_close(diameters[4], 20.0, 0.05);
}

#[test]
fn ring_demo_is_one_slit_annulus_without_holes() {
    let detail = parse_dxf(include_str!("data/ring.dxf"), "ring.dxf").unwrap();

    // Both ARC records sweep CCW from 100° to 80°, i.e. 340°, not the short
    // complementary 20° sector. The two LINE records close the radial slit.
    assert!(detail.children.is_empty());
    let expected = 0.5 * (160.0_f64.powi(2) - 70.0_f64.powi(2)) * 340.0_f64.to_radians();
    assert_relative(polygon_area(&detail.outer).abs(), expected, 0.002);
    assert_eq!(detail.outer.iter().filter(|point| point.exact).count(), 4);

    // The 340° outer arc (100°..80° CCW, slit at the top 20°) includes 0°,
    // 180° and 270° but not 90°, so max_y is the slit edge, not the full radius.
    let (min_x, max_x, min_y, max_y) = bounds(&detail.outer);
    let slit_top = 160.0 + 160.0 * 80.0_f64.to_radians().sin();
    for (actual, expected) in [(min_x, 0.0), (max_x, 320.0), (min_y, 0.0), (max_y, slit_top)] {
        assert_close(actual, expected, 0.05);
    }
}

#[test]
fn demo_details_survive_export_parse_round_trip() {
    for name in ["arm.dxf", "hub.dxf", "ring.dxf"] {
        let source = load_detail(&fixture_path(name)).unwrap();
        let text = export_dxf(&source);
        let parsed = parse_dxf(&text, "round-trip.dxf").unwrap();

        assert_eq!(parsed.children.len(), source.children.len(), "{name}");
        assert_relative(net_area(&parsed), net_area(&source), 1e-6);
        assert!(parsed.outer.iter().all(|point| point.exact), "{name}");
    }
}
