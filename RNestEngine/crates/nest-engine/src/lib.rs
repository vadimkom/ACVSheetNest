//! The nesting engine.
//!
//! Ported from `DeepNestLib.Core` (ACVSheetNest): `SvgNest.cs` (nest loop + genetic
//! algorithm), `Placement/PlacementWorker.cs`, `Placement/PartPlacementWorker.cs`,
//! `Placement/NfpHelper.cs`, `Placement/Compaction.cs`, `Sheet.cs`,
//! `NestProject/{DetailLoadInfo,SheetLoadInfo}.cs`.
//!
//! # Milestone 1 shape
//! - `Config` mirrors the essential `SvgNestConfig` fields.
//! - `nest()` takes load infos (sheets + parts) and a progress callback and returns a
//!   `NestResult` of one best placement per sheet, matching ACVSheetNest's single
//!   "▶ Nest" press.

use nest_io::RawDetail;
use thiserror::Error;

/// Placement strategy. Ported from `PlacementTypeEnum` (Gravity = SVGnest default).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, serde::Serialize, serde::Deserialize)]
pub enum PlacementType {
    #[default]
    Gravity,
    Grid,
}

/// Direction in which parts are packed on a sheet, configurable in settings.
///
/// The default is [`PlacementDirection::BottomRightToTopLeft`]: start at the
/// bottom-right corner, fill leftwards along the bottom, then upwards. This is
/// the primary nesting mode requested for this project.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, serde::Serialize, serde::Deserialize)]
pub enum PlacementDirection {
    /// Right-to-left, bottom-to-top (primary mode).
    #[default]
    BottomRightToTopLeft,
    /// Left-to-right, bottom-to-top.
    BottomLeftToTopRight,
    /// Right-to-left, top-to-bottom.
    TopRightToBottomLeft,
    /// Left-to-right, top-to-bottom.
    TopLeftToBottomRight,
}

/// Essential configuration. Ported from `SvgNestConfig`.
/// `#[serde(default)]` lets older job/profile files omit newer fields.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(default)]
pub struct Config {
    /// Part spacing in units.
    pub spacing: f64,
    /// Rotation steps allowed per part, degrees (0 = no rotation).
    pub rotations: u16,
    /// Clipper integer scale (`ClipperScale`).
    pub clipper_scale: f64,
    /// `UsePriority`: priority parts are placed first on separate sheets.
    pub use_priority: bool,
    pub placement_type: PlacementType,
    /// Packing direction (default: bottom-right to top-left).
    #[serde(default)]
    pub placement_direction: PlacementDirection,
    /// GA population (log), as in `SvgNestConfig.Populations` semantics.
    pub populations: u32,
    /// GA generations per population.
    pub generations: u32,
    /// Blow-up of the space searched during placement (`Blowup`).
    pub blowup: f64,
    /// Re-insertion descent sweeps after placement+compaction (port of
    /// `DeepNestLib Placement/Reinsertion.cs`). 0 disables the pass.
    #[serde(default = "default_reinsertion_passes")]
    pub reinsertion_passes: u32,
    /// Set to stop a running nest: it returns [`NestError::Cancelled`] soon after.
    #[serde(skip)]
    pub cancel: Option<std::sync::Arc<std::sync::atomic::AtomicBool>>,
}

fn default_reinsertion_passes() -> u32 {
    3
}

impl Default for Config {
    fn default() -> Self {
        // Values from SvgNestConfig defaults in ACVSheetNest, with the packing
        // direction defaulted to the project's primary mode.
        Self {
            spacing: 0.0,
            rotations: 0,
            // Higher than the C# default (10): finer clipper integer grid avoids
            // 0.05-unit placement snapping (visible as slivers between parts).
            clipper_scale: 100.0,
            use_priority: false,
            placement_type: PlacementType::Gravity,
            placement_direction: PlacementDirection::BottomRightToTopLeft,
            populations: 1,
            generations: 1,
            blowup: 50.0,
            reinsertion_passes: default_reinsertion_passes(),
            cancel: None,
        }
    }
}

/// Sheet stock of the job. Ported from `SheetLoadInfo`.
#[derive(Debug, Clone, Default, serde::Serialize, serde::Deserialize)]
pub struct SheetLoadInfo {
    /// 0 = rectangular stock; otherwise a real outline drawn like an offcut.
    pub width: f64,
    pub height: f64,
    pub quantity: u32,
    /// Optional name, e.g. the offcut's file name.
    pub source: Option<String>,
    /// The outline of an offcut of any shape, in sheet coordinates (its box is
    /// `0..width` by `0..height`). Empty = the plain rectangle.
    #[serde(default)]
    pub outline: Vec<nest_geom::SvgPoint>,
    /// Holes of the sheet (cut-outs already made), in sheet coordinates.
    #[serde(default)]
    pub holes: Vec<Vec<nest_geom::SvgPoint>>,
    /// Parts that already lie on this sheet. They stay where they are and the
    /// nest fills the room left; they are not repeated in the result.
    #[serde(default)]
    pub occupied: Vec<PartPlacement>,
    /// Stock with a lower number is used first (offcuts before whole sheets).
    #[serde(default)]
    pub priority: i32,
}

/// A part as the user asked for it. Ported from `DetailLoadInfo`.
#[derive(Debug, Clone, Default, serde::Serialize, serde::Deserialize)]
pub struct DetailLoadInfo {
    pub name: String,
    pub quantity: u32,
    /// Allowed rotations, degrees (empty = no rotation).
    #[serde(default)]
    pub allowed_rotations: Vec<f64>,
    /// Mirrored copies (`Mirrored` column): quantity of left/right twins.
    #[serde(default)]
    pub mirrored_quantity: u32,
    /// Let the nester use a mirrored copy **if it packs better** (the
    /// "mirror if needed" flag), as opposed to `mirrored_quantity`, which
    /// demands an exact count of mirrored twins.
    #[serde(default)]
    pub mirror_allowed: bool,
    #[serde(default)]
    pub is_priority: bool,
    /// Placement rank: parts with a lower number are placed first, so a part
    /// of a worse rank never takes the room of a better one. Equal = no order.
    #[serde(default)]
    pub priority: i32,
}

/// A loaded part: geometry + instructions. Ported from the `Detail`/`IPart` role.
#[derive(Debug, Clone)]
pub struct Detail {
    pub id: usize,
    pub name: String,
    pub source: RawDetail,
    pub allowed_rotations: Vec<f64>,
    pub is_priority: bool,
}

#[derive(Debug, Error)]
pub enum NestError {
    #[error("no sheets")]
    NoSheets,
    #[error("no parts")]
    NoParts,
    #[error("IO: {0}")]
    Io(#[from] nest_io::IoError),
    #[error("cancelled")]
    Cancelled,
    #[error("placing {part}: {msg}")]
    Placement { part: String, msg: String },
}

/// One part placed on one sheet. Port of `PartPlacement`.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct PartPlacement {
    pub id: usize,
    pub name: String,
    pub sheet_id: usize,
    pub x: f64,
    pub y: f64,
    pub rotation: f64,
    pub is_priority: bool,
    /// Placed outer contour in sheet/world coordinates (ready for render/export).
    #[serde(default)]
    pub outer: Vec<nest_geom::SvgPoint>,
    /// Placed hole contours in sheet/world coordinates.
    #[serde(default)]
    pub holes: Vec<Vec<nest_geom::SvgPoint>>,
    /// Local outer contour in placement orientation, normalized to (0,0) at placement origin (x,y).
    #[serde(default)]
    pub local_outer: Vec<nest_geom::SvgPoint>,
    /// Local hole contours in placement orientation.
    #[serde(default)]
    pub local_holes: Vec<Vec<nest_geom::SvgPoint>>,
    /// Bend lines in placement orientation, same frame as `local_outer` (not cut).
    #[serde(default)]
    pub local_bends: Vec<Vec<nest_geom::SvgPoint>>,
}

/// All parts placed onto one sheet. Port of `SheetPlacement` (thin for milestone 1).
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct SheetPlacement {
    pub sheet_id: usize,
    pub sheet_width: f64,
    pub sheet_height: f64,
    pub placements: Vec<PartPlacement>,
    /// Merged cut length if computed (`MergedLength`).
    pub merged_length: f64,
    /// The outline of the sheet when it is an offcut of some shape (empty = the
    /// rectangle), and its holes; sheet coordinates.
    #[serde(default)]
    pub outline: Vec<nest_geom::SvgPoint>,
    #[serde(default)]
    pub holes: Vec<Vec<nest_geom::SvgPoint>>,
    /// A sheet the shop does not have: stock to buy for the parts left over.
    #[serde(default)]
    pub virtual_stock: bool,
}

/// The result of one nest. Port of `NestResult` (essentials).
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct NestResult {
    /// Fitness of the final placement (used area ratio; higher is better).
    pub fitness: f64,
    pub sheets: Vec<SheetPlacement>,
    /// Parts that could not be placed.
    pub unplaced: Vec<String>,
    /// For each name of `unplaced`: the copy left over is a mirrored twin.
    #[serde(default)]
    pub unplaced_flipped: Vec<bool>,
    pub placement_type: PlacementType,
    /// Elapsed, ms.
    pub duration_ms: u64,
}

/// Whether a part fits anywhere at all (used in diagnostics).
pub struct Stats {
    pub fitness: f64,
    pub part_count: usize,
}

pub mod engine;
pub mod geom;
pub use engine::{CatalogueBuilder, ResolvedCatalogue, rectangle_detail};
mod svgnest;
mod fill;
pub use fill::fill_sheet;

pub use engine::nest;

#[cfg(test)]
mod tests {
    use super::*;

    fn two_sheets_two_parts() -> (Vec<SheetLoadInfo>, Vec<DetailLoadInfo>) {
        let sheets = vec![SheetLoadInfo {
            width: 100.0,
            height: 50.0,
            quantity: 2,
            source: None,
            ..Default::default()
        }];
        let parts = vec![
            DetailLoadInfo {
                name: "plate".into(),
                quantity: 2,
                allowed_rotations: vec![],
                mirrored_quantity: 0, mirror_allowed: false,
                is_priority: false,
                ..Default::default()
            },
            DetailLoadInfo {
                name: "arm".into(),
                quantity: 1,
                allowed_rotations: vec![],
                mirrored_quantity: 0, mirror_allowed: false,
                is_priority: false,
                ..Default::default()
            },
        ];
        (sheets, parts)
    }

    #[test]
    fn nest_is_deterministic_and_places_all_parts() {
        let (sheets, parts) = two_sheets_two_parts();
        let catalogue = |name: &str| -> RawDetail {
            let (w, h) = match name {
                "plate" => (30.0, 20.0),
                _ => (10.0, 10.0),
            };
            RawDetail {
                bends: Vec::new(),
                name: name.into(),
                outer: vec![
                    nest_geom::SvgPoint::new(0.0, 0.0),
                    nest_geom::SvgPoint::new(w, 0.0),
                    nest_geom::SvgPoint::new(w, h),
                    nest_geom::SvgPoint::new(0.0, h),
                ],
                children: vec![],
            }
        };
        let result = nest(&sheets, &parts, catalogue, Config::default()).expect("nest");
        assert!(result.unplaced.is_empty(), "all parts placed");
        assert!(!result.sheets.is_empty());
    }

    fn rect_detail(w: f64, h: f64) -> RawDetail {
        RawDetail {
            bends: Vec::new(),
            name: "r".into(),
            outer: vec![
                nest_geom::SvgPoint::new(0.0, 0.0),
                nest_geom::SvgPoint::new(w, 0.0),
                nest_geom::SvgPoint::new(w, h),
                nest_geom::SvgPoint::new(0.0, h),
            ],
            children: vec![],
        }
    }

    #[test]
    fn default_direction_is_bottom_right_to_top_left() {
        assert_eq!(Config::default().placement_direction, PlacementDirection::BottomRightToTopLeft);
        let sheets = vec![SheetLoadInfo { width: 100.0, height: 50.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![
            DetailLoadInfo { name: "a".into(), quantity: 1, allowed_rotations: vec![], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
            DetailLoadInfo { name: "b".into(), quantity: 1, allowed_rotations: vec![], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
        ];
        let catalogue = |name: &str| -> RawDetail {
            if name == "a" { rect_detail(20.0, 10.0) } else { rect_detail(20.0, 10.0) }
        };
        let result = nest(&sheets, &parts, catalogue, Config::default()).expect("nest");
        assert!(result.unplaced.is_empty());
        let pls = &result.sheets[0].placements;
        // first placed (largest/first in our order) should anchor at bottom-right corner area
        let first = &pls[0];
        assert!(first.x >= 79.0 - 1e-6, "first x={} should be at right", first.x);
        assert!(first.y <= 1e-6, "first y={} should be at bottom", first.y);
        let second = &pls[1];
        // Right-to-left, bottom-to-top can be walked as rows (left along the
        // bottom) or as columns (up the rightmost column); both are in direction.
        assert!(
            (second.y <= 1e-6 && second.x <= first.x - 19.0 + 1e-6)
                || (second.x >= first.x - 1e-6 && second.y > 1e-6),
            "second should go left along the bottom or up the right column: {:?}",
            second
        );
    }

    #[test]
    fn bottom_left_direction_places_first_at_left() {
        let sheets = vec![SheetLoadInfo { width: 100.0, height: 50.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![DetailLoadInfo { name: "a".into(), quantity: 1, allowed_rotations: vec![], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() }];
        let catalogue = |_name: &str| -> RawDetail { rect_detail(20.0, 10.0) };
        let mut cfg = Config::default();
        cfg.placement_direction = PlacementDirection::BottomLeftToTopRight;
        let result = nest(&sheets, &parts, catalogue, cfg).expect("nest");
        let p = &result.sheets[0].placements[0];
        assert!(p.x <= 1e-6, "x={} should be at left", p.x);
        assert!(p.y <= 1e-6, "y={} should be at bottom", p.y);
    }

    #[test]
    fn mirrored_quantity_creates_copies() {
        let sheets = vec![SheetLoadInfo { width: 100.0, height: 50.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![DetailLoadInfo { name: "a".into(), quantity: 1, allowed_rotations: vec![], mirrored_quantity: 2, mirror_allowed: false, is_priority: false, ..Default::default() }];
        let catalogue = |_name: &str| -> RawDetail { rect_detail(10.0, 10.0) };
        let result = nest(&sheets, &parts, catalogue, Config::default()).expect("nest");
        assert_eq!(result.sheets[0].placements.len() + result.unplaced.len(), 3);
        assert!(result.unplaced.is_empty());
    }

    #[test]
    fn impossible_part_is_unplaced() {
        let sheets = vec![SheetLoadInfo { width: 10.0, height: 10.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![DetailLoadInfo { name: "big".into(), quantity: 1, allowed_rotations: vec![], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() }];
        let catalogue = |_name: &str| -> RawDetail { rect_detail(20.0, 5.0) };
        let result = nest(&sheets, &parts, catalogue, Config::default()).expect("nest");
        assert_eq!(result.unplaced.len(), 1);
    }

    #[test]
    fn spacing_keeps_parts_apart() {
        let sheets = vec![SheetLoadInfo { width: 25.0, height: 10.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![
            DetailLoadInfo { name: "a".into(), quantity: 1, allowed_rotations: vec![], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
            DetailLoadInfo { name: "b".into(), quantity: 1, allowed_rotations: vec![], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
        ];
        let catalogue = |_name: &str| -> RawDetail { rect_detail(10.0, 10.0) };
        let mut cfg = Config::default();
        cfg.spacing = 6.0; // 10 + 6 + 10 = 26 > 25, so both cannot fit with spacing
        let result = nest(&sheets, &parts, catalogue, cfg).expect("nest");
        let placed: usize = result.sheets.iter().map(|s| s.placements.len()).sum();
        assert_eq!(placed + result.unplaced.len(), 2);
        assert!(!result.unplaced.is_empty(), "spacing should prevent both fitting");

        // with zero spacing they fit
        let result0 = nest(&sheets, &parts, |_n: &str| rect_detail(10.0,10.0), Config::default()).expect("nest");
        assert!(result0.unplaced.is_empty(), "without spacing both should fit");
    }

    fn frame_and_small_catalogue(name: &str) -> RawDetail {
        if name == "frame" {
            RawDetail {
                bends: Vec::new(),
                name: name.into(),
                outer: vec![
                    nest_geom::SvgPoint::new(0.0, 0.0),
                    nest_geom::SvgPoint::new(60.0, 0.0),
                    nest_geom::SvgPoint::new(60.0, 60.0),
                    nest_geom::SvgPoint::new(0.0, 60.0),
                ],
                children: vec![vec![
                    nest_geom::SvgPoint::new(10.0, 10.0),
                    nest_geom::SvgPoint::new(50.0, 10.0),
                    nest_geom::SvgPoint::new(50.0, 50.0),
                    nest_geom::SvgPoint::new(10.0, 50.0),
                ]],
            }
        } else {
            rect_detail(20.0, 20.0)
        }
    }

    #[test]
    fn merged_length_counts_shared_edges() {
        // 4 squares of 10x10 exactly fill 40x10: 3 shared edges of length 10.
        let sheets = vec![SheetLoadInfo { width: 40.0, height: 10.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![DetailLoadInfo {
            name: "sq".into(),
            quantity: 4,
            allowed_rotations: vec![],
            mirrored_quantity: 0, mirror_allowed: false,
            is_priority: false,
            ..Default::default()
        }];
        let result = nest(&sheets, &parts, |_n: &str| rect_detail(10.0, 10.0), Config::default()).expect("nest");
        assert!(result.unplaced.is_empty());
        let merged: f64 = result.sheets.iter().map(|s| s.merged_length).sum();
        assert!(
            (merged - 30.0).abs() < 0.5,
            "expected ~30 shared edge length, got {merged}"
        );
    }

    #[test]
    fn mirror_allowed_is_optional_in_saved_jobs() {
        // Old project/job JSON has no `mirror_allowed`: it must load as false.
        let old: DetailLoadInfo = serde_json::from_str(r#"{"name":"a","quantity":2}"#).expect("old JSON");
        assert_eq!(old.quantity, 2);
        assert!(!old.mirror_allowed);
        let new: DetailLoadInfo =
            serde_json::from_str(r#"{"name":"a","quantity":2,"mirror_allowed":true}"#).expect("new JSON");
        assert!(new.mirror_allowed);
    }

    #[test]
    fn mirror_if_needed_ignores_symmetric_shapes() {
        // A rectangle's mirror is itself: the flag must not double the variants.
        let r = nest(
            &[SheetLoadInfo { width: 100.0, height: 100.0, quantity: 1, source: None, ..Default::default() }],
            &[DetailLoadInfo {
                name: "r".into(),
                quantity: 1,
                allowed_rotations: vec![0.0],
                mirrored_quantity: 0,
                mirror_allowed: true,
                is_priority: false,
                ..Default::default()
            }],
            |_n: &str| rect_detail(10.0, 10.0),
            Config::default(),
        )
        .expect("nest");
        assert_eq!(r.sheets.iter().map(|s| s.placements.len()).sum::<usize>(), 1);
    }

    fn circle_detail(name: &str, c: (f64, f64), r: f64, n: usize) -> RawDetail {
        let outer = (0..n)
            .map(|k| {
                let a = std::f64::consts::TAU * k as f64 / n as f64;
                nest_geom::SvgPoint::new(c.0 + r * a.cos(), c.1 + r * a.sin())
            })
            .collect();
        RawDetail { bends: Vec::new(), name: name.into(), outer, children: vec![] }
    }

    #[test]
    fn merged_length_counts_round_hole_sharing() {
        // A circular part flush in a circular hole shares the whole circumference.
        let n = 64;
        let r = 20.0;
        let sheets = vec![SheetLoadInfo { width: 100.0, height: 100.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![
            DetailLoadInfo { name: "round-frame".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
            DetailLoadInfo { name: "disc".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
        ];
        let catalogue = |name: &str| -> RawDetail {
            if name == "round-frame" {
                // 100x100 plate with a circular hole at its centre (50,50).
                let mut plate = RawDetail {
                    bends: Vec::new(),
                    name: "round-frame".into(),
                    outer: vec![
                        nest_geom::SvgPoint::new(0.0, 0.0),
                        nest_geom::SvgPoint::new(100.0, 0.0),
                        nest_geom::SvgPoint::new(100.0, 100.0),
                        nest_geom::SvgPoint::new(0.0, 100.0),
                    ],
                    children: vec![],
                };
                plate.children.push(circle_detail("hole", (50.0, 50.0), r, n).outer);
                plate
            } else {
                circle_detail("disc", (0.0, 0.0), r, n)
            }
        };
        let result = nest(&sheets, &parts, catalogue, Config::default()).expect("nest");
        assert!(result.unplaced.is_empty(), "disc must fit the round hole: {:?}", result.unplaced);
        let merged: f64 = result.sheets.iter().map(|s| s.merged_length).sum();
        let circumference = std::f64::consts::TAU * r;
        assert!(
            (merged - circumference).abs() < 1.0,
            "expected ~{circumference:.1} of shared arc, got {merged:.1}"
        );
    }

    #[test]
    fn stock_order_tries_the_big_sheet_first() {
        // Six 50x50 squares: the 100x100 stock holds four, then two more go on
        // the 200x100 stock -> two sheets if the small one is filled first.
        // Filling the big stock first takes all six, saving the small sheet.
        let sheets = vec![
            SheetLoadInfo { width: 100.0, height: 100.0, quantity: 1, source: None, ..Default::default() },
            SheetLoadInfo { width: 200.0, height: 100.0, quantity: 1, source: None, ..Default::default() },
        ];
        let parts = vec![DetailLoadInfo {
            name: "sq".into(),
            quantity: 6,
            allowed_rotations: vec![0.0],
            mirrored_quantity: 0,
            mirror_allowed: false,
            is_priority: false,
            ..Default::default()
        }];
        let result = nest(&sheets, &parts, |_n: &str| rect_detail(50.0, 50.0), Config::default()).expect("nest");
        assert!(result.unplaced.is_empty(), "all squares must be placed: {:?}", result.unplaced);
        assert_eq!(result.sheets.len(), 1, "big stock first must save the small sheet");
        assert_eq!(result.sheets[0].sheet_width, 200.0);
        assert_eq!(result.sheets[0].placements.len(), 6);
    }

    #[test]
    fn merged_length_counts_hole_boundary_sharing() {
        // A 40x40 part nested flush in the 40x40 hole of a frame shares the
        // whole hole boundary: 4 x 40 = 160 of common line.
        let sheets = vec![SheetLoadInfo { width: 100.0, height: 100.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![
            DetailLoadInfo { name: "frame".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
            DetailLoadInfo { name: "fill".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
        ];
        let catalogue = |name: &str| -> RawDetail {
            if name == "frame" {
                RawDetail {
                    bends: Vec::new(),
                    name: "frame".into(),
                    outer: vec![
                        nest_geom::SvgPoint::new(0.0, 0.0),
                        nest_geom::SvgPoint::new(60.0, 0.0),
                        nest_geom::SvgPoint::new(60.0, 60.0),
                        nest_geom::SvgPoint::new(0.0, 60.0),
                    ],
                    children: vec![vec![
                        nest_geom::SvgPoint::new(10.0, 10.0),
                        nest_geom::SvgPoint::new(50.0, 10.0),
                        nest_geom::SvgPoint::new(50.0, 50.0),
                        nest_geom::SvgPoint::new(10.0, 50.0),
                    ]],
                }
            } else {
                rect_detail(40.0, 40.0)
            }
        };
        let result = nest(&sheets, &parts, catalogue, Config::default()).expect("nest");
        assert!(result.unplaced.is_empty(), "fill must fit the hole: {:?}", result.unplaced);
        let merged: f64 = result.sheets.iter().map(|s| s.merged_length).sum();
        assert!((merged - 160.0).abs() < 1.0, "expected ~160 shared hole boundary, got {merged}");
    }

    #[test]
    fn part_can_be_placed_inside_hole_of_placed_part() {
        // Frame (outer 60x60 with centered 40x40 hole) leaves only 10mm border on 70x70 sheet,
        // but a 20x20 part can fit inside the hole.
        let sheets = vec![SheetLoadInfo { width: 70.0, height: 70.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![
            DetailLoadInfo { name: "frame".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
            DetailLoadInfo { name: "small".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
        ];
        let result = nest(&sheets, &parts, frame_and_small_catalogue, Config::default()).expect("nest");
        assert!(result.unplaced.is_empty(), "small part should fit inside frame hole: unplaced={:?}", result.unplaced);
        assert_eq!(result.sheets[0].placements.len(), 2);
        let frame = result.sheets[0].placements.iter().find(|p| p.name == "frame").unwrap();
        let small = result.sheets[0].placements.iter().find(|p| p.name == "small").unwrap();
        // small material (20x20) must be inside the frame hole (frame.x+10 .. frame.x+50 world)
        assert!(small.x >= frame.x + 10.0 - 1e-6 && small.x + 20.0 <= frame.x + 50.0 + 1e-6, "small x {} frame x {}", small.x, frame.x);
        assert!(small.y >= frame.y + 10.0 - 1e-6 && small.y + 20.0 <= frame.y + 50.0 + 1e-6, "small y {} frame y {}", small.y, frame.y);
    }

    #[test]
    fn part_in_hole_respects_spacing_to_hole_boundary() {
        // On a 90x90 sheet the border strips around the 72x72 effective frame are
        // 18 wide — narrower than the 26-wide effective small part, so it must go
        // inside the hole. With spacing 6 the opening (40-12=28) still fits it;
        // with spacing 24 the opening (16) is too small and the part stays unplaced.
        let sheets = vec![SheetLoadInfo { width: 90.0, height: 90.0, quantity: 1, source: None, ..Default::default() }];
        let parts = vec![
            DetailLoadInfo { name: "frame".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
            DetailLoadInfo { name: "small".into(), quantity: 1, allowed_rotations: vec![0.0], mirrored_quantity: 0, mirror_allowed: false, is_priority: false, ..Default::default() },
        ];

        let mut cfg = Config::default();
        cfg.spacing = 6.0;
        let result = nest(&sheets, &parts, frame_and_small_catalogue, cfg).expect("nest");
        assert!(result.unplaced.is_empty(), "spacing 6 should still allow in-hole fit: unplaced={:?}", result.unplaced);
        let frame = result.sheets[0].placements.iter().find(|p| p.name == "frame").unwrap();
        let small = result.sheets[0].placements.iter().find(|p| p.name == "small").unwrap();
        // hole in local coords is 13..53 after the spacing-6 eff normalization
        assert!(small.x >= frame.x + 13.0 - 1e-6 && small.x + 20.0 <= frame.x + 53.0 + 1e-6, "small x {} frame x {}", small.x, frame.x);
        assert!(small.y >= frame.y + 13.0 - 1e-6 && small.y + 20.0 <= frame.y + 53.0 + 1e-6, "small y {} frame y {}", small.y, frame.y);

        let mut cfg2 = Config::default();
        cfg2.spacing = 24.0;
        let result2 = nest(&sheets, &parts, frame_and_small_catalogue, cfg2).expect("nest");
        assert!(result2.unplaced.iter().any(|u| u == "small"), "spacing 24 must prevent in-hole fit: unplaced={:?}", result2.unplaced);
    }

}

#[cfg(test)]
mod shop_tests {
    use super::*;
    use nest_geom::SvgPoint;
    use nest_io::RawDetail;

    fn ring(points: &[(f64, f64)]) -> Vec<SvgPoint> {
        points.iter().map(|&(x, y)| SvgPoint::new(x, y)).collect()
    }
    fn rect(w: f64, h: f64) -> RawDetail {
        RawDetail { bends: Vec::new(), name: "r".into(), outer: ring(&[(0.0, 0.0), (w, 0.0), (w, h), (0.0, h)]), children: vec![] }
    }
    fn part(name: &str, quantity: u32) -> DetailLoadInfo {
        DetailLoadInfo { name: name.into(), quantity, ..Default::default() }
    }
    fn sheet(width: f64, height: f64) -> SheetLoadInfo {
        SheetLoadInfo { width, height, quantity: 1, ..Default::default() }
    }
    fn boxes(result: &NestResult) -> Vec<(f64, f64, f64, f64)> {
        result.sheets.iter().flat_map(|s| s.placements.iter()).map(|p| {
            let xs = p.outer.iter().map(|q| q.x);
            let ys = p.outer.iter().map(|q| q.y);
            (xs.clone().fold(f64::INFINITY, f64::min), ys.clone().fold(f64::INFINITY, f64::min), xs.fold(f64::NEG_INFINITY, f64::max), ys.fold(f64::NEG_INFINITY, f64::max))
        }).collect()
    }

    #[test]
    fn offcut_of_any_shape_takes_parts_only_where_there_is_sheet() {
        // An L: the top right quarter of 100 x 100 is gone.
        let mut l = sheet(100.0, 100.0);
        l.outline = ring(&[(0.0, 0.0), (100.0, 0.0), (100.0, 50.0), (50.0, 50.0), (50.0, 100.0), (0.0, 100.0)]);
        let result = nest(&[l], &[part("a", 4)], |_| rect(50.0, 50.0), Config::default()).expect("nest");
        assert_eq!(result.unplaced.len(), 1, "three quarters hold three squares");
        for (x0, y0, x1, y1) in boxes(&result) {
            assert!(!(x1 > 50.0 + 1e-6 && y1 > 50.0 + 1e-6), "a part lies where the offcut has no metal: {x0},{y0} .. {x1},{y1}");
        }
    }

    #[test]
    fn a_hole_of_the_sheet_is_kept_clear() {
        let mut s = sheet(100.0, 40.0);
        s.holes = vec![ring(&[(40.0, 0.0), (60.0, 0.0), (60.0, 40.0), (40.0, 40.0)])];
        let result = nest(&[s], &[part("a", 3)], |_| rect(40.0, 40.0), Config::default()).expect("nest");
        assert_eq!(result.unplaced.len(), 1, "the cut-out leaves room for two");
        for (x0, _, x1, _) in boxes(&result) {
            assert!(x1 <= 40.0 + 1e-6 || x0 >= 60.0 - 1e-6, "a part lies over the hole: {x0} .. {x1}");
        }
    }

    #[test]
    fn parts_already_on_the_sheet_stay_and_the_rest_fills_the_room() {
        let first = nest(&[sheet(100.0, 50.0)], &[part("a", 1)], |_| rect(50.0, 50.0), Config::default()).expect("nest");
        let there = first.sheets[0].placements.clone();
        let mut s = sheet(100.0, 50.0);
        s.occupied = there.clone();
        let result = nest(&[s], &[part("b", 2)], |_| rect(50.0, 50.0), Config::default()).expect("nest");
        assert_eq!(result.unplaced.len(), 1, "room for one more");
        assert_eq!(result.sheets[0].placements.len(), 1, "the part already there is not repeated");
        let (old, new) = (boxes(&first)[0], boxes(&result)[0]);
        assert!(new.2 <= old.0 + 1e-6 || new.0 >= old.2 - 1e-6, "the new part overlaps the old one");
    }

    #[test]
    fn stock_of_a_better_priority_is_used_first() {
        let mut offcut = sheet(60.0, 60.0);
        offcut.priority = -1;
        let result = nest(&[sheet(200.0, 200.0), offcut], &[part("a", 1)], |_| rect(50.0, 50.0), Config::default()).expect("nest");
        assert_eq!(result.sheets.len(), 1);
        assert_eq!(result.sheets[0].sheet_id, 1, "the offcut, not the whole sheet");
    }

    #[test]
    fn a_part_of_a_better_rank_is_not_crowded_out() {
        // Room for one of the two; by area the big one would go first.
        let mut small = part("small", 1);
        small.priority = 1;
        let mut big = part("big", 1);
        big.priority = 5;
        let catalogue = |name: &str| if name == "big" { rect(60.0, 50.0) } else { rect(50.0, 50.0) };
        let result = nest(&[sheet(100.0, 50.0)], &[big, small], catalogue, Config::default()).expect("nest");
        assert_eq!(result.unplaced, vec!["big".to_string()]);
    }

    #[test]
    fn a_cancelled_nest_says_so() {
        let cfg = Config { cancel: Some(std::sync::Arc::new(std::sync::atomic::AtomicBool::new(true))), ..Config::default() };
        let result = nest(&[sheet(100.0, 50.0)], &[part("a", 2)], |_| rect(10.0, 10.0), cfg);
        assert!(matches!(result, Err(NestError::Cancelled)));
    }

    #[test]
    fn holes_too_small_for_any_part_stay_in_the_result() {
        let mut plate = rect(60.0, 60.0);
        plate.children = vec![ring(&[(10.0, 10.0), (12.0, 10.0), (12.0, 12.0), (10.0, 12.0)]), ring(&[(20.0, 20.0), (50.0, 20.0), (50.0, 50.0), (20.0, 50.0)])];
        let catalogue = move |name: &str| if name == "plate" { plate.clone() } else { rect(20.0, 20.0) };
        let result = nest(&[sheet(60.0, 60.0)], &[part("plate", 1), part("b", 1)], catalogue, Config::default()).expect("nest");
        assert!(result.unplaced.is_empty(), "the small part goes into the big hole");
        let placed = result.sheets[0].placements.iter().find(|p| p.name == "plate").expect("plate");
        assert_eq!(placed.holes.len(), 2);
        assert_eq!(placed.local_holes.len(), 2);
    }
}
