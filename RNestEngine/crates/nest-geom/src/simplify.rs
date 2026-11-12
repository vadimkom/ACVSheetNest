//! Polygon simplification: radial distance + Ramer-Douglas-Peucker, combined as in
//! SVGnest. Ported from `DeepNestLib.Core/Simplify.cs`.

use crate::{almost_equal, SvgPoint};

/// Square distance between two points (`Simplify.GetSqDist`).
pub fn get_sq_dist(p1: SvgPoint, p2: SvgPoint) -> f64 {
    let dx = p1.x - p2.x;
    let dy = p1.y - p2.y;
    (dx * dx) + (dy * dy)
}

/// Square distance from a point to a segment (`Simplify.GetSqSegDist`).
pub fn get_sq_seg_dist(p: SvgPoint, p1: SvgPoint, p2: SvgPoint) -> f64 {
    let mut x = p1.x;
    let mut y = p1.y;
    let dx = p2.x - x;
    let dy = p2.y - y;

    if dx != 0.0 || dy != 0.0 {
        let t = (((p.x - x) * dx) + ((p.y - y) * dy)) / ((dx * dx) + (dy * dy));
        if t > 1.0 {
            x = p2.x;
            y = p2.y;
        } else if t > 0.0 {
            x += dx * t;
            y += dy * t;
        }
    }

    let dx = p.x - x;
    let dy = p.y - y;
    (dx * dx) + (dy * dy)
}

/// Basic distance-based simplification (`SimplifyRadialDist`). Marked points, i.e.
/// drawing vertices, are always kept.
fn simplify_radial_dist(points: &[SvgPoint], sq_tolerance: f64) -> Vec<SvgPoint> {
    let mut new_points = vec![points[0]];
    let mut prev_point = points[0];
    let mut last = points[0];
    let len = points.len();
    for i in 1..len {
        let point = points[i];
        if point.exact || get_sq_dist(point, prev_point) > sq_tolerance {
            new_points.push(point);
            prev_point = point;
        }
        last = point;
    }
    if !almost_equal(prev_point.x, last.x) || !almost_equal(prev_point.y, last.y) {
        new_points.push(last);
    }
    new_points
}

/// Recursive Douglas-Peucker step (`SimplifyDPStep`).
fn simplify_dp_step(
    points: &[SvgPoint],
    first: usize,
    last: usize,
    sq_tolerance: f64,
    simplified: &mut Vec<SvgPoint>,
) {
    let mut max_sq_dist = sq_tolerance;
    let mut index = first; // index stays valid: `first` is always pushed by callers below
    for i in (first + 1)..last {
        let sq_dist = get_sq_seg_dist(points[i], points[first], points[last]);
        if sq_dist > max_sq_dist {
            index = i;
            max_sq_dist = sq_dist;
        }
    }

    if max_sq_dist > sq_tolerance {
        if index.saturating_sub(first) > 1 {
            simplify_dp_step(points, first, index, sq_tolerance, simplified);
        }
        simplified.push(points[index]);
        if last.saturating_sub(index) > 1 {
            simplify_dp_step(points, index, last, sq_tolerance, simplified);
        }
    }
}

/// Ramer-Douglas-Peucker simplification (`SimplifyDouglasPeucker`).
pub fn simplify_douglas_peucker(points: &[SvgPoint], sq_tolerance: f64) -> Vec<SvgPoint> {
    let last = points.len() - 1;
    let mut simplified = vec![points[0]];
    simplify_dp_step(points, 0, last, sq_tolerance, &mut simplified);
    simplified.push(points[last]);
    simplified
}

/// Options for [`simplify_polygon`].
#[derive(Debug, Clone, Copy)]
pub struct SimplifyOptions {
    /// Tolerance in units (the original passes `1.0` for most uses).
    pub tolerance: f64,
    pub do_simplify_radial_dist: bool,
    pub do_simplify_douglas_peucker: bool,
}

impl Default for SimplifyOptions {
    fn default() -> Self {
        Self {
            tolerance: 1.0,
            do_simplify_radial_dist: true,
            do_simplify_douglas_peucker: true,
        }
    }
}

/// Both algorithms combined for performance. Port of `Simplify.SimplifyPolygon`.
pub fn simplify_polygon(points: &[SvgPoint], options: SimplifyOptions) -> Vec<SvgPoint> {
    if points.len() <= 2 {
        return points.to_vec();
    }

    let sq_tolerance = options.tolerance * options.tolerance;
    let mut result = points.to_vec();

    if options.do_simplify_radial_dist {
        result = simplify_radial_dist(&result, sq_tolerance);
    }
    if options.do_simplify_douglas_peucker {
        result = simplify_douglas_peucker(&result, sq_tolerance);
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{polygon_area, SvgPoint};

    #[test]
    fn collinear_run_collapses() {
        let pts: Vec<SvgPoint> = (0..50)
            .map(|i| SvgPoint::new(i as f64, (i as f64) * 2.0))
            .chain(std::iter::once(SvgPoint::new(50.0, 0.0)))
            .chain(std::iter::once(SvgPoint::new(0.0, 0.0)))
            .collect();
        let simplified = simplify_polygon(&pts, SimplifyOptions::default());
        assert!(simplified.len() <= 4);
        // Area must be preserved by simplification (closed triangle).
        let a_orig = polygon_area(&pts).abs();
        let a_new = polygon_area(&simplified).abs();
        assert!((a_orig - a_new).abs() < 1e-6);
    }

    #[test]
    fn curved_arc_keeps_shape() {
        let n = 72;
        let pts: Vec<SvgPoint> = (0..n)
            .map(|i| {
                let t = i as f64 * 2.0 * std::f64::consts::PI / n as f64;
                SvgPoint::new(50.0 + 20.0 * t.cos(), 50.0 + 20.0 * t.sin())
            })
            .collect();
        let simplified = simplify_polygon(&pts, SimplifyOptions { tolerance: 0.5, ..Default::default() });
        assert!(simplified.len() < n);
        let a_orig = polygon_area(&pts).abs();
        let a_new = polygon_area(&simplified).abs();
        assert!((a_orig - a_new) / a_orig < 0.05); // within 5%
    }
}
