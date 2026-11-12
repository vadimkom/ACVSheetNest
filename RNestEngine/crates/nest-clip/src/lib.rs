//! Polygon boolean operations wrapper over `clipper2`, matching ACVSheetNest's
//! use of Clipper (`DeepNestClipper`, `NfpHelper`): scale to integer space,
//! run union/difference/intersection, scale back to `SvgPoint`.
//!
//! The original uses `SvgNestConfig.ClipperScale` (default 10). We map that to
//! `clipper2::Deci` (×10) by default; callers may choose a different scaler
//! through the generic API, but the convenience functions use scale 10.

use clipper2::{difference, intersect, union, Deci, FillRule, Path, Paths, Point, PointScaler};
use nest_geom::SvgPoint;

/// Scale used to convert to and from Clipper integer space (`SvgNestConfig.ClipperScale`).
pub const DEFAULT_CLIPPER_SCALE: f64 = 10.0;

/// Boolean operation.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClipOp {
    Union,
    Difference,
    Intersection,
}

/// Convert a closed contour of `SvgPoint`s to a clipper `Path`.
pub fn path_from_points<P: PointScaler>(points: &[SvgPoint]) -> Path<P> {
    let pts: Vec<Point<P>> = points
        .iter()
        .map(|p| Point::<P>::new(p.x, p.y))
        .collect();
    Path::new(pts)
}

/// Convert a clipper `Path` back to `SvgPoint`s (exactness lost; coords scaled back).
pub fn points_from_path<P: PointScaler>(path: &Path<P>) -> Vec<SvgPoint> {
    path.iter()
        .map(|pt| SvgPoint::new(pt.x(), pt.y()))
        .collect()
}

/// Convert multiple contours to `Paths`.
pub fn paths_from_contours<P: PointScaler>(contours: &[Vec<SvgPoint>]) -> Paths<P> {
    Paths::new(contours.iter().map(|c| path_from_points::<P>(c)).collect())
}

/// Convert `Paths` back to contour vectors.
pub fn contours_from_paths<P: PointScaler>(paths: &Paths<P>) -> Vec<Vec<SvgPoint>> {
    paths.iter().map(|p| points_from_path::<P>(p)).collect()
}

/// Run a boolean op with the given fill rule. `subject` and `clip` are sets of
/// closed paths (outer + holes as separate paths; orientation/fill rule decides).
pub fn boolean_op<P: PointScaler>(
    op: ClipOp,
    subject: &[Vec<SvgPoint>],
    clip: &[Vec<SvgPoint>],
    fill_rule: FillRule,
) -> Result<Vec<Vec<SvgPoint>>, clipper2::ClipperError> {
    let subj = paths_from_contours::<P>(subject);
    let cl = paths_from_contours::<P>(clip);
    let out = match op {
        ClipOp::Union => union::<P>(subj, cl, fill_rule)?,
        ClipOp::Difference => difference::<P>(subj, cl, fill_rule)?,
        ClipOp::Intersection => intersect::<P>(subj, cl, fill_rule)?,
    };
    Ok(contours_from_paths::<P>(&out))
}

/// Convenience using `Deci` (scale 10) and `NonZero` fill rule — close to how
/// nesting uses Clipper for collision/containment.
pub fn boolean_op_deci(
    op: ClipOp,
    subject: &[Vec<SvgPoint>],
    clip: &[Vec<SvgPoint>],
) -> Result<Vec<Vec<SvgPoint>>, clipper2::ClipperError> {
    boolean_op::<Deci>(op, subject, clip, FillRule::NonZero)
}

/// True if two sets of closed contours intersect in area (non-empty intersection).
pub fn contours_intersect(a: &[Vec<SvgPoint>], b: &[Vec<SvgPoint>]) -> bool {
    match boolean_op_deci(ClipOp::Intersection, a, b) {
        Ok(res) => res.iter().any(|p| p.len() >= 3 && polygon_area_abs(p) > 1e-9),
        Err(_) => true, // conservative on failure
    }
}

/// Signed/abs area helper.
fn polygon_area_abs(points: &[SvgPoint]) -> f64 {
    nest_geom::polygon_area(points).abs()
}

/// Difference `a \\ b`.
pub fn difference_paths(a: &[Vec<SvgPoint>], b: &[Vec<SvgPoint>]) -> Vec<Vec<SvgPoint>> {
    boolean_op_deci(ClipOp::Difference, a, b).unwrap_or_default()
}

/// Intersection `a ∩ b`.
pub fn intersection_paths(a: &[Vec<SvgPoint>], b: &[Vec<SvgPoint>]) -> Vec<Vec<SvgPoint>> {
    boolean_op_deci(ClipOp::Intersection, a, b).unwrap_or_default()
}

/// Union of two path sets.
pub fn union_paths(a: &[Vec<SvgPoint>], b: &[Vec<SvgPoint>]) -> Vec<Vec<SvgPoint>> {
    boolean_op_deci(ClipOp::Union, a, b).unwrap_or_default()
}

/// Total absolute area of a set of contours (treating each as a region; caller
/// should pass oriented outer+holes appropriately if mixing).
pub fn paths_area(contours: &[Vec<SvgPoint>]) -> f64 {
    contours.iter().map(|c| polygon_area_abs(c)).sum()
}

/// Inflate/deflate closed paths by `delta` (positive = outward). Uses square
/// joins/polygon end type for nesting spacing; `miter_limit` 2.0.
pub fn inflate_paths(contours: &[Vec<SvgPoint>], delta: f64) -> Vec<Vec<SvgPoint>> {
    let p = paths_from_contours::<Deci>(contours);
    let out = p.inflate(delta, clipper2::JoinType::Square, clipper2::EndType::Polygon, 2.0);
    contours_from_paths::<Deci>(&out)
}

/// Inflate/deflate with round joins: a uniform Euclidean halo, used for exact
/// offsets of round shapes (the engine's spacing halo uses square joins).
pub fn inflate_paths_round(contours: &[Vec<SvgPoint>], delta: f64) -> Vec<Vec<SvgPoint>> {
    let p = paths_from_contours::<Deci>(contours);
    let out = p.inflate(delta, clipper2::JoinType::Round, clipper2::EndType::Polygon, 2.0);
    contours_from_paths::<Deci>(&out)
}

pub fn inflate_paths_round_at_scale(contours: &[Vec<SvgPoint>], delta: f64, factor: f64) -> Vec<Vec<SvgPoint>> {
    if (factor - 1.0).abs() < 1e-12 {
        return inflate_paths_round(contours, delta);
    }
    let scaled = scale_contours(contours, factor);
    let out = inflate_paths_round(&scaled, delta * factor);
    unscale_contours(&out, factor)
}

/// ---- Configurable effective scale ---------------------------------------
///
/// The convenience functions above use `Deci` (×10). The engine exposes
/// `Config.ClipperScale`; a different value is applied by pre-scaling the
/// coordinates by `factor = clipper_scale / DEFAULT_CLIPPER_SCALE`, running the
/// `Deci` operations, and scaling results back. At `factor == 1.0` (the
/// default) the plain functions are called directly, so behaviour and cost are
/// unchanged.

#[inline]
fn scale_contours(contours: &[Vec<SvgPoint>], k: f64) -> Vec<Vec<SvgPoint>> {
    contours
        .iter()
        .map(|c| c.iter().map(|p| SvgPoint::new(p.x * k, p.y * k)).collect())
        .collect()
}

#[inline]
fn unscale_contours(contours: &[Vec<SvgPoint>], k: f64) -> Vec<Vec<SvgPoint>> {
    scale_contours(contours, 1.0 / k)
}

#[inline]
fn maybe_scale(contours: &[Vec<SvgPoint>], k: f64) -> Vec<Vec<SvgPoint>> {
    if (k - 1.0).abs() < 1e-12 { contours.to_vec() } else { scale_contours(contours, k) }
}

/// `contours_intersect` at a caller-chosen effective scale.
pub fn contours_intersect_at_scale(a: &[Vec<SvgPoint>], b: &[Vec<SvgPoint>], factor: f64) -> bool {
    if (factor - 1.0).abs() < 1e-12 {
        return contours_intersect(a, b);
    }
    let (a, b) = (scale_contours(a, factor), scale_contours(b, factor));
    match boolean_op_deci(ClipOp::Intersection, &a, &b) {
        Ok(res) => res.iter().any(|p| p.len() >= 3 && polygon_area_abs(p) > 1e-9 * factor * factor),
        Err(_) => true, // conservative on failure
    }
}

/// `intersection_paths` at a caller-chosen effective scale (output unscaled back).
pub fn intersection_paths_at_scale(a: &[Vec<SvgPoint>], b: &[Vec<SvgPoint>], factor: f64) -> Vec<Vec<SvgPoint>> {
    if (factor - 1.0).abs() < 1e-12 {
        return intersection_paths(a, b);
    }
    let (a, b) = (scale_contours(a, factor), scale_contours(b, factor));
    unscale_contours(&boolean_op_deci(ClipOp::Intersection, &a, &b).unwrap_or_default(), factor)
}

/// `difference_paths` at a caller-chosen effective scale (output unscaled back).
pub fn difference_paths_at_scale(a: &[Vec<SvgPoint>], b: &[Vec<SvgPoint>], factor: f64) -> Vec<Vec<SvgPoint>> {
    if (factor - 1.0).abs() < 1e-12 {
        return difference_paths(a, b);
    }
    let (a, b) = (scale_contours(a, factor), scale_contours(b, factor));
    unscale_contours(&boolean_op_deci(ClipOp::Difference, &a, &b).unwrap_or_default(), factor)
}

/// Area of contours at a caller-chosen effective scale (result in unscaled units).
pub fn paths_area_at_scale(contours: &[Vec<SvgPoint>], factor: f64) -> f64 {
    if (factor - 1.0).abs() < 1e-12 {
        return paths_area(contours);
    }
    paths_area(&scale_contours(contours, factor)) / (factor * factor)
}

/// `inflate_paths` at a caller-chosen effective scale (`delta` in unscaled units).
pub fn inflate_paths_at_scale(contours: &[Vec<SvgPoint>], delta: f64, factor: f64) -> Vec<Vec<SvgPoint>> {
    if (factor - 1.0).abs() < 1e-12 {
        return inflate_paths(contours, delta);
    }
    let scaled = scale_contours(contours, factor);
    let out = inflate_paths(&scaled, delta * factor);
    unscale_contours(&out, factor)
}

/// Convenience: the pre-scale factor for a configured clipper scale.
pub fn scale_factor(clipper_scale: f64) -> f64 {
    let k = clipper_scale / DEFAULT_CLIPPER_SCALE;
    if k.is_finite() && k > 0.0 { k } else { 1.0 }
}

/// Outer no-fit polygon of B around A as a Minkowski sum, A ⊕ (−B): the
/// positions of B's origin at which B touches or overlaps A. Built as the union
/// of one parallelogram per pair of edges plus A itself, at `units_per_mm`
/// clipper units (1e4 keeps contact positions within the placement noise).
/// Only the outer boundaries are returned: a position enclosed by the NFP (B
/// inside a closed pocket of A) is given up, as it was by the orbiting NFP.
///
/// Replaces the orbiting NFP for part against part: the same region, without
/// the orbit's cubic cost on outlines of a hundred points.
pub fn minkowski_nfp(a: &[SvgPoint], b: &[SvgPoint], units_per_mm: f64) -> Vec<Vec<SvgPoint>> {
    if a.len() < 3 || b.len() < 3 {
        return Vec::new();
    }
    let k = units_per_mm / DEFAULT_CLIPPER_SCALE;
    let signed_area = |p: &[SvgPoint]| -> f64 {
        (0..p.len()).map(|i| { let (u, v) = (p[i], p[(i + 1) % p.len()]); u.x * v.y - v.x * u.y }).sum::<f64>() / 2.0
    };
    let ccw = |mut p: Vec<SvgPoint>| -> Option<Vec<SvgPoint>> {
        let area = signed_area(&p);
        if area.abs() < 1e-12 {
            return None;
        }
        if area < 0.0 {
            p.reverse();
        }
        Some(p)
    };
    let at = |p: SvgPoint, q: SvgPoint| SvgPoint::new((p.x - q.x) * k, (p.y - q.y) * k);
    let mut pieces: Vec<Vec<SvgPoint>> = Vec::with_capacity(a.len() * b.len() + 1);
    pieces.extend(ccw(a.iter().map(|&p| at(p, b[0])).collect()));
    for i in 0..a.len() {
        let (a1, a2) = (a[i], a[(i + 1) % a.len()]);
        for j in 0..b.len() {
            let (b1, b2) = (b[j], b[(j + 1) % b.len()]);
            pieces.extend(ccw(vec![at(a1, b1), at(a2, b1), at(a2, b2), at(a1, b2)]));
        }
    }
    let merged = boolean_op_deci(ClipOp::Union, &pieces, &[]).unwrap_or_default();
    merged
        .into_iter()
        .filter(|p| p.len() >= 3 && signed_area(p) > 0.0)
        .map(|p| p.into_iter().map(|q| SvgPoint::new(q.x / k, q.y / k)).collect())
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rect(x: f64, y: f64, w: f64, h: f64) -> Vec<SvgPoint> {
        vec![
            SvgPoint::new(x, y),
            SvgPoint::new(x + w, y),
            SvgPoint::new(x + w, y + h),
            SvgPoint::new(x, y + h),
        ]
    }

    #[test]
    fn scale_wrappers_match_default_scale() {
        let a = vec![rect(0.0, 0.0, 10.0, 10.0)];
        let b = vec![rect(5.0, 5.0, 10.0, 10.0)];
        let f = scale_factor(100.0);
        assert!((f - 10.0).abs() < 1e-12);
        let area_default = paths_area(&intersection_paths(&a, &b));
        let area_scaled = paths_area_at_scale(&intersection_paths_at_scale(&a, &b, f), f);
        assert!((area_default - area_scaled).abs() < 1e-6, "{area_default} vs {area_scaled}");
        let diff_default = paths_area(&difference_paths(&a, &b));
        let diff_scaled = paths_area_at_scale(&difference_paths_at_scale(&a, &b, f), f);
        assert!((diff_default - diff_scaled).abs() < 1e-6, "{diff_default} vs {diff_scaled}");
        assert!(contours_intersect_at_scale(&a, &b, f));
        let infl_default = inflate_paths(&a, 2.0);
        let infl_scaled = inflate_paths_at_scale(&a, 2.0, f);
        let ad = paths_area(&infl_default);
        let as_ = paths_area_at_scale(&infl_scaled, f);
        assert!((ad - as_).abs() < 0.5, "inflate {ad} vs {as_}");
    }

    #[test]
    fn intersection_of_overlapping_rects() {
        let a = vec![rect(0.0, 0.0, 10.0, 10.0)];
        let b = vec![rect(5.0, 5.0, 10.0, 10.0)];
        let inter = intersection_paths(&a, &b);
        let area = paths_area(&inter);
        assert!((area - 25.0).abs() < 0.5, "area {area}");
        assert!(contours_intersect(&a, &b));
    }

    #[test]
    fn disjoint_rects_do_not_intersect() {
        let a = vec![rect(0.0, 0.0, 10.0, 10.0)];
        let b = vec![rect(20.0, 20.0, 5.0, 5.0)];
        assert!(!contours_intersect(&a, &b));
        assert!(intersection_paths(&a, &b).is_empty() || paths_area(&intersection_paths(&a, &b)) < 1e-6);
    }

    #[test]
    fn difference_removes_overlap() {
        let a = vec![rect(0.0, 0.0, 10.0, 10.0)];
        let b = vec![rect(5.0, 0.0, 10.0, 10.0)];
        let diff = difference_paths(&a, &b);
        let area = paths_area(&diff);
        assert!((area - 50.0).abs() < 0.5, "area {area}");
    }

    #[test]
    fn union_merges_touching() {
        let a = vec![rect(0.0, 0.0, 10.0, 10.0)];
        let b = vec![rect(10.0, 0.0, 10.0, 10.0)];
        let u = union_paths(&a, &b);
        let area = paths_area(&u);
        assert!((area - 200.0).abs() < 1.0, "area {area}");
    }

    #[test]
    fn scale_roundtrip_deci() {
        let pts = rect(1.23, 4.56, 7.0, 3.0);
        let path = path_from_points::<Deci>(&pts);
        let back = points_from_path::<Deci>(&path);
        for (a,b) in pts.iter().zip(back.iter()) {
            assert!((a.x-b.x).abs() < 0.06 && (a.y-b.y).abs() < 0.06, "{:?} vs {:?}", a,b);
        }
    }
}


#[cfg(test)]
mod minkowski_tests {
    use super::*;

    fn bounds(p: &[SvgPoint]) -> (f64, f64, f64, f64) {
        p.iter().fold((f64::MAX, f64::MAX, f64::MIN, f64::MIN), |b, q| (b.0.min(q.x), b.1.min(q.y), b.2.max(q.x), b.3.max(q.y)))
    }

    #[test]
    fn nfp_of_square_and_triangle_is_by_the_origin() {
        let a = vec![SvgPoint::new(0.0, 0.0), SvgPoint::new(10.0, 0.0), SvgPoint::new(10.0, 10.0), SvgPoint::new(0.0, 10.0)];
        // A triangle whose first vertex is not at the origin of its box.
        let b = vec![SvgPoint::new(2.0, 0.0), SvgPoint::new(4.0, 3.0), SvgPoint::new(0.0, 3.0)];
        let nfp = minkowski_nfp(&a, &b, 1e4);
        assert_eq!(nfp.len(), 1);
        let (x0, y0, x1, y1) = bounds(&nfp[0]);
        // From A.min - B.max to A.max - B.min.
        for (got, want) in [(x0, -4.0), (y0, -3.0), (x1, 10.0), (y1, 10.0)] {
            assert!((got - want).abs() < 1e-3, "{got} != {want}");
        }
        // 10 x 13 box less the two corners the slanted sides cut off (2 x 3 / 2 each).
        assert!((paths_area(&nfp) - (14.0 * 13.0 - 6.0)).abs() < 1e-2, "area {}", paths_area(&nfp));
    }

    #[test]
    fn nfp_of_a_concave_part_keeps_the_notch() {
        // An L: a 2 x 2 square fits into its notch, so the NFP is concave too.
        let l = vec![
            SvgPoint::new(0.0, 0.0), SvgPoint::new(10.0, 0.0), SvgPoint::new(10.0, 4.0),
            SvgPoint::new(4.0, 4.0), SvgPoint::new(4.0, 10.0), SvgPoint::new(0.0, 10.0),
        ];
        let sq = vec![SvgPoint::new(0.0, 0.0), SvgPoint::new(2.0, 0.0), SvgPoint::new(2.0, 2.0), SvgPoint::new(0.0, 2.0)];
        let nfp = minkowski_nfp(&l, &sq, 1e4);
        assert_eq!(nfp.len(), 1);
        // 12 x 12 less the 6 x 6 notch.
        assert!((paths_area(&nfp) - (144.0 - 36.0)).abs() < 1e-2, "area {}", paths_area(&nfp));
    }
}
