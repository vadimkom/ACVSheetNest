//! No-fit polygon (NFP) computation.
//!
//! Ported from `DeepNestLib.Core/Geometry/NoFitPolygonUtil.cs` (the SVGnest orbiting
//! algorithm) and `NoFitPolygon.cs` (the polygon container).
//!
//! # Rules of the port
//! - Public types match the C# shapes closely enough for a faithful algorithm port.
//! - `Marked` flags on the C# `SvgPoint` are *cube-local working state* of the orbiting
//!   algorithm; the Rust port keeps them on the internal `PolyPoint` and does not let
//!   them leak into `nest-geom::SvgPoint`.

use nest_geom::{get_polygon_bounds, polygon_area, point_in_polygon, SvgPoint};

/// Caption equivalent of `NoFitPolygon` (the polygon container used by the engine).
#[derive(Debug, Clone, Default, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct Nfp {
    pub points: Vec<SvgPoint>,
    /// Children are the holes (e.g. a washer's bore).
    #[serde(default)]
    pub children: Vec<Nfp>,
    /// Translation applied when the NFP is measured (`OffsetX/OffsetY`).
    #[serde(default)]
    pub offset: Offset,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct Offset {
    #[serde(default)]
    pub x: Option<f64>,
    #[serde(default)]
    pub y: Option<f64>,
}

impl Nfp {
    pub fn new(points: Vec<SvgPoint>) -> Self {
        Self {
            points,
            children: Vec::new(),
            offset: Offset::default(),
        }
    }

    pub fn len(&self) -> usize {
        self.points.len()
    }

    pub fn is_empty(&self) -> bool {
        self.points.is_empty()
    }

    pub fn area(&self) -> f64 {
        polygon_area(&self.points).abs()
    }

    /// Port of `INfp.MinX/MaxX/MinY/MaxY` (always computed fresh; no cache in this port).
    pub fn bounds(&self) -> Option<nest_geom::PolygonBounds> {
        if self.points.len() < 3 {
            return None;
        }
        Some(get_polygon_bounds(&self.points))
    }

    /// Port of `INfp.Contains`: `Some(true)` inside, `Some(false)` out, `None` on the boundary.
    pub fn contains(&self, point: SvgPoint) -> Option<bool> {
        point_in_polygon(point, &self.points, (self.offset.x.unwrap_or(0.0), self.offset.y.unwrap_or(0.0)))
    }
}

/// The point type used by the orbiting algorithm: an `SvgPoint` plus the `Marked`
/// working flag of the C# port (used like `svg-nest`'s `marked = 1`).
#[derive(Debug, Clone, Copy)]
struct PolyPoint {
    point: SvgPoint,
    marked: bool,
}

impl PolyPoint {
    fn new(x: f64, y: f64) -> Self {
        Self {
            point: SvgPoint::new(x, y),
            marked: false,
        }
    }

    fn from_svg(p: SvgPoint) -> Self {
        Self { point: p, marked: false }
    }
}

struct Poly {
    points: Vec<PolyPoint>,
    offset: (f64, f64), // OffsetX/OffsetY while orbiting
}

impl Poly {
    fn x(&self, i: usize) -> f64 {
        self.points[i].point.x
    }
    fn y(&self, i: usize) -> f64 {
        self.points[i].point.y
    }
    fn len(&self) -> usize {
        self.points.len()
    }
    /// Translated X: `a[i].X + offsetx`, as the C# reads all over the algorithm.
    fn ox(&self, i: usize) -> f64 {
        self.points[i].point.x + self.offset.0
    }
    fn oy(&self, i: usize) -> f64 {
        self.points[i].point.y + self.offset.1
    }
}

mod orbits;

pub use orbits::{no_fit_polygon, no_fit_polygon_rectangle};

#[cfg(test)]
mod tests {
    use super::*;
    use nest_geom::SvgPoint;

    fn square(x: f64, y: f64, s: f64) -> Vec<SvgPoint> {
        vec![
            SvgPoint::new(x, y),
            SvgPoint::new(x + s, y),
            SvgPoint::new(x + s, y + s),
            SvgPoint::new(x, y + s),
        ]
    }

    #[test]
    fn rect_nfp_square_in_square() {
        let a = Nfp::new(square(0.0, 0.0, 10.0));
        let b = Nfp::new(square(0.0, 0.0, 3.0));
        let nfp = no_fit_polygon_rectangle(&a, &b).expect("fits");
        // The rectangular NFP is a 10-3=7 wide square.
        let bnd = nfp.bounds().expect("bounds");
        assert!((bnd.width - 7.0).abs() < 1e-9);
        assert!((bnd.height - 7.0).abs() < 1e-9);
    }

    #[test]
    fn rect_nfp_too_big_returns_none() {
        let a = Nfp::new(square(0.0, 0.0, 3.0));
        let b = Nfp::new(square(0.0, 0.0, 10.0));
        assert!(no_fit_polygon_rectangle(&a, &b).is_none());
    }

    #[test]
    fn outer_nfp_of_two_squares() {
        let a = Nfp::new(square(0.0, 0.0, 10.0));
        let b = Nfp::new(square(0.0, 0.0, 4.0));
        let nfps = no_fit_polygon(&a, &b, false, false).expect("outer NFP");
        assert_eq!(nfps.len(), 1, "convex pair gives a single NFP");
        let nfp = &nfps[0];
        assert!((nfp.area() - 196.0).abs() < 1e-6, "NFP area of two squares is 196");
        assert_eq!(nfp.len(), 4);
    }
}
