//! Parametric shapes without a DXF. Ports `PartShapes`/`SheetShape` of
//! ACVSheetNest: rectangle, square, circle, washer (DIN 125 / DIN 9021 / custom),
//! perforated tape.

use nest_geom::SvgPoint;

/// Polygon from points that are all drawing vertices.
fn poly(points: Vec<SvgPoint>) -> Vec<SvgPoint> {
    points
}

/// `+ Rectangle` — one `RawDetail` of given size (0,0)-anchored.
pub fn rectangle(width: f64, height: f64) -> crate::RawDetail {
    let inv = 1.0 / width.max(1e-9);
    // proportional anchors just for stable ordering; real points below
    let _ = inv;
    crate::RawDetail {
        bends: Vec::new(),
        name: format!("rect_{width}x{height}"),
        outer: poly(points_of_quad(width, height)),
        children: Vec::new(),
    }
}

/// `+ Square`.
pub fn square(side: f64) -> crate::RawDetail {
    rectangle(side, side)
}

/// `+ Circle` of the given diameter.
pub fn circle(diameter: f64) -> crate::RawDetail {
    let outer = cylinder_ring(diameter, 0.0);
    crate::RawDetail {
        bends: Vec::new(),
        name: format!("circle_d{diameter}"),
        outer,
        children: Vec::new(),
    }
}

/// `+ Washer`: outer diameter plus one through-hole. `inner_diameter = 0` is the
/// plain circle. Standard bores: DIN 125 part fits on bolt diameter; DIN 9021 widens.
/// This mirrors `PartShapes.Washer(outerDiameter, innerDiameter)`.
pub fn washer(outer_diameter: f64, inner_diameter: f64) -> crate::RawDetail {
    let outer = cylinder_ring(outer_diameter, 0.0);
    let hole = if inner_diameter > 0.0 {
        vec![cylinder_ring(inner_diameter, 0.0)]
    } else {
        Vec::new()
    };
    crate::RawDetail {
        bends: Vec::new(),
        name: format!("washer_{outer_diameter}x{inner_diameter}"),
        outer,
        children: hole,
    }
}

/// A ring approximated in both `angle` resolution and the naming of the acvent shop.
fn cylinder_ring(diameter: f64, phase: f64) -> Vec<SvgPoint> {
    let radius = diameter / 2.0;
    let steps = 24.max((diameter * 0.8) as usize);
    (0..steps)
        .map(|i| {
            let angle = phase + (i as f64) * std::f64::consts::TAU / (steps as f64);
            SvgPoint::new(radius * angle.cos(), radius * angle.sin())
        })
        .collect()
}

fn points_of_quad(width: f64, height: f64) -> Vec<SvgPoint> {
    vec![
        SvgPoint::new(0.0, 0.0),
        SvgPoint::new(width, 0.0),
        SvgPoint::new(width, height),
        SvgPoint::new(0.0, height),
    ]
}

/// Perforated tape: a rectangular strip with a row of circular holes along its length.
/// Holes are centered across the width, spaced by `pitch`, starting at `edge_margin` from each end.
pub fn perforated_tape(length: f64, width: f64, hole_diameter: f64, pitch: f64) -> crate::RawDetail {
    let length = length.max(1.0);
    let width = width.max(1.0);
    let hole_d = hole_diameter.max(0.1).min(width * 0.9);
    let pitch = pitch.max(hole_d + 0.1);
    let edge = (pitch / 2.0).min(length / 2.0);
    let outer = points_of_quad(length, width);
    let cy = width / 2.0;
    let mut children = Vec::new();
    let mut x = edge;
    // place holes while center fits within margins
    while x <= length - edge + 1e-9 {
        let ring = cylinder_ring(hole_d, 0.0);
        let hole = ring.into_iter().map(|p| SvgPoint::new(p.x + x, p.y + cy)).collect();
        children.push(hole);
        x += pitch;
    }
    crate::RawDetail {
        bends: Vec::new(),
        name: format!("tape_{length}x{width}_d{hole_d}_p{pitch}"),
        outer,
        children,
    }
}

/// The perforated blanks of the shop, built from their pattern (no drawing).
/// Ports `PerforatedTapeShape` and `ProfileShape` of ACVSheetNest (PartShapes.cs).
/// The blank is the sheet-sized piece; the bridged cuts between strips are not
/// modelled here, only the holes and slots.
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum PerforatedKind {
    /// Tape, strip 26, big Ø8.2 every 25, small Ø3.5 in pairs (the shop's drawing).
    B26,
    /// Tape, strip 25, big Ø8, small Ø3.2.
    B25,
    /// Tape, strip 17, big Ø6.2, small Ø3.2.
    B17,
    /// Profile 100 wide: round Ø10 every 62.5 with a slot after each.
    B100,
    /// Profile 66 wide.
    B66,
}

impl PerforatedKind {
    pub const ALL: [PerforatedKind; 5] = [Self::B26, Self::B25, Self::B17, Self::B100, Self::B66];

    pub fn is_profile(self) -> bool {
        matches!(self, Self::B100 | Self::B66)
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::B26 => "B26 Ø8.2",
            Self::B25 => "B25 Ø8",
            Self::B17 => "B17 Ø6.2",
            Self::B100 => "B100",
            Self::B66 => "B66",
        }
    }
}

/// A circle of `diameter` centred at (`x`, `y`).
fn circle_at(x: f64, y: f64, diameter: f64) -> Vec<nest_geom::SvgPoint> {
    cylinder_ring(diameter, 0.0).into_iter().map(|p| SvgPoint::new(p.x + x, p.y + y)).collect()
}

/// An obround slot of `length` × `width` centred at (`x`, `y`), along x.
fn slot_at(x: f64, y: f64, length: f64, width: f64) -> Vec<nest_geom::SvgPoint> {
    let r = width / 2.0;
    let d = (length - width) / 2.0;
    let mut out = Vec::new();
    for i in 0..=16 {
        let a = -std::f64::consts::FRAC_PI_2 + i as f64 * std::f64::consts::PI / 16.0;
        out.push(SvgPoint::new(x + d + r * a.cos(), y + r * a.sin()));
    }
    for i in 0..=16 {
        let a = std::f64::consts::FRAC_PI_2 + i as f64 * std::f64::consts::PI / 16.0;
        out.push(SvgPoint::new(x - d + r * a.cos(), y + r * a.sin()));
    }
    out
}

/// Builds a perforated blank of `length` × `width` mm. Errors name what does not fit.
pub fn perforated(kind: PerforatedKind, length: f64, width: f64) -> Result<crate::RawDetail, String> {
    if !(length > 0.0 && width > 0.0) {
        return Err("the blank needs a length and a width".into());
    }
    let children = if kind.is_profile() {
        profile_holes(kind, length, width)?
    } else {
        tape_holes(kind, length, width)?
    };
    Ok(crate::RawDetail {
        bends: Vec::new(),
        name: format!("{}_{length}x{width}", kind.label()),
        outer: points_of_quad(length, width),
        children,
    })
}

/// Tape: `strips` of the blank, each with a row of groups along it. A group is a
/// big hole in the middle and four small ones around it. Where the small holes of
/// two neighbouring groups meet (pitch = 2 × small offset), the right pair of a
/// group is the left pair of the next, so it is cut once.
fn tape_holes(kind: PerforatedKind, length: f64, width: f64) -> Result<Vec<Vec<nest_geom::SvgPoint>>, String> {
    let (strip, big, small, pitch, row_gap, small_offset) = match kind {
        PerforatedKind::B26 => (26.0, 8.2, 3.5, 25.0, 18.0, 12.5),
        PerforatedKind::B25 => (25.0, 8.0, 3.2, 20.0, 10.0, 5.0),
        _ => (17.0, 6.2, 3.2, 15.0, 8.0, 5.0),
    };
    let strips = (width / strip).floor() as usize;
    if strips < 1 {
        return Err("the blank is narrower than one strip".into());
    }
    if big >= strip || row_gap / 2.0 + small / 2.0 >= strip / 2.0 {
        return Err("the holes do not fit in a strip".into());
    }
    let reach = (small_offset + small / 2.0).max(big / 2.0);
    let groups_f = ((length - 2.0 * reach - 2.0 * small) / pitch).floor() + 1.0;
    if groups_f < 1.0 {
        return Err("the blank is too short for one group".into());
    }
    let groups = groups_f as usize;
    let first = (length - (groups as f64 - 1.0) * pitch) / 2.0;
    let shared = (pitch - 2.0 * small_offset).abs() < 1e-6;
    let mut out = Vec::new();
    for k in 0..strips {
        let yc = (k as f64 + 0.5) * strip;
        for g in 0..groups {
            let xc = first + g as f64 * pitch;
            out.push(circle_at(xc, yc, big));
            for dy in [-row_gap / 2.0, row_gap / 2.0] {
                out.push(circle_at(xc - small_offset, yc + dy, small));
                if !(shared && g + 1 < groups) {
                    out.push(circle_at(xc + small_offset, yc + dy, small));
                }
            }
        }
    }
    Ok(out)
}

/// Profile: one row of round holes along the middle, a slot after each but the last.
fn profile_holes(kind: PerforatedKind, length: f64, width: f64) -> Result<Vec<Vec<nest_geom::SvgPoint>>, String> {
    let (hole, pitch, slot_offset, slot_length, slot_width) = (10.0, 62.5, 32.5, 28.0, 10.0);
    if width <= hole || slot_width >= width {
        return Err(format!("{} is narrower than its holes", kind.label()));
    }
    let holes_f = ((length - 3.0 * hole) / pitch).floor() + 1.0;
    if holes_f < 1.0 {
        return Err("the blank is too short for one hole".into());
    }
    let holes = holes_f as usize;
    let first = (length - (holes as f64 - 1.0) * pitch) / 2.0;
    let y = width / 2.0;
    let mut out = Vec::new();
    for i in 0..holes {
        let xc = first + i as f64 * pitch;
        out.push(circle_at(xc, y, hole));
        if i + 1 < holes {
            out.push(slot_at(xc + slot_offset, y, slot_length, slot_width));
        }
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn b26_strip_of_1237_8_mm_has_49_big_and_100_small_holes_as_the_drawing() {
        // One strip: the drawing's count, with the shared small holes cut once.
        let blank = perforated(PerforatedKind::B26, 1237.8, 26.0).expect("blank");
        assert_eq!(blank.children.len(), 49 + 100);
    }

    #[test]
    fn b100_2000_mm_has_32_holes_and_31_slots() {
        let blank = perforated(PerforatedKind::B100, 2000.0, 100.0).expect("blank");
        assert_eq!(blank.children.len(), 32 + 31);
    }

    #[test]
    fn a_wide_blank_gets_every_strip() {
        let one = perforated(PerforatedKind::B25, 1000.0, 25.0).expect("one");
        // 250 / 25 = 10 strips, each with the same groups.
        let ten = perforated(PerforatedKind::B25, 1000.0, 250.0).expect("ten");
        assert_eq!(ten.children.len(), 10 * one.children.len());
    }

    #[test]
    fn washer_has_one_hole() {
        let washer = washer(24.0, 9.0);
        assert_eq!(washer.children.len(), 1);
        assert_eq!(washer.outer.len(), 24.max((24.0 * 0.8) as usize));
    }
}
