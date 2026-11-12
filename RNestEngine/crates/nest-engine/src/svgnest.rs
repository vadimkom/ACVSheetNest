//! Nesting engine: NFP-based candidate placement with configurable packing
//! direction (default: bottom-right to top-left).
//!
//! Improvements:
//! - true-shape outer contours (and holes) for collision/containment via
//!   `nest-clip` (clipper2) with geometric fallback,
//! - NFP candidates from feasible region: inner NFP(sheet, part) minus union of
//!   outer NFPs(placed, part), with vertex + edge sampling,
//! - spacing applied by inflating part outer for placement/NFP/collision,
//! - configurable placement direction in scoring/anchoring,
//! - rotations, mirrored copies, multi-start ordering, NFP caching.
//!
//! Part-in-hole: holes of placed parts can host smaller parts (inner NFP of the
//! hole as container, spacing-aware via inflated effective outlines).
//!
//! TODO: full GA, non-rectangular/holed sheets, common-line, merged length,
//! exact edge-slide/contact heuristics parity.

use crate::geom::{
    bounds_of, details_overlap, inside_sheet, normalize_detail, placements_too_close,
    translate_points, Bounds, PLACEMENT_NOISE,
};
use crate::{
    Config, DetailLoadInfo, NestError, NestResult, PartPlacement, PlacementDirection,
    SheetLoadInfo, SheetPlacement,
};
use nest_geom::SvgPoint;
use nest_io::RawDetail;
use nest_nfp::{no_fit_polygon, no_fit_polygon_rectangle, Nfp};
use rayon::prelude::*;
use std::collections::HashMap;
use std::sync::{Arc, RwLock};
use std::time::Instant;

/// Shape signature used in NFP cache keys (see `shape_sig`).
type ShapeSig = (usize, i64, i64, i64, i64, i64, u64);
type NfpKey = (ShapeSig, ShapeSig, bool);
/// NFPs computed so far, shared by the parallel attempts of one nest. An NFP
/// depends only on its key, so who computes it first does not change a result.
#[derive(Default)]
struct NfpCache(RwLock<HashMap<NfpKey, Option<Arc<Vec<Nfp>>>>>, PairMemo, Vec<Vec<Placed>>);

/// Verdicts of the exact pair check, shared by the attempts of one nest. A
/// verdict depends only on the two shapes and the offset between them (the
/// check runs in the placed part's own frame), and compaction asks about the
/// same neighbours at the same offsets over and over.
type PairKey = (usize, usize, i64, i64);
struct PairMemo(Vec<std::sync::Mutex<HashMap<PairKey, bool>>>);

impl Default for PairMemo {
    fn default() -> Self {
        PairMemo((0..64).map(|_| Default::default()).collect())
    }
}

impl PairMemo {
    fn get_or(&self, key: PairKey, make: impl FnOnce() -> bool) -> bool {
        let h = (key.0 as u64 ^ (key.1 as u64).rotate_left(17) ^ (key.2 as u64).wrapping_mul(0x9E3779B97F4A7C15) ^ (key.3 as u64).wrapping_mul(0xC2B2AE3D27D4EB4F)).wrapping_mul(0x9E3779B97F4A7C15);
        let shard = &self.0[(h >> 58) as usize];
        if let Some(&v) = shard.lock().unwrap().get(&key) {
            return v;
        }
        let v = make();
        shard.lock().unwrap().insert(key, v);
        v
    }
}

impl NfpCache {
    fn get(&self, a: &Nfp, b: &Nfp, inside: bool) -> Option<Arc<Vec<Nfp>>> {
        let key = nfp_key(a, b, inside);
        if let Some(hit) = self.0.read().unwrap().get(&key) {
            return hit.clone();
        }
        let _g = prof::g(4);
        if inside {
            let made = inner_nfp(a, b).map(Arc::new);
            return self.0.write().unwrap().entry(key).or_insert(made).clone();
        }
        let made = outer_nfp(a, b).map(Arc::new);
        let mut map = self.0.write().unwrap();
        if let Some(nfps) = &made {
            // B around A, mirrored through the origin, is A around B: one
            // computation serves both orders.
            let mirrored: Vec<Nfp> = nfps.iter().map(|n| Nfp::new(n.points.iter().map(|p| SvgPoint::new(-p.x, -p.y)).collect())).collect();
            map.entry(nfp_key(b, a, false)).or_insert_with(|| Some(Arc::new(mirrored)));
        }
        map.entry(key).or_insert(made).clone()
    }
}

/// Rotate a shape by `deg` about the origin and re-normalise its box to (0,0).
fn rotated_normalized(n: &Nfp, deg: f64) -> Nfp {
    if deg.abs() < 1e-12 {
        return n.clone();
    }
    let mut pts: Vec<SvgPoint> = crate::geom::rotate_points(&n.points, deg);
    if let Some(b) = bounds_of(&pts) {
        let (dx, dy) = (-b.min_x, -b.min_y);
        for p in pts.iter_mut() {
            p.x += dx;
            p.y += dy;
        }
    }
    Nfp::new(pts)
}

impl NfpCache {
    #[cfg(test)]
    fn map_total(&self) -> usize {
        self.0.read().unwrap().len()
    }

    /// Rotation-aware lookup. For rotations that are multiples of 90° the NFP
    /// of rotated shapes is the rotated NFP of the canonical pair:
    ///   NFP(A_r, B_s) = R_r · NFP(A_0, B_{s−r})
    /// (both translation- and rotation-covariant; box normalisation commutes
    /// with quarter turns). One orbit per canonical pair serves all four
    /// orientations of the same two drawings.
    fn get_rot(&self, a: &Nfp, b: &Nfp, inside: bool, ra: f64, rb: f64) -> Option<Arc<Vec<Nfp>>> {
        let quarter = |v: f64| -> Option<f64> {
            let m = (v / 90.0).round();
            ((v - m * 90.0).abs() < 1e-6).then_some(m * 90.0)
        };
        let (Some(ra), Some(rb)) = (quarter(ra), quarter(rb)) else {
            return self.get(a, b, inside);
        };
        let a0 = rotated_normalized(a, -ra);
        let bt = rotated_normalized(b, -ra);
        let base = self.get(&a0, &bt, inside)?;
        if ra.rem_euclid(360.0).abs() < 1e-9 {
            return Some(base);
        }
        let mut rotated: Vec<Nfp> = base
            .iter()
            .map(|n| Nfp::new(crate::geom::rotate_points(&n.points, ra)))
            .collect();
        if !inside {
            // Rotating the canonical region is exact up to the box-normalisation
            // offsets of the two variants; re-align to the target pair's box
            // convention (`A.bounds ⊕ −B.bounds`), which is exact by definition.
            align_to_box(&mut rotated, a, b);

            // The reversed order shares the orbit too:
            //   NFP(B_s, A_r) = −R_{−t} · NFP(A_0, B_t),  t = s − r,
            // stored under the reversed canonical pair (B_0, A_{−t}) so a later
            // lookup with the parts swapped is a cache hit.
            let t = rb - ra;
            let b0 = rotated_normalized(&bt, -t);
            let a_neg = rotated_normalized(&a0, -t);
            let mut rev_pts: Vec<Nfp> = base
                .iter()
                .map(|n| {
                    let negated: Vec<SvgPoint> = n.points.iter().map(|p| SvgPoint::new(-p.x, -p.y)).collect();
                    Nfp::new(crate::geom::rotate_points(&negated, -t))
                })
                .collect();
            align_to_box(&mut rev_pts, &b0, &a_neg);
            let _ = self
                .0
                .write()
                .unwrap()
                .entry(nfp_key(&b0, &a_neg, false))
                .or_insert_with(|| Some(Arc::new(rev_pts)));
        }
        Some(Arc::new(rotated))
    }
}

/// Translate an NFP region so its box starts where `A.bounds ⊕ −B.bounds`
/// starts (the convention the engine's placement math expects).
fn align_to_box(region: &mut [Nfp], a: &Nfp, b: &Nfp) {
    let (Some(ab), Some(bb)) = (bounds_of(&a.points), bounds_of(&b.points)) else { return; };
    let mut min_x = f64::INFINITY;
    let mut min_y = f64::INFINITY;
    for p in region.iter().flat_map(|n| n.points.iter()) {
        min_x = min_x.min(p.x);
        min_y = min_y.min(p.y);
    }
    if !min_x.is_finite() { return; }
    let (dx, dy) = (ab.min_x - bb.max_x - min_x, ab.min_y - bb.max_y - min_y);
    for n in region.iter_mut() {
        for p in n.points.iter_mut() {
            p.x += dx;
            p.y += dy;
        }
    }
}

fn shift_nfp(n: &mut Nfp, dx: f64, dy: f64) {
    for p in &mut n.points {
        p.x += dx;
        p.y += dy;
    }
    for c in &mut n.children {
        shift_nfp(c, dx, dy);
    }
}

/// Analytic interior NFP for a disc inside a disc: centre offsets within
/// `rA - rB`. Used before the orbit (see `inner_nfp`).
fn inner_circle_nfp(a: &Nfp, b: &Nfp) -> Option<Nfp> {
    let (ca, ra) = circle_of(&a.points)?;
    let (cb, rb) = circle_of(&b.points)?;
    if rb > ra + 1e-9 { return None; }
    let (cx, cy) = (ca.x - cb.x, ca.y - cb.y);
    // Equal radii leave a single valid centre: a tiny region around it, so the
    // candidate sampler still produces the point.
    let r = (ra - rb).max(1e-3);
    let n = 64;
    let pts = (0..n)
        .map(|k| {
            let ang = std::f64::consts::TAU * k as f64 / n as f64;
            SvgPoint::new(cx + r * ang.cos(), cy + r * ang.sin())
        })
        .collect();
    Some(Nfp::new(pts))
}

/// Positions of B's origin inside the container A. The orbit traces B's first
/// vertex (the SVGnest convention) while the engine places B by the origin of
/// its box; inside a rectangle the NFP comes from the boxes and is by the
/// origin already. Told apart by trying a vertex: B must lie inside A there.
fn inner_nfp(a: &Nfp, b: &Nfp) -> Option<Vec<Nfp>> {
    // Round container: a disc of radius rB fits inside disc rA exactly when its
    // centre stays within rA - rB of the container centre. The orbiter cannot
    // produce this for equally sized discs (its start-point search aborts on
    // identical shapes), which used to make a circular part unplaceable in a
    // circular hole of the same size.
    if let Some(nfp) = inner_circle_nfp(a, b) {
        return Some(vec![nfp]);
    }
    let mut nfps = no_fit_polygon(a, b, true, false)?;
    let by_origin = nfps.first().and_then(|n| n.points.first()).is_some_and(|at| {
        b.points.iter().all(|p| {
            nest_geom::point_in_polygon(SvgPoint::new(p.x + at.x, p.y + at.y), &a.points, (0.0, 0.0)) != Some(false)
        })
    });
    if let Some(first) = b.points.first().copied().filter(|_| !by_origin) {
        for n in &mut nfps {
            shift_nfp(n, -first.x, -first.y);
        }
    }
    Some(nfps)
}

/// Detect a contour that is a polygon approximation of a circle: every vertex
/// lies within a small tolerance of the mean radius around the box centre.
/// Returns the centre and radius.
fn circle_of(points: &[SvgPoint]) -> Option<(SvgPoint, f64)> {
    if points.len() < 8 || points.len() > 4096 { return None; }
    let b = bounds_of(points)?;
    let (w, h) = (b.width(), b.height());
    if w <= 0.0 || h <= 0.0 || (w - h).abs() > 1e-3 * w.max(h) { return None; }
    let c = SvgPoint::new(b.min_x + w / 2.0, b.min_y + h / 2.0);
    let mut r_sum = 0.0f64;
    for p in points {
        r_sum += ((p.x - c.x).powi(2) + (p.y - c.y).powi(2)).sqrt();
    }
    let r = r_sum / points.len() as f64;
    if r <= 0.0 { return None; }
    let tol = (1e-3 * r).max(1e-6);
    let circular = points.iter().all(|p| {
        let d = ((p.x - c.x).powi(2) + (p.y - c.y).powi(2)).sqrt();
        (d - r).abs() <= tol
    });
    circular.then_some((c, r))
}

/// Positions of B's origin at which B touches or overlaps A (A ⊕ −B).
///
/// The orbiting NFP is tried first: it is the faster one and its vertices are
/// exact. It traces B's first vertex, so it is moved to where the positions of
/// B's origin must lie, a box known exactly whatever the shapes: from
/// A.min − B.max to A.max − B.min. An orbit that failed, or whose result has
/// not that box or less area than a Minkowski sum can have, is replaced by the
/// sum computed with clipper, which is slower but cannot go astray.
fn outer_nfp(a: &Nfp, b: &Nfp) -> Option<Vec<Nfp>> {
    let (ab, bb) = (bounds_of(&a.points)?, bounds_of(&b.points)?);

    // Round-shape fast paths: the orbiter is O(n*m) on the edges, which hurt
    // drawings whose circles arrive as 200-vertex polygons. A disc offsets the
    // other outline uniformly, so one round clipper offset gives the exact NFP:
    //   NFP = A ⊕ (−B)      (translations of B that touch/overlap A)
    //   B = disc(cB, rB)  →  inflate(A, rB) − cB
    //   A = disc(cA, rA)  →  cA − inflate(B, rA)
    // Both are then shifted to the box convention used by the orbiter
    // (`A.bounds ⊕ −B.bounds`).
    let shift_to_box = |contours: &mut Vec<Vec<SvgPoint>>| {
        let mut min_x = f64::INFINITY;
        let mut min_y = f64::INFINITY;
        for p in contours.iter().flatten() {
            min_x = min_x.min(p.x);
            min_y = min_y.min(p.y);
        }
        if !min_x.is_finite() { return; }
        let (dx, dy) = (ab.min_x - bb.max_x - min_x, ab.min_y - bb.max_y - min_y);
        for c in contours.iter_mut() {
            for p in c.iter_mut() { p.x += dx; p.y += dy; }
        }
    };
    if let Some((cb, rb)) = circle_of(&b.points) {
        let mut grown = nest_clip::inflate_paths_round(&[a.points.clone()], rb);
        grown.retain(|c| c.len() >= 3);
        if !grown.is_empty() {
            for c in grown.iter_mut() {
                for p in c.iter_mut() { p.x -= cb.x; p.y -= cb.y; }
            }
            shift_to_box(&mut grown);
            return Some(grown.into_iter().map(Nfp::new).collect());
        }
    }
    if let Some((ca, ra)) = circle_of(&a.points) {
        let mut grown = nest_clip::inflate_paths_round(&[b.points.clone()], ra);
        grown.retain(|c| c.len() >= 3);
        if !grown.is_empty() {
            for c in grown.iter_mut() {
                for p in c.iter_mut() { p.x = ca.x - p.x; p.y = ca.y - p.y; }
            }
            shift_to_box(&mut grown);
            return Some(grown.into_iter().map(Nfp::new).collect());
        }
    }
    let orbited = no_fit_polygon(a, b, false, false).and_then(|mut nfps| {
        let (mut min_x, mut min_y, mut max_x, mut max_y) = (f64::INFINITY, f64::INFINITY, f64::NEG_INFINITY, f64::NEG_INFINITY);
        for p in nfps.iter().flat_map(|n| n.points.iter()) {
            min_x = min_x.min(p.x);
            min_y = min_y.min(p.y);
            max_x = max_x.max(p.x);
            max_y = max_y.max(p.y);
        }
        let tol = 1e-6 * (ab.width() + ab.height() + bb.width() + bb.height()).max(1.0);
        let sized = ((max_x - min_x) - (ab.width() + bb.width())).abs() <= tol && ((max_y - min_y) - (ab.height() + bb.height())).abs() <= tol;
        let area = nfps.iter().map(|n| nest_geom::polygon_area(&n.points).abs()).fold(0.0, f64::max);
        let least = (nest_geom::polygon_area(&a.points).abs().sqrt() + nest_geom::polygon_area(&b.points).abs().sqrt()).powi(2);
        if !sized || area < least * (1.0 - 1e-9) {
            return None;
        }
        let (dx, dy) = (ab.min_x - bb.max_x - min_x, ab.min_y - bb.max_y - min_y);
        for n in &mut nfps {
            shift_nfp(n, dx, dy);
        }
        Some(nfps)
    });
    orbited.or_else(|| {
        let _g = prof::g(7);
        let polys = nest_clip::minkowski_nfp(&a.points, &b.points, 1e4);
        (!polys.is_empty()).then(|| polys.into_iter().map(Nfp::new).collect())
    })
}

pub fn svgnest_nest(
    sheets: &[SheetLoadInfo],
    parts: &[DetailLoadInfo],
    catalogue: &impl Fn(&str) -> RawDetail,
    config: Config,
) -> Result<NestResult, NestError> {
    if sheets.is_empty() {
        return Err(NestError::NoSheets);
    }
    if parts
        .iter()
        .all(|p| p.quantity == 0 && p.mirrored_quantity == 0)
    {
        return Err(NestError::NoParts);
    }

    let t0 = Instant::now();
    let phases = std::env::var_os("NEST_PHASES").is_some();
    let mut expanded = expand_parts(parts, catalogue, &config);
    drop_useless_holes(&mut expanded);
    let expanded = expanded;
    let t_expand = t0.elapsed();
    if expanded.is_empty() {
        return Err(NestError::NoParts);
    }

    let mut attempts = build_attempts(&expanded, &config);
    // Mixed-orientation attempts: on the stock that will actually hold the job,
    // long thin parts (strips, rails) stand along the stock's long side while
    // wide panels stay free to lie down — the two patterns complement each
    // other, which neither an all-free nor an all-locked attempt finds.
    if sheets
        .iter()
        .max_by(|a, b| (a.width * a.height).partial_cmp(&(b.width * b.height)).unwrap())
        .is_some()
    {
        let first_order = attempts.first().cloned();
        let aspect_locked = |tall: bool| -> Option<Gene> {
            let order = first_order.clone()?;
            let gene: Gene = order
                .iter()
                .map(|&(i, _)| {
                    let variants = &expanded[i].variants;
                    if variants.len() < 2 {
                        return (i, None);
                    }
                    let b = &variants[0].bounds;
                    let (long, short) = (b.width().max(b.height()), b.width().min(b.height()));
                    if short <= 0.0 || long / short < 2.0 {
                        return (i, None);
                    }
                    let mut best = 0;
                    for k in 1..variants.len() {
                        let v = &variants[k];
                        let vb = &v.bounds;
                        let (vl, vs) = (vb.width().max(vb.height()), vb.width().min(vb.height()));
                        let vb2 = &variants[best].bounds;
                        let (bl, bs) = (vb2.width().max(vb2.height()), vb2.width().min(vb2.height()));
                        let score = |h: f64, w: f64| if tall { h } else { w };
                        if score(vl, vs) > score(bl, bs) + 1e-9 {
                            best = k;
                        }
                    }
                    (i, Some(best))
                })
                .collect();
            Some(gene)
        };
        for tall in [true, false] {
            if let Some(gene) = aspect_locked(tall) {
                if !attempts.contains(&gene) {
                    attempts.push(gene);
                }
            }
        }
    }
    let instances = sheet_instances(sheets);
    let orders = sheet_orders(&instances);
    // Every (part order, stock order) pair is an independent attempt.
    // Row-first packing is the base. A column-first scan pays off when the job
    // is mostly long, narrow parts (strips, rails): they stack tighter standing
    // on a tall stock. It is not free, so it only runs for such jobs, and only
    // for the first genes plus the best row layout afterwards.
    let thin_job = {
        let mut aspects: Vec<f64> = expanded
            .iter()
            .filter_map(|p| {
                let b = &p.variants.first()?.bounds;
                let (long, short) = (b.width().max(b.height()), b.width().min(b.height()));
                (short > 1e-9).then_some(long / short)
            })
            .collect();
        aspects.sort_by(|a, b| a.partial_cmp(b).unwrap());
        aspects.get(aspects.len() / 2).copied().unwrap_or(1.0) >= 2.0
    };
    let combos: Vec<(Gene, Vec<usize>, bool)> = attempts
        .iter()
        .enumerate()
        .flat_map(|(gi, gene)| {
            orders.iter().flat_map(move |order| {
                let modes: &[bool] = if gi < 2 { &[false, true] } else { &[false] };
                modes.iter().map(move |&x_first| (gene.clone(), order.clone(), x_first))
            })
        })
        .collect();
    // The attempts are independent: run them in parallel over one NFP cache and
    // fold them in attempt order, so the result never depends on timing.
    let mut nfp_cache = NfpCache::default();
    nfp_cache.2 = sheets.iter().map(|sheet| sheet_fixtures(sheet, &config)).collect();
    let nfp_cache = nfp_cache;
    warm_nfp_cache(&expanded, &nfp_cache);
    let t_warm = t0.elapsed();
    // Cheap passes first: re-insertion (the most expensive step) runs only on
    // the winning gene at the end, not inside every attempt.
    let candidates: Vec<(Gene, Vec<usize>, bool, NestResult)> = {
        let results: Vec<NestResult> = combos
            .par_iter()
            .map(|(gene, order, x_first)| place_once(sheets, &expanded, gene, &config, &nfp_cache, false, order, *x_first))
            .collect();
        combos
            .into_iter()
            .zip(results)
            .map(|((gene, order, x_first), result)| (gene, order, x_first, result))
            .collect()
    };
    // Second chance: re-nest the best row-first layout column-first.
    let row_best = candidates
        .iter()
        .filter(|(_, _, x_first, _)| !*x_first)
        .min_by(|a, b| {
            if is_better(&a.3, &b.3) { std::cmp::Ordering::Less }
            else if is_better(&b.3, &a.3) { std::cmp::Ordering::Greater }
            else { std::cmp::Ordering::Equal }
        })
        .map(|(gene, order, _, _)| (gene.clone(), order.clone()));
    let mut candidates = candidates;
    if thin_job {
        if let Some((gene, order)) = row_best {
            let result = place_once(sheets, &expanded, &gene, &config, &nfp_cache, false, &order, true);
            candidates.push((gene, order, true, result));
        }
    }
    let mut best: Option<(Gene, Vec<usize>, bool, NestResult)> = None;
    let mut best_idx = 0usize;
    if std::env::var_os("NEST_DBG_ATT").is_some() {
        for (i, (gene, order, xf, r)) in candidates.iter().enumerate() {
            let locks: String = gene.iter().map(|(_, v)| match v { None => '.', Some(_) => 'L' }).collect();
            let util: f64 = r.sheets.iter().map(|s| {
                let a: f64 = s.placements.iter().map(|p| {
                    let o = nest_geom::polygon_area(&p.local_outer).abs();
                    let h: f64 = p.local_holes.iter().map(|c| nest_geom::polygon_area(c).abs()).sum();
                    (o - h).max(0.0)
                }).sum();
                if s.sheet_width * s.sheet_height > 0.0 { a / (s.sheet_width * s.sheet_height) } else { 0.0 }
            }).sum::<f64>();
            let bbox_area: f64 = r.sheets.iter().map(|s| {
                let (mut nx, mut ny, mut xx, mut xy) = (f64::INFINITY, f64::INFINITY, f64::NEG_INFINITY, f64::NEG_INFINITY);
                for p in &s.placements {
                    if let Some(b) = bounds_of(&p.local_outer) {
                        nx = nx.min(b.min_x + p.x); ny = ny.min(b.min_y + p.y);
                        xx = xx.max(b.max_x + p.x); xy = xy.max(b.max_y + p.y);
                    }
                }
                if nx.is_finite() { (xx - nx) * (xy - ny) } else { 0.0 }
            }).sum();
            eprintln!("ATT #{i} sheet0={:?} locks={} sheets={} util={:.3} bbox={:.0} fit={:.0} unpl={} xf={}", order.first(), locks, r.sheets.len(), util, bbox_area, r.fitness, r.unplaced.len(), xf);
        }
    }
    for (i, (gene, order, x_first, candidate)) in candidates.into_iter().enumerate() {
        best = Some(match best {
            None => { best_idx = i; (gene, order, x_first, candidate) }
            Some((bg, bo, bx, prev)) => {
                if is_better(&candidate, &prev) { best_idx = i; (gene, order, x_first, candidate) } else { (bg, bo, bx, prev) }
            }
        });
    }
    if phases { eprintln!("ATTEMPTS winner #{best_idx} of {}", attempts.len() * orders.len()); }
    // GA search (port of `SvgNest.cs` population/generation loop +
    // `GeneticAlgorithm/Procreant.cs`), runs additionally when configured.
    if config.generations > 1 || config.populations > 1 {
        if let Some((ga_gene, ga_best)) = ga_nest(sheets, &expanded, &config, &nfp_cache, &orders[0]) {
            best = Some(match best {
                None => (ga_gene, orders[0].clone(), false, ga_best),
                Some((bg, bo, bx, prev)) => {
                    if is_better(&ga_best, &prev) { (ga_gene, orders[0].clone(), false, ga_best) } else { (bg, bo, bx, prev) }
                }
            });
        }
    }

    // Re-insertion descent on the winner only (same result the per-attempt pass
    // produced, at a fraction of the cost).
    let t_attempts = t0.elapsed();
    let mut best = best.expect("at least one attempt");
    if config.reinsertion_passes > 0 && !cancelled(&config) {
        let refined = place_once(sheets, &expanded, &best.0, &config, &nfp_cache, true, &best.1, best.2);
        if is_better(&refined, &best.3) {
            best.3 = refined;
        }
    }
    let t_refine = t0.elapsed();
    if phases {
        eprintln!("PHASES expand {:?} warm {:?} attempts {:?} refine {:?} total {:?}",
            t_expand, t_warm - t_expand, t_attempts - t_warm, t_refine - t_attempts, t_refine);
    }
    if cancelled(&config) {
        return Err(NestError::Cancelled);
    }
    let mut result = best.3;
    result.duration_ms = t0.elapsed().as_millis() as u64;
    if std::env::var_os("NEST_PROFILE").is_some() { prof::dump(); }
    result.placement_type = config.placement_type;
    Ok(result)
}

/// True once the caller asked the running nest to stop.
fn cancelled(config: &Config) -> bool {
    config.cancel.as_ref().is_some_and(|c| c.load(std::sync::atomic::Ordering::Relaxed))
}

/// A thing that lies on the sheet before the nest and does not move, at the
/// place of `outer` (sheet coordinates). `spacing` grows its outline as a
/// part's is grown; a ghost takes 0 and so keeps parts half a gap away, the
/// way the sheet's edge does.
fn fixture(name: &str, outer: &[SvgPoint], holes: &[Vec<SvgPoint>], spacing: f64, ghost: bool, config: &Config) -> Option<Placed> {
    if outer.len() < 3 {
        return None;
    }
    let factor = nest_clip::scale_factor(config.clipper_scale);
    let eff = effective_outline(outer, spacing, config.clipper_scale);
    let b = bounds_of(&eff)?;
    // The origin on the position grid, at or below the box: the pair check
    // remembers its verdicts by grid offsets.
    let q = 1.0 / factor;
    let (x, y) = (snap_to_clip((b.min_x / q).floor() * q, factor), snap_to_clip((b.min_y / q).floor() * q, factor));
    let local = |points: &[SvgPoint]| translate_points(points, -x, -y);
    let detail = RawDetail { bends: Vec::new(), name: name.to_owned(), outer: local(outer), children: holes.iter().map(|h| local(h)).collect() };
    let hole_bounds = detail.children.iter().map(|c| bounds_of(c)).collect();
    let bounds = Bounds { min_x: 0.0, min_y: 0.0, max_x: b.max_x - x, max_y: b.max_y - y };
    if bounds.width() <= 0.0 || bounds.height() <= 0.0 {
        return None;
    }
    Some(Placed {
        id: usize::MAX,
        name: name.to_owned(),
        is_priority: false,
        rotation: 0.0,
        _mirrored: false,
        x,
        y,
        detail: Arc::new(detail),
        hole_bounds: Arc::new(hole_bounds),
        effective_outer: local(&eff),
        bounds,
        all_holes: None,
        fixed: true,
        ghost,
    })
}

/// What lies on a sheet before the nest: the room outside an offcut's outline
/// (its box less the outline), the sheet's holes, and the parts already there.
fn sheet_fixtures(sheet: &SheetLoadInfo, config: &Config) -> Vec<Placed> {
    let factor = nest_clip::scale_factor(config.clipper_scale);
    let mut out = Vec::new();
    if sheet.outline.len() >= 3 {
        let rect = vec![
            SvgPoint::new(0.0, 0.0), SvgPoint::new(sheet.width, 0.0),
            SvgPoint::new(sheet.width, sheet.height), SvgPoint::new(0.0, sheet.height),
        ];
        for piece in nest_clip::difference_paths_at_scale(&[rect], &[sheet.outline.clone()], factor) {
            // Slivers thinner than the grid are clipping noise, not sheet shape.
            let thick = bounds_of(&piece).is_some_and(|b| b.width() > 2.0 / factor && b.height() > 2.0 / factor);
            if thick && nest_geom::polygon_area(&piece).abs() > 4.0 / (factor * factor) {
                out.extend(fixture("", &piece, &[], 0.0, true, config));
            }
        }
    }
    for hole in &sheet.holes {
        out.extend(fixture("", hole, &[], 0.0, true, config));
    }
    for part in &sheet.occupied {
        out.extend(fixture(&part.name, &part.outer, &part.holes, config.spacing, false, config));
    }
    out
}

/// Holes no part of the job could lie in are dropped from the geometry the
/// placement works with (they stay in the result): a perforated front carries
/// a thousand of them, and each is a contour to test and to copy.
fn drop_useless_holes(parts: &mut [ExpandedPart]) {
    let mut sizes: Vec<(f64, f64)> = parts.iter().flat_map(|p| p.variants.iter().map(|v| (v.bounds.width(), v.bounds.height()))).collect();
    sizes.sort_by(|a, b| a.partial_cmp(b).unwrap());
    sizes.dedup();
    let useful = |hb: &Option<Bounds>| hb.is_some_and(|hb| sizes.iter().any(|&(w, h)| w <= hb.width() + 1e-6 && h <= hb.height() + 1e-6));
    // Copies of a row share one detail; they must share the thinned one too.
    type Thinned = (Arc<RawDetail>, Arc<Vec<Option<Bounds>>>, Arc<Vec<Vec<SvgPoint>>>);
    let mut thinned: HashMap<usize, Option<Thinned>> = HashMap::new();
    for part in parts.iter_mut() {
        for var in part.variants.iter_mut() {
            let key = Arc::as_ptr(&var.detail) as usize;
            let made = thinned.entry(key).or_insert_with(|| {
                if var.hole_bounds.iter().all(|hb| useful(hb)) {
                    return None;
                }
                let keep: Vec<usize> = (0..var.detail.children.len()).filter(|&i| useful(&var.hole_bounds[i])).collect();
                let detail = RawDetail {
                    bends: Vec::new(),
                    name: var.detail.name.clone(),
                    outer: var.detail.outer.clone(),
                    children: keep.iter().map(|&i| var.detail.children[i].clone()).collect(),
                };
                let bounds = keep.iter().map(|&i| var.hole_bounds[i]).collect();
                Some((Arc::new(detail), Arc::new(bounds), Arc::new(var.detail.children.clone())))
            });
            if let Some((detail, bounds, all)) = made {
                var.detail = detail.clone();
                var.hole_bounds = bounds.clone();
                var.all_holes = Some(all.clone());
            }
        }
    }
}

#[derive(Debug, Clone)]
struct PartVariant {
    rotation: f64,
    mirrored: bool,
    /// Original normalized detail (for output/collision material shape reference).
    /// Shared: a drawing's holes can run to tens of thousands of points, and
    /// candidates/placements must not copy them.
    detail: Arc<RawDetail>,
    /// Bounds of each hole of `detail` (same frame), for cheap rejects.
    hole_bounds: Arc<Vec<Option<Bounds>>>,
    /// Effective outer used for spacing/NFP/collision boundary (inflated by spacing/2 if needed).
    effective_outer: Vec<SvgPoint>,
    bounds: Bounds,
    area: f64,
    nfp_outer: Nfp,
    /// Every hole of the part, when `detail` keeps only the holes another part
    /// of the job could lie in (`None` = `detail` has them all).
    all_holes: Option<Arc<Vec<Vec<SvgPoint>>>>,
}

#[derive(Debug, Clone)]
struct ExpandedPart {
    id: usize,
    name: String,
    is_priority: bool,
    /// Placement rank (lower first), from `DetailLoadInfo.priority`.
    rank: i32,
    /// A mirrored twin (a copy of `mirrored_quantity`).
    mirrored: bool,
    variants: Vec<PartVariant>,
}

#[derive(Debug, Clone)]
struct Placed {
    id: usize,
    name: String,
    is_priority: bool,
    rotation: f64,
    _mirrored: bool,
    x: f64,
    y: f64,
    detail: Arc<RawDetail>,
    hole_bounds: Arc<Vec<Option<Bounds>>>,
    effective_outer: Vec<SvgPoint>,
    bounds: Bounds,
    all_holes: Option<Arc<Vec<Vec<SvgPoint>>>>,
    /// Stays where it is: a part that already lay on the sheet, or a ghost.
    fixed: bool,
    /// Not a part at all but a piece of "no sheet here": the room between an
    /// offcut's outline and its box, or a hole of the sheet.
    ghost: bool,
}

impl Placed {
    fn of(part: &ExpandedPart, var: &PartVariant, x: f64, y: f64) -> Placed {
        Placed {
            id: part.id,
            name: part.name.clone(),
            is_priority: part.is_priority,
            rotation: var.rotation,
            _mirrored: var.mirrored,
            x,
            y,
            detail: var.detail.clone(),
            hole_bounds: var.hole_bounds.clone(),
            effective_outer: var.effective_outer.clone(),
            bounds: var.bounds,
            all_holes: var.all_holes.clone(),
            fixed: false,
            ghost: false,
        }
    }
    /// Every hole of the part, in its own frame.
    fn holes(&self) -> &[Vec<SvgPoint>] {
        self.all_holes.as_ref().map(|h| h.as_slice()).unwrap_or(&self.detail.children)
    }
    fn world_outer(&self) -> Vec<SvgPoint> {
        translate_points(&self.detail.outer, self.x, self.y)
    }
}

/// One orientation of a part, moved so the box of its spaced outline starts at
/// the origin. `strict` = refuse a degenerate outline instead of keeping it.
fn make_variant(source: &RawDetail, rot: f64, mirrored: bool, config: &Config, strict: bool) -> Option<PartVariant> {
    let zero = Bounds { min_x: 0.0, min_y: 0.0, max_x: 0.0, max_y: 0.0 };
    let detail = normalize_detail(source, rot, mirrored);
    if strict && bounds_of(&detail.outer).is_none() {
        return None;
    }
    let eff = effective_outline(&detail.outer, config.spacing, config.clipper_scale);
    let b = bounds_of(&eff).unwrap_or(zero);
    let (dx, dy) = (-b.min_x, -b.min_y);
    let detail = RawDetail {
        name: detail.name,
        outer: translate_points(&detail.outer, dx, dy),
        children: detail.children.iter().map(|c| translate_points(c, dx, dy)).collect(),
        bends: detail.bends.iter().map(|l| translate_points(l, dx, dy)).collect(),
    };
    let eff = translate_points(&eff, dx, dy);
    let bounds = bounds_of(&eff).unwrap_or(zero);
    let area = nest_geom::polygon_area(&eff).abs();
    let hole_bounds = detail.children.iter().map(|c| bounds_of(c)).collect();
    Some(PartVariant {
        rotation: rot,
        mirrored,
        detail: Arc::new(detail),
        hole_bounds: Arc::new(hole_bounds),
        nfp_outer: Nfp::new(eff.clone()),
        effective_outer: eff,
        bounds,
        area,
        all_holes: None,
    })
}

/// True when two variants are the same shape (a mirror that changes nothing),
/// comparing contours as point sets so point order/orientation do not matter.
fn same_variant_geometry(a: &PartVariant, b: &PartVariant) -> bool {
    fn sig(points: &[SvgPoint]) -> Vec<(i64, i64)> {
        let mut v: Vec<(i64, i64)> = points
            .iter()
            .map(|p| ((p.x * 1e6).round() as i64, (p.y * 1e6).round() as i64))
            .collect();
        v.sort_unstable();
        v
    }
    sig(&a.detail.outer) == sig(&b.detail.outer)
        && a.detail.children.len() == b.detail.children.len()
        && a.detail.children.iter().zip(b.detail.children.iter()).all(|(ha, hb)| sig(ha) == sig(hb))
}

pub(crate) fn effective_outline(outer: &[SvgPoint], spacing: f64, clip_scale: f64) -> Vec<SvgPoint> {
    if spacing.abs() < 1e-9 || outer.len() < 3 {
        return outer.to_vec();
    }
    // Inflate by spacing/2 so distance between two parts becomes >= spacing.
    let factor = nest_clip::scale_factor(clip_scale);
    let inflated = nest_clip::inflate_paths_at_scale(&[outer.to_vec()], spacing / 2.0, factor);
    inflated
        .into_iter()
        .max_by(|a, b| nest_geom::polygon_area(a).abs().partial_cmp(&nest_geom::polygon_area(b).abs()).unwrap())
        .unwrap_or_else(|| outer.to_vec())
}

fn expand_parts(
    parts: &[DetailLoadInfo],
    catalogue: &impl Fn(&str) -> RawDetail,
    config: &Config,
) -> Vec<ExpandedPart> {
    let mut out = Vec::new();
    let mut next_id = 0usize;

    for part in parts {
        let source = catalogue(&part.name);
        let total = part.quantity.saturating_add(part.mirrored_quantity);
        if total == 0 {
            continue;
        }
        for copy in 0..total {
            let mirrored = copy >= part.quantity;
            let id = next_id;
            next_id += 1;
            let rotations = rotations_for(part, config);
            let mut variants = Vec::new();
            // Copies of one row share their variants' geometry (Arc inside).
            if copy > 0 && copy != part.quantity {
                let variants = out.last().map(|p: &ExpandedPart| p.variants.clone()).unwrap_or_default();
                out.push(ExpandedPart { id, name: part.name.clone(), is_priority: part.is_priority, rank: part.priority, mirrored, variants });
                continue;
            }
            for &rot in &rotations {
                variants.extend(make_variant(&source, rot, mirrored, config, true));
                // "Mirror if needed": the same copy may also be nested flipped
                // when that packs better. Skipped for shapes whose mirror is the
                // shape itself (no benefit, double the candidates).
                if part.mirror_allowed && !mirrored {
                    if let (Some(front), Some(flipped)) = (
                        make_variant(&source, rot, false, config, true),
                        make_variant(&source, rot, true, config, true),
                    ) {
                        if !same_variant_geometry(&front, &flipped) {
                            variants.push(flipped);
                        }
                    }
                }
            }
            if variants.is_empty() {
                variants.extend(make_variant(&source, 0.0, mirrored, config, false));
            }
            out.push(ExpandedPart { id, name: part.name.clone(), is_priority: part.is_priority, rank: part.priority, mirrored, variants });
        }
    }
    out
}

pub(crate) fn rotations_for(part: &DetailLoadInfo, config: &Config) -> Vec<f64> {
    if !part.allowed_rotations.is_empty() {
        return part.allowed_rotations.clone();
    }
    let steps = config.rotations.max(1) as usize;
    if steps <= 1 {
        return vec![0.0];
    }
    (0..steps).map(|i| 360.0 * (i as f64) / (steps as f64)).collect()
}

/// A GA gene: placement order with a per-part variant lock (`None` = let the
/// placement choose the best variant). `(usize::MAX)`-free; variant index is
/// bounded by the part's `variants.len()`.
pub type Gene = Vec<(usize, Option<usize>)>;

fn gene_from_order(order: &[usize]) -> Gene {
    order.iter().map(|&i| (i, None)).collect()
}

fn build_attempts(parts: &[ExpandedPart], config: &Config) -> Vec<Gene> {
    let base: Vec<usize> = (0..parts.len()).collect();
    let mut orders = Vec::new();

    let mut by_area = base.clone();
    by_area.sort_by(|&a, &b| {
        parts[b].variants[0].area.partial_cmp(&parts[a].variants[0].area).unwrap().then(a.cmp(&b))
    });
    orders.push(by_area);

    let mut by_side = base.clone();
    by_side.sort_by(|&a, &b| {
        let ma = parts[a].variants[0].bounds.width().max(parts[a].variants[0].bounds.height());
        let mb = parts[b].variants[0].bounds.width().max(parts[b].variants[0].bounds.height());
        mb.partial_cmp(&ma).unwrap().then(a.cmp(&b))
    });
    if orders.iter().all(|x| x != &by_side) { orders.push(by_side); }

    // by width (descending)
    let mut by_w = base.clone();
    by_w.sort_by(|&a,&b| parts[b].variants[0].bounds.width().partial_cmp(&parts[a].variants[0].bounds.width()).unwrap().then(a.cmp(&b)));
    if orders.iter().all(|x| x != &by_w) { orders.push(by_w); }
    // by height (descending)
    let mut by_h = base.clone();
    by_h.sort_by(|&a,&b| parts[b].variants[0].bounds.height().partial_cmp(&parts[a].variants[0].bounds.height()).unwrap().then(a.cmp(&b)));
    if orders.iter().all(|x| x != &by_h) { orders.push(by_h); }
    // by area ascending (small first) - can help fill gaps
    let mut by_area_asc = base.clone();
    by_area_asc.sort_by(|&a,&b| parts[a].variants[0].area.partial_cmp(&parts[b].variants[0].area).unwrap().then(a.cmp(&b)));
    if orders.iter().all(|x| x != &by_area_asc) { orders.push(by_area_asc); }

    if orders.iter().all(|x| x != &base) { orders.push(base); }
    let draft = config.populations == 0 && config.generations == 0;
    if draft { orders.truncate(2); }
    let mut genes: Vec<Gene> = orders.iter().map(|o| gene_from_order(o)).collect();

    // Orientation-locked attempts: every part standing, then every part lying.
    // Left free, each part takes the orientation that grows the layout's box
    // least, so short parts lie down and a row holds fewer of them (the door
    // job: panels 290 x 425 lay on their side above the strips and one of 16
    // stayed off; standing, four go in a row).
    if parts.iter().any(|p| p.variants.len() > 1) {
        let locked = |i: usize, tall: bool| -> Option<usize> {
            let v = &parts[i].variants;
            if v.len() < 2 { return None; }
            let side = |k: usize| if tall { v[k].bounds.height() } else { v[k].bounds.width() };
            let mut best = 0;
            for k in 1..v.len() {
                if side(k) > side(best) + 1e-9 { best = k; }
            }
            Some(best)
        };
        for order in orders.iter().take(if draft { 1 } else { 2 }) {
            for tall in [true, false] {
                let gene: Gene = order.iter().map(|&i| (i, locked(i, tall))).collect();
                if !genes.contains(&gene) { genes.push(gene); }
            }
        }
    }
    genes
}

/// Snap a coordinate to the clipper integer grid (`1/factor` units). Stored
/// positions then match exactly what the boolean validity checks quantize to,
/// so sub-grid overlaps can no longer slip through (they used to show up as
/// ~half-unit slivers between parts after compaction slides).
fn snap_to_clip(x: f64, factor: f64) -> f64 {
    if !(factor.is_finite()) || factor <= 0.0 { return x; }
    let q = 1.0 / factor;
    (x / q).round() * q
}

/// Deterministic PRNG (xorshift64*) seeded per job — reproducible GA runs.
struct Rng(u64);
impl Rng {
    fn new(seed: u64) -> Self { Rng(seed | 1) }
    fn next_u64(&mut self) -> u64 {
        let mut x = self.0;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        self.0 = x;
        x.wrapping_mul(0x2545F4914F6CDD1D)
    }
    fn next_f64(&mut self) -> f64 { (self.next_u64() >> 11) as f64 / (1u64 << 53) as f64 }
    fn below(&mut self, n: usize) -> usize { if n == 0 { 0 } else { (self.next_u64() % n as u64) as usize } }
}

/// Ranking key matching `pick_better`: fewer unplaced first, then fewer sheets,
/// then lower fitness (lower is better).
/// Used bounding-box area of a layout (summed over sheets): the material the
/// job actually occupies. Ranking by it means a denser nest wins even when the
/// reference-style fitness (which weights width heavily) prefers a taller one.
fn layout_bbox_area(r: &NestResult) -> f64 {
    r.sheets
        .iter()
        .map(|s| {
            let (mut min_x, mut min_y) = (f64::INFINITY, f64::INFINITY);
            let (mut max_x, mut max_y) = (f64::NEG_INFINITY, f64::NEG_INFINITY);
            for p in &s.placements {
                if let Some(b) = bounds_of(&p.local_outer) {
                    min_x = min_x.min(b.min_x + p.x);
                    min_y = min_y.min(b.min_y + p.y);
                    max_x = max_x.max(b.max_x + p.x);
                    max_y = max_y.max(b.max_y + p.y);
                }
            }
            if min_x.is_finite() { (max_x - min_x) * (max_y - min_y) } else { 0.0 }
        })
        .sum()
}

/// Material actually nested (outer minus holes), the counterweight to the
/// bounding box: without it, "place one small part and leave the big one off"
/// would look denser than "place the big one".
fn placed_area(r: &NestResult) -> f64 {
    r.sheets
        .iter()
        .flat_map(|s| s.placements.iter())
        .map(|p| {
            let outer = nest_geom::polygon_area(&p.local_outer).abs();
            let holes: f64 = p.local_holes.iter().map(|c| nest_geom::polygon_area(c).abs()).sum();
            (outer - holes).max(0.0)
        })
        .sum()
}

/// Layout ranking (lower is better): every part placed, then fewer stock
/// sheets, then the most material nested, then the tightest used box, then the
/// reference-style fitness as the last word.
fn is_better(a: &NestResult, b: &NestResult) -> bool {
    let sa = a.sheets.iter().filter(|s| !s.placements.is_empty()).count();
    let sb = b.sheets.iter().filter(|s| !s.placements.is_empty()).count();
    if a.unplaced.len() != b.unplaced.len() { return a.unplaced.len() < b.unplaced.len(); }
    if sa != sb { return sa < sb; }
    let (pa, pb) = (placed_area(a), placed_area(b));
    if (pa - pb).abs() > 1e-6 { return pa > pb; }
    let (ba, bb) = (layout_bbox_area(a), layout_bbox_area(b));
    if (ba - bb).abs() > 1e-6 { return ba < bb; }
    a.fitness < b.fitness - 1e-12
}

fn pick_better(a: NestResult, b: NestResult) -> NestResult {
    let sa = a.sheets.iter().filter(|s| !s.placements.is_empty()).count();
    let sb = b.sheets.iter().filter(|s| !s.placements.is_empty()).count();
    // Placing every part comes first: a nest on fewer sheets that leaves parts
    // off is not the better nest.
    if a.unplaced.len() != b.unplaced.len() { return if a.unplaced.len() < b.unplaced.len() { a } else { b }; }
    if sa != sb { return if sa < sb { a } else { b }; }
    if (a.fitness - b.fitness).abs() > 1e-12 { return if a.fitness < b.fitness { a } else { b }; }
    a
}

fn place_once(
    sheets: &[SheetLoadInfo],
    parts: &[ExpandedPart],
    order: &Gene,
    config: &Config,
    nfp_cache: &NfpCache,
    do_reinsert: bool,
    sheet_order: &[usize],
    x_first: bool,
) -> NestResult {
    let _g = prof::g(11);
    let mut placed_sheets: Vec<SheetPlacement> = Vec::new();
    // A better rank goes first on every sheet, so a part of a worse rank never
    // takes the room a better one needs. The sort is stable: within a rank the
    // gene's order stands.
    let rank = |i: usize| (!(config.use_priority && parts[i].is_priority), parts[i].rank);
    let mut remaining: Gene = order.to_vec();
    remaining.sort_by_key(|&(i, _)| rank(i));

    let instances = sheet_instances(sheets);
    for &idx in sheet_order {
        if remaining.is_empty() || cancelled(config) { break; }
        let (sheet_id, sheet) = instances[idx];
        let (sheet_res, left) = place_on_sheet(sheet_id, sheet, parts, &remaining, config, nfp_cache, do_reinsert, x_first);
        remaining = left;
        if !sheet_res.placements.is_empty() {
            placed_sheets.push(sheet_res);
        }
    }

    let unplaced = remaining.iter().map(|&(i, _)| parts[i].name.clone()).collect();
    let unplaced_flipped = remaining.iter().map(|&(i, _)| parts[i].mirrored).collect();
    let mut result = NestResult {
        fitness: 0.0,
        sheets: placed_sheets,
        unplaced,
        unplaced_flipped,
        placement_type: config.placement_type,
        duration_ms: 0,
    };
    result.fitness = compute_fitness(&result.sheets, &result.unplaced, parts);
    result
}

/// Port of `DeepNestLib GeneticAlgorithm/Procreant.cs` + the `SvgNest.cs`
/// population/generation loop. Gene = part order + per-part variant lock.
/// Deterministic (seeded xorshift PRNG). Returns the best nest found.
fn ga_nest(
    sheets: &[SheetLoadInfo],
    parts: &[ExpandedPart],
    config: &Config,
    nfp_cache: &NfpCache,
    sheet_order: &[usize],
) -> Option<(Gene, NestResult)> {
    if parts.is_empty() { return None; }
    let mut rng = Rng::new(0x9E3779B97F4A7C15 ^ (parts.len() as u64).wrapping_mul(0x517CC1B727220A95));

    let adam_variant = |i: usize| -> Option<usize> {
        // Smart seed: the tightest-bounding-box variant (port of SmartRotationSeed).
        parts[i]
            .variants
            .iter()
            .enumerate()
            .min_by(|(_, a), (_, b)| {
                a.bounds.area().partial_cmp(&b.bounds.area()).unwrap()
            })
            .map(|(k, _)| k)
    };

    // Adam: parts sorted by area descending (port of CreateAdam).
    let adam: Gene = {
        let mut order: Vec<usize> = (0..parts.len()).collect();
        order.sort_by(|&a, &b| {
            parts[b].variants[0].area.partial_cmp(&parts[a].variants[0].area).unwrap().then(a.cmp(&b))
        });
        order.iter().map(|&i| (i, adam_variant(i))).collect()
    };

    let population_size = (config.populations as usize).saturating_mul(10).clamp(6, 60);
    let mutation_rate = 5.0f64; // percent-like rate per element (svg-nest uses 0.01 * MutationRate)

    let mut ancestors: std::collections::HashSet<(Vec<usize>, Vec<Option<usize>>)> = std::collections::HashSet::new();
    let key = |g: &Gene| -> (Vec<usize>, Vec<Option<usize>>) {
        (g.iter().map(|(i, _)| *i).collect(), g.iter().map(|(_, v)| *v).collect())
    };

    // Genes are drawn one after another (the random sequence does not depend on
    // any fitness within a batch), nested in parallel and folded in the order
    // drawn: the same search as one by one.
    let evaluate = |genes: &[Gene]| -> Vec<(f64, NestResult)> {
        genes
            .par_iter()
            .map(|gene| {
                // Fitness is judged on the cheap pass; the winning gene is
                // re-nested once with re-insertion by the caller.
                let result = place_once(sheets, parts, gene, config, nfp_cache, false, sheet_order, false);
                (result.fitness, result)
            })
            .collect()
    };

    let mutate = |gene: &Gene, rng: &mut Rng| -> Gene {
        let mut g = gene.clone();
        for i in 0..g.len() {
            if rng.next_f64() < 0.01 * mutation_rate {
                let j = (i + 1).min(g.len() - 1);
                g.swap(i, j);
            }
            if rng.next_f64() < 0.01 * mutation_rate {
                let (pi, _) = g[i];
                let variants = parts[pi].variants.len();
                g[i].1 = if variants > 1 { Some(rng.below(variants)) } else { None };
            }
        }
        g
    };

    // Single-point crossover with gene completion (port of Mate/CompleteGene).
    let mate = |male: &Gene, female: &Gene, rng: &mut Rng| -> (Gene, Gene) {
        let cut = ((rng.next_f64().clamp(0.1, 0.9) * (male.len().saturating_sub(1)) as f64).round() as usize).max(1);
        let complete = |initiant: &[(usize, Option<usize>)], supplicant: &Gene| -> Gene {
            let mut out: Gene = initiant.to_vec();
            for (pi, vi) in supplicant {
                if !out.iter().any(|(o, _)| o == pi) {
                    out.push((*pi, *vi));
                }
            }
            out
        };
        (complete(&male[..cut], female), complete(&female[..cut], male))
    };

    let random_weighted = |pop: &[usize], fitnesses: &[f64], rng: &mut Rng, exclude: Option<usize>| -> usize {
        let candidates: Vec<usize> = (0..pop.len())
            .filter(|&k| Some(k) != exclude)
            .collect();
        if candidates.is_empty() { return pop[0]; }
        // Front-weighted selection (lower fitness more likely).
        let mut acc = 0.0f64;
        let total: f64 = candidates.iter().map(|&k| (fitnesses[pop[k]] + 1e-9).recip()).sum();
        let pick = rng.next_f64() * total;
        for &k in &candidates {
            acc += (fitnesses[pop[k]] + 1e-9).recip();
            if acc >= pick { return pop[k]; }
        }
        *candidates.last().unwrap()
    };

    // Initial population: adam + mutants (unique only).
    let mut population: Vec<(Gene, f64)> = Vec::new();
    let mut best: Option<(f64, NestResult, Gene)> = None;
    let mut batch: Vec<Gene> = vec![adam.clone()];
    ancestors.insert(key(&adam));
    // A job of one or two parts has fewer different genes than the population
    // asks for: stop looking for new ones after a bounded number of draws.
    let mut draws = 0usize;
    while batch.len() < population_size && draws < population_size * 20 {
        draws += 1;
        let mutant = mutate(&adam, &mut rng);
        let k = key(&mutant);
        if !ancestors.insert(k) { continue; }
        batch.push(mutant);
    }
    for (gene, (f, r)) in batch.iter().zip(evaluate(&batch)) {
        if best.as_ref().map(|(bf, _, _)| f < *bf).unwrap_or(true) {
            best = Some((f, r, gene.clone()));
        }
        population.push((gene.clone(), f));
    }

    let elitism = (population_size / 10).max(1);

    for _gen in 0..config.generations.saturating_sub(1) {
        // sort by fitness ascending
        population.sort_by(|a, b| a.1.partial_cmp(&b.1).unwrap());
        let mut new_pop: Vec<(Gene, f64)> = population[..elitism.min(population.len())].to_vec();
        let mut guard = 0usize;
        let mut batch: Vec<Gene> = Vec::new();
        while new_pop.len() + batch.len() < population_size && guard < population_size * 20 {
            guard += 1;
            let idxs: Vec<usize> = (0..population.len()).collect();
            let fitnesses: Vec<f64> = population.iter().map(|p| p.1).collect();
            let male_i = random_weighted(&idxs, &fitnesses, &mut rng, None);
            let female_i = random_weighted(&idxs, &fitnesses, &mut rng, Some(male_i));
            let (son, daughter) = mate(&population[male_i].0, &population[female_i].0, &mut rng);
            for child in [son, daughter] {
                if new_pop.len() + batch.len() >= population_size { break; }
                let child = mutate(&child, &mut rng);
                let k = key(&child);
                if !ancestors.insert(k) { continue; }
                batch.push(child);
            }
        }
        for (gene, (f, r)) in batch.iter().zip(evaluate(&batch)) {
            if best.as_ref().map(|(bf, _, _)| f < *bf).unwrap_or(true) {
                best = Some((f, r, gene.clone()));
            }
            new_pop.push((gene.clone(), f));
        }
        if new_pop.len() <= elitism { break; } // search space exhausted
        population = new_pop;
    }

    best.map(|(_, r, g)| (g, r))
}

/// Port of `DeepNestLib GeneticAlgorithm/OriginalFitness.cs` +
/// `OriginalFitnessSheet.cs` (lower is better). Deviation: hull area is
/// approximated by the placement bounding box (no convex hull here).
fn compute_fitness(sheets: &[SheetPlacement], unplaced: &[String], parts: &[ExpandedPart]) -> f64 {
    let name_area = |name: &str| -> f64 {
        parts
            .iter()
            .find(|p| p.name == name)
            .map(|p| p.variants[0].area)
            .unwrap_or(1.0)
            .max(1.0)
    };

    // Unplaced: overweighted penalty so an extra sheet is always preferred.
    let mut fitness: f64 = unplaced.iter().map(|n| 50.0 * name_area(n)).sum();

    for s in sheets.iter().filter(|s| !s.placements.is_empty()) {
        let sheet_area = s.sheet_width * s.sheet_height;
        let mut parts_area = 0.0f64;
        let mut min_x = f64::INFINITY; let mut min_y = f64::INFINITY;
        let mut max_x = f64::NEG_INFINITY; let mut max_y = f64::NEG_INFINITY;
        for p in &s.placements {
            let outer = nest_geom::polygon_area(&p.local_outer).abs();
            let holes: f64 = p.local_holes.iter().map(|c| nest_geom::polygon_area(c).abs()).sum();
            parts_area += (outer - holes).max(0.0);
            if let Some(b) = bounds_of(&p.local_outer) {
                min_x = min_x.min(b.min_x + p.x); min_y = min_y.min(b.min_y + p.y);
                max_x = max_x.max(b.max_x + p.x); max_y = max_y.max(b.max_y + p.y);
            }
        }
        let util = if sheet_area > 0.0 { (parts_area / sheet_area).clamp(0.0, 1.0) } else { 0.0 };
        let rb_w = (max_x - min_x).max(0.0);
        let rb_h = (max_y - min_y).max(0.0);
        let rb_area = rb_w * rb_h;

        // Sheets: penalise each additional sheet by its area.
        fitness += sheet_area;
        // MaterialUtilization penalty.
        fitness += (1.0 - util).powf(1.1) * sheet_area;
        // Bounds (Gravity: prefer left squeeze / width compression).
        let bound = if s.sheet_width > 0.0 { rb_w / s.sheet_width * sheet_area } else { rb_area };
        let grav_area = ((rb_w * 3.0 + rb_h) / 4.0).powi(2);
        fitness += (bound * 4.0 + grav_area) / 7.0;
        // MaterialWasted.
        let mut wasted = if util < 0.6 { rb_area * 2.0 } else { sheet_area };
        wasted += rb_area; // hull area approximation
        wasted *= 2.0;
        wasted -= if util < 0.6 { 7.0 } else { 6.0 * parts_area };
        if util < 0.3 { wasted *= 1.25; }
        fitness += (wasted / 2.0).max(0.0);
    }
    fitness
}

/// Total shared-edge (common-line) length between placed parts. Ported in
/// spirit from the common-line concept of `DeepNestLib Placement` (`MergedLength`):
/// when two nested parts touch along collinear overlapping edge segments, that
/// length is cut only once per part pair, so the effective cut path shrinks.
/// This simplified estimate considers outer contours only (holes excluded).
/// Total shared-edge (common-line) length between placed parts. Ported in
/// spirit from the common-line concept of `DeepNestLib Placement` (`MergedLength`):
/// when two nested parts touch along collinear overlapping edge segments, that
/// length is cut only once per part pair, so the effective cut path shrinks.
/// Holes take part in the comparison too: a part nested flush in another's hole
/// shares the hole boundary, and two coincident hole boundaries share as well.
fn merged_length_of(placed: &[Placed]) -> f64 {
    // All material boundaries of a placement: outer + holes, in world coordinates.
    let contours = |p: &Placed| -> Vec<Vec<SvgPoint>> {
        let mut v = vec![p.world_outer()];
        v.extend(p.holes().iter().map(|h| translate_points(h, p.x, p.y)));
        v
    };
    let world: Vec<Vec<Vec<SvgPoint>>> = placed.iter().map(contours).collect();
    let mut total = 0.0f64;
    for i in 0..placed.len() {
        for j in (i + 1)..placed.len() {
            for a in &world[i] {
                for b in &world[j] {
                    // Coincident round boundaries (a part flush in a round hole,
                    // a round cradle) share an arc, which the straight-segment
                    // test cannot see: chords of two discretisations are not
                    // collinear. Count the arc first and skip the straight test
                    // when it found something, so an identical discretisation is
                    // not counted twice.
                    let arc = shared_arc_length(a, b);
                    total += if arc > 1e-9 { arc } else { shared_boundary_length(a, b) };
                }
            }
        }
    }
    total
}

/// Shared length along a circle: either two coincident circular contours (both
/// detected as circles: the shared boundary is the whole circumference), or one
/// circular contour against the chords of the other that lie on that circle.
/// Chord pieces are recognised by both endpoints lying on the circle and the
/// midpoint sagging by at most the chord's own sagitta (`c²/8r`).
fn shared_arc_length(a: &[SvgPoint], b: &[SvgPoint]) -> f64 {
    const ON_CIRCLE: f64 = 0.05;
    if let (Some((ca, ra)), Some((cb, rb))) = (circle_of(a), circle_of(b)) {
        let same = (ra - rb).abs() <= 0.05_f64.max(1e-3 * ra.max(rb))
            && ((ca.x - cb.x).powi(2) + (ca.y - cb.y).powi(2)).sqrt() <= 0.05;
        if same {
            return std::f64::consts::TAU * ra.min(rb);
        }
    }
    // Full circle against the chords of the other contour.
    for (circle, other) in [(circle_of(a), b), (circle_of(b), a)] {
        let Some((c, r)) = circle else { continue; };
        let mut len = 0.0f64;
        for k in 0..other.len() {
            let u = other[k];
            let v = other[(k + 1) % other.len()];
            let du = ((u.x - c.x).powi(2) + (u.y - c.y).powi(2)).sqrt();
            let dv = ((v.x - c.x).powi(2) + (v.y - c.y).powi(2)).sqrt();
            if (du - r).abs() > ON_CIRCLE || (dv - r).abs() > ON_CIRCLE {
                continue;
            }
            let m = SvgPoint::new((u.x + v.x) / 2.0, (u.y + v.y) / 2.0);
            let dm = ((m.x - c.x).powi(2) + (m.y - c.y).powi(2)).sqrt();
            let chord = ((v.x - u.x).powi(2) + (v.y - u.y).powi(2)).sqrt();
            let sagitta = chord * chord / (8.0 * r.max(1e-9));
            if dm <= r + ON_CIRCLE && r - dm <= sagitta + ON_CIRCLE {
                len += chord;
            }
        }
        if len > 1e-9 {
            return len.min(std::f64::consts::TAU * r);
        }
    }
    0.0
}

/// Length of collinear overlapping segments between two closed contours.
fn shared_boundary_length(a: &[SvgPoint], b: &[SvgPoint]) -> f64 {
    if a.len() < 3 || b.len() < 3 { return 0.0; }
    let mut total = 0.0f64;
    for i in 0..a.len() {
        let a1 = a[i];
        let a2 = a[(i + 1) % a.len()];
        for j in 0..b.len() {
            let b1 = b[j];
            let b2 = b[(j + 1) % b.len()];
            total += collinear_overlap(a1, a2, b1, b2);
        }
    }
    total
}

/// Overlap length of two segments lying on the same infinite line.
fn collinear_overlap(a1: SvgPoint, a2: SvgPoint, b1: SvgPoint, b2: SvgPoint) -> f64 {
    let dax = a2.x - a1.x;
    let day = a2.y - a1.y;
    let dbx = b2.x - b1.x;
    let dby = b2.y - b1.y;
    let la = (dax * dax + day * day).sqrt();
    if la < 1e-12 { return 0.0; }
    // parallel?
    let cross = dax * dby - day * dbx;
    let lb2 = (dbx * dbx + dby * dby).sqrt();
    if lb2 < 1e-12 { return 0.0; }
    if cross.abs() > 1e-7 * la * lb2 { return 0.0; }
    // b1 must lie on the line through a1..a2
    let perp = ((b1.x - a1.x) * day - (b1.y - a1.y) * dax) / la;
    if perp.abs() > 1e-7 { return 0.0; }
    // project both b endpoints onto a's parametric range [0, 1]
    let t_of = |p: SvgPoint| ((p.x - a1.x) * dax + (p.y - a1.y) * day) / (la * la);
    let (t0, t1) = (t_of(b1), t_of(b2));
    let lo = t0.min(t1).max(0.0);
    let hi = t0.max(t1).min(1.0);
    if hi > lo { (hi - lo) * la } else { 0.0 }
}

/// One stock copy of the job: (sheet index in the job, sheet info).
fn sheet_instances(sheets: &[SheetLoadInfo]) -> Vec<(usize, &SheetLoadInfo)> {
    let mut out = Vec::new();
    for (sheet_id, sheet) in sheets.iter().enumerate() {
        for _ in 0..sheet.quantity.max(1) as usize {
            out.push((sheet_id, sheet));
        }
    }
    out
}

/// Orders in which the stock copies are tried. The job's own order first, then
/// the biggest stock first: filling the large sheet may take every part, which
/// saves a whole smaller sheet instead of leaving it half-used (the quality key
/// prefers fewer used sheets).
fn sheet_orders(instances: &[(usize, &SheetLoadInfo)]) -> Vec<Vec<usize>> {
    let base: Vec<usize> = (0..instances.len()).collect();
    let mut by_area = base.clone();
    by_area.sort_by(|&a, &b| {
        let aa = instances[a].1.width * instances[a].1.height;
        let ab = instances[b].1.width * instances[b].1.height;
        ab.partial_cmp(&aa).unwrap().then(a.cmp(&b))
    });
    // Stock of a better priority (offcuts) goes first in either order.
    let mut base = base;
    base.sort_by_key(|&i| instances[i].1.priority);
    by_area.sort_by_key(|&i| instances[i].1.priority);
    let mut orders = vec![base];
    if !orders.contains(&by_area) {
        orders.push(by_area);
    }
    orders
}

fn place_on_sheet(
    sheet_id: usize,
    sheet: &SheetLoadInfo,
    parts: &[ExpandedPart],
    remaining: &Gene,
    config: &Config,
    nfp_cache: &NfpCache,
    do_reinsert: bool,
    x_first: bool,
) -> (SheetPlacement, Gene) {
    // What is on the sheet before the nest: its shape and the parts it holds.
    let mut placed: Vec<Placed> = nfp_cache.2.get(sheet_id).cloned().unwrap_or_default();
    // What stays for the next sheet keeps its orientation lock.
    let mut left: Gene = Vec::new();

    for &(pi, vi) in remaining {
        if cancelled(config) {
            left.push((pi, vi));
            continue;
        }
        let tried = try_place_part(sheet, parts, pi, &placed, config, nfp_cache, vi, x_first);
        if let Some(p) = tried {
            placed.push(p);
        } else {
            left.push((pi, vi));
        }
    }

    compact_placed(sheet, &mut placed, config, nfp_cache);

    // Re-insertion descent (port of `DeepNestLib Placement/Reinsertion.cs`):
    // re-place each part against ALL the others (all rotation variants), keeping
    // only moves that strictly shrink the overall bounding box.
    // Sweeps stop as soon as one no longer tightens the layout: later sweeps
    // only shuffle parts around the same bounding box.
    let mut area = if do_reinsert { bounding_area_of(&placed) } else { 0.0 };
    for _ in 0..if do_reinsert { config.reinsertion_passes } else { 0 } {
        let moved = reinsertion_pass(sheet, parts, &mut placed, config, nfp_cache, x_first);
        if moved == 0 { break; }
        compact_placed(sheet, &mut placed, config, nfp_cache);
        let tightened = bounding_area_of(&placed);
        if tightened >= area - 1e-6 { break; }
        area = tightened;
    }

    // Reinsertion/retry: after compaction, try to place remaining parts again
    if !left.is_empty() && placed.iter().any(|p| !p.fixed) && !cancelled(config) {
        let mut changed = true;
        for _ in 0..3 {
            if !changed || left.is_empty() { break; }
            changed = false;
            let mut still_left = Vec::new();
            for &(pi, vi) in &left {
                if let Some(p) = try_place_part(sheet, parts, pi, &placed, config, nfp_cache, None, x_first) {
                    placed.push(p);
                    changed = true;
                } else {
                    still_left.push((pi, vi));
                }
            }
            left = still_left;
            if changed {
                compact_placed(sheet, &mut placed, config, nfp_cache);
            }
        }
    }

    placed.retain(|p| !p.ghost);
    let merged = merged_length_of(&placed);
    placed.retain(|p| !p.fixed);

    let sp = SheetPlacement {
        sheet_id,
        sheet_width: sheet.width,
        sheet_height: sheet.height,
        placements: placed.iter().map(|p| PartPlacement {
            id: p.id,
            name: p.name.clone(),
            sheet_id,
            x: p.x,
            y: p.y,
            rotation: p.rotation,
            is_priority: p.is_priority,
            outer: translate_points(&p.detail.outer, p.x, p.y),
            holes: p.holes().iter().map(|c| translate_points(c, p.x, p.y)).collect(),
            local_outer: p.detail.outer.clone(),
            local_holes: p.holes().to_vec(),
            local_bends: p.detail.bends.clone(),
        }).collect(),
        merged_length: merged,
        outline: sheet.outline.clone(),
        holes: sheet.holes.clone(),
        virtual_stock: false,
    };
    (sp, left)
}

/// Union bounding-box area of the current placements (effective outlines).
fn bounding_area_of(placed: &[Placed]) -> f64 {
    if placed.iter().all(|p| p.ghost) { return 0.0; }
    let mut min_x = f64::INFINITY; let mut min_y = f64::INFINITY;
    let mut max_x = f64::NEG_INFINITY; let mut max_y = f64::NEG_INFINITY;
    for p in placed.iter().filter(|p| !p.ghost) {
        min_x = min_x.min(p.x); min_y = min_y.min(p.y);
        max_x = max_x.max(p.x + p.bounds.width());
        max_y = max_y.max(p.y + p.bounds.height());
    }
    (max_x - min_x).max(0.0) * (max_y - min_y).max(0.0)
}

/// One re-insertion sweep over all placed parts (largest first). Returns the
/// number of accepted moves. A move is accepted only when it strictly shrinks
/// the overall bounding box; ties are broken by the placement direction.
fn reinsertion_pass(
    sheet: &SheetLoadInfo,
    parts: &[ExpandedPart],
    placed: &mut Vec<Placed>,
    config: &Config,
    nfp_cache: &NfpCache,
    x_first: bool,
) -> usize {
    if placed.iter().filter(|p| !p.ghost).count() < 2 { return 0; }
    let _g = prof::g(5);
    let mut moves = 0;

    // Largest parts first: they constrain the layout most.
    let mut order: Vec<usize> = (0..placed.len()).filter(|&i| !placed[i].fixed).collect();
    order.sort_by(|&a, &b| {
        let aa = placed[a].bounds.area();
        let ab = placed[b].bounds.area();
        ab.partial_cmp(&aa).unwrap().then(a.cmp(&b))
    });

    for &idx in &order {
        let target_id = placed[idx].id;
        let Some(exp) = parts.iter().find(|p| p.id == target_id) else { continue; };

        // A move is kept only when it strictly shrinks the union bounding box.
        // If this part does not touch any side of the union of the others, its
        // position cannot change that box at all, so the sweep would reject
        // every candidate: skip the expensive candidate search for it.
        let (mut omin_x, mut omin_y) = (f64::INFINITY, f64::INFINITY);
        let (mut omax_x, mut omax_y) = (f64::NEG_INFINITY, f64::NEG_INFINITY);
        for (j, p) in placed.iter().enumerate() {
            if j == idx || p.ghost { continue; }
            omin_x = omin_x.min(p.x); omin_y = omin_y.min(p.y);
            omax_x = omax_x.max(p.x + p.bounds.width());
            omax_y = omax_y.max(p.y + p.bounds.height());
        }
        let b = &placed[idx];
        let touches = (b.x <= omin_x + 1e-9)
            || (b.y <= omin_y + 1e-9)
            || (b.x + b.bounds.width() >= omax_x - 1e-9)
            || (b.y + b.bounds.height() >= omax_y - 1e-9);
        if !touches && placed.len() > 1 { continue; }

        let base_area = bounding_area_of(placed);

        let others: Vec<Placed> = placed
            .iter()
            .enumerate()
            .filter(|(j, _)| *j != idx)
            .map(|(_, p)| p.clone())
            .collect();

        // Orientations of one part are independent: try them in parallel (the
        // thread pool is free during the refinement phase). Only a strictly
        // tighter box can be accepted, so candidates that cannot tighten it
        // never pay for a validity test; of those, best first.
        let extent = extent_of(&others);
        let best = exp
            .variants
            .par_iter()
            .map(|var| {
                if var.bounds.width() > sheet.width + 1e-9 || var.bounds.height() > sheet.height + 1e-9 { return None; }
                let (candidates, blocker) = candidate_positions(sheet, var, &others, nfp_cache, config);
                let mut scored: Vec<(CandidateScore, f64, f64)> = candidates
                    .into_iter()
                    .map(|(x, y)| (score_position(sheet, var, x, y, &extent, config.placement_direction, x_first), x, y))
                    .filter(|(s, _, _)| s.bbox_area < base_area - 1e-6)
                    .collect();
                scored.sort_by(|a, b| a.0.order(&b.0));
                scored
                    .into_iter()
                    .find(|&(_, x, y)| !blocker.blocks(x, y) && is_valid_position(sheet, var, x, y, &others, config.clipper_scale, &nfp_cache.1))
                    .map(|(score, x, y)| (score, x, y, var))
            })
            .collect::<Vec<_>>()
            .into_iter()
            .flatten()
            .min_by(|a, b| a.0.order(&b.0));

        if let Some((score, x, y, var)) = best {
            // Guarded descent: accept only a strictly tighter overall bounding box.
            if score.bbox_area < base_area - 1e-6 {
                let factor = nest_clip::scale_factor(config.clipper_scale);
                placed[idx] = Placed::of(exp, var, snap_to_clip(x, factor), snap_to_clip(y, factor));
                moves += 1;
            }
        }
    }
    moves
}

fn shape_sig(n: &Nfp) -> ShapeSig {
    let mut minx = f64::INFINITY; let mut miny = f64::INFINITY;
    let mut maxx = f64::NEG_INFINITY; let mut maxy = f64::NEG_INFINITY;
    let mut sx = 0.0f64; let mut sy = 0.0f64;
    // simple rolling hash of quantized point coords for better collision resistance
    let mut h: u64 = 1469598103934665603;
    for p in &n.points {
        minx = minx.min(p.x); miny = miny.min(p.y);
        maxx = maxx.max(p.x); maxy = maxy.max(p.y);
        sx += p.x; sy += p.y;
        let qx = (p.x * 1000.0).round() as i64;
        let qy = (p.y * 1000.0).round() as i64;
        for b in qx.to_le_bytes() { h ^= b as u64; h = h.wrapping_mul(1099511628211); }
        for b in qy.to_le_bytes() { h ^= b as u64; h = h.wrapping_mul(1099511628211); }
    }
    if n.points.is_empty() { minx=0.0; miny=0.0; maxx=0.0; maxy=0.0; }
    (n.points.len(), (minx*1000.0) as i64, (miny*1000.0) as i64, (maxx*1000.0) as i64, (maxy*1000.0) as i64, ((sx+sy)*100.0) as i64, h)
}

fn nfp_key(a: &Nfp, b: &Nfp, inside: bool) -> NfpKey {
    (shape_sig(a), shape_sig(b), inside)
}

#[allow(clippy::too_many_arguments)]
fn try_place_part(
    sheet: &SheetLoadInfo,
    parts: &[ExpandedPart],
    pi: usize,
    placed: &[Placed],
    config: &Config,
    nfp_cache: &NfpCache,
    force_variant: Option<usize>,
    x_first: bool,
) -> Option<Placed> {
    let part = &parts[pi];
    let mut best: Option<(CandidateScore, Placed)> = None;

    if let Some(vi) = force_variant {
        if vi >= part.variants.len() { return None; }
        return try_place_variant(sheet, part, &part.variants[vi], placed, config, nfp_cache, x_first);
    }
    // Orientations are independent: evaluate them in parallel and keep the
    // best score. Order is preserved by the collect, so ties resolve as before.
    let extent = extent_of(placed);
    let evaluated: Vec<Option<(CandidateScore, Placed)>> = part
        .variants
        .par_iter()
        .map(|var| {
            try_place_variant(sheet, part, var, placed, config, nfp_cache, x_first)
                .map(|p| (score_position(sheet, var, p.x, p.y, &extent, config.placement_direction, x_first), p))
        })
        .collect();
    for cand in evaluated.into_iter().flatten() {
        if best.as_ref().map(|(s, _)| cand.0.better_than(s)).unwrap_or(true) {
            best = Some(cand);
        }
    }
    best.map(|(_, p)| p)
}

fn try_place_variant(
    sheet: &SheetLoadInfo,
    part: &ExpandedPart,
    var: &PartVariant,
    placed: &[Placed],
    config: &Config,
    nfp_cache: &NfpCache,
    x_first: bool,
) -> Option<Placed> {
    let _g = prof::g(9);
    if var.bounds.width() > sheet.width + 1e-9 || var.bounds.height() > sheet.height + 1e-9 { return None; }
    let factor = nest_clip::scale_factor(config.clipper_scale);
    // Score every candidate (cheap), then test best first: the first valid one
    // is the answer. Scores and the NFP test use the candidate as computed, the
    // validity check the position snapped to the clipper grid.
    let (candidates, blocker) = candidate_positions(sheet, var, placed, nfp_cache, config);
    let extent = extent_of(placed);
    let _gs = prof::g(10);
    let mut scored: Vec<(CandidateScore, f64, f64)> = candidates
        .into_iter()
        .map(|(x, y)| (score_position(sheet, var, snap_to_clip(x, factor), snap_to_clip(y, factor), &extent, config.placement_direction, x_first), x, y))
        .collect();
    scored.sort_by(|a, b| a.0.order(&b.0));
    drop(_gs);
    scored
        .into_iter()
        .filter(|&(_, x, y)| !blocker.blocks(x, y))
        .map(|(_, x, y)| (snap_to_clip(x, factor), snap_to_clip(y, factor)))
        .find(|&(x, y)| is_valid_position(sheet, var, x, y, placed, config.clipper_scale, &nfp_cache.1))
        .map(|(x, y)| Placed::of(part, var, x, y))
}


fn sample_edge_points(contours: &[Vec<SvgPoint>], max_per_edge: usize) -> Vec<(f64,f64)> {
    let mut pts = Vec::new();
    for c in contours {
        let n = c.len();
        if n < 2 { continue; }
        for i in 0..n {
            let a = c[i]; let b = c[(i+1)%n];
            pts.push((a.x,a.y));
            for k in 1..=max_per_edge {
                let t = k as f64 / (max_per_edge as f64 + 1.0);
                pts.push((a.x + (b.x-a.x)*t, a.y + (b.y-a.y)*t));
            }
        }
    }
    pts
}

fn candidate_positions(
    sheet: &SheetLoadInfo,
    var: &PartVariant,
    placed: &[Placed],
    nfp_cache: &NfpCache,
    config: &Config,
) -> (Vec<(f64, f64)>, Blocker) {
    let _g = prof::g(1);
    let sheet_poly = Nfp::new(vec![
        SvgPoint::new(0.0,0.0), SvgPoint::new(sheet.width,0.0),
        SvgPoint::new(sheet.width,sheet.height), SvgPoint::new(0.0,sheet.height),
    ]);

    // Inner feasible region for effective part
    let part_bbox_nfp = Nfp::new(vec![
        SvgPoint::new(0.0,0.0), SvgPoint::new(var.bounds.width(),0.0),
        SvgPoint::new(var.bounds.width(),var.bounds.height()), SvgPoint::new(0.0,var.bounds.height()),
    ]);
    let inner = no_fit_polygon_rectangle(&sheet_poly, &part_bbox_nfp).map(|n| vec![n])
        .or_else(|| {

            nfp_cache.get_rot(&sheet_poly, &var.nfp_outer, true, 0.0, var.rotation).map(|n| n.to_vec())
        });

    let Some(inner_regions) = inner else {
        return (vec![], Blocker { forbidden: vec![], bounds: vec![], rows: vec![], in_hole: Default::default() });
    };

    // Union of outer NFPs (forbidden) translated to world positions
    let mut forbidden: Vec<Vec<SvgPoint>> = Vec::new();
    for pl in placed {
        let a_nfp = Nfp::new(pl.effective_outer.clone()); // placed effective at origin in its frame
        let nfps = nfp_cache.get_rot(&a_nfp, &var.nfp_outer, false, pl.rotation, var.rotation);
        if let Some(nfps) = nfps {
            for nfp in nfps.iter() {
                forbidden.push(translate_points(&nfp.points, pl.x, pl.y));
            }
        } else {
            // bbox forbidden fallback
            forbidden.push(vec![
                SvgPoint::new(pl.x - var.bounds.width(), pl.y - var.bounds.height()),
                SvgPoint::new(pl.x + pl.bounds.width()*2.0, pl.y - var.bounds.height()),
                SvgPoint::new(pl.x + pl.bounds.width()*2.0, pl.y + pl.bounds.height()*2.0),
                SvgPoint::new(pl.x - var.bounds.width(), pl.y + pl.bounds.height()*2.0),
            ]);
        }
    }

    let mut feasible: Vec<Vec<SvgPoint>> = inner_regions.iter().map(|n| n.points.clone()).collect();
    if !forbidden.is_empty() {
        // clipper2 difference takes (subject, clip)
        let clipped = nest_clip::difference_paths_at_scale(&feasible, &forbidden, nest_clip::scale_factor(config.clipper_scale));
        if !clipped.is_empty() {
            feasible = clipped;
        } else {
            // fallback to inner if clip failed oddly
            feasible = inner_regions.iter().map(|n| n.points.clone()).collect();
        }
    }

    // Part-in-hole: holes of placed parts can host smaller parts (inner NFP of hole as container)
    let mut hole_feasible: Vec<Vec<SvgPoint>> = Vec::new();
    for pl in placed {
        for (hole, hb) in pl.detail.children.iter().zip(pl.hole_bounds.iter()) {
            if hole.len() < 3 { continue; }
            // quick area/bounds reject: hole must be big enough
            let Some(hb) = hb else { continue; };
            if hb.width() < var.bounds.width() - 1e-6 || hb.height() < var.bounds.height() - 1e-6 { continue; }
            let hole_nfp = Nfp::new(hole.clone());
            let nfps = nfp_cache.get(&hole_nfp, &var.nfp_outer, true);
            if let Some(nfps) = nfps {
                for nfp in nfps.iter() {
                    let t = translate_points(&nfp.points, pl.x, pl.y);
                    hole_feasible.push(t);
                }
            }
        }
    }
    let mut pts: Vec<(f64,f64)> = Vec::new();
    // Vertices of feasible region
    // (the edge samples below include them)
    for c in feasible.iter().filter(|c| c.len() < 2) { for p in c { pts.push((p.x,p.y)); } }
    // Edge samples of feasible region (edge-interior contacts approximation)
    pts.extend(sample_edge_points(&feasible, 3));
    // Also include vertices of forbidden NFPs that may still be valid contacts on boundary after clip precision
    for c in &forbidden { for p in c { pts.push((p.x,p.y)); } }

    // Preferred corners (direction anchors) as explicit candidates
    let corners = [
        (sheet.width - var.bounds.width(), 0.0),
        (0.0, 0.0),
        (sheet.width - var.bounds.width(), sheet.height - var.bounds.height()),
        (0.0, sheet.height - var.bounds.height()),
    ];
    pts.extend_from_slice(&corners);

    // A slot that takes the part exactly (a panel between two neighbours, a
    // strip against the sheet edge) is a feasible region of zero width, which
    // the clipping above drops. The position is where two NFP edges cross, or
    // an NFP edge crosses the edge of the inner region, so add those crossings.
    let mut outlines: Vec<&[SvgPoint]> = inner_regions.iter().map(|n| n.points.as_slice()).collect();
    outlines.extend(forbidden.iter().map(|c| c.as_slice()));
    pts.extend(edge_crossings(&outlines));

    // Positions inside a hole of a placed part lie inside that part's NFP by
    // construction: they are exempt from the NFP test below.
    let mut in_hole = std::collections::HashSet::new();
    let mut hole_pts: Vec<(f64, f64)> = Vec::new();
    for c in &hole_feasible { for p in c { hole_pts.push((p.x,p.y)); } }
    hole_pts.extend(sample_edge_points(&hole_feasible, 3));
    for &(x, y) in &hole_pts { in_hole.insert((x.to_bits(), y.to_bits())); }
    pts.extend(hole_pts);

    pts.sort_by(|a,b| a.0.partial_cmp(&b.0).unwrap().then(a.1.partial_cmp(&b.1).unwrap()));
    pts.dedup_by(|a,b| (a.0-b.0).abs() < 1e-5 && (a.1-b.1).abs() < 1e-5);
    pts.retain(|&(x,y)| x >= -1e-6 && y >= -1e-6 && x + var.bounds.width() <= sheet.width + 1e-6 && y + var.bounds.height() <= sheet.height + 1e-6);
    let bounds: Vec<Option<Bounds>> = forbidden.iter().map(|c| bounds_of(c)).collect();
    let rows = forbidden.iter().map(|_| Default::default()).collect();
    (pts, Blocker { forbidden, bounds, rows, in_hole })
}

/// The edges of a polygon by horizontal band, each edge listed in every band
/// it comes within the placement noise of. Whether a point is inside the
/// polygon, and whether it is near its boundary, are both settled by the edges
/// of the point's band alone.
struct EdgeRows {
    min_y: f64,
    per_unit: f64,
    /// Band `r` holds `edges[start[r]..start[r + 1]]`; an edge is the index of
    /// its first vertex.
    start: Vec<u32>,
    edges: Vec<u32>,
}

impl EdgeRows {
    fn new(poly: &[SvgPoint], b: &Bounds) -> EdgeRows {
        let n = poly.len();
        let rows = (n / 2).clamp(1, 512);
        let per_unit = if b.height() > 0.0 { rows as f64 / b.height() } else { 0.0 };
        let mut index = EdgeRows { min_y: b.min_y, per_unit, start: vec![0; rows + 1], edges: Vec::new() };
        let row = |y: f64| (((y - b.min_y) * per_unit).floor().max(0.0) as usize).min(rows - 1);
        let span = |i: usize| {
            let (p, q) = (poly[i], poly[(i + 1) % n]);
            let pad = 2.0 * PLACEMENT_NOISE;
            (row(p.y.min(q.y) - pad), row(p.y.max(q.y) + pad))
        };
        for i in 0..n {
            let (lo, hi) = span(i);
            for r in lo..=hi {
                index.start[r + 1] += 1;
            }
        }
        for r in 0..rows {
            index.start[r + 1] += index.start[r];
        }
        let mut next: Vec<u32> = index.start[..rows].to_vec();
        let mut edges = vec![0u32; index.start[rows] as usize];
        for i in 0..n {
            let (lo, hi) = span(i);
            for r in lo..=hi {
                edges[next[r] as usize] = i as u32;
                next[r] += 1;
            }
        }
        index.edges = edges;
        index
    }
    fn row(&self, y: f64) -> usize {
        let rows = self.start.len() - 1;
        (((y - self.min_y) * self.per_unit).floor().max(0.0) as usize).min(rows - 1)
    }
    fn at(&self, y: f64) -> &[u32] {
        let r = self.row(y);
        &self.edges[self.start[r] as usize..self.start[r + 1] as usize]
    }
}

/// The NFPs of the placed parts against the part being placed. A position
/// strictly inside one of them overlaps that part, which a point-in-polygon
/// test tells far cheaper than the exact validity check (clipper booleans).
/// Most NFP vertices lie inside a neighbour's NFP: on the demo job 5 million
/// exact checks came from them.
struct Blocker {
    forbidden: Vec<Vec<SvgPoint>>,
    bounds: Vec<Option<Bounds>>,
    /// Built on the first question about a polygon: most are never asked.
    rows: Vec<std::cell::OnceCell<EdgeRows>>,
    in_hole: std::collections::HashSet<(u64, u64)>,
}

impl Blocker {
    fn blocks(&self, x: f64, y: f64) -> bool {
        let _g = prof::g(8);
        if self.in_hole.contains(&(x.to_bits(), y.to_bits())) {
            return false;
        }
        self.forbidden.iter().zip(&self.bounds).zip(&self.rows).any(|((poly, b), rows)| {
            let Some(b) = b else { return false };
            if !(x > b.min_x && x < b.max_x && y > b.min_y && y < b.max_y) {
                return false;
            }
            let rows = rows.get_or_init(|| EdgeRows::new(poly, b));
            // Strictly inside (the crossing rule of `nest_geom::point_in_polygon`)
            // and not within the noise of the boundary, which also covers that
            // function's "on a vertex or an edge".
            let n = poly.len();
            let band = rows.at(y);
            let mut inside = false;
            for &i in band {
                let (p, q) = (poly[i as usize], poly[(i as usize + 1) % n]);
                if nest_geom::almost_equal(p.x, q.x) && nest_geom::almost_equal(p.y, q.y) {
                    continue;
                }
                if (q.y > y) != (p.y > y) && x < (p.x - q.x) * (y - q.y) / (p.y - q.y) + q.x {
                    inside = !inside;
                }
            }
            inside && !band.iter().any(|&i| near_segment(poly[i as usize], poly[(i as usize + 1) % n], x, y, PLACEMENT_NOISE))
        })
    }
}

/// Whether (x, y) lies within `eps` of the segment a-b.
fn near_segment(a: SvgPoint, b: SvgPoint, x: f64, y: f64, eps: f64) -> bool {
    let (dx, dy) = (b.x - a.x, b.y - a.y);
    let len2 = dx * dx + dy * dy;
    let t = if len2 > 0.0 { (((x - a.x) * dx + (y - a.y) * dy) / len2).clamp(0.0, 1.0) } else { 0.0 };
    let (px, py) = (a.x + t * dx - x, a.y + t * dy - y);
    px * px + py * py <= eps * eps
}

/// Geometry snapshot of one part for pair validity checks.
#[derive(Clone, Copy)]
struct PairGeom<'a> {
    detail: &'a RawDetail,
    hole_bounds: &'a [Option<Bounds>],
    effective_outer: &'a [SvgPoint],
    bounds: Bounds,
    at: (f64, f64),
}

impl PairGeom<'_> {
    fn from_variant(var: &PartVariant, x: f64, y: f64) -> PairGeom<'_> {
        PairGeom { detail: &var.detail, hole_bounds: &var.hole_bounds, effective_outer: &var.effective_outer, bounds: var.bounds, at: (x, y) }
    }
    fn from_placed(pl: &Placed) -> PairGeom<'_> {
        PairGeom { detail: &pl.detail, hole_bounds: &pl.hole_bounds, effective_outer: &pl.effective_outer, bounds: pl.bounds, at: (pl.x, pl.y) }
    }
    /// Holes of this part whose box could hold `inner` (world box), moved to
    /// the world. Only these are worth an exact containment test.
    fn holes_around(&self, inner: Bounds) -> impl Iterator<Item = Vec<SvgPoint>> + '_ {
        let at = self.at;
        self.detail.children.iter().zip(self.hole_bounds.iter()).filter_map(move |(h, hb)| {
            let hb = (*hb)?;
            let fits = inner.min_x >= hb.min_x + at.0 - 1e-9 && inner.min_y >= hb.min_y + at.1 - 1e-9
                && inner.max_x <= hb.max_x + at.0 + 1e-9 && inner.max_y <= hb.max_y + at.1 + 1e-9;
            fits.then(|| translate_points(h, at.0, at.1))
        })
    }
}

/// True if `inner` is fully inside `container` (boundary allowed). Fast bbox
/// reject first, then exact-ish clipper containment via intersection area.
fn polygon_contained(inner: &[SvgPoint], container: &[SvgPoint], clip_factor: f64) -> bool {
    if inner.len() < 3 || container.len() < 3 { return false; }
    let (Some(ib), Some(cb)) = (bounds_of(inner), bounds_of(container)) else { return false; };
    if ib.min_x < cb.min_x - 1e-9 || ib.min_y < cb.min_y - 1e-9
        || ib.max_x > cb.max_x + 1e-9 || ib.max_y > cb.max_y + 1e-9 { return false; }
    let ia = nest_clip::paths_area_at_scale(&[inner.to_vec()], clip_factor);
    if ia <= 1e-9 { return false; }
    let inter = nest_clip::intersection_paths_at_scale(&[inner.to_vec()], &[container.to_vec()], clip_factor);
    let iarea = nest_clip::paths_area_at_scale(&inter, clip_factor);
    // Clipper re-traces the intersection, so even identical outlines differ by a
    // small fraction of a percent in area; material overlap is still checked
    // exactly (hole-aware `details_overlap`), so this tolerance cannot let a
    // genuinely oversized part through.
    (iarea - ia).abs() <= 1e-3 * ia.max(1.0)
}

/// Pairwise validity: one part at `a` vs an already placed part `b`.
/// If either part is entirely inside a hole of the other, the outer
/// effective-outline overlap with the host material is allowed; spacing to the
/// hole boundary is enforced naturally because effective outlines are inflated
/// by spacing/2 (an in-hole part must keep its inflated outline inside the hole).
fn pair_valid(a: &PairGeom, b: &PairGeom, clip_factor: f64, memo: &PairMemo) -> bool {
    // bbox reject
    if a.at.0 > b.at.0 + b.bounds.width() + 1e-9 || b.at.0 > a.at.0 + a.bounds.width() + 1e-9
        || a.at.1 > b.at.1 + b.bounds.height() + 1e-9 || b.at.1 > a.at.1 + a.bounds.height() + 1e-9 {
        return true;
    }
    // The exact check, in b's frame: a at the offset between the two, on the
    // position grid, so the verdict is a function of the key alone.
    let q = 1.0 / clip_factor;
    let (dx, dy) = (a.at.0 - b.at.0, a.at.1 - b.at.1);
    let (kx, ky) = ((dx / q).round(), (dy / q).round());
    let on_grid = q.is_finite() && q > 0.0 && (dx - kx * q).abs() < 1e-7 && (dy - ky * q).abs() < 1e-7 && kx.abs() < 1e15 && ky.abs() < 1e15;
    let origin = PairGeom { at: (0.0, 0.0), ..*b };
    if !on_grid {
        return pair_valid_exact(&PairGeom { at: (dx, dy), ..*a }, &origin, clip_factor);
    }
    let id = |g: &PairGeom| g.detail as *const RawDetail as usize;
    memo.get_or((id(a), id(b), kx as i64, ky as i64), || {
        pair_valid_exact(&PairGeom { at: (kx * q, ky * q), ..*a }, &origin, clip_factor)
    })
}

fn pair_valid_exact(a: &PairGeom, b: &PairGeom, clip_factor: f64) -> bool {
    let a_eff_world = translate_points(a.effective_outer, a.at.0, a.at.1);
    let a_in_hole_of_b = !b.detail.children.is_empty()
        && bounds_of(&a_eff_world)
            .is_some_and(|ab| b.holes_around(ab).any(|hh| polygon_contained(&a_eff_world, &hh, clip_factor)));
    let b_in_hole_of_a = if a_in_hole_of_b || a.detail.children.is_empty() { false } else {
        let b_mat = translate_points(&b.detail.outer, b.at.0, b.at.1);
        bounds_of(&b_mat)
            .is_some_and(|bb| a.holes_around(bb).any(|hh| polygon_contained(&b_mat, &hh, clip_factor)))
    };
    if !a_in_hole_of_b && !b_in_hole_of_a {
        let b_eff_world = translate_points(b.effective_outer, b.at.0, b.at.1);
        // Depth-based clearance (PlacementCollision.TooClose port): overlap must
        // bite deeper than the placement noise to count, so welded pairs landing
        // a hair past exact contact are not condemned.
        let _g6 = prof::g(6);
        if placements_too_close(
            &a_eff_world, &[], (0.0, 0.0),
            &b_eff_world, &[], (0.0, 0.0),
            0.0,
            PLACEMENT_NOISE,
            clip_factor,
        ) {
            return false;
        }
    }
    // material overlap (hole-aware) on originals
    !details_overlap(
        &a.detail.outer, &a.detail.children, a.at,
        &b.detail.outer, &b.detail.children, b.at,
    )
}

fn is_valid_position(sheet: &SheetLoadInfo, var: &PartVariant, x: f64, y: f64, placed: &[Placed], clip_scale: f64, memo: &PairMemo) -> bool {
    let _g = prof::g(2);
    let factor = nest_clip::scale_factor(clip_scale);
    let x = snap_to_clip(x, factor);
    let y = snap_to_clip(y, factor);
    // effective outline must stay in sheet
    if !inside_sheet(&var.effective_outer, (x,y), sheet.width, sheet.height) { return false; }
    // also original material inside sheet
    if !inside_sheet(&var.detail.outer, (x,y), sheet.width, sheet.height) { return false; }
    let a = PairGeom::from_variant(var, x, y);
    placed.iter().all(|pl| pair_valid(&a, &PairGeom::from_placed(pl), factor, memo))
}

#[derive(Debug, Clone, Copy)]
struct CandidateScore {
    bbox_area: f64,
    dir_key: (f64, f64),
    corner_dist: f64,
}

impl CandidateScore {
    /// Total order for sorting candidates, best first (same keys as
    /// `better_than`). Keys are compared to a millionth: a bounding box computed
    /// from two positions differs in the last bits, and that noise must not
    /// decide before the direction does.
    fn order(&self, other: &Self) -> std::cmp::Ordering {
        let q = |v: f64| (v * 1e6).round();
        q(self.bbox_area)
            .total_cmp(&q(other.bbox_area))
            .then(q(self.dir_key.0).total_cmp(&q(other.dir_key.0)))
            .then(q(self.dir_key.1).total_cmp(&q(other.dir_key.1)))
            .then(q(self.corner_dist).total_cmp(&q(other.corner_dist)))
    }
    fn better_than(&self, other: &Self) -> bool {
        if (self.bbox_area - other.bbox_area).abs() > 1e-9 { return self.bbox_area < other.bbox_area; }
        if (self.dir_key.0 - other.dir_key.0).abs() > 1e-9 { return self.dir_key.0 < other.dir_key.0; }
        if (self.dir_key.1 - other.dir_key.1).abs() > 1e-9 { return self.dir_key.1 < other.dir_key.1; }
        self.corner_dist < other.corner_dist
    }
}

/// The box of the placed parts (min x, min y, max x, max y), empty when there are none.
fn extent_of(placed: &[Placed]) -> (f64, f64, f64, f64) {
    let mut e = (f64::INFINITY, f64::INFINITY, f64::NEG_INFINITY, f64::NEG_INFINITY);
    for p in placed.iter().filter(|p| !p.ghost) {
        e = (e.0.min(p.x), e.1.min(p.y), e.2.max(p.x + p.bounds.width()), e.3.max(p.y + p.bounds.height()));
    }
    e
}

fn score_position(
    sheet: &SheetLoadInfo,
    var: &PartVariant,
    x: f64, y: f64,
    placed: &(f64, f64, f64, f64),
    dir: PlacementDirection,
    x_first: bool,
) -> CandidateScore {
    let (min_x, min_y) = (x.min(placed.0), y.min(placed.1));
    let (max_x, max_y) = ((x + var.bounds.width()).max(placed.2), (y + var.bounds.height()).max(placed.3));
    let bbox_area = (max_x - min_x).max(0.0) * (max_y - min_y).max(0.0);
    let cx = x + var.bounds.width() / 2.0;
    let cy = y + var.bounds.height() / 2.0;
    let dir_key = if x_first {
        match dir {
            PlacementDirection::BottomRightToTopLeft => (-cx, cy),
            PlacementDirection::BottomLeftToTopRight => (cx, cy),
            PlacementDirection::TopRightToBottomLeft => (-cx, -cy),
            PlacementDirection::TopLeftToBottomRight => (cx, -cy),
        }
    } else {
        match dir {
            PlacementDirection::BottomRightToTopLeft => (cy, -cx),
            PlacementDirection::BottomLeftToTopRight => (cy, cx),
            PlacementDirection::TopRightToBottomLeft => (-cy, -cx),
            PlacementDirection::TopLeftToBottomRight => (-cy, cx),
        }
    };
    let (tx, ty) = match dir {
        PlacementDirection::BottomRightToTopLeft => (sheet.width, 0.0),
        PlacementDirection::BottomLeftToTopRight => (0.0, 0.0),
        PlacementDirection::TopRightToBottomLeft => (sheet.width, sheet.height),
        PlacementDirection::TopLeftToBottomRight => (0.0, sheet.height),
    };
    let corner_dist = (cx - tx).hypot(cy - ty);
    CandidateScore { bbox_area, dir_key, corner_dist }
}

/// Direction-biased compaction: slide parts toward the preferred corner
/// while keeping placements valid. Multiple passes until stable (bounded).
fn compact_placed(sheet: &SheetLoadInfo, placed: &mut [Placed], config: &Config, nfp_cache: &NfpCache) {
    let _g = prof::g(3);
    let memo = &nfp_cache.1;
    if placed.len() <= 1 || placed.iter().all(|p| p.fixed) {
        return;
    }
    let (dx_pref, dy_pref) = match config.placement_direction {
        PlacementDirection::BottomRightToTopLeft => (1.0, -1.0),
        PlacementDirection::BottomLeftToTopRight => (-1.0, -1.0),
        PlacementDirection::TopRightToBottomLeft => (1.0, 1.0),
        PlacementDirection::TopLeftToBottomRight => (-1.0, 1.0),
    };
    for _pass in 0..12 {
        let mut moved = false;
        for i in 0..placed.len() {
            if placed[i].fixed { continue; }
            let bx = placed[i].x; let by = placed[i].y;
            slide_axis(sheet, placed, i, dx_pref, 0.0, config.clipper_scale, memo);
            slide_axis(sheet, placed, i, 0.0, dy_pref, config.clipper_scale, memo);
            // try secondary order (y then x) for better packing
            slide_axis(sheet, placed, i, 0.0, dy_pref, config.clipper_scale, memo);
            slide_axis(sheet, placed, i, dx_pref, 0.0, config.clipper_scale, memo);
            if (placed[i].x - bx).abs() > 1e-6 || (placed[i].y - by).abs() > 1e-6 { moved = true; }
        }
        if !moved { break; }
    }
}

fn slide_axis(sheet: &SheetLoadInfo, placed: &mut [Placed], idx: usize, ax: f64, ay: f64, clip_scale: f64, memo: &PairMemo) {
    if ax == 0.0 && ay == 0.0 {
        return;
    }
    let max_step = sheet.width.max(sheet.height);
    let mut lo = 0.0f64;
    let mut hi = max_step;
    // find feasible upper bound quickly
    if !can_place_at(sheet, placed, idx, placed[idx].x + ax * hi, placed[idx].y + ay * hi, clip_scale, memo) {
        // binary search max feasible
        for _ in 0..24 {
            let mid = (lo + hi) * 0.5;
            if can_place_at(sheet, placed, idx, placed[idx].x + ax * mid, placed[idx].y + ay * mid, clip_scale, memo) {
                lo = mid;
            } else {
                hi = mid;
            }
        }
    } else {
        lo = hi;
    }
    if lo > 1e-6 {
        let factor = nest_clip::scale_factor(clip_scale);
        placed[idx].x = snap_to_clip(placed[idx].x + ax * lo, factor);
        placed[idx].y = snap_to_clip(placed[idx].y + ay * lo, factor);
    }
}

fn can_place_at(sheet: &SheetLoadInfo, placed: &[Placed], idx: usize, x: f64, y: f64, clip_scale: f64, memo: &PairMemo) -> bool {
    let factor = nest_clip::scale_factor(clip_scale);
    let x = snap_to_clip(x, factor);
    let y = snap_to_clip(y, factor);
    let var = &placed[idx];
    // Build a temporary variant-like check using stored geometry
    let eff = &var.effective_outer;
    if !inside_sheet(eff, (x, y), sheet.width, sheet.height) {
        return false;
    }
    if !inside_sheet(&var.detail.outer, (x, y), sheet.width, sheet.height) {
        return false;
    }
    let a = PairGeom { detail: &var.detail, hole_bounds: &var.hole_bounds, effective_outer: eff, bounds: var.bounds, at: (x, y) };
    for (j, pl) in placed.iter().enumerate() {
        if j == idx {
            continue;
        }
        if !pair_valid(&a, &PairGeom::from_placed(pl), factor, memo) {
            return false;
        }
    }
    true
}

/// Computes the part-against-part NFPs of the job up front, in parallel. The
/// orbiting NFP is the engine's most expensive step, and every attempt needs
/// the same ones. Skipped for jobs with very many distinct shapes, where the
/// attempts fill the cache with only what they use.
fn warm_nfp_cache(parts: &[ExpandedPart], cache: &NfpCache) {
    let _g = prof::g(0);
    let mut seen = std::collections::HashSet::new();
    // Keep each unique shape with its rotation: the rotation-aware lookup lets
    // quarter-turn copies share one orbit per canonical pair.
    let mut shapes: Vec<(&Nfp, f64)> = Vec::new();
    for part in parts {
        for var in &part.variants {
            if seen.insert(shape_sig(&var.nfp_outer)) {
                shapes.push((&var.nfp_outer, var.rotation));
            }
        }
    }
    if shapes.len() * shapes.len() > 4096 {
        return;
    }
    let pairs: Vec<(usize, usize)> = (0..shapes.len())
        .flat_map(|a| (a..shapes.len()).map(move |b| (a, b)))
        .collect();
    pairs.par_iter().for_each(|&(a, b)| {
        cache.get_rot(shapes[a].0, shapes[b].0, false, shapes[a].1, shapes[b].1);
    });
}

pub(crate) mod prof {
    use std::sync::atomic::{AtomicU64, Ordering};
    pub static T: [AtomicU64; 12] = [const { AtomicU64::new(0) }; 12];
    pub static N: [AtomicU64; 12] = [const { AtomicU64::new(0) }; 12];
    pub struct G(pub usize, pub std::time::Instant);
    impl Drop for G {
        fn drop(&mut self) {
            T[self.0].fetch_add(self.1.elapsed().as_nanos() as u64, Ordering::Relaxed);
            N[self.0].fetch_add(1, Ordering::Relaxed);
        }
    }
    pub fn g(i: usize) -> G { G(i, std::time::Instant::now()) }
    pub fn dump() {
        let names = ["warm", "candidates", "is_valid", "compact", "nfp_calc", "reinsert", "too_close", "minkowski", "blocks", "place_var", "score", "attempt"];
        for i in 0..12 {
            eprintln!("  {:<11} {:>9.1} ms  x{}", names[i], T[i].load(Ordering::Relaxed) as f64 / 1e6, N[i].load(Ordering::Relaxed));
        }
    }
}

/// Points where edges of different outlines cross. Only outlines of few edges
/// take part: exact slots occur between straight-sided parts, and a pair of
/// curved NFPs would cost tens of thousands of segment tests for nothing.
fn edge_crossings(outlines: &[&[SvgPoint]]) -> Vec<(f64, f64)> {
    const MAX_EDGES: usize = 32;
    let small: Vec<(&[SvgPoint], Bounds)> = outlines
        .iter()
        .filter(|o| o.len() >= 3 && o.len() <= MAX_EDGES)
        .filter_map(|o| bounds_of(o).map(|b| (*o, b)))
        .collect();
    let mut out = Vec::new();
    for i in 0..small.len() {
        for j in (i + 1)..small.len() {
            let ((a, ab), (b, bb)) = (small[i], small[j]);
            if ab.max_x < bb.min_x || bb.max_x < ab.min_x || ab.max_y < bb.min_y || bb.max_y < ab.min_y {
                continue;
            }
            for k in 0..a.len() {
                let (p, p2) = (a[k], a[(k + 1) % a.len()]);
                let (rx, ry) = (p2.x - p.x, p2.y - p.y);
                for l in 0..b.len() {
                    let (q, q2) = (b[l], b[(l + 1) % b.len()]);
                    let (sx, sy) = (q2.x - q.x, q2.y - q.y);
                    let denom = rx * sy - ry * sx;
                    if denom.abs() < 1e-12 {
                        continue; // parallel: the ends are vertices already
                    }
                    let t = ((q.x - p.x) * sy - (q.y - p.y) * sx) / denom;
                    let u = ((q.x - p.x) * ry - (q.y - p.y) * rx) / denom;
                    let eps = 1e-9;
                    if t >= -eps && t <= 1.0 + eps && u >= -eps && u <= 1.0 + eps {
                        out.push((p.x + t * rx, p.y + t * ry));
                    }
                }
            }
        }
    }
    out
}

#[cfg(test)]
mod mirror_variant_tests {
    use super::*;

    fn l_shape() -> RawDetail {
        // Chiral L: vertical bar + foot (its mirror is not a rotation).
        RawDetail {
            bends: Vec::new(),
            name: "L".into(),
            outer: vec![
                SvgPoint::new(0.0, 0.0),
                SvgPoint::new(10.0, 0.0),
                SvgPoint::new(10.0, 20.0),
                SvgPoint::new(20.0, 20.0),
                SvgPoint::new(20.0, 30.0),
                SvgPoint::new(0.0, 30.0),
            ],
            children: vec![],
        }
    }

    fn part(mirror_allowed: bool, rotations: Vec<f64>) -> DetailLoadInfo {
        DetailLoadInfo {
            name: "L".into(),
            quantity: 1,
            allowed_rotations: rotations,
            mirrored_quantity: 0,
            mirror_allowed,
            is_priority: false,
            ..Default::default()
        }
    }

    #[test]
    fn mirror_if_needed_adds_flipped_variants() {
        let cfg = Config::default();
        let cat = |_n: &str| l_shape();

        let plain = expand_parts(&[part(false, vec![0.0])], &cat, &cfg);
        assert_eq!(plain[0].variants.len(), 1);
        assert!(!plain[0].variants.iter().any(|v| v.mirrored));

        let allowed = expand_parts(&[part(true, vec![0.0])], &cat, &cfg);
        assert_eq!(allowed[0].variants.len(), 2, "front + flipped variants");
        assert!(allowed[0].variants.iter().any(|v| v.mirrored));
        // The flipped variant is the mirror image, not the same outline.
        let front = &allowed[0].variants.iter().find(|v| !v.mirrored).unwrap().detail.outer;
        let flipped = &allowed[0].variants.iter().find(|v| v.mirrored).unwrap().detail.outer;
        assert_ne!(
            front.iter().map(|p| (p.x, p.y)).collect::<Vec<_>>(),
            flipped.iter().map(|p| (p.x, p.y)).collect::<Vec<_>>()
        );
    }

    #[test]
    fn mirror_if_needed_skips_symmetric_shapes() {
        let cfg = Config::default();
        let rect = |_n: &str| RawDetail {
            bends: Vec::new(),
            name: "r".into(),
            outer: vec![
                SvgPoint::new(0.0, 0.0),
                SvgPoint::new(20.0, 0.0),
                SvgPoint::new(20.0, 10.0),
                SvgPoint::new(0.0, 10.0),
            ],
            children: vec![],
        };
        let allowed = expand_parts(&[DetailLoadInfo {
            name: "r".into(),
            quantity: 1,
            allowed_rotations: vec![0.0],
            mirrored_quantity: 0,
            mirror_allowed: true,
            is_priority: false,
            ..Default::default()
        }], &rect, &cfg);
        assert_eq!(allowed[0].variants.len(), 1, "a rectangle's mirror is itself");
    }

    #[test]
    fn mirrored_quantity_still_forces_exact_twins() {
        // The count column is unchanged: it demands exactly that many flipped copies.
        let cfg = Config::default();
        let cat = |_n: &str| l_shape();
        let mut p = part(false, vec![0.0]);
        p.quantity = 2;
        p.mirrored_quantity = 2;
        let parts = expand_parts(&[p], &cat, &cfg);
        assert_eq!(parts.len(), 4);
        let flipped = parts.iter().filter(|e| e.variants.iter().all(|v| v.mirrored)).count();
        assert_eq!(flipped, 2);
    }
}

#[cfg(test)]
mod nfp_rotation_tests {
    use super::*;

    fn l_shape() -> Nfp {
        Nfp::new(vec![
            SvgPoint::new(0.0, 0.0),
            SvgPoint::new(10.0, 0.0),
            SvgPoint::new(10.0, 20.0),
            SvgPoint::new(20.0, 20.0),
            SvgPoint::new(20.0, 30.0),
            SvgPoint::new(0.0, 30.0),
        ])
    }

    fn region_stats(nfps: &[Nfp]) -> (f64, f64, f64, f64, f64) {
        let mut min_x = f64::INFINITY;
        let mut min_y = f64::INFINITY;
        let mut max_x = f64::NEG_INFINITY;
        let mut max_y = f64::NEG_INFINITY;
        let mut area = 0.0f64;
        for n in nfps {
            area += nest_geom::polygon_area(&n.points).abs();
            for p in &n.points {
                min_x = min_x.min(p.x);
                min_y = min_y.min(p.y);
                max_x = max_x.max(p.x);
                max_y = max_y.max(p.y);
            }
        }
        (area, min_x, min_y, max_x, max_y)
    }

    #[test]
    fn quarter_turn_derivation_matches_the_orbit() {
        let a90 = rotated_normalized(&l_shape(), 90.0);
        let b180 = rotated_normalized(&l_shape(), 180.0);
        let direct = outer_nfp(&a90, &b180).expect("direct orbit NFP");

        let cache = NfpCache::default();
        let derived = cache
            .get_rot(&a90, &b180, false, 90.0, 180.0)
            .expect("derived NFP");

        let (da, dminx, dminy, dmaxx, dmaxy) = region_stats(&direct);
        let (ra, rminx, rminy, rmaxx, rmaxy) = region_stats(&derived);
        assert!((da - ra).abs() <= 1e-6 * da.max(1.0), "area {da} vs {ra}");
        for (d, r, what) in [
            (dminx, rminx, "min_x"),
            (dminy, rminy, "min_y"),
            (dmaxx, rmaxx, "max_x"),
            (dmaxy, rmaxy, "max_y"),
        ] {
            assert!((d - r).abs() <= 1e-6 * d.abs().max(1.0), "{what}: {d} vs {r}");
        }
    }

    #[test]
    fn quarter_turn_derivation_shares_one_orbit_per_canonical_pair() {
        let base = l_shape();
        let cache = NfpCache::default();
        // Four orientations of the same drawing: all must derive from one base.
        let mut results = Vec::new();
        for (ra, rb) in [(0.0, 0.0), (90.0, 90.0), (180.0, 180.0), (270.0, 270.0)] {
            let a = rotated_normalized(&base, ra);
            let b = rotated_normalized(&base, rb);
            results.push(cache.get_rot(&a, &b, false, ra, rb).expect("nfp"));
        }
        // Same pair relative rotation -> congruent regions (equal area).
        let areas: Vec<f64> = results.iter().map(|r| region_stats(r).0).collect();
        for w in areas.windows(2) {
            assert!((w[0] - w[1]).abs() <= 1e-6 * w[0].max(1.0), "areas {areas:?}");
        }
    }
}

#[cfg(test)]
mod nfp_reverse_order_tests {
    use super::*;

    fn p_shape() -> Nfp {
        // Chiral pentomino-ish hook so the reverse NFP is not symmetric.
        Nfp::new(vec![
            SvgPoint::new(0.0, 0.0),
            SvgPoint::new(30.0, 0.0),
            SvgPoint::new(30.0, 10.0),
            SvgPoint::new(10.0, 10.0),
            SvgPoint::new(10.0, 30.0),
            SvgPoint::new(0.0, 30.0),
        ])
    }

    fn stats(nfps: &[Nfp]) -> (f64, f64, f64, f64, f64) {
        let mut min_x = f64::INFINITY;
        let mut min_y = f64::INFINITY;
        let mut max_x = f64::NEG_INFINITY;
        let mut max_y = f64::NEG_INFINITY;
        let mut area = 0.0f64;
        for n in nfps {
            area += nest_geom::polygon_area(&n.points).abs();
            for p in &n.points {
                min_x = min_x.min(p.x); min_y = min_y.min(p.y);
                max_x = max_x.max(p.x); max_y = max_y.max(p.y);
            }
        }
        (area, min_x, min_y, max_x, max_y)
    }

    #[test]
    fn reversed_order_is_served_by_the_shared_orbit() {
        let base = p_shape();
        let a = rotated_normalized(&base, 90.0);
        let b = rotated_normalized(&base, 180.0);

        // Compute the forward pair (fills both canonical directions).
        let cache = NfpCache::default();
        let _ = cache.get_rot(&a, &b, false, 90.0, 180.0).expect("forward");
        let hits_before = cache.map_total();

        // The reversed query (parts swapped, rotations swapped) should be a hit.
        let rev = cache.get_rot(&b, &a, false, 180.0, 90.0).expect("reverse");
        let hits_after = cache.map_total();
        assert_eq!(hits_before, hits_after, "reverse order must reuse the orbit");

        // And it must match the direct orbit for the swapped pair.
        let direct = outer_nfp(&b, &a).expect("direct reverse NFP");
        let (da, dminx, dminy, dmaxx, dmaxy) = stats(&direct);
        let (ra, rminx, rminy, rmaxx, rmaxy) = stats(&rev);
        assert!((da - ra).abs() <= 1e-6 * da.max(1.0), "area {da} vs {ra}");
        for (d, r, what) in [
            (dminx, rminx, "min_x"), (dminy, rminy, "min_y"),
            (dmaxx, rmaxx, "max_x"), (dmaxy, rmaxy, "max_y"),
        ] {
            assert!((d - r).abs() <= 1e-6 * d.abs().max(1.0), "{what}: {d} vs {r}");
        }
    }
}
