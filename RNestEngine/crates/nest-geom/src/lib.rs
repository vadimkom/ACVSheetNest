//! 2D geometry primitives and utilities.
//!
//! Ported from `DeepNestLib.Core` (ACVSheetNest / DeepNestPort): `GeometryUtil.cs`,
//! `Simplify.cs`, `PolygonBounds.cs`, `SvgPoint.cs`.

pub mod simplify;

pub use simplify::{simplify_polygon, SimplifyOptions};

/// Floating-point tolerance ported from `GeometryUtil.Tolerance` (1e-9).
pub const TOLERANCE: f64 = 1e-9;

/// Converts degrees to radians.
pub fn to_radians(degrees: f64) -> f64 {
    degrees * std::f64::consts::PI / 180.0
}

/// A point of a detail polygon. Ported from `SvgPoint` (ACVSheetNest).
#[derive(Debug, Clone, Copy, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct SvgPoint {
    pub x: f64,
    pub y: f64,
    /// Ported from `SvgPoint.Exact`: true when the point is vertex of the original
    /// drawing rather than a tessellated chord of a curve.
    #[serde(default)]
    pub exact: bool,
}

impl SvgPoint {
    pub fn new(x: f64, y: f64) -> Self {
        Self { x, y, exact: false }
    }

    pub fn exact(x: f64, y: f64) -> Self {
        Self { x, y, exact: true }
    }
}

/// Rectangular bounding box. Ported from `PolygonBounds`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PolygonBounds {
    pub x_min: f64,
    pub y_min: f64,
    pub width: f64,
    pub height: f64,
}

impl PolygonBounds {
    pub fn new(x_min: f64, y_min: f64, width: f64, height: f64) -> Self {
        Self {
            x_min,
            y_min,
            width,
            height,
        }
    }

    pub fn x_max(&self) -> f64 {
        self.x_min + self.width
    }

    pub fn y_max(&self) -> f64 {
        self.y_min + self.height
    }

    /// Intersection area of two bounding boxes (0 when they do not overlap).
    pub fn overlap_area(&self, other: &PolygonBounds) -> f64 {
        let x_min = self.x_min.max(other.x_min);
        let y_min = self.y_min.max(other.y_min);
        let x_max = self.x_max().min(other.x_max());
        let y_max = self.y_max().min(other.y_max());
        if x_max > x_min && y_max > y_min {
            (x_max - x_min) * (y_max - y_min)
        } else {
            0.0
        }
    }
}

/// Returns true when the two points are within `distance` of each other.
/// Ported from `GeometryUtil.WithinDistance`.
pub fn within_distance(p1: SvgPoint, p2: SvgPoint, distance: f64) -> bool {
    let dx = p1.x - p2.x;
    let dy = p1.y - p2.y;
    (dx * dx) + (dy * dy) < distance * distance
}

/// Returns the rectangular bounding box of the given polygon.
/// Ported from `GeometryUtil.GetPolygonBounds`. Panics for polygons with fewer
/// than 3 points, exactly like the original throws `ArgumentException`.
pub fn get_polygon_bounds(polygon: &[SvgPoint]) -> PolygonBounds {
    assert!(polygon.len() >= 3, "GetPolygonBounds needs at least 3 points");
    let mut bounds = PolygonBounds::new(polygon[0].x, polygon[0].y, 0.0, 0.0);
    let mut x_max = polygon[0].x;
    let mut y_max = polygon[0].y;
    for p in &polygon[1..] {
        if p.x > x_max {
            x_max = p.x;
        } else if p.x < bounds.x_min {
            bounds.x_min = p.x;
        }
        if p.y > y_max {
            y_max = p.y;
        } else if p.y < bounds.y_min {
            bounds.y_min = p.y;
        }
    }
    bounds.width = x_max - bounds.x_min;
    bounds.height = y_max - bounds.y_min;
    bounds
}

/// Absolute-difference test with the global tolerance.
pub fn almost_equal(a: f64, b: f64) -> bool {
    almost_equal_tol(a, b, TOLERANCE)
}

/// Absolute-difference test with an explicit tolerance.
pub fn almost_equal_tol(a: f64, b: f64, tolerance: f64) -> bool {
    (a - b).abs() < tolerance
}

/// Returns true if `p` lies on the line segment defined by `a`-`b`, excluding its endpoints.
/// Ported from `GeometryUtil.OnSegment`.
pub fn on_segment(a: SvgPoint, b: SvgPoint, p: SvgPoint) -> bool {
    // vertical line
    if almost_equal(a.x, b.x) && almost_equal(p.x, a.x) {
        return !almost_equal(p.y, b.y)
            && !almost_equal(p.y, a.y)
            && p.y < b.y.max(a.y)
            && p.y > b.y.min(a.y);
    }

    // horizontal line
    if almost_equal(a.y, b.y) && almost_equal(p.y, a.y) {
        return !almost_equal(p.x, b.x)
            && !almost_equal(p.x, a.x)
            && p.x < b.x.max(a.x)
            && p.x > b.x.min(a.x);
    }

    // range check
    if (p.x < a.x && p.x < b.x)
        || (p.x > a.x && p.x > b.x)
        || (p.y < a.y && p.y < b.y)
        || (p.y > a.y && p.y > b.y)
    {
        return false;
    }

    // exclude end points
    if (almost_equal(p.x, a.x) && almost_equal(p.y, a.y))
        || (almost_equal(p.x, b.x) && almost_equal(p.y, b.y))
    {
        return false;
    }

    let cross = ((p.y - a.y) * (b.x - a.x)) - ((p.x - a.x) * (b.y - a.y));
    if cross.abs() > TOLERANCE {
        return false;
    }

    let dot = ((p.x - a.x) * (b.x - a.x)) + ((p.y - a.y) * (b.y - a.y));
    if dot < 0.0 || almost_equal(dot, 0.0) {
        return false;
    }

    let len2 = ((b.x - a.x) * (b.x - a.x)) + ((b.y - a.y) * (b.y - a.y));
    if dot > len2 || almost_equal(dot, len2) {
        return false;
    }

    true
}

/// Signed area of a closed polygon (shoelace). Ported from `GeometryUtil.PolygonArea`.
pub fn polygon_area(points: &[SvgPoint]) -> f64 {
    if points.len() < 3 {
        return 0.0;
    }
    let mut area = 0.0;
    let n = points.len();
    for i in 0..n {
        let j = (i + n - 1) % n;
        area += (points[j].x + points[i].x) * (points[j].y - points[i].y);
    }
    0.5 * area
}

/// Point-in-polygon test.
///
/// Ported from `GeometryUtil.PointInPolygon`:
/// - `Some(true)` — inside,
/// - `Some(false)` — outside,
/// - `None` — exactly on a vertex or a segment (the original returns `null`).
pub fn point_in_polygon(point: SvgPoint, polygon: &[SvgPoint], offset: (f64, f64)) -> Option<bool> {
    assert!(polygon.len() >= 3, "PointInPolygon needs at least 3 points");
    let (offset_x, offset_y) = offset;
    let mut inside = false;
    let n = polygon.len();
    for i in 0..n {
        let j = (i + n - 1) % n;
        let xi = polygon[i].x + offset_x;
        let yi = polygon[i].y + offset_y;
        let xj = polygon[j].x + offset_x;
        let yj = polygon[j].y + offset_y;

        if almost_equal(xi, point.x) && almost_equal(yi, point.y) {
            return None; // no result: on a vertex
        }

        if on_segment(SvgPoint::new(xi, yi), SvgPoint::new(xj, yj), point) {
            return None; // exactly on a segment
        }

        if almost_equal(xi, xj) && almost_equal(yi, yj) {
            continue; // ignore very small lines
        }

        let intersect =
            (yi > point.y) != (yj > point.y) && point.x < ((xj - xi) * (point.y - yi) / (yj - yi)) + xi;
        if intersect {
            inside = !inside;
        }
    }
    Some(inside)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn square(x: f64, y: f64, s: f64) -> Vec<SvgPoint> {
        vec![
            SvgPoint::new(x, y),
            SvgPoint::new(x + s, y),
            SvgPoint::new(x + s, y + s),
            SvgPoint::new(x, y + s),
        ]
    }

    #[test]
    fn area_of_unit_square_is_one() {
        assert!((polygon_area(&square(0.0, 0.0, 1.0)).abs() - 1.0).abs() < TOLERANCE);
    }

    #[test]
    fn point_in_square() {
        let s = square(-2.0, -2.0, 4.0);
        assert_eq!(point_in_polygon(SvgPoint::new(0.0, 0.0), &s, (0.0, 0.0)), Some(true));
        assert_eq!(
            point_in_polygon(SvgPoint::new(5.0, 0.0), &s, (0.0, 0.0)),
            Some(false)
        );
        // On the edge -> None
        assert_eq!(point_in_polygon(SvgPoint::new(0.0, 2.0), &s, (0.0, 0.0)), None);
    }

    #[test]
    fn bounds() {
        let b = get_polygon_bounds(&square(-1.0, 2.0, 3.0));
        assert_eq!(b.x_min, -1.0);
        assert_eq!(b.y_min, 2.0);
        assert_eq!(b.width, 3.0);
        assert_eq!(b.height, 3.0);
    }

    #[test]
    fn on_segment_cases() {
        let a = SvgPoint::new(0.0, 0.0);
        let b = SvgPoint::new(10.0, 0.0);
        assert!(on_segment(a, b, SvgPoint::new(5.0, 0.0)));
        assert!(!on_segment(a, b, SvgPoint::new(0.0, 0.0))); // endpoint
        assert!(!on_segment(a, b, SvgPoint::new(5.0, 1.0))); // off the line
    }
}
