//! Geometric helpers for the nesting engine: rotation, mirroring, bounds,
//! translation and exact overlap/containment tests used by placement.

use nest_geom::{get_polygon_bounds, point_in_polygon, polygon_area, SvgPoint};
use nest_io::RawDetail;

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Bounds {
    pub min_x: f64,
    pub min_y: f64,
    pub max_x: f64,
    pub max_y: f64,
}

impl Bounds {
    pub fn width(&self) -> f64 { self.max_x - self.min_x }
    pub fn height(&self) -> f64 { self.max_y - self.min_y }
    pub fn area(&self) -> f64 { self.width() * self.height() }
}

pub fn bounds_of(points: &[SvgPoint]) -> Option<Bounds> {
    if points.len() < 3 { return None; }
    let b = get_polygon_bounds(points);
    Some(Bounds { min_x: b.x_min, min_y: b.y_min, max_x: b.x_min + b.width, max_y: b.y_min + b.height })
}

pub fn rotate_point(p: SvgPoint, angle_deg: f64) -> SvgPoint {
    let a = angle_deg.to_radians();
    let (s, c) = a.sin_cos();
    SvgPoint { x: p.x * c - p.y * s, y: p.x * s + p.y * c, exact: p.exact }
}

pub fn rotate_points(points: &[SvgPoint], angle_deg: f64) -> Vec<SvgPoint> {
    if angle_deg.abs() < 1e-12 { return points.to_vec(); }
    points.iter().map(|&p| rotate_point(p, angle_deg)).collect()
}

pub fn mirror_x_points(points: &[SvgPoint]) -> Vec<SvgPoint> {
    points.iter().map(|&p| SvgPoint { x: -p.x, y: p.y, exact: p.exact }).collect()
}

pub fn translate_points(points: &[SvgPoint], dx: f64, dy: f64) -> Vec<SvgPoint> {
    points.iter().map(|&p| SvgPoint { x: p.x + dx, y: p.y + dy, exact: p.exact }).collect()
}

/// Normalize a contour so its axis-aligned bounding box starts at (0,0).
pub fn normalize_to_origin(points: &[SvgPoint]) -> Vec<SvgPoint> {
    let Some(b) = bounds_of(points) else { return points.to_vec(); };
    translate_points(points, -b.min_x, -b.min_y)
}

pub fn normalize_detail(detail: &RawDetail, angle_deg: f64, mirror: bool) -> RawDetail {
    let mut outer = if mirror { mirror_x_points(&detail.outer) } else { detail.outer.clone() };
    outer = rotate_points(&outer, angle_deg);
    let b = bounds_of(&outer);
    let (ox, oy) = b.map(|b| (b.min_x, b.min_y)).unwrap_or((0.0, 0.0));
    outer = translate_points(&outer, -ox, -oy);
    let children = detail.children.iter().map(|c| {
        let mut cc = if mirror { mirror_x_points(c) } else { c.clone() };
        cc = rotate_points(&cc, angle_deg);
        translate_points(&cc, -ox, -oy)
    }).collect();
    // Bend lines follow the outline exactly: same mirror, rotation and translation.
    let bends = detail.bends.iter().map(|l| {
        let mut bl = if mirror { mirror_x_points(l) } else { l.clone() };
        bl = rotate_points(&bl, angle_deg);
        translate_points(&bl, -ox, -oy)
    }).collect();
    RawDetail { name: detail.name.clone(), outer, children, bends }
}

/// Point strictly inside (Some(true)) or on boundary (None).
pub fn point_inside(poly: &[SvgPoint], p: SvgPoint) -> Option<bool> {
    if poly.len() < 3 { return Some(false); }
    point_in_polygon(p, poly, (0.0, 0.0))
}

fn segments_intersect(a1: SvgPoint, a2: SvgPoint, b1: SvgPoint, b2: SvgPoint) -> bool {
    // Proper intersection or endpoint-on-segment (boundary contact treated as overlap for nesting safety)
    fn orient(a: SvgPoint, b: SvgPoint, c: SvgPoint) -> f64 { (b.x - a.x)*(c.y - a.y) - (b.y - a.y)*(c.x - a.x) }
    fn on_seg(a: SvgPoint, b: SvgPoint, c: SvgPoint) -> bool {
        b.x >= a.x.min(c.x)-1e-9 && b.x <= a.x.max(c.x)+1e-9 && b.y >= a.y.min(c.y)-1e-9 && b.y <= a.y.max(c.y)+1e-9
    }
    let o1 = orient(a1,a2,b1); let o2 = orient(a1,a2,b2); let o3 = orient(b1,b2,a1); let o4 = orient(b1,b2,a2);
    let s = |v: f64| if v.abs() < 1e-9 { 0.0 } else { v.signum() };
    if s(o1)!=0.0 && s(o2)!=0.0 && s(o3)!=0.0 && s(o4)!=0.0 {
        return s(o1)!=s(o2) && s(o3)!=s(o4);
    }
    if s(o1)==0.0 && on_seg(a1,b1,a2) { return true; }
    if s(o2)==0.0 && on_seg(a1,b2,a2) { return true; }
    if s(o3)==0.0 && on_seg(b1,a1,b2) { return true; }
    if s(o4)==0.0 && on_seg(b1,a2,b2) { return true; }
    false
}

/// True if two simple polygons overlap in area or touch along boundary (treated as collision for spacing=0 safety
/// except we allow exact boundary? nesting usually requires spacing; we treat boundary as non-colliding by epsilon).
pub fn polygons_overlap(a: &[SvgPoint], b: &[SvgPoint]) -> bool {
    if a.len() < 3 || b.len() < 3 { return false; }
    // Edge crossings. Two edges can cross only inside both polygons' boxes, so
    // only the edges that reach the other polygon's box are paired up: between
    // neighbours that is a handful out of hundreds.
    if let (Some(ab), Some(bb)) = (bounds_of(a), bounds_of(b)) {
        const EPS: f64 = 1e-6;
        let reaching = |poly: &[SvgPoint], other: &Bounds| -> Vec<(SvgPoint, SvgPoint)> {
            (0..poly.len())
                .map(|i| (poly[i], poly[(i + 1) % poly.len()]))
                .filter(|(p, q)| {
                    p.x.max(q.x) >= other.min_x - EPS && p.x.min(q.x) <= other.max_x + EPS
                        && p.y.max(q.y) >= other.min_y - EPS && p.y.min(q.y) <= other.max_y + EPS
                })
                .collect()
        };
        let (ea, eb) = (reaching(a, &bb), reaching(b, &ab));
        fn orient(p: SvgPoint, q: SvgPoint, r: SvgPoint) -> f64 { (q.x-p.x)*(r.y-p.y)-(q.y-p.y)*(r.x-p.x) }
        let s = |v: f64| if v.abs() < 1e-9 { 0.0 } else { v.signum() };
        for &(a1, a2) in &ea {
            let (ax0, ax1, ay0, ay1) = (a1.x.min(a2.x) - EPS, a1.x.max(a2.x) + EPS, a1.y.min(a2.y) - EPS, a1.y.max(a2.y) + EPS);
            for &(b1, b2) in &eb {
                if b1.x.max(b2.x) < ax0 || b1.x.min(b2.x) > ax1 || b1.y.max(b2.y) < ay0 || b1.y.min(b2.y) > ay1 { continue; }
                // proper crossing only (interior), not mere touching
                let o1=orient(a1,a2,b1); let o2=orient(a1,a2,b2); let o3=orient(b1,b2,a1); let o4=orient(b1,b2,a2);
                if s(o1)!=0.0 && s(o2)!=0.0 && s(o3)!=0.0 && s(o4)!=0.0 && s(o1)!=s(o2) && s(o3)!=s(o4) {
                    return true;
                }
            }
        }
    }
    // containment (vertex strictly inside)
    if matches!(point_inside(a, b[0]), Some(true)) { return true; }
    if matches!(point_inside(b, a[0]), Some(true)) { return true; }
    false
}

/// Part (outer + holes) placed at (dx,dy): material overlaps if outers overlap
/// AND the overlap is not entirely inside a hole of either part.
pub fn details_overlap(a_outer: &[SvgPoint], a_children: &[Vec<SvgPoint>], a_at: (f64,f64),
                       b_outer: &[SvgPoint], b_children: &[Vec<SvgPoint>], b_at: (f64,f64)) -> bool {
    let ao = translate_points(a_outer, a_at.0, a_at.1);
    let bo = translate_points(b_outer, b_at.0, b_at.1);
    if !polygons_overlap(&ao, &bo) { return false; }
    // if B's material is entirely inside a hole of A, no collision
    let b_in_hole_of_a = a_children.iter().any(|h| {
        let hh = translate_points(h, a_at.0, a_at.1);
        bo.iter().all(|&p| matches!(point_inside(&hh,p), Some(true) | None))
    });
    if b_in_hole_of_a { return false; }
    let a_in_hole_of_b = b_children.iter().any(|h| {
        let hh = translate_points(h, b_at.0, b_at.1);
        ao.iter().all(|&p| matches!(point_inside(&hh,p), Some(true) | None))
    });
    if a_in_hole_of_b { return false; }
    // conservative: outers overlap and neither fully inside other's hole
    true
}

/// Check part outer fully inside sheet outer and outside sheet holes (simple rectangular sheet common case fast path).
pub fn inside_sheet(part_outer: &[SvgPoint], part_at: (f64,f64), sheet_w: f64, sheet_h: f64) -> bool {
    let p = translate_points(part_outer, part_at.0, part_at.1);
    p.iter().all(|v| v.x >= -1e-9 && v.y >= -1e-9 && v.x <= sheet_w + 1e-9 && v.y <= sheet_h + 1e-9)
}

/// The finest overlap that means anything: below this two placements are not
/// distinguishable. Port of `DeepNestSharp.RasterNest.PlacementCollision.PlacementNoise`
/// (positions are written to the project rounded to four decimals).
pub const PLACEMENT_NOISE: f64 = 1e-4;

/// Depth-based pair clearance test. Port of `PlacementCollision.TooClose`
/// (`DeepNestSharp/RasterNest/PlacementCollision.cs`):
/// - cheap bounding-box reject with the clearance,
/// - symmetric: grow A by clearance and test against B, then grow B and test
///   against A (the two polygon approximations can disagree on a boundary pair;
///   taking either as disqualifying errs toward the safe verdict),
/// - overlap judged by DEPTH (inscribed diameter of each intersection piece),
///   not area, so a long sliver alongside a real bite cannot average the bite
///   away, and welded pairs landing a hair past exact contact are forgiven
///   anything shallower than `sliver` (the cut width / placement noise).
/// Deviations: growth uses the engine's square-join halo convention (the C#
/// viewer uses round joins), and holes are subtracted explicitly instead of
/// relying on even-odd ring orientation.
#[allow(clippy::too_many_arguments)]
pub fn placements_too_close(
    a_outer: &[SvgPoint], a_children: &[Vec<SvgPoint>], a_at: (f64, f64),
    b_outer: &[SvgPoint], b_children: &[Vec<SvgPoint>], b_at: (f64, f64),
    clearance: f64,
    sliver: f64,
    clip_factor: f64,
) -> bool {
    let a_world = translate_points(a_outer, a_at.0, a_at.1);
    let b_world = translate_points(b_outer, b_at.0, b_at.1);
    let (Some(ab), Some(bb)) = (bounds_of(&a_world), bounds_of(&b_world)) else { return false; };
    // Cheap reject (PlacementCollision.BoxesApart).
    if ab.max_x + clearance <= bb.min_x || bb.max_x <= ab.min_x - clearance
        || ab.max_y + clearance <= bb.min_y || bb.max_y <= ab.min_y - clearance {
        return false;
    }

    let a_holes: Vec<Vec<SvgPoint>> = a_children.iter().map(|h| translate_points(h, a_at.0, a_at.1)).collect();
    let b_holes: Vec<Vec<SvgPoint>> = b_children.iter().map(|h| translate_points(h, b_at.0, b_at.1)).collect();

    let overlap = |grown: &[Vec<SvgPoint>], grown_holes: &[Vec<SvgPoint>], other: &[Vec<SvgPoint>], other_holes: &[Vec<SvgPoint>]| -> bool {
        let inter = nest_clip::intersection_paths_at_scale(grown, other, clip_factor);
        if inter.is_empty() { return false; }
        // holes of either side remove material from the intersection
        let mut holes: Vec<Vec<SvgPoint>> = grown_holes.to_vec();
        holes.extend(other_holes.iter().cloned());
        let pieces = if holes.is_empty() { inter } else { nest_clip::difference_paths_at_scale(&inter, &holes, clip_factor) };
        pieces.iter().any(|p| bites_deeper_than(p, sliver))
    };

    if clearance > 0.0 {
        let grown = nest_clip::inflate_paths_at_scale(&[a_world.clone()], clearance, clip_factor);
        if overlap(&grown, &a_holes, &[b_world.clone()], &b_holes) { return true; }
        let grown = nest_clip::inflate_paths_at_scale(&[b_world.clone()], clearance, clip_factor);
        overlap(&grown, &b_holes, &[a_world], &a_holes)
    } else {
        // Without growth the two orders intersect the same two regions.
        overlap(&[a_world], &a_holes, &[b_world], &b_holes)
    }
}

/// Whether one overlap piece bites deeper than `limit` (the widest the piece
/// gets — the diameter of the largest circle that fits inside it). Port of
/// `PlacementCollision.BitesDeeperThan`.
pub fn bites_deeper_than(piece: &[SvgPoint], limit: f64) -> bool {
    if piece.len() < 3 { return false; }
    let area = nest_geom::polygon_area(piece).abs();
    if area <= 0.0 { return false; }
    let mut perimeter = 0.0f64;
    for i in 0..piece.len() {
        let p = piece[i];
        let q = piece[(i + 1) % piece.len()];
        perimeter += ((q.x - p.x).powi(2) + (q.y - p.y).powi(2)).sqrt();
    }
    if perimeter <= 0.0 { return false; }
    let ratio = 2.0 * area / perimeter; // 2A/P brackets the inscribed diameter in [ratio, 2*ratio]
    if ratio > limit { return true; }
    if 2.0 * ratio <= limit { return false; }
    inscribed_diameter(piece, limit) > limit
}

/// Diameter of the largest circle that fits inside the piece, found by
/// shrinking it until nothing is left (port of `PlacementCollision.InscribedDiameter`).
fn inscribed_diameter(piece: &[SvgPoint], limit: f64) -> f64 {
    let mut lo = 0.0f64;
    let mut hi = limit; // radius; a diameter past 2*hi was already settled by the bounds
    for _ in 0..8 {
        let mid = (lo + hi) / 2.0;
        let shrunk = nest_clip::inflate_paths(&[piece.to_vec()], -mid);
        let nonempty = shrunk.iter().any(|p| p.len() >= 3 && nest_geom::polygon_area(p).abs() > 0.0);
        if nonempty { lo = mid; } else { hi = mid; }
    }
    2.0 * lo
}

pub fn part_area(detail: &RawDetail) -> f64 {
    let outer = polygon_area(&detail.outer).abs();
    let holes: f64 = detail.children.iter().map(|c| polygon_area(c).abs()).sum();
    (outer - holes).max(0.0)
}

#[cfg(test)]
mod collision_tests {
    use super::*;

    fn rect(x: f64, y: f64, w: f64, h: f64) -> Vec<SvgPoint> {
        vec![SvgPoint::new(x, y), SvgPoint::new(x + w, y), SvgPoint::new(x + w, y + h), SvgPoint::new(x, y + h)]
    }

    #[test]
    fn too_close_depth_semantics() {
        let a = rect(0.0, 0.0, 10.0, 10.0);
        // real overlap of 5 -> collision
        let b = rect(5.0, 0.0, 10.0, 10.0);
        assert!(placements_too_close(&a, &[], (0.0, 0.0), &b, &[], (0.0, 0.0), 0.0, PLACEMENT_NOISE, 1.0));
        // exact touch -> allowed (welded pair)
        let c = rect(10.0, 0.0, 10.0, 10.0);
        assert!(!placements_too_close(&a, &[], (0.0, 0.0), &c, &[], (0.0, 0.0), 0.0, PLACEMENT_NOISE, 1.0));
        // a hair past exact contact -> placement noise, forgiven
        let d = rect(10.000001, 0.0, 10.0, 10.0);
        assert!(!placements_too_close(&a, &[], (0.0, 0.0), &d, &[], (0.0, 0.0), 0.0, PLACEMENT_NOISE, 1.0));
        // clearance 2 rejected: boxes 10 apart but need 12
        let e = rect(10.0, 0.0, 10.0, 10.0);
        assert!(placements_too_close(&a, &[], (0.0, 0.0), &e, &[], (0.0, 0.0), 2.0, PLACEMENT_NOISE, 1.0));
        // a part inside another's hole is not a collision
        let frame = rect(0.0, 0.0, 60.0, 60.0);
        let hole = rect(10.0, 10.0, 40.0, 40.0);
        let inner = rect(0.0, 0.0, 20.0, 20.0);
        assert!(!placements_too_close(&frame, &[hole], (0.0, 0.0), &inner, &[], (20.0, 20.0), 0.0, PLACEMENT_NOISE, 1.0));
    }
}
