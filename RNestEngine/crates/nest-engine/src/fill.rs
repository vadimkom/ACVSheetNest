//! "Fill the sheets": parts without a count (a sheet of washers) laid in rows
//! over the room a sheet still has. The search nest takes minutes over some
//! hundreds of equal parts; rows take a fraction of a second.
//!
//! One part is tried in every allowed angle, in straight rows, rows shifted by
//! half a step and columns shifted by half a step, each slid in quarters of a
//! step; the lay that seats the most copies is kept. A position counts when
//! the part, grown by half the spacing, lies inside the sheet's outline and
//! clear of the sheet's holes and of what already lies there.

use crate::geom::{bounds_of, normalize_detail, part_area, translate_points, Bounds};
use crate::svgnest::{effective_outline, rotations_for};
use crate::{Config, DetailLoadInfo, PartPlacement, PlacementDirection, SheetLoadInfo};
use nest_geom::SvgPoint;
use nest_io::RawDetail;
use rayon::prelude::*;

/// Overlaps smaller than this are clipping noise, mm².
const NOISE_AREA: f64 = 0.02;

/// A part in one angle, at its box origin.
struct Pose {
    rotation: f64,
    detail: RawDetail,
    /// The outline grown by half the spacing.
    eff: Vec<SvgPoint>,
    eb: Bounds,
}

/// What a sheet still has: its shape and the things in the way.
struct Room {
    width: f64,
    height: f64,
    outline: Option<Vec<SvgPoint>>,
    obstacles: Vec<(Bounds, Vec<SvgPoint>)>,
    factor: f64,
    direction: PlacementDirection,
}

fn overlap(a: &[SvgPoint], b: &[SvgPoint], factor: f64) -> bool {
    let both = nest_clip::intersection_paths_at_scale(&[a.to_vec()], &[b.to_vec()], factor);
    nest_clip::paths_area_at_scale(&both, factor) > NOISE_AREA
}

fn boxes_meet(a: &Bounds, b: &Bounds) -> bool {
    a.min_x < b.max_x && b.min_x < a.max_x && a.min_y < b.max_y && b.min_y < a.max_y
}

impl Room {
    fn new(sheet: &SheetLoadInfo, config: &Config) -> Self {
        let factor = nest_clip::scale_factor(config.clipper_scale);
        let mut room = Room {
            width: sheet.width,
            height: sheet.height,
            outline: (sheet.outline.len() >= 3).then(|| sheet.outline.clone()),
            obstacles: Vec::new(),
            factor,
            direction: config.placement_direction,
        };
        for hole in &sheet.holes {
            room.block(hole.clone());
        }
        for part in &sheet.occupied {
            room.block(effective_outline(&part.outer, config.spacing, config.clipper_scale));
        }
        room
    }

    fn block(&mut self, outline: Vec<SvgPoint>) {
        if let Some(b) = bounds_of(&outline) {
            self.obstacles.push((b, outline));
        }
    }

    /// The position of a pose whose box stands `(u, v)` from the corner the
    /// packing starts in.
    fn at(&self, pose: &Pose, u: f64, v: f64) -> (f64, f64) {
        use PlacementDirection::*;
        let from_right = matches!(self.direction, BottomRightToTopLeft | TopRightToBottomLeft);
        let from_top = matches!(self.direction, TopRightToBottomLeft | TopLeftToBottomRight);
        let x = if from_right { self.width - u - pose.eb.max_x } else { u - pose.eb.min_x };
        let y = if from_top { self.height - v - pose.eb.max_y } else { v - pose.eb.min_y };
        (x, y)
    }

    fn free(&self, pose: &Pose, x: f64, y: f64) -> bool {
        const TOL: f64 = 1e-6;
        let b = Bounds { min_x: pose.eb.min_x + x, min_y: pose.eb.min_y + y, max_x: pose.eb.max_x + x, max_y: pose.eb.max_y + y };
        if b.min_x < -TOL || b.min_y < -TOL || b.max_x > self.width + TOL || b.max_y > self.height + TOL {
            return false;
        }
        let near: Vec<&Vec<SvgPoint>> = self.obstacles.iter().filter(|(ob, _)| boxes_meet(ob, &b)).map(|(_, o)| o).collect();
        if self.outline.is_none() && near.is_empty() {
            return true;
        }
        let placed = translate_points(&pose.eff, x, y);
        if let Some(outline) = &self.outline {
            let outside = nest_clip::difference_paths_at_scale(&[placed.clone()], &[outline.clone()], self.factor);
            if nest_clip::paths_area_at_scale(&outside, self.factor) > NOISE_AREA {
                return false;
            }
        }
        near.iter().all(|o| !overlap(&placed, o, self.factor))
    }
}

/// A lattice: steps `a` along the row and `b` to the next row.
#[derive(Clone, Copy)]
struct Lattice {
    a: (f64, f64),
    b: (f64, f64),
}

/// True when copies of the pose on the lattice do not overlap each other.
fn clear(pose: &Pose, l: &Lattice, factor: f64) -> bool {
    let (w, h) = (pose.eb.width(), pose.eb.height());
    let shortest = [l.a, l.b].iter().map(|t| t.0.hypot(t.1)).fold(f64::INFINITY, f64::min);
    if shortest < 1e-6 {
        return false;
    }
    let reach = (((w.max(h)) / shortest).ceil() as i32 + 1).min(12);
    for j in 0..=reach {
        for i in -reach..=reach {
            if j == 0 && i <= 0 {
                continue;
            }
            let (tx, ty) = (i as f64 * l.a.0 + j as f64 * l.b.0, i as f64 * l.a.1 + j as f64 * l.b.1);
            if tx.abs() >= w || ty.abs() >= h {
                continue;
            }
            if overlap(&pose.eff, &translate_points(&pose.eff, tx, ty), factor) {
                return false;
            }
        }
    }
    true
}

/// The smallest `t` in `(0, top]` for which `make(t)` is a clear lattice;
/// `make(top)` must be one.
fn smallest(pose: &Pose, top: f64, factor: f64, make: impl Fn(f64) -> Lattice) -> f64 {
    let (mut lo, mut hi) = (0.0, top);
    for _ in 0..16 {
        let mid = (lo + hi) / 2.0;
        if clear(pose, &make(mid), factor) { hi = mid } else { lo = mid }
    }
    // The whole box is an exact fit: 25 parts of 40 go on 1000.
    if top - hi < 1e-3 { top } else { hi }
}

fn lattices(pose: &Pose, factor: f64) -> Vec<Lattice> {
    let (w, h) = (pose.eb.width(), pose.eb.height());
    let dx = smallest(pose, w, factor, |t| Lattice { a: (t, 0.0), b: (0.0, h) });
    let dy = smallest(pose, h, factor, |t| Lattice { a: (dx.max(w), 0.0), b: (0.0, t) });
    let mut out = vec![Lattice { a: (dx, 0.0), b: (0.0, dy) }];
    // Rows shifted by half a step.
    let row = smallest(pose, h, factor, |t| Lattice { a: (dx, 0.0), b: (dx / 2.0, t) });
    if clear(pose, &Lattice { a: (dx, 0.0), b: (dx / 2.0, row) }, factor) {
        out.push(Lattice { a: (dx, 0.0), b: (dx / 2.0, row) });
    }
    // Columns shifted by half a step.
    let col = smallest(pose, w, factor, |t| Lattice { a: (0.0, dy), b: (t, dy / 2.0) });
    if clear(pose, &Lattice { a: (0.0, dy), b: (col, dy / 2.0) }, factor) {
        out.push(Lattice { a: (0.0, dy), b: (col, dy / 2.0) });
    }
    out.retain(|l| clear(pose, l, factor));
    out
}

/// The free positions of a pose on a lattice slid by `(su, sv)`, nearest the
/// starting corner first.
fn seats(room: &Room, pose: &Pose, l: &Lattice, su: f64, sv: f64) -> Vec<(f64, f64)> {
    let (w, h) = (pose.eb.width(), pose.eb.height());
    let (max_u, max_v) = (room.width - w, room.height - h);
    let mut spots: Vec<(f64, f64)> = Vec::new();
    // Rows run along `a`; `b` steps to the next row.
    let along_x = l.a.1 == 0.0;
    let (a_len, b_along, b_across) = if along_x { (l.a.0, l.b.0, l.b.1) } else { (l.a.1, l.b.1, l.b.0) };
    let (max_along, max_across, s_along, s_across) = if along_x { (max_u, max_v, su, sv) } else { (max_v, max_u, sv, su) };
    if a_len <= 0.0 || b_across <= 0.0 {
        return Vec::new();
    }
    let mut j = 0;
    loop {
        let across = s_across + j as f64 * b_across;
        if across > max_across + 1e-9 {
            break;
        }
        // The first copy of the row at or after the edge.
        let start = s_along + j as f64 * b_along;
        let mut along = start - (start / a_len).floor() * a_len;
        while along <= max_along + 1e-9 {
            spots.push(if along_x { (along, across) } else { (across, along) });
            along += a_len;
        }
        j += 1;
    }
    spots.sort_by(|p, q| p.1.total_cmp(&q.1).then(p.0.total_cmp(&q.0)));
    spots.into_iter().map(|(u, v)| room.at(pose, u, v)).filter(|&(x, y)| room.free(pose, x, y)).collect()
}

fn poses(part: &DetailLoadInfo, source: &RawDetail, config: &Config) -> Vec<Pose> {
    let mut out: Vec<Pose> = Vec::new();
    for rotation in rotations_for(part, config) {
        let detail = normalize_detail(source, rotation, false);
        let eff = effective_outline(&detail.outer, config.spacing, config.clipper_scale);
        let Some(eb) = bounds_of(&eff) else { continue };
        // A square or a circle turned a quarter is the same pose.
        let same = |p: &Pose| (p.eb.width() - eb.width()).abs() < 1e-6 && (p.eb.height() - eb.height()).abs() < 1e-6
            && p.detail.outer.len() == detail.outer.len()
            && p.detail.outer.iter().zip(&detail.outer).all(|(a, b)| (a.x - b.x).abs() < 1e-6 && (a.y - b.y).abs() < 1e-6);
        if !out.iter().any(same) {
            out.push(Pose { rotation, detail, eff, eb });
        }
    }
    out
}

/// A part ready to fill with: its poses and their lattices.
struct Filler<'a> {
    part: &'a DetailLoadInfo,
    poses: Vec<(Pose, Vec<Lattice>)>,
}

impl Filler<'_> {
    /// The lay that seats the most copies in the room: the pose and where.
    fn best(&self, room: &Room) -> Option<(&Pose, Vec<(f64, f64)>)> {
        let mut tries: Vec<(usize, usize, f64, f64)> = Vec::new();
        for (pi, (_, ls)) in self.poses.iter().enumerate() {
            for (li, l) in ls.iter().enumerate() {
                let (step_u, step_v) = (l.a.0.abs().max(l.b.0.abs()), l.a.1.abs().max(l.b.1.abs()));
                for ku in 0..4 {
                    for kv in 0..4 {
                        tries.push((pi, li, step_u * ku as f64 / 4.0, step_v * kv as f64 / 4.0));
                    }
                }
            }
        }
        let found: Vec<Vec<(f64, f64)>> = tries.par_iter().map(|&(pi, li, su, sv)| seats(room, &self.poses[pi].0, &self.poses[pi].1[li], su, sv)).collect();
        // The most copies; of equal ones the first tried (deterministic).
        let mut best: Option<usize> = None;
        for (i, f) in found.iter().enumerate() {
            if !f.is_empty() && best.is_none_or(|b| f.len() > found[b].len()) {
                best = Some(i);
            }
        }
        let i = best?;
        let mut found = found;
        Some((&self.poses[tries[i].0].0, std::mem::take(&mut found[i])))
    }
}

fn placement(part: &DetailLoadInfo, pose: &Pose, x: f64, y: f64) -> PartPlacement {
    PartPlacement {
        id: 0,
        name: part.name.clone(),
        sheet_id: 0,
        x,
        y,
        rotation: pose.rotation,
        is_priority: part.is_priority,
        outer: translate_points(&pose.detail.outer, x, y),
        holes: pose.detail.children.iter().map(|c| translate_points(c, x, y)).collect(),
        local_outer: pose.detail.outer.clone(),
        local_holes: pose.detail.children.clone(),
        local_bends: pose.detail.bends.clone(),
    }
}

fn stopped(config: &Config) -> bool {
    config.cancel.as_ref().is_some_and(|c| c.load(std::sync::atomic::Ordering::Relaxed))
}

/// Lays the fillers over the room one after another, each as many as fit but
/// no more than `limit` copies; returns what went on, per filler.
fn lay(mut room: Room, fillers: &[Filler], limit: Option<usize>, config: &Config) -> Vec<Vec<PartPlacement>> {
    let mut out = Vec::new();
    for f in fillers {
        let mut placed = Vec::new();
        if !stopped(config) {
            if let Some((pose, mut spots)) = f.best(&room) {
                if let Some(n) = limit {
                    spots.truncate(n);
                }
                for (x, y) in spots {
                    room.block(translate_points(&pose.eff, x, y));
                    placed.push(placement(f.part, pose, x, y));
                }
            }
        }
        out.push(placed);
    }
    out
}

/// Fills the free room of a sheet with parts that have no count. `sheet.occupied`
/// is what lies on it already. Bigger fillers go first.
///
/// `as_kit`: several fillers are one kit (a bracket of two parts) and come out
/// as many of each; otherwise each takes all the room it finds.
///
/// The placements carry `id` 0 and `sheet_id` 0: the caller numbers them.
pub fn fill_sheet(
    sheet: &SheetLoadInfo,
    parts: &[DetailLoadInfo],
    catalogue: impl Fn(&str) -> RawDetail,
    as_kit: bool,
    config: &Config,
) -> Vec<PartPlacement> {
    let factor = nest_clip::scale_factor(config.clipper_scale);
    let mut sized: Vec<(f64, Filler)> = Vec::new();
    for part in parts {
        let source = catalogue(&part.name);
        let poses: Vec<(Pose, Vec<Lattice>)> = poses(part, &source, config).into_iter().map(|p| { let ls = lattices(&p, factor); (p, ls) }).collect();
        if !poses.is_empty() {
            sized.push((part_area(&source), Filler { part, poses }));
        }
    }
    sized.sort_by(|a, b| b.0.total_cmp(&a.0));
    let fillers: Vec<Filler> = sized.into_iter().map(|(_, f)| f).collect();
    if fillers.is_empty() {
        return Vec::new();
    }
    let room = || Room::new(sheet, config);
    if !as_kit || fillers.len() < 2 {
        return lay(room(), &fillers, None, config).into_iter().flatten().collect();
    }
    // A kit: the most sets for which every filler still gets its copy.
    let whole = |n: usize| -> Option<Vec<Vec<PartPlacement>>> {
        let laid = lay(room(), &fillers, Some(n), config);
        laid.iter().all(|p| p.len() >= n).then_some(laid)
    };
    let most = fillers[0].best(&room()).map_or(0, |(_, spots)| spots.len());
    let (mut lo, mut hi) = (0usize, most);
    let mut kept: Vec<Vec<PartPlacement>> = Vec::new();
    while lo < hi && !stopped(config) {
        let mid = (lo + hi).div_ceil(2);
        match whole(mid) {
            Some(laid) => { lo = mid; kept = laid; }
            None => hi = mid - 1,
        }
    }
    kept.into_iter().flatten().collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geom::placements_too_close;

    fn circle(d: f64) -> RawDetail {
        let n = 48;
        let outer = (0..n).map(|i| {
            let a = i as f64 / n as f64 * std::f64::consts::TAU;
            SvgPoint::new(d / 2.0 + d / 2.0 * a.cos(), d / 2.0 + d / 2.0 * a.sin())
        }).collect();
        RawDetail { bends: Vec::new(), name: "c".into(), outer, children: vec![] }
    }

    fn rect(w: f64, h: f64) -> RawDetail {
        RawDetail { bends: Vec::new(), name: "r".into(), outer: vec![SvgPoint::new(0.0, 0.0), SvgPoint::new(w, 0.0), SvgPoint::new(w, h), SvgPoint::new(0.0, h)], children: vec![] }
    }

    fn part(name: &str) -> DetailLoadInfo {
        DetailLoadInfo { name: name.into(), allowed_rotations: vec![0.0], ..Default::default() }
    }

    fn sheet(w: f64, h: f64) -> SheetLoadInfo {
        SheetLoadInfo { width: w, height: h, quantity: 1, ..Default::default() }
    }

    fn close(a: &[SvgPoint], b: &[SvgPoint], clearance: f64) -> bool {
        placements_too_close(a, &[], (0.0, 0.0), b, &[], (0.0, 0.0), clearance, 0.05, 1.0)
    }

    fn apart(placed: &[PartPlacement], spacing: f64) {
        for (i, a) in placed.iter().enumerate() {
            for b in &placed[i + 1..] {
                assert!(!close(&a.outer, &b.outer, spacing - 0.1), "{} and {} closer than the spacing", a.name, b.name);
            }
        }
    }

    #[test]
    fn washers_fill_a_sheet_in_shifted_rows() {
        let cfg = Config { spacing: 4.0, ..Config::default() };
        let started = std::time::Instant::now();
        let placed = fill_sheet(&sheet(1250.0, 500.0), &[part("c")], |_| circle(24.0), true, &cfg);
        // Straight rows seat 44 x 17 = 748; shifted rows more.
        assert!(placed.len() > 760, "{} washers", placed.len());
        assert!(started.elapsed().as_secs_f64() < 5.0);
        for p in &placed {
            let b = bounds_of(&p.outer).unwrap();
            assert!(b.min_x >= 1.9 && b.min_y >= 1.9 && b.max_x <= 1248.1 && b.max_y <= 498.1);
        }
        apart(&placed[..60], 4.0);
    }

    #[test]
    fn fillers_keep_clear_of_parts_and_of_the_offcut_shape() {
        let cfg = Config { spacing: 4.0, ..Config::default() };
        let mut sh = sheet(600.0, 400.0);
        // An L: the top right quarter is gone.
        sh.outline = vec![SvgPoint::new(0.0, 0.0), SvgPoint::new(600.0, 0.0), SvgPoint::new(600.0, 200.0), SvgPoint::new(300.0, 200.0), SvgPoint::new(300.0, 400.0), SvgPoint::new(0.0, 400.0)];
        let lying = rect(200.0, 100.0);
        sh.occupied = vec![PartPlacement {
            id: 0, name: "r".into(), sheet_id: 0, x: 0.0, y: 0.0, rotation: 0.0, is_priority: false,
            outer: translate_points(&lying.outer, 2.0, 2.0), holes: vec![], local_outer: lying.outer.clone(), local_holes: vec![], local_bends: vec![],
        }];
        let placed = fill_sheet(&sh, &[part("c")], |_| circle(40.0), true, &cfg);
        assert!(placed.len() > 60, "{} placed", placed.len());
        for p in &placed {
            let b = bounds_of(&p.outer).unwrap();
            assert!(!(b.max_x > 300.0 && b.max_y > 200.0), "a filler outside the offcut");
            assert!(!close(&p.outer, &sh.occupied[0].outer, 3.9), "a filler on the part");
        }
        apart(&placed, 4.0);
    }

    #[test]
    fn a_kit_comes_out_as_many_of_each_and_the_maximum_does_not() {
        let cfg = Config { spacing: 4.0, ..Config::default() };
        let cat = |name: &str| if name == "big" { rect(200.0, 100.0) } else { rect(60.0, 40.0) };
        let count = |placed: &[PartPlacement], name: &str| placed.iter().filter(|p| p.name == name).count();
        let kit = fill_sheet(&sheet(820.0, 524.0), &[part("big"), part("small")], cat, true, &cfg);
        assert!(count(&kit, "big") > 5);
        assert_eq!(count(&kit, "big"), count(&kit, "small"));
        apart(&kit, 4.0);
        let most = fill_sheet(&sheet(820.0, 524.0), &[part("big"), part("small")], cat, false, &cfg);
        assert!(count(&most, "big") > count(&kit, "big"));
        apart(&most, 4.0);
    }
}
