//! Orbiting no-fit polygon (NFP) computation.
//!
//! Faithful port of `NoFitPolygonUtil.NoFitPolygon(a, b, inside, searchEdges)` and
//! `NoFitPolygonUtil.SearchStartPoint` from `DeepNestLib.Core/Geometry/NoFitPolygonUtil.cs`
//! (ACVSheetNest), including the geometry helpers they need: `PolygonSlideDistance`,
//! `PolygonProjectionDistance`, `SegmentDistance`, `PointDistance`, `LineIntersect`,
//! `NormalizeVector`, `Intersect`, `InNfp`. The C# is itself a transcription of
//! svg-nest's `geometryutil.js` (`noFitPolygon`, `searchStartPoint`); wherever the C#
//! text looks like a transcription slip this port follows svg-nest and documents the
//! deviation in the comment of the affected function.
//!
//! The `Marked` flags of the C# `SvgPoint` are working state of the orbiting
//! algorithm; they are modeled here on the internal `PolyPoint` and never leak into
//! `nest_geom::SvgPoint`.

use crate::Nfp;
use nest_geom::{
    almost_equal, get_polygon_bounds, on_segment, point_in_polygon, SvgPoint, TOLERANCE,
};

/// Tolerance of the ACV `GeometryUtil`: `Math.Pow(10, -9)`.
const TOL: f64 = TOLERANCE;

/// The point type used by the orbiting algorithm: a working `SvgPoint` plus the
/// `Marked` flag of the C# port (svg-nest's `marked = 1`).
#[derive(Debug, Clone, Copy)]
struct PolyPoint {
    point: SvgPoint,
    marked: bool,
}

/// A polygon under translation. `offset` is `OffsetX/OffsetY` of the C#
/// `NoFitPolygon` while it is being orbited (`a` keeps offset `(0, 0)`, only `b` moves).
#[derive(Debug, Clone)]
struct Poly {
    points: Vec<PolyPoint>,
    offset: (f64, f64),
}

impl Poly {
    /// Builds the working polygon from a caller's point list; all `Marked` flags
    /// start `false`, like freshly created C# `SvgPoint`s.
    fn from_svg_points(points: &[SvgPoint]) -> Self {
        Self {
            points: points
                .iter()
                .map(|p| PolyPoint {
                    point: *p,
                    marked: false,
                })
                .collect(),
            offset: (0.0, 0.0),
        }
    }

    fn len(&self) -> usize {
        self.points.len()
    }

    fn x(&self, i: usize) -> f64 {
        self.points[i].point.x
    }

    fn y(&self, i: usize) -> f64 {
        self.points[i].point.y
    }

    fn p(&self, i: usize) -> SvgPoint {
        self.points[i].point
    }

    /// `a[i].X + offsetx` — the translated position the C# reads all over the loops.
    fn ox(&self, i: usize) -> f64 {
        self.x(i) + self.offset.0
    }

    fn oy(&self, i: usize) -> f64 {
        self.y(i) + self.offset.1
    }

    fn op(&self, i: usize) -> SvgPoint {
        SvgPoint::new(self.ox(i), self.oy(i))
    }

    fn svg_points(&self) -> Vec<SvgPoint> {
        self.points.iter().map(|p| p.point).collect()
    }

    /// Port of `INfp.Slice(0)`: a point copy with the offset dropped (`Slice` builds a
    /// fresh `NoFitPolygon`, whose `OffsetX/OffsetY` are null).
    ///
    /// Deviation from the C#: the C# `Slice` builds *fresh* points and thereby loses
    /// `Marked`; this copy retains marks already set by the orbiter so `searchEdges`
    /// can skip A vertices whose orbits have already been traced.
    fn slice0(&self) -> Poly {
        Poly {
            points: self.points.clone(),
            offset: (0.0, 0.0),
        }
    }

    /// `Slice(0)` plus the svg-nest "close the loop for polygons" step
    /// (`if (A[0] != A[A.length-1]) A.push(A[0])`).
    fn closed(&self) -> Poly {
        let mut copy = self.slice0();
        if !points_value_equal(&copy.points[0], &copy.points[copy.points.len() - 1]) {
            copy.points.push(copy.points[0]);
        }
        copy
    }
}

/// Port of the C# `SvgPoint.Equals` value comparison (`HashCode.Combine(Exact, Marked,
/// Math.Round(X, 4), Math.Round(Y, 4))`), used for the "close the loop" checks.
fn points_value_equal(a: &PolyPoint, b: &PolyPoint) -> bool {
    (a.point.x * 10000.0).round() == (b.point.x * 10000.0).round()
        && (a.point.y * 10000.0).round() == (b.point.y * 10000.0).round()
        && a.point.exact == b.point.exact
        && a.marked == b.marked
}

/// Which polygon a `NVector` endpoint refers to. The C# `NVector` holds `SvgPoint`
/// *references* into the A/B point arrays, so `translate.Start.Marked = true` marks
/// the polygon vertex itself; here it is a tagged index.
#[derive(Debug, Clone, Copy)]
enum PtRef {
    A(usize),
    B(usize),
}

/// Port of `NoFitPolygonUtil.NVector`: a translation candidate with `Start`/`End`
/// point references (kept as `PtRef` indices here).
#[derive(Debug, Clone, Copy)]
struct NVector {
    x: f64,
    y: f64,
    start: PtRef,
    end: PtRef,
}

/// Port of `NoFitPolygonUtil.TouchingItem` (`Type` 0: touching vertices, 1: a B
/// vertex on an A edge, 2: an A vertex on a B edge).
#[derive(Debug, Clone, Copy)]
struct TouchingItem {
    kind: u8,
    a: usize,
    b: usize,
}

/// Returns an interior NFP for the special case where `a` is a rectangle.
/// Full port of `NoFitPolygonUtil.NoFitPolygonRectangle`.
pub fn no_fit_polygon_rectangle(a: &Nfp, b: &Nfp) -> Option<Nfp> {
    let bounds_a = a.bounds()?;
    let bounds_b = b.bounds()?;

    if bounds_b.width > bounds_a.width || bounds_b.height > bounds_a.height {
        return None;
    }

    // Region of translations `t` with `b + t` inside `a`, computed from bounds.
    // (The classic svg-nest formula anchors on `outer[0]`, which assumes the
    // polygon starts at its bottom-left corner vertex; contours coming from
    // clipping (e.g. spacing-inflated outlines) may start anywhere, so we use
    // the bounds directly — identical result for corner-anchored contours.)
    Some(Nfp::new(vec![
        SvgPoint::new(
            bounds_a.x_min - bounds_b.x_min,
            bounds_a.y_min - bounds_b.y_min,
        ),
        SvgPoint::new(
            bounds_a.x_max() - bounds_b.x_max(),
            bounds_a.y_min - bounds_b.y_min,
        ),
        SvgPoint::new(
            bounds_a.x_max() - bounds_b.x_max(),
            bounds_a.y_max() - bounds_b.y_max(),
        ),
        SvgPoint::new(
            bounds_a.x_min - bounds_b.x_min,
            bounds_a.y_max() - bounds_b.y_max(),
        ),
    ]))
}

/// Port of `GeometryUtil.IsRectangle` (svg-nest `isRectangle(poly, tolerance)`, with
/// the default tolerance): every vertex lies on an edge of the bounding box.
fn is_rectangle(points: &[SvgPoint]) -> bool {
    let bb = get_polygon_bounds(points);
    points.iter().all(|p| {
        (almost_equal(p.x, bb.x_min) || almost_equal(p.x, bb.x_max()))
            && (almost_equal(p.y, bb.y_min) || almost_equal(p.y, bb.y_max()))
    })
}

/// Given a static polygon A and a movable polygon B, computes no-fit polygons by
/// orbiting B about A. If `inside` is set, B is orbited inside A rather than outside.
/// If `search_edges` is set, all edges of A are explored — possibly multiple NFPs.
///
/// Port of `NoFitPolygonUtil.NoFitPolygon` (svg-nest `noFitPolygon`).
///
/// Deviations from the C# text (all towards the svg-nest original the C# is based on):
/// - Interior NFPs of a rectangular A are routed to `no_fit_polygon_rectangle`
///   (the exact special case), mirroring the svg-nest caller in `svgnest.js`
///   (`if (pair.key.inside) { if (GeometryUtil.isRectangle(A)) …
///   noFitPolygonRectangle … else noFitPolygon(A, B, true, …) }`). The raw C#
///   `SearchStartPoint` can never produce an *interior* startpoint for aligned
///   shapes anyway: every candidate has a B vertex coincident with an A vertex and
///   the "A and B are the same" guard then aborts the search.
/// - `Marked` flags survive the internal `Slice(0)` copies (svg-nest shares the
///   point objects; the C# `Slice` accidentally drops them).
/// - Redundant collinear points are removed from the returned traces; raw traces are
///   retained internally for `InNfp`, so this only changes the representation.
/// - `NormalizeVector` uses full `f64` precision (the C# carries an accidental
///   `(float)` cast from the deepnest.js port; svg-nest has none).
/// - `line_intersect` rejects non-finite results like svg-nest's `isFinite` check
///   (the C# `IsFinite` helper is a no-op stub).
pub fn no_fit_polygon(a: &Nfp, b: &Nfp, inside: bool, search_edges: bool) -> Option<Vec<Nfp>> {
    if a.points.len() < 3 || b.points.len() < 3 {
        return None;
    }

    // svg-nest caller routing (svgnest.js): interior NFP for a rectangular A uses
    // the exact rectangle special case.
    if inside && is_rectangle(&a.points) {
        return no_fit_polygon_rectangle(a, b).map(|nfp| vec![nfp]);
    }

    let mut a = Poly::from_svg_points(&a.points);
    let mut b = Poly::from_svg_points(&b.points);

    // a.OffsetX = 0; a.OffsetY = 0 (fresh working polygons already carry zero offsets).

    // Clear the orbiter's mark state (the C# never clears A[0].Marked — ported as-is),
    // and find the bottom-most point of A and the top-most point of B.
    let mut min_a = a.y(0);
    let mut min_a_index = 0usize;
    for i in 1..a.len() {
        a.points[i].marked = false;
        if a.y(i) < min_a {
            min_a = a.y(i);
            min_a_index = i;
        }
    }

    let mut max_b = b.y(0);
    let mut max_b_index = 0usize;
    for i in 1..b.len() {
        b.points[i].marked = false;
        if b.y(i) > max_b {
            max_b = b.y(i);
            max_b_index = i;
        }
    }

    let mut startpoint: Option<SvgPoint> = if !inside {
        // shift B such that the bottom-most point of B is at the top-most point of A.
        // This guarantees an initial placement with no intersections
        Some(SvgPoint::new(
            a.x(min_a_index) - b.x(max_b_index),
            a.y(min_a_index) - b.y(max_b_index),
        ))
    } else {
        // no reliable heuristic for inside
        search_start_point(&mut a, &b, inside, &[])
    };

    let mut nfp_list: Vec<Vec<SvgPoint>> = Vec::new();

    while let Some(sp) = startpoint {
        b.offset = (sp.x, sp.y);

        // keep track of previous vector
        let mut prevvector: Option<(f64, f64)> = None;
        // NFP started as [{x: B[0].x + B.offsetx, y: B[0].y + B.offsety}]
        let mut nfp: Option<Vec<SvgPoint>> = Some(vec![SvgPoint::new(
            b.x(0) + b.offset.0,
            b.y(0) + b.offset.1,
        )]);
        let mut referencex = b.x(0) + b.offset.0;
        let mut referencey = b.y(0) + b.offset.1;
        let startx = referencex;
        let starty = referencey;
        let mut counter: usize = 0;

        while counter < 10 * (a.len() + b.len()) {
            // sanity check, prevent infinite loop

            // find touching vertices/edges
            let mut touching: Vec<TouchingItem> = Vec::new();
            for i in 0..a.len() {
                let nexti = if i == a.len() - 1 { 0 } else { i + 1 };
                for j in 0..b.len() {
                    let nextj = if j == b.len() - 1 { 0 } else { j + 1 };
                    if almost_equal(a.x(i), b.ox(j)) && almost_equal(a.y(i), b.oy(j)) {
                        touching.push(TouchingItem {
                            kind: 0,
                            a: i,
                            b: j,
                        });
                    } else if on_segment(a.p(i), a.p(nexti), b.op(j)) {
                        touching.push(TouchingItem {
                            kind: 1,
                            a: nexti,
                            b: j,
                        });
                    } else if on_segment(b.op(j), b.op(nextj), a.p(i)) {
                        touching.push(TouchingItem {
                            kind: 2,
                            a: i,
                            b: nextj,
                        });
                    }
                }
            }

            // generate translation vectors from touching vertices/edges
            let mut vectors: Vec<NVector> = Vec::new();
            for t in &touching {
                a.points[t.a].marked = true;

                // adjacent A vertices
                let prev_aindex = if t.a == 0 { a.len() - 1 } else { t.a - 1 };
                let next_aindex = if t.a + 1 >= a.len() { 0 } else { t.a + 1 };

                // adjacent B vertices
                let prev_bindex = if t.b == 0 { b.len() - 1 } else { t.b - 1 };
                let next_bindex = if t.b + 1 >= b.len() { 0 } else { t.b + 1 };

                if t.kind == 0 {
                    let v_a1 = NVector {
                        x: a.x(prev_aindex) - a.x(t.a),
                        y: a.y(prev_aindex) - a.y(t.a),
                        start: PtRef::A(t.a),
                        end: PtRef::A(prev_aindex),
                    };
                    let v_a2 = NVector {
                        x: a.x(next_aindex) - a.x(t.a),
                        y: a.y(next_aindex) - a.y(t.a),
                        start: PtRef::A(t.a),
                        end: PtRef::A(next_aindex),
                    };
                    // B vectors need to be inverted
                    let v_b1 = NVector {
                        x: b.x(t.b) - b.x(prev_bindex),
                        y: b.y(t.b) - b.y(prev_bindex),
                        start: PtRef::B(prev_bindex),
                        end: PtRef::B(t.b),
                    };
                    let v_b2 = NVector {
                        x: b.x(t.b) - b.x(next_bindex),
                        y: b.y(t.b) - b.y(next_bindex),
                        start: PtRef::B(next_bindex),
                        end: PtRef::B(t.b),
                    };

                    vectors.push(v_a1);
                    vectors.push(v_a2);
                    vectors.push(v_b1);
                    vectors.push(v_b2);
                } else if t.kind == 1 {
                    vectors.push(NVector {
                        x: a.x(t.a) - (b.x(t.b) + b.offset.0),
                        y: a.y(t.a) - (b.y(t.b) + b.offset.1),
                        start: PtRef::A(prev_aindex),
                        end: PtRef::A(t.a),
                    });
                    vectors.push(NVector {
                        x: a.x(prev_aindex) - (b.x(t.b) + b.offset.0),
                        y: a.y(prev_aindex) - (b.y(t.b) + b.offset.1),
                        start: PtRef::A(t.a),
                        end: PtRef::A(prev_aindex),
                    });
                } else if t.kind == 2 {
                    vectors.push(NVector {
                        x: a.x(t.a) - (b.x(t.b) + b.offset.0),
                        y: a.y(t.a) - (b.y(t.b) + b.offset.1),
                        start: PtRef::B(prev_bindex),
                        end: PtRef::B(t.b),
                    });
                    vectors.push(NVector {
                        x: a.x(t.a) - (b.x(prev_bindex) + b.offset.0),
                        y: a.y(t.a) - (b.y(prev_bindex) + b.offset.1),
                        start: PtRef::B(t.b),
                        end: PtRef::B(prev_bindex),
                    });
                }
            }

            // todo: there should be a faster way to reject vectors that will cause
            // immediate intersection. For now just check them all
            let mut translate: Option<NVector> = None;
            let mut maxd = 0.0f64;
            let a_edges = EdgeIndex::new(&a);

            for v in &vectors {
                if v.x == 0.0 && v.y == 0.0 {
                    continue;
                }

                // if this vector points us back to where we came from, ignore it.
                // ie cross product = 0, dot product < 0
                if let Some((prevx, prevy)) = prevvector {
                    if (v.y * prevy) + (v.x * prevx) < 0.0 {
                        // compare magnitude with unit vectors
                        let vectorlength = (v.x * v.x + v.y * v.y).sqrt();
                        let unitv = (v.x / vectorlength, v.y / vectorlength);

                        let prevlength = (prevx * prevx + prevy * prevy).sqrt();
                        let prevunit = (prevx / prevlength, prevy / prevlength);

                        // we need to scale down to unit vectors to normalize vector
                        // length. Could also just do a tan here
                        if ((unitv.1 * prevunit.0) - (unitv.0 * prevunit.1)).abs() < 0.0001 {
                            continue;
                        }
                    }
                }

                let mut d = slide_distance_within(&a_edges, &b, v);
                let vecd2 = (v.x * v.x) + (v.y * v.y);

                if d.is_none() || d.unwrap_or(f64::NAN) * d.unwrap_or(f64::NAN) > vecd2 {
                    let vecd = (v.x * v.x + v.y * v.y).sqrt();
                    d = Some(vecd);
                }

                if let Some(dd) = d {
                    if dd > maxd {
                        maxd = dd;
                        translate = Some(*v);
                    }
                }
            }

            let Some(v) = translate else {
                // didn't close the loop, something went wrong here
                nfp = None;
                break;
            };
            if almost_equal(maxd, 0.0) {
                nfp = None;
                break;
            }

            // mark the points this vector slides between (the C# mutates the point
            // objects referenced by NVector.Start/End)
            match v.start {
                PtRef::A(i) => a.points[i].marked = true,
                PtRef::B(i) => b.points[i].marked = true,
            }
            match v.end {
                PtRef::A(i) => a.points[i].marked = true,
                PtRef::B(i) => b.points[i].marked = true,
            }

            // trim (ported after `prevvector = translate`, whose referenced object
            // therefore ends up holding the trimmed values)
            let vlength2 = (v.x * v.x) + (v.y * v.y);
            let mut vx = v.x;
            let mut vy = v.y;
            if maxd * maxd < vlength2 && !almost_equal(maxd * maxd, vlength2) {
                let scale = ((maxd * maxd) / vlength2).sqrt();
                vx *= scale;
                vy *= scale;
            }
            prevvector = Some((vx, vy));

            referencex += vx;
            referencey += vy;

            if almost_equal(referencex, startx) && almost_equal(referencey, starty) {
                // we've made a full loop
                break;
            }

            // if A and B start on a touching horizontal line, the end point may not
            // be the start point
            let mut looped = false;
            if let Some(pts) = &nfp {
                for i in 0..pts.len().saturating_sub(1) {
                    if almost_equal(referencex, pts[i].x) && almost_equal(referencey, pts[i].y) {
                        looped = true;
                    }
                }
            }
            if looped {
                // we've made a full loop
                break;
            }

            if let Some(pts) = &mut nfp {
                pts.push(SvgPoint::new(referencex, referencey));
            }

            b.offset.0 += vx;
            b.offset.1 += vy;

            counter += 1;
        }

        if let Some(pts) = nfp {
            if !pts.is_empty() {
                nfp_list.push(pts);
            }
        }

        if !search_edges {
            // only get outer NFP or first inner NFP
            break;
        }

        startpoint = search_start_point(&mut a, &b, inside, &nfp_list);
    }

    // C# distinguishes invalid input (null) from a valid search with no traces
    // (an empty array); Option<Vec<_>> preserves that distinction.
    Some(
        nfp_list
            .into_iter()
            .map(|points| Nfp::new(remove_collinear_points(points)))
            .collect(),
    )
}

/// Removes redundant points on straight runs produced when the contact pair changes
/// while B continues along the same geometric edge. This does not alter the orbit or
/// its `InNfp` bookkeeping (which uses the raw trace above), only its representation.
fn remove_collinear_points(points: Vec<SvgPoint>) -> Vec<SvgPoint> {
    let mut points = points;
    let mut i = 0usize;

    while points.len() >= 3 && i < points.len() {
        let previous = (i + points.len() - 1) % points.len();
        let next = (i + 1) % points.len();
        let p = points[previous];
        let q = points[i];
        let r = points[next];
        let cross = ((q.x - p.x) * (r.y - q.y)) - ((q.y - p.y) * (r.x - q.x));
        let forward_dot = ((q.x - p.x) * (r.x - q.x)) + ((q.y - p.y) * (r.y - q.y));

        if almost_equal(cross, 0.0) && forward_dot >= 0.0 {
            points.remove(i);
            i = i.saturating_sub(1);
        } else {
            i += 1;
        }
    }

    points
}

/// searches for an arrangement of A and B such that they do not overlap; if an NFP is
/// given, only search for startpoints that have not already been traversed in the
/// given NFP.
///
/// Port of `NoFitPolygonUtil.SearchStartPoint` (private in the C#). Existing marks
/// survive the internal slice; as in C#, marks made on the helper's own slice are
/// local to that search. The post-slide `bInside` deliberately retains its previous
/// non-null value when every tested point is on the boundary.
fn search_start_point(
    a: &mut Poly,
    b_poly: &Poly,
    inside: bool,
    nfps: &[Vec<SvgPoint>],
) -> Option<SvgPoint> {
    // clone arrays (the slice drops the offset like the svg-nest `poly.slice(0)`)
    let mut a = a.slice0();
    let mut b = b_poly.slice0();

    // close the loop for polygons
    if !points_value_equal(&a.points[0], &a.points[a.len() - 1]) {
        a.points.push(a.points[0]);
    }
    if !points_value_equal(&b.points[0], &b.points[b.len() - 1]) {
        b.points.push(b.points[0]);
    }

    for i in 0..a.len() - 1 {
        if !a.points[i].marked {
            a.points[i].marked = true;
            for j in 0..b.len() {
                b.offset = (a.x(i) - b.x(j), a.y(i) - b.y(j));

                let a_points = a.svg_points();
                let mut b_inside: Option<bool> = None;
                for k in 0..b.len() {
                    let inpoly = point_in_polygon(b.op(k), &a_points, (0.0, 0.0));
                    if inpoly.is_some() {
                        b_inside = inpoly;
                        break;
                    }
                }

                if b_inside.is_none() {
                    // A and B are the same
                    return None;
                }

                let start_point = SvgPoint::new(b.offset.0, b.offset.1);
                if candidate_permitted(b_inside, inside, &start_point, &a, &b, nfps) {
                    return Some(start_point);
                }

                // slide B along vector
                let mut vx = a.x(i + 1) - a.x(i);
                let mut vy = a.y(i + 1) - a.y(i);

                let d1 = polygon_projection_distance(&a, &b, SvgPoint::new(vx, vy));
                let d2 = polygon_projection_distance(&b, &a, SvgPoint::new(-vx, -vy));

                let d = match (d1, d2) {
                    (None, None) => None,
                    (None, Some(x)) => Some(x),
                    (Some(x), None) => Some(x),
                    (Some(x), Some(y)) => Some(x.min(y)),
                };

                // only slide until no longer negative
                let Some(d) = d else { continue };
                if !(!almost_equal(d, 0.0) && d > 0.0) {
                    continue;
                }

                let vd2 = (vx * vx) + (vy * vy);

                if d * d < vd2 && !almost_equal(d * d, vd2) {
                    let vd = (vx * vx + vy * vy).sqrt();
                    vx *= d / vd;
                    vy *= d / vd;
                }

                b.offset.0 += vx;
                b.offset.1 += vy;

                let a_points = a.svg_points();
                // C# deliberately keeps the previous non-null verdict when every
                // post-slide point lies on the boundary.
                for k in 0..b.len() {
                    let inpoly = point_in_polygon(b.op(k), &a_points, (0.0, 0.0));
                    if inpoly.is_some() {
                        b_inside = inpoly;
                        break;
                    }
                }
                let start_point = SvgPoint::new(b.offset.0, b.offset.1);
                if candidate_permitted(b_inside, inside, &start_point, &a, &b, nfps) {
                    return Some(start_point);
                }
            }
        }
    }

    None
}

/// C# `((Binside && inside) || (!Binside && !inside))`, followed by the
/// no-intersection and not-already-in-an-NFP checks.
fn candidate_permitted(
    b_inside: Option<bool>,
    inside: bool,
    start_point: &SvgPoint,
    a: &Poly,
    b: &Poly,
    nfps: &[Vec<SvgPoint>],
) -> bool {
    let position_ok = b_inside == Some(inside);

    position_ok && !intersect(a, b) && !in_nfp(*start_point, nfps)
}

/// Port of `NoFitPolygonUtil.PolygonSlideDistance` (svg-nest `polygonSlideDistance`).
///
/// Deviation from the C#: the offsets are captured *before* slicing (like svg-nest).
/// The C# reads `edgeA[j].X + a.OffsetX ?? 0` from the freshly sliced polygon whose
/// `OffsetX` is null, i.e. it computes `(x + null) ?? 0 = 0` and loses the offset —
/// an apparent transcription bug that also makes the C# text unworkable, so the
/// svg-nest semantics are ported.
#[cfg(test)]
fn polygon_slide_distance(
    a: &Poly,
    b: &Poly,
    direction: &NVector,
    ignore_negative: bool,
) -> Option<f64> {
    let a_offset = a.offset;
    let b_offset = b.offset;

    let a = a.closed();
    let b = b.closed();

    let mut distance: Option<f64> = None;

    let dir = normalize_vector(SvgPoint::new(direction.x, direction.y));

    for i in 0..b.len() - 1 {
        for j in 0..a.len() - 1 {
            let a_current = SvgPoint::new(a.x(j) + a_offset.0, a.y(j) + a_offset.1);
            let a_next = SvgPoint::new(a.x(j + 1) + a_offset.0, a.y(j + 1) + a_offset.1);
            let b_current = SvgPoint::new(b.x(i) + b_offset.0, b.y(i) + b_offset.1);
            let b_next = SvgPoint::new(b.x(i + 1) + b_offset.0, b.y(i + 1) + b_offset.1);

            if (almost_equal(a_current.x, a_next.x) && almost_equal(a_current.y, a_next.y))
                || (almost_equal(b_current.x, b_next.x) && almost_equal(b_current.y, b_next.y))
            {
                continue; // ignore extremely small lines
            }

            let d = segment_distance(a_current, a_next, b_current, b_next, dir);

            if let Some(d) = d {
                if distance.is_none() || d < distance.unwrap_or(f64::NAN) {
                    if !ignore_negative || d > 0.0 || almost_equal(d, 0.0) {
                        distance = Some(d);
                    }
                }
            }
        }
    }

    distance
}

/// The edges of A (closed, offset applied) with their boxes, sorted by the left
/// side of the box, for [`slide_distance_within`].
struct EdgeIndex {
    /// (min_x, max_x, min_y, max_y, start, end)
    edges: Vec<(f64, f64, f64, f64, SvgPoint, SvgPoint)>,
    /// The widest box: an edge reaching `x` starts no further left than this.
    widest: f64,
}

impl EdgeIndex {
    fn new(a: &Poly) -> Self {
        let offset = a.offset;
        let a = a.closed();
        let mut edges = Vec::with_capacity(a.len());
        let mut widest = 0.0f64;
        for j in 0..a.len() - 1 {
            let p = SvgPoint::new(a.x(j) + offset.0, a.y(j) + offset.1);
            let q = SvgPoint::new(a.x(j + 1) + offset.0, a.y(j + 1) + offset.1);
            if almost_equal(p.x, q.x) && almost_equal(p.y, q.y) {
                continue; // ignore extremely small lines
            }
            widest = widest.max((p.x - q.x).abs());
            edges.push((p.x.min(q.x), p.x.max(q.x), p.y.min(q.y), p.y.max(q.y), p, q));
        }
        edges.sort_by(|u, v| u.0.total_cmp(&v.0));
        Self { edges, widest }
    }
}

/// [`polygon_slide_distance`] (negative distances ignored) for the orbit's own
/// question: how far can B slide along `direction` before it hits A, when it
/// goes no further than the vector's length anyway? The caller replaces a
/// missing or longer distance by that length, so a pair of edges matters only
/// if B's edge, swept along the vector, comes near A's edge: every distance
/// the pair can yield is between a point of one and a point of the other along
/// the direction. The rest, nearly all pairs on outlines of hundreds of
/// points, are skipped by their boxes.
fn slide_distance_within(a: &EdgeIndex, b: &Poly, direction: &NVector) -> Option<f64> {
    const PAD: f64 = 1e-3;
    let b_offset = b.offset;
    let b = b.closed();

    let mut distance: Option<f64> = None;
    let dir = normalize_vector(SvgPoint::new(direction.x, direction.y));

    for i in 0..b.len() - 1 {
        let b_current = SvgPoint::new(b.x(i) + b_offset.0, b.y(i) + b_offset.1);
        let b_next = SvgPoint::new(b.x(i + 1) + b_offset.0, b.y(i + 1) + b_offset.1);
        if almost_equal(b_current.x, b_next.x) && almost_equal(b_current.y, b_next.y) {
            continue; // ignore extremely small lines
        }
        let (x0, x1) = (b_current.x.min(b_next.x), b_current.x.max(b_next.x));
        let (y0, y1) = (b_current.y.min(b_next.y), b_current.y.max(b_next.y));
        let (min_x, max_x) = (x0.min(x0 + direction.x) - PAD, x1.max(x1 + direction.x) + PAD);
        let (min_y, max_y) = (y0.min(y0 + direction.y) - PAD, y1.max(y1 + direction.y) + PAD);

        let from = a.edges.partition_point(|e| e.0 < min_x - a.widest);
        for e in &a.edges[from..] {
            if e.0 > max_x {
                break;
            }
            if e.1 < min_x || e.2 > max_y || e.3 < min_y {
                continue;
            }
            if let Some(d) = segment_distance(e.4, e.5, b_current, b_next, dir) {
                if (distance.is_none() || d < distance.unwrap_or(f64::NAN)) && (d > 0.0 || almost_equal(d, 0.0)) {
                    distance = Some(d);
                }
            }
        }
    }

    distance
}

/// Port of `NoFitPolygonUtil.PolygonProjectionDistance` (svg-nest
/// `polygonProjectionDistance`). Offsets are captured before slicing (same note as
/// [`polygon_slide_distance`]).
fn polygon_projection_distance(a: &Poly, b: &Poly, direction: SvgPoint) -> Option<f64> {
    let b_offset = b.offset;
    let a_offset = a.offset;

    let a = a.closed();
    let b = b.closed();

    let mut distance: Option<f64> = None;

    for i in 0..b.len() {
        // the shortest/most negative projection of B onto A
        let mut minprojection: Option<f64> = None;
        for j in 0..a.len() - 1 {
            let p = SvgPoint::new(b.x(i) + b_offset.0, b.y(i) + b_offset.1);
            let s1 = SvgPoint::new(a.x(j) + a_offset.0, a.y(j) + a_offset.1);
            let s2 = SvgPoint::new(a.x(j + 1) + a_offset.0, a.y(j + 1) + a_offset.1);

            if ((s2.y - s1.y) * direction.x - (s2.x - s1.x) * direction.y).abs() < TOL {
                continue;
            }

            // project point, ignore edge boundaries
            let d = point_distance(p, s1, s2, direction, false);

            if let Some(d) = d {
                if minprojection.is_none() || d < minprojection.unwrap_or(f64::NAN) {
                    minprojection = Some(d);
                }
            }
        }

        if let Some(mp) = minprojection {
            if distance.is_none() || mp > distance.unwrap_or(f64::NAN) {
                distance = Some(mp);
            }
        }
    }

    distance
}

/// normalize vector into a unit vector.
/// Port of `NoFitPolygonUtil.NormalizeVector` (svg-nest `_normalizeVector`).
/// Deviation from the C#: the C# carries an accidental `(float)` cast of the inverse
/// length (a leftover from porting); svg-nest uses full precision, and so does this
/// port.
fn normalize_vector(v: SvgPoint) -> SvgPoint {
    if almost_equal((v.x * v.x) + (v.y * v.y), 1.0) {
        return v; // given vector was already a unit vector
    }

    let len = ((v.x * v.x) + (v.y * v.y)).sqrt();
    let inverse = 1.0 / len;

    SvgPoint::new(v.x * inverse, v.y * inverse)
}

/// Port of `NoFitPolygonUtil.SegmentDistance` (svg-nest `segmentDistance`).
/// Returns the distance you can slide segment `ab` along `direction` before it hits
/// segment `ef`, or `None` when they cannot collide along that direction.
#[allow(clippy::too_many_lines, clippy::many_single_char_names)]
fn segment_distance(
    a: SvgPoint,
    b: SvgPoint,
    e: SvgPoint,
    f: SvgPoint,
    direction: SvgPoint,
) -> Option<f64> {
    let normal = SvgPoint::new(direction.y, -direction.x);
    let reverse = SvgPoint::new(-direction.x, -direction.y);

    let dot_a = (a.x * normal.x) + (a.y * normal.y);
    let dot_b = (b.x * normal.x) + (b.y * normal.y);
    let dot_e = (e.x * normal.x) + (e.y * normal.y);
    let dot_f = (f.x * normal.x) + (f.y * normal.y);

    let cross_a = (a.x * direction.x) + (a.y * direction.y);
    let cross_b = (b.x * direction.x) + (b.y * direction.y);
    let cross_e = (e.x * direction.x) + (e.y * direction.y);
    let cross_f = (f.x * direction.x) + (f.y * direction.y);

    let ab_min = dot_a.min(dot_b);
    let ab_max = dot_a.max(dot_b);

    let ef_max = dot_e.max(dot_f);
    let ef_min = dot_e.min(dot_f);

    // segments that will merely touch at one point
    if almost_equal_3(ab_max, ef_min, TOL) || almost_equal_3(ab_min, ef_max, TOL) {
        return None;
    }

    // segments miss eachother completely
    if ab_max < ef_min || ab_min > ef_max {
        return None;
    }

    let overlap;

    if (ab_max > ef_max && ab_min < ef_min) || (ef_max > ab_max && ef_min < ab_min) {
        overlap = 1.0;
    } else {
        let min_max = ab_max.min(ef_max);
        let max_min = ab_min.max(ef_min);
        let max_max = ab_max.max(ef_max);
        let min_min = ab_min.min(ef_min);

        overlap = (min_max - max_min) / (max_max - min_min);
    }

    let cross_abe = ((e.y - a.y) * (b.x - a.x)) - ((e.x - a.x) * (b.y - a.y));
    let cross_abf = ((f.y - a.y) * (b.x - a.x)) - ((f.x - a.x) * (b.y - a.y));

    // lines are colinear
    if almost_equal(cross_abe, 0.0) && almost_equal(cross_abf, 0.0) {
        // segment normals must point in opposite directions
        let mut ab_norm = SvgPoint::new(b.y - a.y, a.x - b.x);
        let mut ef_norm = SvgPoint::new(f.y - e.y, e.x - f.x);

        let ab_norm_length = (ab_norm.x * ab_norm.x + ab_norm.y * ab_norm.y).sqrt();
        ab_norm.x /= ab_norm_length;
        ab_norm.y /= ab_norm_length;

        let ef_norm_length = (ef_norm.x * ef_norm.x + ef_norm.y * ef_norm.y).sqrt();
        ef_norm.x /= ef_norm_length;
        ef_norm.y /= ef_norm_length;

        if ((ab_norm.y * ef_norm.x) - (ab_norm.x * ef_norm.y)).abs() < TOL
            && (ab_norm.y * ef_norm.y) + (ab_norm.x * ef_norm.x) < 0.0
        {
            // normal of AB segment must point in same direction as given direction vector
            let normdot = (ab_norm.y * direction.y) + (ab_norm.x * direction.x);

            // the segments merely slide along eachother
            if almost_equal_3(normdot, 0.0, TOL) {
                return None;
            }

            if normdot < 0.0 {
                return Some(0.0);
            }
        }

        return None;
    }

    let mut distances: Vec<f64> = Vec::new();

    // coincident points
    if almost_equal(dot_a, dot_e) {
        distances.push(cross_a - cross_e);
    } else if almost_equal(dot_a, dot_f) {
        distances.push(cross_a - cross_f);
    } else if dot_a > ef_min && dot_a < ef_max {
        let mut d = point_distance(a, e, f, reverse, false);
        if let Some(dd) = d {
            if almost_equal(dd, 0.0) {
                // A currently touches EF, but AB is moving away from EF
                let d_b = point_distance(b, e, f, reverse, true);
                if let Some(d_b) = d_b {
                    if d_b < 0.0 || almost_equal(d_b * overlap, 0.0) {
                        d = None;
                    }
                }
            }
        }

        if let Some(dd) = d {
            distances.push(dd);
        }
    }

    if almost_equal(dot_b, dot_e) {
        distances.push(cross_b - cross_e);
    } else if almost_equal(dot_b, dot_f) {
        distances.push(cross_b - cross_f);
    } else if dot_b > ef_min && dot_b < ef_max {
        let mut d = point_distance(b, e, f, reverse, false);

        if let Some(dd) = d {
            if almost_equal(dd, 0.0) {
                // crossA>crossB A currently touches EF, but AB is moving away from EF
                let d_a = point_distance(a, e, f, reverse, true);
                if let Some(d_a) = d_a {
                    if d_a < 0.0 || almost_equal(d_a * overlap, 0.0) {
                        d = None;
                    }
                }
            }
        }

        if let Some(dd) = d {
            distances.push(dd);
        }
    }

    if dot_e > ab_min && dot_e < ab_max {
        let mut d = point_distance(e, a, b, direction, false);
        if let Some(dd) = d {
            if almost_equal(dd, 0.0) {
                // crossF<crossE A currently touches EF, but AB is moving away from EF
                let d_f = point_distance(f, a, b, direction, true);
                if let Some(d_f) = d_f {
                    if d_f < 0.0 || almost_equal(d_f * overlap, 0.0) {
                        d = None;
                    }
                }
            }
        }

        if let Some(dd) = d {
            distances.push(dd);
        }
    }

    if dot_f > ab_min && dot_f < ab_max {
        let mut d = point_distance(f, a, b, direction, false);
        if let Some(dd) = d {
            if almost_equal(dd, 0.0) {
                // A currently touches EF, but AB is moving away from EF
                let d_e = point_distance(e, a, b, direction, true);
                if let Some(d_e) = d_e {
                    if d_e < 0.0 || almost_equal(d_e * overlap, 0.0) {
                        d = None;
                    }
                }
            }
        }

        if let Some(dd) = d {
            distances.push(dd);
        }
    }

    if distances.is_empty() {
        return None;
    }

    Some(distances.into_iter().fold(f64::INFINITY, f64::min))
}

/// returns the intersection of AB and EF, or `None` when there are no intersections or
/// numerical error; segments are finite here (the `infinite` flag of the original has
/// no caller with `true`).
/// Port of `NoFitPolygonUtil.LineIntersect` (svg-nest `_lineIntersect`).
/// Deviation from the C#: non-finite results are rejected like svg-nest's `isFinite`
/// check; the C# `IsFinite` helper is a no-op stub.
fn line_intersect(a: SvgPoint, b: SvgPoint, e: SvgPoint, f: SvgPoint) -> Option<SvgPoint> {
    let a1 = b.y - a.y;
    let b1 = a.x - b.x;
    let c1 = (b.x * a.y) - (a.x * b.y);
    let a2 = f.y - e.y;
    let b2 = e.x - f.x;
    let c2 = (f.x * e.y) - (e.x * f.y);

    let denom = (a1 * b2) - (a2 * b1);

    let x = ((b1 * c2) - (b2 * c1)) / denom;
    let y = ((a2 * c1) - (a1 * c2)) / denom;

    if !x.is_finite() || !y.is_finite() {
        return None;
    }

    // coincident points do not count as intersecting
    if (a.x - b.x).abs() > TOL
        && (if a.x < b.x {
            x < a.x || x > b.x
        } else {
            x > a.x || x < b.x
        })
    {
        return None;
    }

    if (a.y - b.y).abs() > TOL
        && (if a.y < b.y {
            y < a.y || y > b.y
        } else {
            y > a.y || y < b.y
        })
    {
        return None;
    }

    if (e.x - f.x).abs() > TOL
        && (if e.x < f.x {
            x < e.x || x > f.x
        } else {
            x > e.x || x < f.x
        })
    {
        return None;
    }

    if (e.y - f.y).abs() > TOL
        && (if e.y < f.y {
            y < e.y || y > f.y
        } else {
            y > e.y || y < f.y
        })
    {
        return None;
    }

    Some(SvgPoint::new(x, y))
}

/// Port of `NoFitPolygonUtil.PointDistance` (svg-nest `pointDistance`): signed distance
/// of `p` from segment `s1s2` along `normal`; inverted when `p` is on the segment's
/// line but "inside" it.
fn point_distance(
    p: SvgPoint,
    s1: SvgPoint,
    s2: SvgPoint,
    normal: SvgPoint,
    infinite: bool,
) -> Option<f64> {
    let normal = normalize_vector(normal);

    let dir = SvgPoint::new(normal.y, -normal.x);

    let pdot = (p.x * dir.x) + (p.y * dir.y);
    let s1dot = (s1.x * dir.x) + (s1.y * dir.y);
    let s2dot = (s2.x * dir.x) + (s2.y * dir.y);

    let pdotnorm = (p.x * normal.x) + (p.y * normal.y);
    let s1dotnorm = (s1.x * normal.x) + (s1.y * normal.y);
    let s2dotnorm = (s2.x * normal.x) + (s2.y * normal.y);

    if !infinite {
        if ((pdot < s1dot || almost_equal(pdot, s1dot))
            && (pdot < s2dot || almost_equal(pdot, s2dot)))
            || ((pdot > s1dot || almost_equal(pdot, s1dot))
                && (pdot > s2dot || almost_equal(pdot, s2dot)))
        {
            return None; // dot doesn't collide with segment, or lies directly on the vertex
        }

        if almost_equal(pdot, s1dot)
            && almost_equal(pdot, s2dot)
            && pdotnorm > s1dotnorm
            && pdotnorm > s2dotnorm
        {
            return Some((pdotnorm - s1dotnorm).min(pdotnorm - s2dotnorm));
        }

        if almost_equal(pdot, s1dot)
            && almost_equal(pdot, s2dot)
            && pdotnorm < s1dotnorm
            && pdotnorm < s2dotnorm
        {
            return Some(-((s1dotnorm - pdotnorm).min(s2dotnorm - pdotnorm)));
        }
    }

    Some(-(pdotnorm - s1dotnorm + ((s1dotnorm - s2dotnorm) * (s1dot - pdot) / (s1dot - s2dot))))
}

/// returns true if the two offset polygons intersect.
/// Port of `NoFitPolygonUtil.Intersect` (svg-nest `intersect`).
///
/// Note the offsets are captured before slicing like svg-nest does; points checked
/// against the *sliced* polygons use no polygon offset, exactly like the original
/// (which passes the freshly sliced arrays into `pointInPolygon`).
fn intersect(a: &Poly, b: &Poly) -> bool {
    let a_offsetx = a.offset.0;
    let a_offsety = a.offset.1;

    let b_offsetx = b.offset.0;
    let b_offsety = b.offset.1;

    let a = a.slice0();
    let b = b.slice0();

    for i in 0..a.len() - 1 {
        for j in 0..b.len() - 1 {
            let a1 = SvgPoint::new(a.x(i) + a_offsetx, a.y(i) + a_offsety);
            let a2 = SvgPoint::new(a.x(i + 1) + a_offsetx, a.y(i + 1) + a_offsety);
            let b1 = SvgPoint::new(b.x(j) + b_offsetx, b.y(j) + b_offsety);
            let b2 = SvgPoint::new(b.x(j + 1) + b_offsetx, b.y(j + 1) + b_offsety);

            let mut prevbindex = if j == 0 { b.len() - 1 } else { j - 1 };
            let mut prevaindex = if i == 0 { a.len() - 1 } else { i - 1 };
            let mut nextbindex = if j + 1 == b.len() - 1 { 0 } else { j + 2 };
            let mut nextaindex = if i + 1 == a.len() - 1 { 0 } else { i + 2 };

            // go even further back if we happen to hit on a loop end point
            // (svg-nest first compares object identity, i.e. the aliased closing
            // point; the almost-equal comparison then covers coincident points by
            // value, which is what is ported here)
            if almost_equal(b.x(prevbindex), b.x(j)) && almost_equal(b.y(prevbindex), b.y(j)) {
                prevbindex = if prevbindex == 0 {
                    b.len() - 1
                } else {
                    prevbindex - 1
                };
            }

            if almost_equal(a.x(prevaindex), a.x(i)) && almost_equal(a.y(prevaindex), a.y(i)) {
                prevaindex = if prevaindex == 0 {
                    a.len() - 1
                } else {
                    prevaindex - 1
                };
            }

            // go even further forward if we happen to hit on a loop end point
            if almost_equal(b.x(nextbindex), b.x(j + 1))
                && almost_equal(b.y(nextbindex), b.y(j + 1))
            {
                nextbindex = if nextbindex == b.len() - 1 {
                    0
                } else {
                    nextbindex + 1
                };
            }

            if almost_equal(a.x(nextaindex), a.x(i + 1))
                && almost_equal(a.y(nextaindex), a.y(i + 1))
            {
                nextaindex = if nextaindex == a.len() - 1 {
                    0
                } else {
                    nextaindex + 1
                };
            }

            let a0 = SvgPoint::new(a.x(prevaindex) + a_offsetx, a.y(prevaindex) + a_offsety);
            let b0 = SvgPoint::new(b.x(prevbindex) + b_offsetx, b.y(prevbindex) + b_offsety);

            let a3 = SvgPoint::new(a.x(nextaindex) + a_offsetx, a.y(nextaindex) + a_offsety);
            let b3 = SvgPoint::new(b.x(nextbindex) + b_offsetx, b.y(nextbindex) + b_offsety);

            let a_points = a.svg_points();
            let b_points = b.svg_points();

            if on_segment(a1, a2, b1) || (almost_equal(a1.x, b1.x) && almost_equal(a1.y, b1.y)) {
                // if a point is on a segment, it could intersect or it could not. Check
                // via the neighboring points
                let b0in = point_in_polygon(b0, &a_points, (0.0, 0.0));
                let b2in = point_in_polygon(b2, &a_points, (0.0, 0.0));
                if matches!(b0in, Some(true)) && matches!(b2in, Some(false))
                    || matches!(b0in, Some(false)) && matches!(b2in, Some(true))
                {
                    return true;
                } else {
                    continue;
                }
            }

            if on_segment(a1, a2, b2) || (almost_equal(a2.x, b2.x) && almost_equal(a2.y, b2.y)) {
                let b1in = point_in_polygon(b1, &a_points, (0.0, 0.0));
                let b3in = point_in_polygon(b3, &a_points, (0.0, 0.0));
                if matches!(b1in, Some(true)) && matches!(b3in, Some(false))
                    || matches!(b1in, Some(false)) && matches!(b3in, Some(true))
                {
                    return true;
                } else {
                    continue;
                }
            }

            if on_segment(b1, b2, a1) || (almost_equal(a1.x, b2.x) && almost_equal(a1.y, b2.y)) {
                let a0in = point_in_polygon(a0, &b_points, (0.0, 0.0));
                let a2in = point_in_polygon(a2, &b_points, (0.0, 0.0));
                if matches!(a0in, Some(true)) && matches!(a2in, Some(false))
                    || matches!(a0in, Some(false)) && matches!(a2in, Some(true))
                {
                    return true;
                } else {
                    continue;
                }
            }

            if on_segment(b1, b2, a2) || (almost_equal(a2.x, b1.x) && almost_equal(a2.y, b1.y)) {
                let a1in = point_in_polygon(a1, &b_points, (0.0, 0.0));
                let a3in = point_in_polygon(a3, &b_points, (0.0, 0.0));
                if matches!(a1in, Some(true)) && matches!(a3in, Some(false))
                    || matches!(a1in, Some(false)) && matches!(a3in, Some(true))
                {
                    return true;
                } else {
                    continue;
                }
            }

            let p = line_intersect(b1, b2, a1, a2);

            if p.is_some() {
                return true;
            }
        }
    }

    false
}

/// returns true if point already exists in the given nfp.
/// Port of `NoFitPolygonUtil.InNfp` (svg-nest `inNfp`).
fn in_nfp(p: SvgPoint, nfps: &[Vec<SvgPoint>]) -> bool {
    nfps.iter().any(|nfp| {
        nfp.iter()
            .any(|pt| almost_equal(p.x, pt.x) && almost_equal(p.y, pt.y))
    })
}

/// `AlmostEqual` with default tolerance (same as `nest_geom::almost_equal`); a separate
/// 3-arg version keeps the C# call sites (`GeometryUtil.AlmostEqual(a, b, tolerance)`).
fn almost_equal_3(a: f64, b: f64, tolerance: f64) -> bool {
    (a - b).abs() < tolerance
}

#[cfg(test)]
mod tests {
    use super::*;
    use nest_geom::polygon_area;

    fn square(x: f64, y: f64, s: f64) -> Vec<SvgPoint> {
        vec![
            SvgPoint::new(x, y),
            SvgPoint::new(x + s, y),
            SvgPoint::new(x + s, y + s),
            SvgPoint::new(x, y + s),
        ]
    }

    fn nfp_of(points: Vec<SvgPoint>) -> Nfp {
        Nfp::new(points)
    }

    /// Independent overlap test used by the tests only: proper edge crossings count,
    /// as do vertices and edge midpoints strictly inside the other polygon. Midpoints
    /// catch containment where every vertex happens to lie on the other boundary.
    fn polygons_collide(p: &[SvgPoint], q: &[SvgPoint]) -> bool {
        for i in 0..p.len() {
            let p1 = p[i];
            let p2 = p[(i + 1) % p.len()];
            for j in 0..q.len() {
                let q1 = q[j];
                let q2 = q[(j + 1) % q.len()];
                if proper_cross(p1, p2, q1, q2) {
                    return true;
                }
            }
        }

        fn interior_samples(poly: &[SvgPoint]) -> impl Iterator<Item = SvgPoint> + '_ {
            poly.iter().enumerate().flat_map(|(i, point)| {
                let next = poly[(i + 1) % poly.len()];
                [
                    *point,
                    SvgPoint::new((point.x + next.x) * 0.5, (point.y + next.y) * 0.5),
                ]
            })
        }

        interior_samples(p).any(|v| point_in_polygon(v, q, (0.0, 0.0)) == Some(true))
            || interior_samples(q).any(|v| point_in_polygon(v, p, (0.0, 0.0)) == Some(true))
    }

    fn orient(p: SvgPoint, q: SvgPoint, r: SvgPoint) -> f64 {
        (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x)
    }

    fn proper_cross(a: SvgPoint, b: SvgPoint, c: SvgPoint, d: SvgPoint) -> bool {
        fn strict_side(value: f64) -> i8 {
            // Do not turn round-off at a tangent vertex into a proper crossing.
            if value > 1e-8 {
                1
            } else if value < -1e-8 {
                -1
            } else {
                0
            }
        }

        let d1 = strict_side(orient(a, b, c));
        let d2 = strict_side(orient(a, b, d));
        let d3 = strict_side(orient(c, d, a));
        let d4 = strict_side(orient(c, d, b));
        d1 * d2 < 0 && d3 * d4 < 0
    }

    /// Brute-force consistency check: for a grid of placements of B (positions of the
    /// B reference point), an independent collision test must agree with membership
    /// in the NFP: strictly inside the NFP => collision, strictly outside => none.
    fn assert_nfp_consistency(a: &[SvgPoint], b: &[SvgPoint], nfp: &[SvgPoint], label: &str) {
        // Every sampled boundary placement may touch A, but must not overlap it.
        for (i, point) in nfp.iter().enumerate() {
            let next = nfp[(i + 1) % nfp.len()];
            for (x, y) in [
                (point.x, point.y),
                ((point.x + next.x) * 0.5, (point.y + next.y) * 0.5),
            ] {
                let placed = b
                    .iter()
                    .map(|p| SvgPoint::new(p.x + x, p.y + y))
                    .collect::<Vec<_>>();
                assert!(
                    !polygons_collide(a, &placed),
                    "{label}: NFP-boundary placement ({x}, {y}) must not collide"
                );
            }
        }

        let bounds = get_polygon_bounds(nfp);
        let x0 = bounds.x_min - 2.0;
        let y0 = bounds.y_min - 2.0;
        let x1 = bounds.x_max() + 2.0;
        let y1 = bounds.y_max() + 2.0;
        let mut inside_n = 0;
        let mut outside_n = 0;
        let step = 0.5;

        let mut x = x0;
        while x <= x1 {
            let mut y = y0;
            while y <= y1 {
                let point = SvgPoint::new(x, y);
                match point_in_polygon(point, nfp, (0.0, 0.0)) {
                    None => {}
                    Some(inside) => {
                        let placed = b
                            .iter()
                            .map(|p| SvgPoint::new(p.x + x, p.y + y))
                            .collect::<Vec<_>>();
                        let collide = polygons_collide(a, &placed);
                        if inside {
                            assert!(
                                collide,
                                "{label}: NFP-interior placement ({x}, {y}) must collide"
                            );
                            inside_n += 1;
                        } else {
                            assert!(
                                !collide,
                                "{label}: NFP-exterior placement ({x}, {y}) must not collide"
                            );
                            outside_n += 1;
                        }
                    }
                }
                y += step;
            }
            x += step;
        }

        assert!(
            inside_n > 10,
            "{label}: too few interior samples ({inside_n})"
        );
        assert!(
            outside_n > 10,
            "{label}: too few exterior samples ({outside_n})"
        );
    }

    #[test]
    fn outer_nfp_of_two_squares_is_a_single_196_square() {
        let a = nfp_of(square(0.0, 0.0, 10.0));
        let b = nfp_of(square(0.0, 0.0, 4.0));

        let nfps = no_fit_polygon(&a, &b, false, false).expect("outer NFP");

        assert_eq!(nfps.len(), 1, "convex pair gives a single NFP");
        let nfp = &nfps[0];
        assert_eq!(nfp.len(), 4, "NFP of two squares has 4 vertices");
        assert!((nfp.area() - 196.0).abs() < 1e-6, "NFP area must be 196");

        // anchored 4 units left of/below A: B reference point range is [-4, 10]^2
        let bounds = nfp.bounds().expect("bounds");
        assert!((bounds.x_min - (-4.0)).abs() < 1e-6);
        assert!((bounds.y_min - (-4.0)).abs() < 1e-6);
        assert!((bounds.width - 14.0).abs() < 1e-6);
        assert!((bounds.height - 14.0).abs() < 1e-6);

        assert_nfp_consistency(&a.points, &b.points, &nfp.points, "squares");
    }

    #[test]
    fn outer_nfp_of_square_and_rotated_square() {
        let a = nfp_of(square(0.0, 0.0, 10.0));
        // a 4x4 square rotated by 45 degrees (a diamond; winding kept identical)
        let sqrt2 = std::f64::consts::SQRT_2;
        let b = nfp_of(vec![
            SvgPoint::new(0.0, 0.0),
            SvgPoint::new(4.0 / sqrt2, 4.0 / sqrt2),
            SvgPoint::new(0.0, 8.0 / sqrt2),
            SvgPoint::new(-4.0 / sqrt2, 4.0 / sqrt2),
        ]);

        let nfps = no_fit_polygon(&a, &b, false, false).expect("outer NFP");

        assert_eq!(nfps.len(), 1);
        let nfp = &nfps[0];
        // the rotated square adds a vertex for every new edge direction
        assert!(
            nfp.len() > 4,
            "rotated pair must add NFP vertices, got {}",
            nfp.len()
        );
        let expected_area = 116.0 + 80.0 * sqrt2;
        assert!(
            (nfp.area() - expected_area).abs() < 1e-6,
            "rotated pair NFP area must be {expected_area}, got {}",
            nfp.area()
        );
        // zero winding area would mean a degenerate trace
        assert!((polygon_area(&nfp.points).abs()) > 0.0);

        assert_nfp_consistency(&a.points, &b.points, &nfp.points, "rotated square");
    }

    #[test]
    fn triangle_pair_outer_nfp_matches_brute_force() {
        let a = nfp_of(vec![
            SvgPoint::new(0.0, 0.0),
            SvgPoint::new(10.0, 0.0),
            SvgPoint::new(0.0, 10.0),
        ]);
        let b = nfp_of(vec![
            SvgPoint::new(0.0, 0.0),
            SvgPoint::new(3.0, 0.0),
            SvgPoint::new(0.0, 3.0),
        ]);

        let nfps = no_fit_polygon(&a, &b, false, false).expect("outer NFP");
        assert_eq!(nfps.len(), 1);
        let nfp = &nfps[0];

        // Minkowski sum of A with (-B) is this hexagon
        assert_eq!(nfp.len(), 6, "NFP of the two right triangles is a hexagon");
        let expected_area = 114.5;
        assert!(
            (nfp.area() - expected_area).abs() < 1e-6,
            "expected hexagon area 114.5, got {}",
            nfp.area()
        );

        assert_nfp_consistency(&a.points, &b.points, &nfp.points, "triangles");
    }

    #[test]
    fn search_edges_two_squares_still_single_nfp() {
        // with searchEdges the algorithm explores all edges of A for further NFPs;
        // for two convex squares every startpoint candidate collapses (B coincides
        // with A) or is already covered, so exactly one NFP must result.
        let a = nfp_of(square(0.0, 0.0, 10.0));
        let b = nfp_of(square(0.0, 0.0, 4.0));
        let nfps = no_fit_polygon(&a, &b, false, true).expect("outer NFP");
        assert_eq!(nfps.len(), 1);
        assert!((nfps[0].area() - 196.0).abs() < 1e-6);
    }

    #[test]
    fn interior_nfp_of_rectangle_in_rectangle_is_the_7x7_square() {
        let a = nfp_of(square(0.0, 0.0, 10.0));
        let b = nfp_of(square(0.0, 0.0, 3.0));

        let nfps = no_fit_polygon(&a, &b, true, false).expect("interior NFP");
        assert_eq!(nfps.len(), 1);
        let nfp = &nfps[0];
        assert_eq!(nfp.len(), 4);
        assert!(
            (nfp.area() - 49.0).abs() < 1e-6,
            "interior NFP area must be 49"
        );

        let bounds = nfp.bounds().expect("bounds");
        assert!((bounds.x_min - 0.0).abs() < 1e-6);
        assert!((bounds.y_min - 0.0).abs() < 1e-6);
        assert!((bounds.width - 7.0).abs() < 1e-6);
        assert!((bounds.height - 7.0).abs() < 1e-6);
    }

    #[test]
    fn interior_nfp_when_b_bigger_than_a_is_none() {
        let a = nfp_of(square(0.0, 0.0, 3.0));
        let b = nfp_of(square(0.0, 0.0, 10.0));
        assert!(no_fit_polygon(&a, &b, true, false).is_none());
    }

    #[test]
    fn small_polygons_return_none() {
        let a = nfp_of(square(0.0, 0.0, 10.0));
        let b = nfp_of(vec![SvgPoint::new(0.0, 0.0), SvgPoint::new(1.0, 1.0)]);
        assert!(no_fit_polygon(&a, &b, false, false).is_none());
    }

    /// The orbit's slide distance looks only at the pairs of edges near B's
    /// path. It must answer what the full scan answers once the orbit has
    /// capped the distance by the vector's length.
    #[test]
    fn slide_distance_within_matches_the_full_scan() {
        let mut seed = 0x2545_F491_4F6C_DD1Du64;
        let mut rnd = move || {
            seed ^= seed << 13;
            seed ^= seed >> 7;
            seed ^= seed << 17;
            (seed >> 11) as f64 / (1u64 << 53) as f64
        };
        let mut star = |n: usize, r: f64| -> Vec<SvgPoint> {
            (0..n)
                .map(|i| {
                    let t = std::f64::consts::TAU * i as f64 / n as f64;
                    let rr = r * (if i % 2 == 0 { 1.0 } else { 0.6 }) * (0.9 + 0.2 * rnd());
                    SvgPoint::new(rr * t.cos(), rr * t.sin())
                })
                .collect()
        };
        let a = Poly::from_svg_points(&star(48, 10.0));
        let mut b = Poly::from_svg_points(&star(36, 6.0));
        let capped = |d: Option<f64>, v: &NVector| -> f64 {
            let len2 = v.x * v.x + v.y * v.y;
            match d {
                Some(d) if d * d <= len2 => d,
                _ => len2.sqrt(),
            }
        };
        let (mut checked, mut hits) = (0, 0);
        for k in 0..60 {
            let t = std::f64::consts::TAU * k as f64 / 60.0;
            let dist = 9.0 + 9.0 * (k % 7) as f64 / 6.0;
            b.offset = (dist * t.cos(), dist * t.sin());
            let index = EdgeIndex::new(&a);
            for i in 0..a.len() + b.len() {
                let (p, q) = if i < a.len() {
                    (a.p(i), a.p((i + 1) % a.len()))
                } else {
                    let j = i - a.len();
                    (b.p((j + 1) % b.len()), b.p(j))
                };
                for scale in [1.0, -1.0, 3.5] {
                    let v = NVector { x: (q.x - p.x) * scale, y: (q.y - p.y) * scale, start: PtRef::A(0), end: PtRef::A(0) };
                    let full = capped(polygon_slide_distance(&a, &b, &v, true), &v);
                    let fast = capped(slide_distance_within(&index, &b, &v), &v);
                    assert_eq!(full.to_bits(), fast.to_bits(), "offset {k}, vector {i} x {scale}: {full} vs {fast}");
                    checked += 1;
                    if full < (v.x * v.x + v.y * v.y).sqrt() {
                        hits += 1;
                    }
                }
            }
        }
        assert!(hits > checked / 20, "too few slides stopped by A: {hits} of {checked}");
    }
}
